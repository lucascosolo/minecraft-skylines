using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>One moving thing as an upright box in CS1 coordinates.</summary>
    public struct MovingObject
    {
        /// <summary>A vehicle; <see cref="Id"/> indexes <c>VehicleManager.m_vehicles</c>.</summary>
        public const byte Vehicle = 1;
        /// <summary>A walking citizen; <see cref="Id"/> indexes <c>CitizenManager.m_instances</c>.</summary>
        public const byte Citizen = 2;

        /// <summary><see cref="Vehicle"/> or <see cref="Citizen"/>.</summary>
        public byte Kind;
        /// <summary>The game's id of the object.</summary>
        public uint Id;
        /// <summary>Box centre.</summary>
        public Vector3 Center;
        /// <summary>Unity heading of the box's length axis, degrees (Euler Y of the forward vector).</summary>
        public float HeadingDeg;
        /// <summary>Half extents: x across, y vertical, z along the length.</summary>
        public Vector3 HalfExtents;
    }

    /// <summary>
    /// Spawned vehicles (trailers included) and walking citizens near a point, placed where the game draws them this frame:
    /// the same frame pair, Bezier and rotation lerp as <c>Vehicle.RenderInstance</c> and <c>CitizenInstance.RenderInstance</c>,
    /// skipping what they skip on the surface view (inside buildings, underground, unless in transition). Main thread only.
    /// </summary>
    public static class MovingObjects
    {
        /// <summary>Half extents of a citizen (0.6 x 1.8 x 0.6 m).</summary>
        public static readonly Vector3 CitizenHalf = new Vector3(0.3f, 0.9f, 0.3f);

        private const float VehicleCell = 32f;      // VehicleManager.VEHICLEGRID_CELL_SIZE
        private const int VehicleGrid = 540;        // VehicleManager.VEHICLEGRID_RESOLUTION
        private const float CitizenCell = 8f;       // CitizenManager.CITIZENGRID_CELL_SIZE
        private const int CitizenGrid = 2160;       // CitizenManager.CITIZENGRID_RESOLUTION
        private const float GridSlack = 16f;        // grid cells hold the last simulated position, not the drawn one
        private const int ChainLimit = 65536;

        /// <summary>Appends every vehicle and citizen whose drawn position is within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static void Collect(Vector3 center, float radius, List<MovingObject> into)
        {
            if (VehicleManager.exists) Vehicles(center, radius, into);
            if (CitizenManager.exists) Citizens(center, radius, into);
        }

        private static void Vehicles(Vector3 center, float radius, List<MovingObject> into)
        {
            VehicleManager vm = Singleton<VehicleManager>.instance;
            Vehicle[] buf = vm.m_vehicles.m_buffer;
            float timer = Singleton<SimulationManager>.instance.m_referenceTimer;
            int x0, z0, x1, z1;
            Cells(center, radius + GridSlack, VehicleCell, VehicleGrid, out x0, out z0, out x1, out z1);
            for (int gz = z0; gz <= z1; gz++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    ushort id = vm.m_vehicleGrid[gz * VehicleGrid + gx];
                    for (int guard = 0; id != 0 && guard < ChainLimit; guard++)
                    {
                        Vehicle v = buf[id];
                        VehicleInfo info = v.Info;
                        if ((v.m_flags & Vehicle.Flags.Spawned) != 0 && info != null && info.m_generatedInfo != null)
                        {
                            uint target = v.GetTargetFrame(info, id);
                            Vehicle.Frame a = v.GetFrameData(target - 32), b = v.GetFrameData(target - 16);
                            bool transition = a.m_transition || b.m_transition;
                            if (transition || !((a.m_underground && b.m_underground) || (a.m_insideBuilding && b.m_insideBuilding)))
                            {
                                float t = ((target & 15) + timer) * 0.0625f;
                                Vector3 pos = Smooth(a.m_position, a.m_velocity, b.m_position, b.m_velocity, t);
                                Vector3 size = info.m_generatedInfo.m_size;
                                Add(into, MovingObject.Vehicle, id, pos, Quaternion.Lerp(a.m_rotation, b.m_rotation, t), size * 0.5f, center, radius);
                            }
                        }
                        id = v.m_nextGridVehicle;
                    }
                }
        }

        private static void Citizens(Vector3 center, float radius, List<MovingObject> into)
        {
            CitizenManager cm = Singleton<CitizenManager>.instance;
            CitizenInstance[] buf = cm.m_instances.m_buffer;
            SimulationManager sim = Singleton<SimulationManager>.instance;
            int x0, z0, x1, z1;
            Cells(center, radius + GridSlack, CitizenCell, CitizenGrid, out x0, out z0, out x1, out z1);
            for (int gz = z0; gz <= z1; gz++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    ushort id = cm.m_citizenGrid[gz * CitizenGrid + gx];
                    for (int guard = 0; id != 0 && guard < ChainLimit; guard++)
                    {
                        CitizenInstance c = buf[id];
                        if ((c.m_flags & CitizenInstance.Flags.Character) != 0 && c.Info != null)
                        {
                            uint target = sim.m_referenceFrameIndex - ((uint)(id << 4) / 65536u);
                            CitizenInstance.Frame a = c.GetFrameData(target - 32), b = c.GetFrameData(target - 16);
                            bool transition = a.m_transition || b.m_transition;
                            bool hang = (c.m_flags & CitizenInstance.Flags.HangAround) != 0;
                            if (transition || hang || !((a.m_underground && b.m_underground) || (a.m_insideBuilding && b.m_insideBuilding)))
                            {
                                float t = ((target & 15) + sim.m_referenceTimer) * 0.0625f;
                                Vector3 feet = Smooth(a.m_position, a.m_velocity, b.m_position, b.m_velocity, t);
                                Add(into, MovingObject.Citizen, id, feet, Quaternion.Lerp(a.m_rotation, b.m_rotation, t), CitizenHalf, center, radius);
                            }
                        }
                        id = c.m_nextGridInstance;
                    }
                }
        }

        // The pivot is at the bottom of the box (Vehicle.RenderOverlay spans pivot.y to pivot.y + m_size.y).
        private static void Add(List<MovingObject> into, byte kind, ushort id, Vector3 pivot, Quaternion rot, Vector3 half, Vector3 center, float radius)
        {
            float dx = pivot.x - center.x, dz = pivot.z - center.z;
            if (dx * dx + dz * dz > radius * radius || Mathf.Abs(pivot.y - center.y) > radius) return;
            Vector3 fwd = rot * Vector3.forward;
            into.Add(new MovingObject
            {
                Kind = kind,
                Id = id,
                Center = new Vector3(pivot.x, pivot.y + half.y, pivot.z),
                HeadingDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg,
                HalfExtents = half,
            });
        }

        private static Vector3 Smooth(Vector3 p0, Vector3 v0, Vector3 p1, Vector3 v1, float t)
        {
            return new Bezier3 { a = p0, b = p0 + v0 * 0.333f, c = p1 - v1 * 0.333f, d = p1 }.Position(t);
        }

        private static void Cells(Vector3 c, float r, float cell, int res, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = Mathf.Clamp((int)((c.x - r) / cell + res * 0.5f), 0, res - 1);
            z0 = Mathf.Clamp((int)((c.z - r) / cell + res * 0.5f), 0, res - 1);
            x1 = Mathf.Clamp((int)((c.x + r) / cell + res * 0.5f), 0, res - 1);
            z1 = Mathf.Clamp((int)((c.z + r) / cell + res * 0.5f), 0, res - 1);
        }
    }
}
