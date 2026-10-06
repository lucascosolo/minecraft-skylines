using System;
using System.Collections.Generic;
using System.Text;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using Skylines.Core.Geometry;
using Skylines.Host;
using Skylines.Host.Geometry;
using UnityEngine;
using UInput = UnityEngine.Input;

namespace MinecraftSkylines.Mod.Diagnostics
{
    /// <summary>
    /// Ctrl+Shift+D (city camera and Minecraft mode): logs how the game renders the network around the camera, to find out
    /// how tunnels are drawn. Every segment and node within <see cref="Radius"/> m with its NetInfo's segment and node
    /// meshes, materials, shaders, render layers and flag filters; the main and UndergroundView camera culling masks; and
    /// underground vehicles with their underground material; each NetInfo's surface level, widths and lanes; and the
    /// raised-pavement step NetGeometry computes for the ground segment nearest the camera. One delimited block in the mod log per press; nothing else
    /// is written or changed.
    /// </summary>
    internal sealed class NetRenderDump
    {
        /// <summary>Search radius around the camera, metres (xz).</summary>
        public const float Radius = 60f;

        private readonly HostLog _log;
        private readonly Func<bool> _playerOn;

        public NetRenderDump(HostLog log, Func<bool> playerOn)
        {
            _log = log;
            _playerOn = playerOn;
        }

        /// <summary>Per-frame key check; wire to MainThreadPump.Updated.</summary>
        public void Update()
        {
            if (!UInput.GetKeyDown(KeyCode.D) || !CtrlShiftHeld()) return;
            // In Minecraft mode the shortcut blocker is the modal; in the city a focused text field or dialog keeps the key.
            if (!_playerOn() && (UIView.HasModalInput() || UIView.HasInputFocus())) return;
            if (!CityState.Capture().InCity || !NetManager.exists) return;
            try
            {
                _log.Info(Dump());
            }
            catch (Exception e)
            {
                _log.Error("net render dump", e);
            }
        }

        /// <summary>True when Ctrl and Shift are held.</summary>
        public static bool CtrlShiftHeld()
        {
            return (UInput.GetKey(KeyCode.LeftControl) || UInput.GetKey(KeyCode.RightControl))
                && (UInput.GetKey(KeyCode.LeftShift) || UInput.GetKey(KeyCode.RightShift));
        }

        private static string Dump()
        {
            Camera cam = Camera.main;
            Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
            GameObject ug = GameObject.FindGameObjectWithTag("UndergroundView");
            Camera under = ug == null ? null : ug.GetComponent<Camera>();
            var sb = new StringBuilder();
            sb.Append("===== net render dump begin: camera (").Append(Fmt(c)).Append("), radius ").Append(Radius).Append(" m =====\n");
            sb.Append("main camera: ").Append(cam == null ? "none" : MaskText(cam)).Append('\n');
            sb.Append("UndergroundView camera: ").Append(under == null ? "not found" : MaskText(under) + ", enabled " + under.enabled + ", depth " + under.depth).Append('\n');

            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            NetNode[] nodes = nm.m_nodes.m_buffer;
            var infos = new List<NetInfo>();
            int segCount = 0, nodeCount = 0;
            for (int i = 1; i < segs.Length; i++)
            {
                if ((segs[i].m_flags & NetSegment.Flags.Created) == 0) continue;
                Bezier3 b = segs[i].GenerateBezier((ushort)i, segs[i].m_startNode);
                if (!Near(b, c)) continue;
                NetInfo info = segs[i].Info;
                if (info == null) continue;
                if (!infos.Contains(info)) infos.Add(info);
                segCount++;
                sb.Append("segment ").Append(i).Append(": ").Append(InfoText(info)).Append(", flags ").Append(segs[i].m_flags)
                    .Append(", flags2 ").Append(segs[i].m_flags2).Append(", node elevation ").Append(nodes[segs[i].m_startNode].m_elevation)
                    .Append('/').Append(nodes[segs[i].m_endNode].m_elevation).Append(", drawn entries [");
                if (info.m_segments != null)
                {
                    for (int k = 0; k < info.m_segments.Length; k++)
                    {
                        bool turn;
                        if (info.m_segments[k] != null && info.m_segments[k].CheckFlags(segs[i].m_flags, segs[i].m_flags2, out turn)) sb.Append(' ').Append(k);
                    }
                }
                sb.Append(" ]\n");
            }
            for (int i = 1; i < nodes.Length; i++)
            {
                if ((nodes[i].m_flags & NetNode.Flags.Created) == 0) continue;
                Vector3 p = nodes[i].m_position;
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude > Radius * Radius) continue;
                NetInfo info = nodes[i].Info;
                if (info == null) continue;
                if (!infos.Contains(info)) infos.Add(info);
                nodeCount++;
                sb.Append("node ").Append(i).Append(": ").Append(InfoText(info)).Append(", flags ").Append(nodes[i].m_flags)
                    .Append(", flags2 ").Append(nodes[i].m_flags2).Append(", elevation ").Append(nodes[i].m_elevation)
                    .Append(", y ").Append(p.y.ToString("0.00")).Append('\n');
            }
            foreach (NetInfo info in infos) AppendInfo(sb, info);
            sb.Append(NetGeometry.DescribePavementSteps(c)).Append('\n');
            int vehicles = AppendVehicles(sb, c);
            AppendLife(sb, c);
            AppendDecals(sb, c);
            AppendProps(sb, c);
            sb.Append("===== net render dump end: ").Append(segCount).Append(" segments, ").Append(nodeCount).Append(" nodes, ")
                .Append(infos.Count).Append(" infos, ").Append(vehicles).Append(" underground vehicles =====");
            return sb.ToString();
        }

        private static bool Near(Bezier3 b, Vector3 c)
        {
            for (int k = 0; k <= 8; k++)
            {
                Vector3 p = b.Position(k / 8f);
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude <= Radius * Radius) return true;
            }
            return false;
        }

        private static string InfoText(NetInfo info)
        {
            NetAI ai = info.m_netAI;
            return "'" + info.name + "' ai " + (ai == null ? "none" : ai.GetType().Name)
                + (ai == null ? "" : ", IsUnderground " + ai.IsUnderground() + ", IsBridge " + ai.IsBridge());
        }

        private static void AppendInfo(StringBuilder sb, NetInfo info)
        {
            sb.Append("info '").Append(info.name).Append("': class layer ").Append(info.m_class == null ? "?" : info.m_class.m_layer.ToString())
                .Append(", prefab data layer ").Append(LayerText(info.m_prefabDataLayer)).Append(", net layers 0x").Append(info.m_netLayers.ToString("X8"))
                .Append(" (").Append(MaskNames(info.m_netLayers)).Append("), clipTerrain ").Append(info.m_clipTerrain)
                .Append(", flattenTerrain ").Append(info.m_flattenTerrain).Append(", surfaceLevel ").Append(info.m_surfaceLevel.ToString("0.00"))
                .Append(", halfWidth ").Append(info.m_halfWidth.ToString("0.00")).Append(", pavementWidth ").Append(info.m_pavementWidth.ToString("0.00")).Append('\n');
            if (info.m_lanes != null)
            {
                for (int k = 0; k < info.m_lanes.Length; k++)
                {
                    NetInfo.Lane l = info.m_lanes[k];
                    if (l == null) continue;
                    sb.Append("  lane[").Append(k).Append("] type ").Append(l.m_laneType).Append(", vehicles ").Append(l.m_vehicleType)
                        .Append(", position ").Append(l.m_position.ToString("0.00")).Append(", width ").Append(l.m_width.ToString("0.00"))
                        .Append(", verticalOffset ").Append(l.m_verticalOffset.ToString("0.00")).Append(", direction ").Append(l.m_direction)
                        .Append(" (final ").Append(l.m_finalDirection).Append(")\n");
                }
            }
            if (info.m_segments != null)
            {
                for (int k = 0; k < info.m_segments.Length; k++)
                {
                    NetInfo.Segment s = info.m_segments[k];
                    if (s == null) continue;
                    sb.Append("  segment[").Append(k).Append("] layer ").Append(LayerText(s.m_layer))
                        .Append(", mesh ").Append(MeshText(s.m_mesh)).Append(", lodMesh ").Append(MeshText(s.m_lodMesh))
                        .Append(", segmentMesh ").Append(MeshText(s.m_segmentMesh))
                        .Append(", material ").Append(MaterialText(s.m_material)).Append(", lodMaterial ").Append(MaterialText(s.m_lodMaterial))
                        .Append(", segmentMaterial ").Append(MaterialText(s.m_segmentMaterial))
                        .Append(", forward req ").Append(s.m_forwardRequired).Append(" forb ").Append(s.m_forwardForbidden)
                        .Append(", backward req ").Append(s.m_backwardRequired).Append(" forb ").Append(s.m_backwardForbidden).Append('\n');
                }
            }
            if (info.m_nodes != null)
            {
                for (int k = 0; k < info.m_nodes.Length; k++)
                {
                    NetInfo.Node n = info.m_nodes[k];
                    if (n == null) continue;
                    sb.Append("  node[").Append(k).Append("] layer ").Append(LayerText(n.m_layer))
                        .Append(", mesh ").Append(MeshText(n.m_mesh)).Append(", lodMesh ").Append(MeshText(n.m_lodMesh))
                        .Append(", nodeMesh ").Append(MeshText(n.m_nodeMesh))
                        .Append(", material ").Append(MaterialText(n.m_material)).Append(", lodMaterial ").Append(MaterialText(n.m_lodMaterial))
                        .Append(", nodeMaterial ").Append(MaterialText(n.m_nodeMaterial))
                        .Append(", req ").Append(n.m_flagsRequired).Append(" forb ").Append(n.m_flagsForbidden)
                        .Append(", connectGroup ").Append(n.m_connectGroup).Append('\n');
                }
            }
        }

        private static int AppendVehicles(StringBuilder sb, Vector3 c)
        {
            Vehicle[] vs = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            int n = 0;
            for (int i = 1; i < vs.Length; i++)
            {
                if ((vs[i].m_flags & Vehicle.Flags.Created) == 0 || (vs[i].m_flags & Vehicle.Flags.Underground) == 0) continue;
                Vector3 p = vs[i].GetLastFramePosition();
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude > Radius * Radius) continue;
                VehicleInfo info = vs[i].Info;
                n++;
                sb.Append("underground vehicle ").Append(i).Append(" '").Append(info == null ? "?" : info.name).Append("' at (").Append(Fmt(p))
                    .Append("), undergroundMaterial ").Append(info == null ? "?" : MaterialText(info.m_undergroundMaterial))
                    .Append(", undergroundLodMaterial ").Append(info == null ? "?" : MaterialText(info.m_undergroundLodMaterial)).Append('\n');
            }
            return n;
        }

        // Owner, 2026-10-06: "like a ghost town" with traffic and voices audible. Counts what the simulation has near the
        // camera (all vehicles and citizen instances within Radius) and whether it is paused.
        private static void AppendLife(StringBuilder sb, Vector3 c)
        {
            SimulationManager sim = Singleton<SimulationManager>.instance;
            sb.Append("simulation: paused ").Append(sim.SimulationPaused).Append(", forced paused ").Append(sim.ForcedSimulationPaused)
                .Append(", speed ").Append(sim.SelectedSimulationSpeed).Append(", frame ").Append(sim.m_currentFrameIndex)
                .Append(", reference frame ").Append(sim.m_referenceFrameIndex).Append('\n');
            RenderManager.CameraInfo ci = Singleton<RenderManager>.instance.CurrentCameraInfo;
            if (ci != null)
                sb.Append("render camera info: layer mask 0x").Append(ci.m_layerMask.ToString("X8")).Append(" (").Append(MaskNames(ci.m_layerMask))
                    .Append("), height ").Append(ci.m_height.ToString("0")).Append(", near ").Append(ci.m_near.ToString("0.00")).Append(", far ")
                    .Append(ci.m_far.ToString("0")).Append(", position (").Append(Fmt(ci.m_position)).Append("), level of detail factor ")
                    .Append(RenderManager.LevelOfDetailFactor.ToString("0.00")).Append('\n');
            Vehicle[] vs = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            int created = 0, spawned = 0, under = 0, shown = 0;
            for (int i = 1; i < vs.Length; i++)
            {
                if ((vs[i].m_flags & Vehicle.Flags.Created) == 0) continue;
                Vector3 p = vs[i].GetLastFramePosition();
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude > Radius * Radius) continue;
                created++;
                if ((vs[i].m_flags & Vehicle.Flags.Spawned) != 0) spawned++;
                if ((vs[i].m_flags & Vehicle.Flags.Underground) != 0) under++;
                if (shown++ < 5)
                {
                    VehicleInfo info = vs[i].Info;
                    sb.Append("vehicle ").Append(i).Append(" '").Append(info == null ? "?" : info.name).Append("' at (").Append(Fmt(p))
                        .Append("), flags ").Append(vs[i].m_flags).Append(", prefab data layer ").Append(info == null ? "?" : LayerText(info.m_prefabDataLayer))
                        .Append(", maxRenderDistance ").Append(info == null ? 0f : info.m_maxRenderDistance)
                        .Append(", material ").Append(info == null ? "?" : MaterialText(info.m_material)).Append('\n');
                }
            }
            VehicleParked[] parked = Singleton<VehicleManager>.instance.m_parkedVehicles.m_buffer;
            int parkedShown = 0;
            for (int i = 1; i < parked.Length && parkedShown < 10; i++)
            {
                if ((parked[i].m_flags & (ushort)VehicleParked.Flags.Created) == 0) continue;
                Vector3 p = parked[i].m_position;
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude > 20f * 20f) continue;
                parkedShown++;
                VehicleInfo info = parked[i].Info;
                Vector3 size = info == null || info.m_generatedInfo == null ? Vector3.zero : info.m_generatedInfo.m_size;
                sb.Append("parked vehicle ").Append(i).Append(" '").Append(info == null ? "?" : info.name).Append("' at (").Append(Fmt(p))
                    .Append("), heading ").Append(parked[i].m_rotation.eulerAngles.y.ToString("0")).Append(" deg, tilt ")
                    .Append(parked[i].m_rotation.eulerAngles.x.ToString("0")).Append("/").Append(parked[i].m_rotation.eulerAngles.z.ToString("0"))
                    .Append(", generated size (").Append(Fmt(size)).Append("), mesh bounds ").Append(info == null || info.m_mesh == null ? "?" : info.m_mesh.bounds.ToString()).Append('\n');
            }
            sb.Append("vehicles within ").Append(Radius).Append(" m: ").Append(created).Append(" created, ").Append(spawned).Append(" spawned, ")
                .Append(under).Append(" underground\n");
            CitizenInstance[] cs = Singleton<CitizenManager>.instance.m_instances.m_buffer;
            int people = 0, character = 0, inside = 0, below = 0;
            shown = 0;
            for (int i = 1; i < cs.Length; i++)
            {
                if ((cs[i].m_flags & CitizenInstance.Flags.Created) == 0) continue;
                CitizenInstance.Frame f = cs[i].GetLastFrameData();
                Vector3 p = f.m_position;
                if (new Vector2(p.x - c.x, p.z - c.z).sqrMagnitude > Radius * Radius) continue;
                people++;
                if ((cs[i].m_flags & CitizenInstance.Flags.Character) != 0) character++;
                if (f.m_insideBuilding) inside++;
                if (f.m_underground) below++;
                if (shown++ < 5)
                {
                    CitizenInfo info = cs[i].Info;
                    sb.Append("citizen instance ").Append(i).Append(" '").Append(info == null ? "?" : info.name).Append("' at (").Append(Fmt(p))
                        .Append("), flags ").Append(cs[i].m_flags).Append(", prefab data layer ").Append(info == null ? "?" : LayerText(info.m_prefabDataLayer))
                        .Append(", inside building ").Append(f.m_insideBuilding)
                        .Append(", lodRenderDistance ").Append(info == null ? 0f : info.m_lodRenderDistance)
                        .Append(", maxRenderDistance ").Append(info == null ? 0f : info.m_maxRenderDistance).Append('\n');
                }
            }
            sb.Append("citizen instances within ").Append(Radius).Append(" m: ").Append(people).Append(" created, ").Append(character)
                .Append(" with a character, ").Append(inside).Append(" inside buildings, ").Append(below).Append(" underground\n");
        }

        // Owner, 2026-10-06: brick paving in front of houses vanishes when walked on. Lists decal props within 25 m with their
        // mesh height and whether the eye is inside their box.
        private static void AppendDecals(StringBuilder sb, Vector3 eye)
        {
            PropInstance[] ps = Singleton<PropManager>.instance.m_props.m_buffer;
            int n = 0;
            for (int i = 1; i < ps.Length && n < 20; i++)
            {
                if ((ps[i].m_flags & 1) == 0) continue;
                PropInfo info = ps[i].Info;
                if (info == null || !info.m_isDecal) continue;
                Vector3 p = ps[i].Position;
                if (new Vector2(p.x - eye.x, p.z - eye.z).sqrMagnitude > 25f * 25f) continue;
                n++;
                AppendDecal(sb, "decal prop " + i, info, p);
            }
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Building[] bs = bm.m_buildings.m_buffer;
            for (int i = 1; i < bs.Length && n < 40; i++)
            {
                if ((bs[i].m_flags & Building.Flags.Created) == 0) continue;
                BuildingInfo info = bs[i].Info;
                if (info == null || info.m_props == null) continue;
                Vector3 bp = bs[i].m_position;
                if (new Vector2(bp.x - eye.x, bp.z - eye.z).sqrMagnitude > 60f * 60f) continue;
                foreach (BuildingInfo.Prop prop in info.m_props)
                {
                    PropInfo pi = prop.m_finalProp;
                    if (pi == null || !pi.m_isDecal) continue;
                    Vector3 p = bs[i].CalculatePosition(prop.m_position);
                    if (new Vector2(p.x - eye.x, p.z - eye.z).sqrMagnitude > 25f * 25f) continue;
                    if (n++ >= 40) break;
                    AppendDecal(sb, "building " + i + " decal", pi, p);
                }
            }
            sb.Append("decals within 25 m listed: ").Append(n).Append('\n');
        }

        // Owner, 2026-10-06: trash cans outside houses have no collision. Every prop the collision builder considers within
        // 8 m of the eye, with its outcome (collides, too flat, decal, marker...).
        private static void AppendProps(StringBuilder sb, Vector3 eye)
        {
            var obstacles = new ObstacleGeometry();
            var lines = new List<string>();
            obstacles.Trace = (name, p, outcome) =>
            {
                if (new Vector2(p.x - eye.x, p.z - eye.z).sqrMagnitude <= 8f * 8f && lines.Count < 60)
                    lines.Add("prop '" + name + "' at (" + Fmt(p) + "): " + outcome);
            };
            try { obstacles.Emit(eye.x - 8f, eye.z - 8f, eye.x + 8f, eye.z + 8f, new TriangleBuffer()); }
            catch (Exception e) { lines.Add("prop scan failed: " + e.GetType().Name + ": " + e.Message); }
            foreach (string l in lines) sb.Append(l).Append('\n');
            sb.Append("props within 8 m considered for collision: ").Append(lines.Count).Append('\n');
        }

        private static void AppendDecal(StringBuilder sb, string what, PropInfo info, Vector3 p)
        {
            Mesh m = info.m_mesh;
            Bounds b = m != null ? m.bounds : new Bounds();
            sb.Append(what).Append(" '").Append(info.name).Append("' at (").Append(Fmt(p)).Append("), mesh y ")
                .Append(b.min.y.ToString("0.00")).Append("..").Append(b.max.y.ToString("0.00")).Append(", size x ").Append(b.size.x.ToString("0.0"))
                .Append(" z ").Append(b.size.z.ToString("0.0")).Append(", material ").Append(MaterialText(info.m_material))
                .Append(", queue ").Append(info.m_material == null ? 0 : info.m_material.renderQueue).Append('\n');
        }

        private static string MaskText(Camera cam)
        {
            return "'" + cam.name + "' cullingMask 0x" + cam.cullingMask.ToString("X8") + " (" + MaskNames(cam.cullingMask) + ")";
        }

        private static string MaskNames(int mask)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(LayerText(i));
            }
            return sb.ToString();
        }

        private static string LayerText(int layer)
        {
            string name = layer >= 0 && layer < 32 ? LayerMask.LayerToName(layer) : null;
            return layer + (string.IsNullOrEmpty(name) ? "" : ":" + name);
        }

        // Triangle counts only from readable meshes: reading a non-readable mesh's arrays logs a Unity error.
        private static string MeshText(Mesh m)
        {
            if (m == null) return "none";
            return "'" + m.name + "' " + m.vertexCount + " verts, " + (m.isReadable ? (m.triangles.Length / 3) + " tris" : "tris n/a (not readable)");
        }

        private static string MaterialText(Material m)
        {
            if (m == null) return "none";
            return "'" + m.name + "' shader '" + (m.shader == null ? "none" : m.shader.name) + "'";
        }

        private static string Fmt(Vector3 v)
        {
            return v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0");
        }
    }
}
