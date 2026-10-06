using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using Skylines.Core.Geometry;
using Skylines.Host.Geometry;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>
    /// Draws road tunnels near a point as a real road tunnel instead of CS1's underground x-ray look: the road surface is
    /// the matching ground road's own segment meshes and materials, fed the tunnel segment's bezier parameters exactly as
    /// <c>NetSegment.RenderInstance</c> / <c>NetNode.RefreshBendData</c> feed the net shader; walls and ceiling are one
    /// generated mesh (<see cref="TunnelShell"/>) along the collision tube's edges and clearance, textured with a concrete
    /// texture taken from CS1's own bridge pillar material. Everything is drawn with <c>Graphics.DrawMesh</c> on the ground
    /// road's layer, so the main camera's normal lighting applies. Call <see cref="Draw"/> once per frame; nothing of CS1's
    /// is changed. <see cref="Release"/> destroys only what this class created. Main thread only.
    /// </summary>
    public sealed class TunnelInteriorRenderer
    {
        /// <summary>Tunnel parts within this xz distance of the eye are drawn, in metres.</summary>
        public const float Radius = 240f;
        /// <summary>Concrete texture repeat, in metres.</summary>
        public const float TileMetres = 8f;
        /// <summary>
        /// The road on a slope is drawn this far lower so CS1's own ramp surface, where the slope mesh has one, wins the
        /// depth test instead of flickering against it, in metres.
        /// </summary>
        public const float SlopeDrop = 0.05f;

        private const int MaxVertices = 60000;
        private const float GridCell = 64f;       // NetManager.InitializeSegment: (int)(x / 64f + 135f)
        private const int GridSize = 270;         // NetManager.NODEGRID_RESOLUTION
        private const float GridMargin = 256f;
        private const uint SegmentHolder = 49152; // NetSegment.RenderInstance: RenderManager instance holder 49152 + segment
        private const uint NodeHolder = 86016;    // NetNode: 86016 + node

        private readonly HostLog _log;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly ShellMesh _shell = new ShellMesh();
        private readonly HashSet<ushort> _nodes = new HashSet<ushort>();
        private readonly HashSet<NetInfo> _warned = new HashSet<NetInfo>();
        private readonly List<TunnelSection> _sections = new List<TunnelSection>();
        private readonly List<TunnelSection> _mouths = new List<TunnelSection>();
        private Mesh _mesh;
        private Material _concrete;
        private Texture2D _ownTexture, _xys, _aci;
        private bool _materialTried, _capWarned;
        private long _signature;
        private int _layer = -1;

        /// <summary>Creates a renderer that logs to <paramref name="log"/>; nothing is created until the first draw.</summary>
        public TunnelInteriorRenderer(HostLog log)
        {
            _log = log;
        }

        /// <summary>Tunnel and slope segments drawn in the last <see cref="Draw"/>.</summary>
        public int LastSegments { get; private set; }
        /// <summary>Ground road mesh draws issued in the last <see cref="Draw"/> (segments and bend nodes).</summary>
        public int LastRoadDraws { get; private set; }
        /// <summary>Vertices of the current wall and ceiling mesh.</summary>
        public int ShellVertices { get; private set; }
        /// <summary>Where the wall texture came from, for the log and the overlay.</summary>
        public string TextureSource { get; private set; }

        /// <summary>Draws every road tunnel and slope segment, and every underground bend and junction, near <paramref name="eye"/>.</summary>
        public void Draw(Vector3 eye)
        {
            if (!NetManager.exists) return;
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            NetNode[] nodes = nm.m_nodes.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            _nodes.Clear();
            _shell.Clear();
            LastSegments = 0;
            LastRoadDraws = 0;
            long sig = 17;
            for (int cz = Cell(eye.z - Radius - GridMargin); cz <= Cell(eye.z + Radius + GridMargin); cz++)
            {
                for (int cx = Cell(eye.x - Radius - GridMargin); cx <= Cell(eye.x + Radius + GridMargin); cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        if (Near(ref segs[id], eye)) sig = DrawSegment(id, ref segs[id], sig);
                        id = segs[id].m_nextGridSegment;
                    }
                }
            }
            foreach (ushort n in _nodes) sig = DrawNode(n, ref nodes[n], segs, sig);
            if (sig != _signature || _mesh == null)
            {
                _signature = sig;
                Upload();
            }
            Material m = Concrete();
            if (m != null && _mesh != null && ShellVertices > 0 && _layer >= 0)
            {
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, m, _layer);
            }
        }

        /// <summary>Destroys the mesh, material and textures this class created (never CS1's). Idempotent, never throws.</summary>
        public void Release()
        {
            try
            {
                TextureUtil.Replace(ref _mesh, null);
                TextureUtil.Replace(ref _concrete, null);
                TextureUtil.Replace(ref _ownTexture, null);
                TextureUtil.Replace(ref _xys, null);
                TextureUtil.Replace(ref _aci, null);
            }
            catch (Exception e)
            {
                _log.Error("tunnels: release", e);
            }
            _materialTried = false;
            _signature = 0;
            ShellVertices = 0;
            _shell.Clear();
        }

        private static int Cell(float v)
        {
            return Mathf.Clamp((int)(v / GridCell + 135f), 0, GridSize - 1);
        }

        private static bool Near(ref NetSegment seg, Vector3 eye)
        {
            if ((seg.m_flags & NetSegment.Flags.Created) == 0 || (seg.m_flags & NetSegment.Flags.Deleted) != 0) return false;
            Bounds b = seg.m_bounds;
            float dx = Mathf.Max(0f, Mathf.Max(b.min.x - eye.x, eye.x - b.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(b.min.z - eye.z, eye.z - b.max.z));
            return dx * dx + dz * dz <= Radius * Radius;
        }

        private long DrawSegment(ushort id, ref NetSegment seg, long sig)
        {
            NetInfo info = seg.Info;
            NetGeometry.Kind kind = NetGeometry.Classify(info);
            if (kind != NetGeometry.Kind.Tunnel && kind != NetGeometry.Kind.Slope) return sig;
            LastSegments++;
            bool slope = kind == NetGeometry.Kind.Slope;
            NetInfo ground = Ground(info);
            if (ground != null) SegmentRoad(id, ref seg, ground, slope);

            Bezier3 left, right;
            seg.GenerateBezier(id, seg.m_startNode, out left, out right);
            sig = Mix(Mix(Mix(sig, id), left), right);
            AddShell(ToCore(left), ToCore(right), NetGeometry.Clearance(info), !slope);
            _nodes.Add(seg.m_startNode);
            _nodes.Add(seg.m_endNode);
            return sig;
        }

        private NetInfo Ground(NetInfo info)
        {
            NetInfo ground = NetGeometry.GroundInfoOf(info);
            if (ground == null && _warned.Add(info)) _log.Warn("tunnels: no ground road names '" + info.name + "' as its tunnel or slope; its road surface is not drawn");
            return ground;
        }

        // NetSegment.RenderInstance (dirty branch) and RenderSegments, with the ground road's meshes and shape constants.
        private void SegmentRoad(ushort id, ref NetSegment seg, NetInfo ground, bool slope)
        {
            NetNode[] nodes = Singleton<NetManager>.instance.m_nodes.m_buffer;
            Vector3 pos = (nodes[seg.m_startNode].m_position + nodes[seg.m_endNode].m_position) * 0.5f;
            float vScale = ground.m_netAI.GetVScale();
            Vector3 c1, d1, c2, d2, c3, d3, c4, d4, m1, m2, m3, m4;
            bool s1, s2;
            seg.CalculateCorner(id, true, true, true, out c1, out d1, out s1);
            seg.CalculateCorner(id, true, false, true, out c2, out d2, out s2);
            seg.CalculateCorner(id, true, true, false, out c3, out d3, out s1);
            seg.CalculateCorner(id, true, false, false, out c4, out d4, out s2);
            NetSegment.CalculateMiddlePoints(c1, d1, c4, d4, s1, s2, out m1, out m2);
            NetSegment.CalculateMiddlePoints(c3, d3, c2, d2, s1, s2, out m3, out m4);
            Matrix4x4 leftM = NetSegment.CalculateControlMatrix(c1, m1, m2, c4, c3, m3, m4, c2, pos, vScale);
            Matrix4x4 rightM = NetSegment.CalculateControlMatrix(c3, m3, m4, c2, c1, m1, m2, c4, pos, vScale);
            Vector4 a = RenderManager.GetColorLocation(SegmentHolder + id), b = a;
            if (NetNode.BlendJunction(seg.m_startNode)) a = RenderManager.GetColorLocation(NodeHolder + seg.m_startNode);
            if (NetNode.BlendJunction(seg.m_endNode)) b = RenderManager.GetColorLocation(NodeHolder + seg.m_endNode);
            var objectIndex = new Vector4(a.x, a.y, b.x, b.y);
            Color objectColor = ground.m_netAI.GetObjectColor(id, ref seg);
            if (slope) pos.y -= SlopeDrop;

            bool drawn = false;
            foreach (NetInfo.Segment s in ground.m_segments ?? new NetInfo.Segment[0])
            {
                bool turnAround;
                if (!s.CheckFlags(seg.m_flags, seg.m_flags2, out turnAround)) continue;
                if (ground.m_netAI.CanAutoRenderInverted()) turnAround = (seg.m_flags & NetSegment.Flags.Invert) != 0;
                drawn |= RoadMesh(s, ground, pos, leftM, rightM, objectIndex, objectColor, turnAround);
            }
            if (!drawn && _warned.Add(ground)) _log.Warn("tunnels: no segment mesh of '" + ground.name + "' matches tunnel segment flags " + seg.m_flags);
        }

        // NetNode.RefreshBendData and NetNode.RenderSegments for the joint piece between two tunnel or slope segments.
        private void JointRoad(ushort nodeId, ref NetNode node, Bezier3 l, Bezier3 r, NetInfo ground)
        {
            Vector3 pos = node.m_position;
            float vScale = ground.m_netAI.GetVScale();
            Matrix4x4 leftM = NetSegment.CalculateControlMatrix(l.a, l.b, l.c, l.d, r.a, r.b, r.c, r.d, pos, vScale);
            Matrix4x4 rightM = NetSegment.CalculateControlMatrix(r.a, r.b, r.c, r.d, l.a, l.b, l.c, l.d, pos, vScale);
            Vector4 loc = RenderManager.GetColorLocation(NodeHolder + nodeId);
            var objectIndex = new Vector4(loc.x, loc.y, loc.x, loc.y);
            Color objectColor = ground.m_netAI.GetObjectColor(nodeId, ref node);
            NetSegment.Flags flags = ground.m_netAI.GetBendFlags(nodeId, ref node);
            NetSegment.Flags2 events = ground.m_netAI.GetEventFlags(nodeId, ref node);
            foreach (NetInfo.Segment s in ground.m_segments ?? new NetInfo.Segment[0])
            {
                bool turnAround;
                if (!s.CheckFlags(flags, events, out turnAround) || s.m_disableBendNodes) continue;
                RoadMesh(s, ground, pos, leftM, rightM, objectIndex, objectColor, turnAround);
            }
        }

        private bool RoadMesh(NetInfo.Segment s, NetInfo ground, Vector3 pos, Matrix4x4 leftM, Matrix4x4 rightM, Vector4 objectIndex, Color objectColor, bool turnAround)
        {
            if (s.m_segmentMesh == null || s.m_segmentMaterial == null || s.m_requireHeightMap) return false;
            NetManager nm = Singleton<NetManager>.instance;
            var scale = new Vector4(0.5f / ground.m_halfWidth, 1f / ground.m_segmentLength, 1f, 1f);
            if (turnAround)
            {
                scale.x = -scale.x;
                scale.y = -scale.y;
            }
            Color color = ground.m_color;
            color.a = 0f;
            _block.Clear();
            _block.SetMatrix(nm.ID_LeftMatrix, leftM);
            _block.SetMatrix(nm.ID_RightMatrix, rightM);
            _block.SetVector(nm.ID_MeshScale, scale);
            _block.SetVector(nm.ID_ObjectIndex, objectIndex);
            _block.SetColor(nm.ID_Color, color);
            _block.SetColor(nm.ID_ObjectColor, objectColor);
            if (s.m_requireSurfaceMaps && TerrainManager.exists)
            {
                Texture texA, texB;
                Vector4 mapping;
                Singleton<TerrainManager>.instance.GetSurfaceMapping(pos, out texA, out texB, out mapping);
                if (texA != null)
                {
                    _block.SetTexture(nm.ID_SurfaceTexA, texA);
                    _block.SetTexture(nm.ID_SurfaceTexB, texB);
                    _block.SetVector(nm.ID_SurfaceMapping, mapping);
                }
            }
            Graphics.DrawMesh(s.m_segmentMesh, pos, Quaternion.identity, s.m_segmentMaterial, s.m_layer, null, 0, _block);
            if (_layer < 0) _layer = s.m_layer;
            LastRoadDraws++;
            return true;
        }

        private long DrawNode(ushort nodeId, ref NetNode node, NetSegment[] segs, long sig)
        {
            if ((node.m_flags & NetNode.Flags.Underground) == 0 || (node.m_flags & NetNode.Flags.Middle) != 0) return sig;
            float clearance;
            if (NetGeometry.UndergroundJoint(nodeId, ref node, segs, out clearance))
            {
                Bezier3 l, r;
                NetGeometry.JointEdges(nodeId, ref node, segs, out l, out r);
                sig = Mix(Mix(Mix(sig, nodeId), l), r);
                NetInfo ground = Ground(TunnelInfoAt(ref node, segs));
                if (ground != null) JointRoad(nodeId, ref node, l, r, ground);
                AddShell(ToCore(l), ToCore(r), clearance, true);
                return sig;
            }
            if ((node.m_flags & NetNode.Flags.Junction) == 0) return sig;
            _mouths.Clear();
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                NetInfo info = segs[sid].Info;
                NetGeometry.Kind kind = NetGeometry.Classify(info);
                if (kind != NetGeometry.Kind.Tunnel && kind != NetGeometry.Kind.Slope) return sig;
                Bezier3 l, r;
                segs[sid].GenerateBezier(sid, nodeId, out l, out r);
                sig = Mix(Mix(sig, l), r);
                if (NetGeometry.Profile(ToCore(l), ToCore(r), NetGeometry.Clearance(info), kind == NetGeometry.Kind.Tunnel, _sections) > 0)
                {
                    _mouths.Add(_sections[0]);
                }
            }
            sig = Mix(sig, nodeId);
            if (_shell.VertexCount < MaxVertices)
            {
                Vector3 p = node.m_position;
                TunnelShell.Junction(p.x, p.z, _mouths, NetGeometry.CeilingThickness, TileMetres, _shell);
            }
            return sig;
        }

        // The tunnel info among the node's segments, else its slope's: the ground road surface drawn on a joint.
        private static NetInfo TunnelInfoAt(ref NetNode node, NetSegment[] segs)
        {
            NetInfo found = null;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                NetInfo info = segs[sid].Info;
                if (found == null || NetGeometry.Classify(info) == NetGeometry.Kind.Tunnel) found = info;
            }
            return found;
        }

        private void AddShell(Bezier3D left, Bezier3D right, float clearance, bool tunnel)
        {
            if (_shell.VertexCount >= MaxVertices)
            {
                if (!_capWarned) _log.Warn("tunnels: wall mesh reached " + MaxVertices + " vertices; farther tunnel parts get no walls");
                _capWarned = true;
                return;
            }
            NetGeometry.Profile(left, right, clearance, tunnel, _sections);
            TunnelShell.Segment(_sections, NetGeometry.CeilingThickness, TileMetres, _shell);
        }

        private void Upload()
        {
            int n = _shell.VertexCount;
            ShellVertices = n;
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "MinecraftSkylines.TunnelShell", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            if (n == 0) return;
            var v = new Vector3[n];
            var nr = new Vector3[n];
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(_shell.Positions[3 * i], _shell.Positions[3 * i + 1], _shell.Positions[3 * i + 2]);
                nr[i] = new Vector3(_shell.Normals[3 * i], _shell.Normals[3 * i + 1], _shell.Normals[3 * i + 2]);
                uv[i] = new Vector2(_shell.Uvs[2 * i], _shell.Uvs[2 * i + 1]);
            }
            _mesh.vertices = v;
            _mesh.normals = nr;
            _mesh.uv = uv;
            _mesh.triangles = _shell.Indices.ToArray();
            _mesh.RecalculateBounds();
        }

        // The prop shader the block meshes already use (lit by CS1's scene lighting) with neutral XYS/ACI maps, and a
        // concrete texture from CS1: a bridge pillar's main texture, else a tunnel slope's, else flat grey.
        private Material Concrete()
        {
            if (_materialTried) return _concrete;
            _materialTried = true;
            Shader shader = Shader.Find("Custom/Props/Prop/Default") ?? Shader.Find("Diffuse");
            if (shader == null)
            {
                _log.Warn("tunnels: no prop or Diffuse shader; walls and ceiling are not drawn");
                return null;
            }
            Texture tex = FindConcrete();
            if (tex == null)
            {
                _ownTexture = TextureUtil.Solid(new Color32(150, 148, 142, 255), false, "MinecraftSkylines.TunnelConcrete");
                _ownTexture.wrapMode = TextureWrapMode.Repeat;
                tex = _ownTexture;
                TextureSource = "flat grey (no CS1 texture found)";
            }
            _concrete = new Material(shader) { name = "MinecraftSkylines.TunnelConcrete", hideFlags = HideFlags.HideAndDontSave };
            _concrete.SetTexture("_MainTex", tex);
            _concrete.SetColor("_Color", Color.white);
            _xys = TextureUtil.Solid(new Color32(128, 128, 255, 255), true, "MinecraftSkylines.TunnelXYS");
            _aci = TextureUtil.Solid(new Color32(0, 0, 0, 255), true, "MinecraftSkylines.TunnelACI");
            _concrete.SetTexture("_XYSMap", _xys);
            _concrete.SetTexture("_ACIMap", _aci);
            _log.Info("tunnels: walls use shader " + shader.name + " and texture " + TextureSource);
            return _concrete;
        }

        private Texture FindConcrete()
        {
            int n = PrefabCollection<NetInfo>.LoadedCount();
            Texture slope = null;
            string slopeName = null;
            for (uint i = 0; i < n; i++)
            {
                NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                if (info == null) continue;
                RoadBridgeAI bridge = info.m_netAI as RoadBridgeAI;
                BuildingInfo pillar = bridge == null ? null : bridge.m_bridgePillarInfo;
                if (pillar != null && pillar.m_material != null && pillar.m_material.mainTexture != null)
                {
                    TextureSource = "bridge pillar '" + pillar.name + "' of '" + info.name + "'";
                    return pillar.m_material.mainTexture;
                }
                if (slope == null && NetGeometry.Classify(info) == NetGeometry.Kind.Slope && info.m_segments != null)
                {
                    foreach (NetInfo.Segment s in info.m_segments)
                    {
                        if (s.m_material != null && s.m_material.mainTexture != null && s.m_layer != LayerMask.NameToLayer("MetroTunnels"))
                        {
                            slope = s.m_material.mainTexture;
                            slopeName = "slope '" + info.name + "'";
                            break;
                        }
                    }
                }
            }
            if (slope != null) TextureSource = slopeName;
            return slope;
        }

        private static Bezier3D ToCore(Bezier3 c)
        {
            return new Bezier3D
            {
                Ax = c.a.x, Ay = c.a.y, Az = c.a.z, Bx = c.b.x, By = c.b.y, Bz = c.b.z,
                Cx = c.c.x, Cy = c.c.y, Cz = c.c.z, Dx = c.d.x, Dy = c.d.y, Dz = c.d.z,
            };
        }

        private static long Mix(long h, float v)
        {
            return h * 1000003L ^ BitConverter.ToInt32(BitConverter.GetBytes(v), 0);
        }

        private static long Mix(long h, Bezier3 c)
        {
            return Mix(Mix(Mix(Mix(Mix(Mix(h, c.a.x), c.a.y), c.a.z), c.d.x), c.d.y), c.d.z);
        }
    }
}
