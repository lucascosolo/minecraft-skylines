using System;
using System.Collections.Generic;
using ColossalFramework;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Resources;
using UnityEngine;

namespace MinecraftSkylines.Mod.City
{
    /// <summary>
    /// Minor 21: while a city is open and ready, sends CITY_CONDITIONS once a second: the 7 x 7 resource cells around the
    /// player's feet (player mode) or the city view's focus, with crime, unburied dead, and the burning buildings within 96 m.
    /// Main thread only; reads game state without locks, as LightLink does.
    /// </summary>
    internal sealed class CityConditionsLink
    {
        private const double IntervalSeconds = 1.0;
        private const int Half = 3;
        private const float FireRadius = 96f;
        private const int Cells = 512;

        private readonly CityLink _city;
        private readonly PlayerMode _player;
        private readonly List<ConditionCell> _cells = new List<ConditionCell>();
        private readonly List<FireSpot> _fires = new List<FireSpot>();
        private double _lastSeconds = double.NegativeInfinity;
        private uint _seenOpenSeq;

        public CityConditionsLink(CityLink city, PlayerMode player)
        {
            _city = city;
            _player = player;
        }

        /// <summary>Per frame, after the city link and player mode were updated.</summary>
        public void Update(BridgeHost host, double nowSeconds)
        {
            if (host == null || host.State != BridgeState.Connected || host.NegotiatedAppMinor < 21)
            {
                Reset();
                return;
            }
            uint seq = _city.OpenSeq;
            if (seq != _seenOpenSeq)
            {
                _seenOpenSeq = seq;
                Reset();
            }
            if (!_city.IsOpen || !_city.Ready) return;
            if (nowSeconds - _lastSeconds < IntervalSeconds) return;
            Vector3 centre;
            if (!TryCentre(out centre)) return;
            _lastSeconds = nowSeconds;

            _cells.Clear();
            _fires.Clear();
            SampleCells(centre);
            SampleFires(centre);
            var msg = new CityConditions { OpenSeq = seq, Cells = _cells.ToArray(), Fires = _fires.ToArray() };
            host.Send(AppProtocol.CityConditionsType, msg.Encode());
        }

        /// <summary>A new open or link loss: send again at the next chance.</summary>
        public void Reset()
        {
            _lastSeconds = double.NegativeInfinity;
        }

        private bool TryCentre(out Vector3 cs)
        {
            cs = Vector3.zero;
            if (_player.IsActive)
            {
                uint flags;
                double ageMs;
                if (!_player.TryLatestState(out cs, out flags, out ageMs)) return false;
            }
            else
            {
                Camera cam = Camera.main;
                if (cam == null) return false;
                CameraController controller = cam.GetComponent<CameraController>();
                if (controller == null || !controller.enabled) return false;
                cs = controller.m_currentPosition;
            }
            return !(float.IsNaN(cs.x) || float.IsInfinity(cs.x) || float.IsNaN(cs.z) || float.IsInfinity(cs.z));
        }

        private void SampleCells(Vector3 centre)
        {
            NaturalResourceManager.ResourceCell[] grid = Singleton<NaturalResourceManager>.instance.m_naturalResources;
            DistrictManager dm = Singleton<DistrictManager>.instance;
            int c0x = ResourceGrid.Cell(centre.x), c0z = ResourceGrid.Cell(centre.z);
            for (int cz = Math.Max(0, c0z - Half); cz <= Math.Min(Cells - 1, c0z + Half); cz++)
            {
                for (int cx = Math.Max(0, c0x - Half); cx <= Math.Min(Cells - 1, c0x + Half); cx++)
                {
                    NaturalResourceManager.ResourceCell r = grid[cz * Cells + cx];
                    var at = new Vector3((cx - 256 + 0.5f) * 33.75f, 0f, (cz - 256 + 0.5f) * 33.75f);
                    _cells.Add(new ConditionCell
                    {
                        Cx = (ushort)cx, Cz = (ushort)cz,
                        Ore = r.m_ore, Oil = r.m_oil, Fertility = r.m_fertility, Forest = r.m_forest, Pollution = r.m_pollution,
                        Flags = (r.m_modified & 1) != 0 ? CityConditions.Worked : (byte)0,
                        Crime = dm.m_districts.m_buffer[dm.GetDistrict(at)].m_finalCrimeRate,
                        Dead = (byte)Math.Min(255, CountDead(cx, cz)),
                    });
                }
            }
        }

        // Buildings are filed by m_position in 64 m cells (BuildingManager.m_buildingGrid); the grid cells overlapping the
        // resource cell are walked and each building's position is tested against it.
        private static int CountDead(int cx, int cz)
        {
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Building[] buildings = bm.m_buildings.m_buffer;
            float x0 = (cx - 256) * 33.75f, z0 = (cz - 256) * 33.75f;
            float x1 = x0 + 33.75f, z1 = z0 + 33.75f;
            int dead = 0;
            for (int gz = GridCell(z0); gz <= GridCell(z1); gz++)
            {
                for (int gx = GridCell(x0); gx <= GridCell(x1); gx++)
                {
                    ushort id = bm.m_buildingGrid[gz * 270 + gx];
                    int guard = 0;
                    while (id != 0 && guard++ < BuildingManager.MAX_BUILDING_COUNT)
                    {
                        Vector3 p = buildings[id].m_position;
                        if (Live(ref buildings[id]) && p.x >= x0 && p.x < x1 && p.z >= z0 && p.z < z1
                            && (buildings[id].m_problems.m_Problems1 & Notification.Problem1.Death) != 0)
                            dead++;
                        id = buildings[id].m_nextGridBuilding;
                    }
                }
            }
            return dead;
        }

        private void SampleFires(Vector3 centre)
        {
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Building[] buildings = bm.m_buildings.m_buffer;
            for (int gz = GridCell(centre.z - FireRadius); gz <= GridCell(centre.z + FireRadius); gz++)
            {
                for (int gx = GridCell(centre.x - FireRadius); gx <= GridCell(centre.x + FireRadius); gx++)
                {
                    ushort id = bm.m_buildingGrid[gz * 270 + gx];
                    int guard = 0;
                    while (id != 0 && guard++ < BuildingManager.MAX_BUILDING_COUNT)
                    {
                        AddFire(ref buildings[id], centre);
                        if (_fires.Count >= CityConditions.MaxFires) return;
                        id = buildings[id].m_nextGridBuilding;
                    }
                }
            }
        }

        private void AddFire(ref Building b, Vector3 centre)
        {
            if (b.m_fireIntensity == 0 || !Live(ref b)) return;
            float dx = b.m_position.x - centre.x, dz = b.m_position.z - centre.z;
            if (dx * dx + dz * dz > FireRadius * FireRadius) return;
            float w = b.m_width, l = b.m_length;
            _fires.Add(new FireSpot
            {
                X = b.m_position.x, Y = b.m_position.y, Z = -b.m_position.z,
                Radius = 0.5f * (float)Math.Sqrt(w * w + l * l) * 8f,
                Intensity = b.m_fireIntensity,
            });
        }

        private static bool Live(ref Building b)
        {
            return (b.m_flags & Building.Flags.Created) != 0 && (b.m_flags & Building.Flags.Deleted) == 0;
        }

        private static int GridCell(float v)
        {
            return Math.Max(0, Math.Min(269, (int)(v / 64f + 135f)));
        }
    }
}
