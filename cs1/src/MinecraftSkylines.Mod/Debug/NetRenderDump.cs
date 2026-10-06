using System;
using System.Collections.Generic;
using System.Text;
using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using Skylines.Host;
using UnityEngine;
using UInput = UnityEngine.Input;

namespace MinecraftSkylines.Mod.Diagnostics
{
    /// <summary>
    /// Ctrl+Shift+D (city camera and Minecraft mode): logs how the game renders the network around the camera, to find out
    /// how tunnels are drawn. Every segment and node within <see cref="Radius"/> m with its NetInfo's segment and node
    /// meshes, materials, shaders, render layers and flag filters; the main and UndergroundView camera culling masks; and
    /// underground vehicles with their underground material. One delimited block in the mod log per press; nothing else
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
            int vehicles = AppendVehicles(sb, c);
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
                .Append(", flattenTerrain ").Append(info.m_flattenTerrain).Append('\n');
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
