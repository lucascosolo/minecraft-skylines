using System;
using System.Collections.Generic;
using System.Diagnostics;
using ColossalFramework;
using Skylines.Host;
using UnityEngine;

namespace MinecraftSkylines.Mod.SelfTest
{
    /// <summary>The roads and hillside the self-test uses in a new game it started itself.</summary>
    internal sealed class Fixture
    {
        public Vector3 Centre;
        public readonly List<ushort> Ground = new List<ushort>();
        /// <summary>Elevated segments, the level +8 m ones first, then the ramp.</summary>
        public readonly List<ushort> Raised = new List<ushort>();
        public bool HasSlope;
        public SlopeSpot Slope;
    }

    /// <summary>
    /// Builds <see cref="FixturePlan"/>'s roads once in the new game <see cref="SaveAutoloader"/> started (never in a
    /// loaded save): plans on the main thread from terrain samples, creates nodes and segments on the simulation thread
    /// as NetTool does (NetManager.CreateNode / CreateSegment, no construction cost), waits for the segments to update,
    /// then points the city camera at the fixture. Main thread only.
    /// </summary>
    internal sealed class FixtureBuilder
    {
        private enum State { Off, Pending, Building, Settling, Done }

        private const double SettleSeconds = 2;
        private const float CameraDistance = 400f;

        private readonly HostLog _log;
        private readonly Stopwatch _settle = new Stopwatch();
        private State _state;
        private AsyncAction _action;
        private Fixture _building;
        private string _buildError;

        public FixtureBuilder(HostLog log)
        {
            _log = log;
        }

        /// <summary>The finished fixture, or null.</summary>
        public Fixture Fixture { get; private set; }

        /// <summary>True from the level load until the fixture is ready or has failed.</summary>
        public bool Busy { get { return _state == State.Pending || _state == State.Building || _state == State.Settling; } }

        public void OnLevelLoaded()
        {
            Fixture = null;
            _state = SaveAutoloader.InOurNewGame() ? State.Pending : State.Off;
            if (_state == State.Pending) _log.Info("fixture: new game started by autoload; building the self-test roads");
        }

        public void OnLevelUnloading()
        {
            Fixture = null;
            _state = State.Off;
        }

        public void Update(bool cityReady)
        {
            if (!Busy) return;
            if (!cityReady) return;
            try
            {
                if (_state == State.Pending) Plan();
                else if (_state == State.Building && _action.completedOrFailed) Built();
                else if (_state == State.Settling && _settle.Elapsed.TotalSeconds >= SettleSeconds) Finish();
            }
            catch (Exception e)
            {
                _log.Error("fixture", e);
                _state = State.Done;
            }
        }

        private void Plan()
        {
            TerrainManager tm = Singleton<TerrainManager>.instance;
            Func<double, double, double> height = (x, z) => tm.SampleRawHeightSmooth((float)x, (float)z);
            Func<double, double, bool> water = (x, z) =>
            {
                var p = new Vector3((float)x, 0f, (float)z);
                return tm.SampleRawHeightSmoothWithWater(p, false, 0f) - tm.SampleRawHeightSmooth(p) > 0.1f;
            };
            int tx, tz;
            float minX, minZ, maxX, maxZ;
            GameAreaManager areas = Singleton<GameAreaManager>.instance;
            areas.GetStartTile(out tx, out tz);
            areas.GetAreaBounds(tx, tz, out minX, out minZ, out maxX, out maxZ);
            double cx, cz, spread;
            if (!FixturePlan.FindFlattest(height, water, minX + 32, minZ + 32, maxX - 32, maxZ - 32, (minX + maxX) / 2, (minZ + maxZ) / 2, out cx, out cz, out spread))
            {
                _log.Warn("fixture: no dry 300 m square in the starting tile; no fixture");
                _state = State.Done;
                return;
            }
            var f = new Fixture { Centre = new Vector3((float)cx, (float)height(cx, cz), (float)cz) };
            SlopeSpot slope;
            f.HasSlope = FixturePlan.SteepestSlope(height, water, cx, cz, out slope);
            f.Slope = slope;
            _log.Info("fixture: start tile (" + tx + ", " + tz + "), area centre (" + cx.ToString("0") + ", " + cz.ToString("0") + "), height spread "
                + spread.ToString("0.00") + " m; slope " + (f.HasSlope ? slope.Degrees.ToString("0.0") + " deg at (" + slope.X.ToString("0") + ", " + slope.Z.ToString("0") + ")" : "none 10-30 deg within 300 m"));
            FixtureNode[] ground = FixturePlan.GroundRoad(cx, cz), raised = FixturePlan.ElevatedRoad(cx, cz);
            Vector3[] gp = Positions(ground, height), rp = Positions(raised, height);
            _building = f;
            _buildError = null;
            _action = Singleton<SimulationManager>.instance.AddAction("MCSK self-test fixture", () =>
            {
                try
                {
                    Road(gp, ground, f.Ground);
                    var ids = new List<ushort>();
                    Road(rp, raised, ids);
                    for (int i = 0; i < ids.Count; i++) if (raised[i].Elevation == raised[i + 1].Elevation) f.Raised.Add(ids[i]);
                    for (int i = 0; i < ids.Count; i++) if (raised[i].Elevation != raised[i + 1].Elevation) f.Raised.Add(ids[i]);
                }
                catch (Exception e) { _buildError = e.ToString(); }
            });
            _state = State.Building;
        }

        private static Vector3[] Positions(FixtureNode[] nodes, Func<double, double, double> height)
        {
            var p = new Vector3[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) p[i] = new Vector3((float)nodes[i].X, (float)(height(nodes[i].X, nodes[i].Z) + nodes[i].Elevation), (float)nodes[i].Z);
            return p;
        }

        // Simulation thread. NetTool.CreateNodeImpl's create path (NetTool.cs:3286-3312 and :3816-3880): the node's and
        // the segment's info come from the road AI's GetInfo for their elevations (RoadAI.GetInfo, RoadAI.cs:132-172:
        // > 0.1 m gives m_elevatedInfo), elevated nodes carry m_elevation, ground nodes OnGround; each node advances the
        // build index by one, each segment by two. NetManager.CreateNode/CreateSegment charge nothing (EconomyManager
        // is only called by NetTool, NetTool.cs:3793).
        // "Basic Road" is the base game's two-lane road, but its prefab name is not in the decompiled code, so
        // if it is missing fall back to the narrowest loaded plain RoadAI road that has an elevated variant
        // (the fixture needs one for its raised section). The chosen name is logged by the caller's report.
        internal static string FixtureRoadName = "";

        private static NetInfo FixtureRoad()
        {
            NetInfo road = PrefabCollection<NetInfo>.FindLoaded("Basic Road");
            if (road == null)
            {
                int n = PrefabCollection<NetInfo>.LoadedCount();
                for (uint i = 0; i < n; i++)
                {
                    NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                    RoadAI ai = info == null ? null : info.m_netAI as RoadAI;
                    if (ai == null || ai.GetType() != typeof(RoadAI) || ai.m_elevatedInfo == null) continue;
                    if (road == null || info.m_halfWidth < road.m_halfWidth) road = info;
                }
            }
            if (road == null) throw new InvalidOperationException("no plain RoadAI road with an elevated variant is loaded");
            FixtureRoadName = road.name;
            return road;
        }

        private static void Road(Vector3[] pos, FixtureNode[] plan, List<ushort> segments)
        {
            NetInfo road = FixtureRoad();
            NetManager nm = Singleton<NetManager>.instance;
            SimulationManager sm = Singleton<SimulationManager>.instance;
            var nodes = new ushort[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                float e = (float)plan[i].Elevation;
                NetInfo info = Info(road, e, e, 0f);
                if (!nm.CreateNode(out nodes[i], ref sm.m_randomizer, info, pos[i], sm.m_currentBuildIndex))
                    throw new InvalidOperationException("CreateNode failed (node buffer full?)");
                if (e > 0.1f && info.m_netAI.IsOverground()) nm.m_nodes.m_buffer[nodes[i]].m_elevation = (byte)Mathf.Clamp(Mathf.RoundToInt(e), 1, 255);
                else nm.m_nodes.m_buffer[nodes[i]].m_flags |= NetNode.Flags.OnGround;
                sm.m_currentBuildIndex++;
            }
            for (int i = 0; i + 1 < pos.Length; i++)
            {
                Vector3 dir = new Vector3(pos[i + 1].x - pos[i].x, 0f, pos[i + 1].z - pos[i].z);
                float length = dir.magnitude;
                dir /= length;
                float lo = (float)Math.Min(plan[i].Elevation, plan[i + 1].Elevation), hi = (float)Math.Max(plan[i].Elevation, plan[i + 1].Elevation);
                ushort seg;
                if (!nm.CreateSegment(out seg, ref sm.m_randomizer, Info(road, lo, hi, length), nodes[i], nodes[i + 1], dir, -dir, sm.m_currentBuildIndex, sm.m_currentBuildIndex, false))
                    throw new InvalidOperationException("CreateSegment failed (segment buffer full?)");
                sm.m_currentBuildIndex += 2u;
                segments.Add(seg);
            }
        }

        private static NetInfo Info(NetInfo road, float minElevation, float maxElevation, float length)
        {
            ToolBase.ToolErrors errors = ToolBase.ToolErrors.None;
            return road.m_netAI.GetInfo(minElevation, maxElevation, length, false, false, false, false, ref errors) ?? road;
        }

        private void Built()
        {
            if (_buildError != null || _building.Ground.Count == 0)
            {
                _log.Warn("fixture: building the roads failed: " + (_buildError ?? "no segments"));
                _state = State.Done;
                return;
            }
            _settle.Reset();
            _settle.Start();
            _state = State.Settling;
        }

        private void Finish()
        {
            NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            var names = new List<string>();
            foreach (ushort id in _building.Ground) names.Add(id + " " + segs[id].Info.name);
            foreach (ushort id in _building.Raised) names.Add(id + " " + segs[id].Info.name + (segs[id].Info.m_netAI.IsBridge() ? " (bridge)" : ""));
            _log.Info("fixture: segments " + string.Join(", ", names.ToArray()));
            Fixture = _building;
            _state = State.Done;
            CameraController ctl = Camera.main == null ? null : Camera.main.GetComponent<CameraController>();
            if (ctl == null) { _log.Warn("fixture: no CameraController; camera not moved"); return; }
            ctl.m_targetPosition = Fixture.Centre;
            ctl.m_currentPosition = Fixture.Centre;
            ctl.m_targetSize = CameraDistance;
            ctl.m_currentSize = CameraDistance;
        }
    }
}
