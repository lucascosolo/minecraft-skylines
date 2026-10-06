using System.Collections.Generic;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Motion;
using Vec3d = MinecraftSkylines.Protocol.Vec3d;
using CoreVec = Skylines.Core.Motion.Vec3d;
using Skylines.Host;
using Skylines.Host.Geometry;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// While the player is in Minecraft, sends every vehicle and walking citizen within 48 m as DYNAMIC_OBSTACLES (minor 7)
    /// about 20 times a second, with velocities observed on the real clock. Main thread only.
    /// </summary>
    internal sealed class ObstacleLink
    {
        private const double IntervalSeconds = 0.05;
        private const float Radius = 48f;

        private readonly HostLog _log;
        private readonly List<MovingObject> _found = new List<MovingObject>();
        private readonly VelocityTracker _velocity = new VelocityTracker();
        private double _lastSeconds = double.NegativeInfinity;
        private bool _logged;
        private double _lastCountSeconds = double.NegativeInfinity;

        /// <summary>The set last sent, for the Ctrl+Shift+G viewer. Main thread only.</summary>
        public static readonly List<MovingObject> LastSent = new List<MovingObject>();

        public ObstacleLink(HostLog log)
        {
            _log = log;
        }

        public void Update(BridgeHost host, PlayerMode player, double nowSeconds)
        {
            Vector3 feet;
            uint flags;
            double ageMs;
            if (host == null || host.State != BridgeState.Connected || host.NegotiatedAppMinor < 7 || !player.IsActive
                || !player.TryLatestState(out feet, out flags, out ageMs))
                return;
            if (nowSeconds - _lastSeconds < IntervalSeconds) return;
            _lastSeconds = nowSeconds;

            _found.Clear();
            MovingObjects.Collect(feet, Radius, _found);
            _velocity.Begin(nowSeconds);
            var msg = new DynamicObstacles { Obstacles = new MovingObstacle[System.Math.Min(_found.Count, ushort.MaxValue)] };
            for (int i = 0; i < msg.Obstacles.Length; i++)
            {
                MovingObject o = _found[i];
                Vec3d mc = MinecraftFrame.CsToMc(new Vec3d { X = o.Center.x, Y = o.Center.y, Z = o.Center.z });
                CoreVec v = _velocity.Sample(((long)o.Kind << 32) | o.Id, new CoreVec { X = mc.X, Y = mc.Y, Z = mc.Z });
                msg.Obstacles[i] = new MovingObstacle
                {
                    Kind = o.Kind == MovingObject.Vehicle ? DynamicObstacles.Vehicle
                        : o.Kind == MovingObject.ParkedVehicle ? DynamicObstacles.ParkedVehicle : DynamicObstacles.Citizen,
                    Id = o.Id,
                    X = (float)mc.X, Y = (float)mc.Y, Z = (float)mc.Z,
                    Yaw = (float)MinecraftFrame.UnityEulerToMc(0, o.HeadingDeg).Yaw,
                    HalfWidth = o.HalfExtents.x, HalfHeight = o.HalfExtents.y, HalfLength = o.HalfExtents.z,
                    VX = (float)v.X, VY = (float)v.Y, VZ = (float)v.Z,
                };
            }
            _velocity.End();
            LastSent.Clear();
            LastSent.AddRange(_found);
            if (nowSeconds - _lastCountSeconds >= 10)
            {
                _lastCountSeconds = nowSeconds;
                int cars = 0, parked = 0, people = 0;
                foreach (MovingObject o in _found)
                {
                    if (o.Kind == MovingObject.Vehicle) cars++;
                    else if (o.Kind == MovingObject.ParkedVehicle) parked++;
                    else people++;
                }
                _log.Info("obstacles: within " + Radius + " m of (" + feet.x.ToString("0") + ", " + feet.y.ToString("0") + ", " + feet.z.ToString("0")
                    + "): " + cars + " moving vehicles, " + parked + " parked, " + people + " citizens");
            }
            if (host.Send(AppProtocol.DynamicObstaclesType, msg.Encode()) && !_logged)
            {
                _logged = true;
                _log.Info("obstacles: first DYNAMIC_OBSTACLES sent, " + msg.Obstacles.Length + " within " + Radius + " m");
            }
        }
    }
}
