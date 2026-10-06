using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Road, rail and path surfaces of the network overlapping a world-space rectangle, as collision triangles in CS1
    /// coordinates, built from the edges the game draws (<c>NetSegment.GenerateBezier</c>). Ground segments are 1 m
    /// slabs whose top is the drawn surface; bridge and elevated segments are 1 m decks (walkable underneath) with a
    /// railing along each edge; raised pavements are slabs at the pedestrian lanes' height. Road tunnels are tubes
    /// (floor, side walls, ceiling, open ends); tunnel slopes get the floor, walls up to the portal's ground level and
    /// a ceiling where the slope is deep enough to be roofed, plus the portal structure above the road from the slope's own
    /// mesh (<see cref="PortalShapes.EmitCollision"/>). Other underground segments are skipped. Main thread only.
    /// </summary>
    public sealed class NetGeometry
    {
        /// <summary>Flags value of a ground road surface triangle (bit 1).</summary>
        public const ushort RoadSurfaceFlag = 1 << 1;
        /// <summary>Flags value of a bridge or elevated deck triangle (bit 2).</summary>
        public const ushort BridgeDeckFlag = 1 << 2;
        /// <summary>Deck thickness in metres.</summary>
        public const float DeckThickness = 1.0f;

        /// <summary>
        /// Ground roads are emitted as slabs this deep, not as zero-thickness ribbons: a guest's step-up
        /// logic (Minecraft's included) only climbs onto a surface after bumping into its side, so a
        /// sideless ribbon a few centimetres above the terrain lets the player walk underneath it
        /// (owner's M2 test, 2026-10-05). The bottom face sits below ground, where nobody reaches it.
        /// </summary>
        public const float GroundRoadDepth = 1.0f;

        /// <summary>Flags value of a bridge railing triangle (bit 4).</summary>
        public const ushort RailingFlag = 1 << 4;
        /// <summary>Flags value of a tunnel wall or ceiling triangle (bit 5).</summary>
        public const ushort TunnelFlag = 1 << 5;
        /// <summary>Tunnel wall width and ceiling thickness, in metres.</summary>
        public const float TunnelWallWidth = 0.5f, CeilingThickness = 1f;

        /// <summary>Railing height above the deck and width, in metres.</summary>
        public const float RailingHeight = 1.0f, RailingWidth = 0.2f;

        private const float StripStep = 4f;
        private const int DiscSegments = 16;
        private const float GridCell = 64f;       // NetManager.InitializeSegment: (int)(x / 64f + 135f)
        private const int GridSize = 270;         // NetManager.NODEGRID_RESOLUTION
        private const float GridMargin = 256f;    // segments are filed under their midpoint; searched with slack, then bounds-tested

        /// <summary>How a segment is exported: not at all, as a ground slab, or as a bridge deck.</summary>
        public enum Kind
        {
            /// <summary>Not exported (power lines, pipes, underground).</summary>
            Skip,
            /// <summary>A ground slab.</summary>
            Ground,
            /// <summary>A bridge or elevated deck.</summary>
            Bridge,
            /// <summary>A road tunnel: floor, walls and ceiling.</summary>
            Tunnel,
            /// <summary>A road tunnel slope (portal ramp): floor, walls to ground level, ceiling over its deep part.</summary>
            Slope,
        }

        // NetInfos that RoadAI prefabs name as their elevated/bridge or slope variant (RoadAI.cs:12-18), rebuilt when the
        // loaded prefab count changes.
        private static readonly HashSet<NetInfo> s_elevatedInfos = new HashSet<NetInfo>();
        private static readonly HashSet<NetInfo> s_slopeInfos = new HashSet<NetInfo>();
        private static readonly Dictionary<NetInfo, NetInfo> s_groundOf = new Dictionary<NetInfo, NetInfo>();
        private static int s_infoCount = -1;

        private readonly HashSet<ushort> _nodes = new HashSet<ushort>();
        private readonly float[] _ring = new float[3 * 16];
        private readonly List<TunnelSection> _sections = new List<TunnelSection>();
        private readonly float[] _roof = new float[3 * 16];
        private static readonly Dictionary<NetInfo, TunnelDims> s_dims = new Dictionary<NetInfo, TunnelDims>();

        /// <summary>Number of segments emitted by the last <see cref="Emit"/>.</summary>
        public int LastSegmentCount { get; private set; }

        /// <summary>Appends the surfaces overlapping the rectangle to <paramref name="into"/>.</summary>
        public void Emit(float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            NetNode[] nodes = nm.m_nodes.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            _nodes.Clear();
            LastSegmentCount = 0;

            int cx0 = Cell(minX - GridMargin), cx1 = Cell(maxX + GridMargin);
            int cz0 = Cell(minZ - GridMargin), cz1 = Cell(maxZ + GridMargin);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        EmitSegment(id, ref segs[id], nodes, minX, minZ, maxX, maxZ, into);
                        id = segs[id].m_nextGridSegment;
                    }
                }
            }

            foreach (ushort n in _nodes) EmitJunction(n, segs, nodes, minX, minZ, maxX, maxZ, into);
        }

        /// <summary>How far past a tunnel slope's drawn edges its terrain hole may reach, in metres.</summary>
        public const float PortalMargin = 2f;

        private readonly StripFootprint _portals = new StripFootprint(PortalMargin);

        /// <summary>
        /// The xz footprint, grown by <see cref="PortalMargin"/>, of every tunnel slope (portal ramp, <see cref="Kind.Slope"/>)
        /// segment near the rectangle: the only places where terrain the game clipped is left out of the collision.
        /// Ordinary roads and buildings clip the terrain under them too, but their own collision does not cover the whole
        /// cut on bends and embankments. The returned instance is reused by the next call.
        /// </summary>
        public StripFootprint PortalFootprint(float minX, float minZ, float maxX, float maxZ)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            _portals.Clear();
            float x0 = minX - PortalMargin, z0 = minZ - PortalMargin, x1 = maxX + PortalMargin, z1 = maxZ + PortalMargin;
            for (int cz = Cell(minZ - GridMargin); cz <= Cell(maxZ + GridMargin); cz++)
            {
                for (int cx = Cell(minX - GridMargin); cx <= Cell(maxX + GridMargin); cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        NetSegment seg = segs[id];
                        Bounds b = seg.m_bounds;
                        if ((seg.m_flags & NetSegment.Flags.Created) != 0 && (seg.m_flags & NetSegment.Flags.Deleted) == 0
                            && !(b.max.x < x0 || b.min.x > x1 || b.max.z < z0 || b.min.z > z1) && Classify(seg.Info) == Kind.Slope)
                        {
                            Bezier3 left, right;
                            seg.GenerateBezier(id, seg.m_startNode, out left, out right);
                            float length = Vector3.Distance(left.a, left.d);
                            _portals.Add(ToCore(left, 0f), ToCore(right, 0f), Mathf.Max(1, Mathf.CeilToInt(length / StripStep)));
                        }
                        id = seg.m_nextGridSegment;
                    }
                }
            }
            return _portals;
        }

        private static int Cell(float v)
        {
            return Mathf.Clamp((int)(v / GridCell + 135f), 0, GridSize - 1);
        }

        /// <summary>How segments of <paramref name="info"/> are exported.</summary>
        public static Kind Classify(NetInfo info)
        {
            NetAI ai = info == null ? null : info.m_netAI;
            if (ai == null) return Kind.Skip;
            if (!(ai is RoadBaseAI || ai is PedestrianPathAI || ai is PedestrianBridgeAI || ai is PedestrianWayAI
                  || ai is TrainTrackBaseAI || ai is MetroTrackBaseAI)) return Kind.Skip;
            RefreshInfoRoles();
            // Slopes are RoadTunnelAI infos with m_flattenTerrain set (RoadTunnelAI.BuildUnderground, GetElevationLimits).
            if (s_slopeInfos.Contains(info)) return Kind.Slope;
            if (ai.IsUnderground())
            {
                if (!(ai is RoadBaseAI)) return Kind.Skip;
                return info.m_flattenTerrain ? Kind.Slope : Kind.Tunnel;
            }
            return ai.IsBridge() ? Kind.Bridge : Kind.Ground;
        }

        private static void RefreshInfoRoles()
        {
            int n = PrefabCollection<NetInfo>.LoadedCount();
            if (n == s_infoCount) return;
            s_infoCount = n;
            s_elevatedInfos.Clear();
            s_slopeInfos.Clear();
            s_groundOf.Clear();
            s_dims.Clear();
            for (uint i = 0; i < n; i++)
            {
                NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                RoadAI road = info == null ? null : info.m_netAI as RoadAI;
                if (road == null) continue;
                if (road.m_elevatedInfo != null) s_elevatedInfos.Add(road.m_elevatedInfo);
                if (road.m_bridgeInfo != null) s_elevatedInfos.Add(road.m_bridgeInfo);
                if (road.m_slopeInfo != null) s_slopeInfos.Add(road.m_slopeInfo);
                if (road.m_slopeInfo != null) s_groundOf[road.m_slopeInfo] = info;
                if (road.m_tunnelInfo != null) s_groundOf[road.m_tunnelInfo] = info;
            }
        }

        /// <summary>
        /// The ground road whose RoadAI names <paramref name="info"/> as its tunnel or slope variant (RoadAI.m_tunnelInfo,
        /// m_slopeInfo), or null.
        /// </summary>
        public static NetInfo GroundInfoOf(NetInfo info)
        {
            if (info == null) return null;
            RefreshInfoRoles();
            NetInfo ground;
            return s_groundOf.TryGetValue(info, out ground) ? ground : null;
        }

        /// <summary>
        /// The cross-section of tunnel or slope <paramref name="info"/>: measured from the portal mesh of its slope variant
        /// (itself, or its ground road's <c>RoadAI.m_slopeInfo</c>) in the mesh cache (<see cref="TunnelProfile.FromPortal"/>),
        /// else <see cref="TunnelProfile.Fallback"/> of its half-width. Cached per NetInfo until the loaded prefabs change.
        /// </summary>
        public static TunnelDims Dims(NetInfo info)
        {
            RefreshInfoRoles();
            TunnelDims d;
            if (s_dims.TryGetValue(info, out d)) return d;
            d = TunnelProfile.Fallback(info.m_halfWidth);
            NetInfo ground = GroundInfoOf(info);
            RoadAI road = ground == null ? null : ground.m_netAI as RoadAI;
            NetInfo slope = Classify(info) == Kind.Slope ? info : road == null ? null : road.m_slopeInfo;
            if (slope != null && slope.m_segments != null)
            {
                foreach (NetInfo.Segment s in slope.m_segments)
                {
                    PortalShapes.Shape shape;
                    TunnelDims found;
                    if (s == null || !PortalShapes.TryGet(s.m_segmentMesh, out shape)) continue;
                    if (!TunnelProfile.FromPortal(shape.Mesh.Positions, shape.Kept, slope.m_halfWidth, out found)) continue;
                    d = found;
                    break;
                }
            }
            s_dims[info] = d;
            return d;
        }

        /// <summary>How far the terrain cut stays inside a sunken road's pavements (or its edges), in metres.</summary>
        public const float CurbMargin = 0.25f;

        private readonly HashSet<ushort> _cutNodes = new HashSet<ushort>();

        /// <summary>
        /// Adds to <paramref name="cut"/> the carriageway of every ground road near the rectangle whose drawn surface lies
        /// below the terrain the game flattened for it (<c>m_clipTerrain</c>, <c>m_surfaceLevel</c> &lt; 0; Basic Road: 0.3 m),
        /// so the terrain there, which the game does not draw, can be left out of the collision and the player walks on
        /// the road with the pavements a step up (owner, 2026-10-06: "the sidewalks on my built roads are still the exact
        /// same walking height as the road"). Each area lies inside the road's own collision (segment strips inset by the
        /// pavement plus <see cref="CurbMargin"/>; junction fans shrunk towards the node), so no hole is opened.
        /// </summary>
        public void SunkenRoadCuts(float minX, float minZ, float maxX, float maxZ, ConvexCut cut)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            NetNode[] nodes = nm.m_nodes.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            _cutNodes.Clear();
            for (int cz = Cell(minZ - GridMargin); cz <= Cell(maxZ + GridMargin); cz++)
            {
                for (int cx = Cell(minX - GridMargin); cx <= Cell(maxX + GridMargin); cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        NetSegment seg = segs[id];
                        Bounds b = seg.m_bounds;
                        if ((seg.m_flags & NetSegment.Flags.Created) != 0 && (seg.m_flags & NetSegment.Flags.Deleted) == 0
                            && !(b.max.x < minX || b.min.x > maxX || b.max.z < minZ || b.min.z > maxZ) && Sunken(seg.Info))
                        {
                            Bezier3 left, right;
                            seg.GenerateBezier(id, seg.m_startNode, out left, out right);
                            Bezier3D l = ToCore(left, 0f), r = ToCore(right, 0f), il, ir;
                            float w = Mathf.Max(0f, seg.Info.m_pavementWidth) + CurbMargin;
                            if (Strip.Inset(l, r, w, 0f, out il) && Strip.Inset(r, l, w, 0f, out ir))
                            {
                                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(left.a, left.d) / StripStep));
                                float[] l0 = il.At(0f), r0 = ir.At(0f);
                                for (int k = 1; k <= n; k++)
                                {
                                    float[] l1 = il.At(k / (float)n), r1 = ir.At(k / (float)n);
                                    cut.AddTriangle(l0[0], l0[2], l1[0], l1[2], r1[0], r1[2]);
                                    cut.AddTriangle(l0[0], l0[2], r1[0], r1[2], r0[0], r0[2]);
                                    l0 = l1;
                                    r0 = r1;
                                }
                            }
                            _cutNodes.Add(seg.m_startNode);
                            _cutNodes.Add(seg.m_endNode);
                        }
                        id = seg.m_nextGridSegment;
                    }
                }
            }
            foreach (ushort n in _cutNodes) JunctionCut(n, segs, nodes, minX, minZ, maxX, maxZ, cut);
        }

        private static bool Sunken(NetInfo info)
        {
            return info != null && Classify(info) == Kind.Ground && info.m_clipTerrain && info.m_surfaceLevel < -0.05f;
        }

        // The node's collision (EmitJunction) shrunk inwards by the widest pavement plus CurbMargin: a disc for a
        // two-segment node, otherwise each fan triangle (centre, corner, next corner) pulled towards the centre.
        private void JunctionCut(ushort nodeId, NetSegment[] segs, NetNode[] nodes, float minX, float minZ, float maxX, float maxZ, ConvexCut cut)
        {
            NetNode node = nodes[nodeId];
            if ((node.m_flags & NetNode.Flags.Middle) != 0) return;
            int count = 0;
            float radius = 0f, w = 0f;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                NetInfo info = segs[sid].Info;
                Kind kind = Classify(info);
                if (kind == Kind.Skip) continue;
                if (kind != Kind.Ground || !Sunken(info)) return; // mixed with bridges, tunnels or level roads: leave the terrain
                radius = Mathf.Max(radius, info.m_halfWidth);
                w = Mathf.Max(w, Mathf.Max(0f, info.m_pavementWidth) + CurbMargin);
                Bezier3 left, right;
                segs[sid].GenerateBezier(sid, nodeId, out left, out right);
                foreach (Vector3 c in new[] { left.a, right.a })
                {
                    _ring[3 * count] = c.x; _ring[3 * count + 1] = c.y; _ring[3 * count + 2] = c.z;
                    count++;
                }
            }
            if (count == 0) return;
            Vector3 p = node.m_position;
            if (p.x + radius < minX || p.x - radius > maxX || p.z + radius < minZ || p.z - radius > maxZ) return;
            if (count == 2)
            {
                float rin = radius - w;
                if (rin <= 0f) return;
                var disc = new float[2 * DiscSegments];
                for (int i = 0; i < DiscSegments; i++)
                {
                    double a = 2 * Math.PI * i / DiscSegments;
                    disc[2 * i] = p.x + rin * (float)Math.Cos(a);
                    disc[2 * i + 1] = p.z + rin * (float)Math.Sin(a);
                }
                cut.Add(disc, DiscSegments);
                return;
            }
            var order = new int[count];
            var angle = new double[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                angle[i] = Math.Atan2(_ring[3 * i + 2] - p.z, _ring[3 * i] - p.x);
            }
            Array.Sort(angle, order);
            for (int i = 0; i < count; i++)
            {
                float ax, az, bx, bz;
                if (!Pull(p, order[i], w, out ax, out az) || !Pull(p, order[(i + 1) % count], w, out bx, out bz)) continue;
                cut.AddTriangle(p.x, p.z, ax, az, bx, bz);
            }
        }

        // Ring corner `i` moved `w` towards the node centre in xz; false when it is not further than that from the centre.
        private bool Pull(Vector3 p, int i, float w, out float x, out float z)
        {
            float dx = _ring[3 * i] - p.x, dz = _ring[3 * i + 2] - p.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            x = p.x;
            z = p.z;
            if (d <= w) return false;
            float s = (d - w) / d;
            x = p.x + dx * s;
            z = p.z + dz * s;
            return true;
        }

        private void EmitSegment(ushort id, ref NetSegment seg, NetNode[] nodes, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            if ((seg.m_flags & NetSegment.Flags.Created) == 0 || (seg.m_flags & NetSegment.Flags.Deleted) != 0) return;
            Bounds b = seg.m_bounds;
            if (b.max.x < minX || b.min.x > maxX || b.max.z < minZ || b.min.z > maxZ) return;
            NetInfo info = seg.Info;
            Kind kind = Classify(info);
            if (kind == Kind.Skip) return;

            // The game's own drawn edges: corners include the node height offset, the surface level and junction trimming.
            Bezier3 left, right;
            seg.GenerateBezier(id, seg.m_startNode, out left, out right);
            bool deck = kind == Kind.Bridge;
            float depth = deck ? DeckThickness : GroundRoadDepth;
            Bezier3D l = ToCore(left, 0f), r = ToCore(right, 0f);
            if (kind == Kind.Tunnel || kind == Kind.Slope)
            {
                TunnelDims d = Dims(info);
                Floor(l, r, d, into);
                Profile(l, r, d, kind == Kind.Tunnel, _sections);
                Tube(l, r, _sections, kind == Kind.Tunnel, d, into);
                if (kind == Kind.Slope) PortalShapes.EmitCollision(id, ref seg, info, TunnelFlag, into);
            }
            else
            {
                Strip.Between(l, r, StripStep, depth, deck ? BridgeDeckFlag : RoadSurfaceFlag, into);
                float liftL, liftR;
                PavementLifts(ref seg, info, left, right, out liftL, out liftR);
                if (liftL > 0f) Slab(l, r, info.m_pavementWidth, liftL, liftL + depth, RoadSurfaceFlag, into);
                if (liftR > 0f) Slab(r, l, info.m_pavementWidth, liftR, liftR + depth, RoadSurfaceFlag, into);
                if (deck || s_elevatedInfos.Contains(info) || Elevated(nodes[seg.m_startNode]) || Elevated(nodes[seg.m_endNode]))
                {
                    Slab(l, r, RailingWidth, liftL + RailingHeight, RailingHeight, RailingFlag, into);
                    Slab(r, l, RailingWidth, liftR + RailingHeight, RailingHeight, RailingFlag, into);
                }
            }
            _nodes.Add(seg.m_startNode);
            _nodes.Add(seg.m_endNode);
            LastSegmentCount++;
        }

        // Railings also go on ramps whose segment is an ordinary road but one end node stands above the ground.
        private static bool Elevated(NetNode node)
        {
            return node.m_elevation > 0 && (node.m_flags & NetNode.Flags.Underground) == 0;
        }

        // A `width`-wide slab along `edge`, inset towards `other`, top `lift` above the edge, `thickness` deep.
        private static void Slab(Bezier3D edge, Bezier3D other, float width, float lift, float thickness, ushort flags, TriangleBuffer into)
        {
            Bezier3D inset;
            if (!Strip.Inset(edge, other, width, lift, out inset)) return;
            Strip.Between(Lift(edge, lift), inset, StripStep, thickness, flags, into);
        }

        private static Bezier3D Lift(Bezier3D c, float dy)
        {
            c.Ay += dy; c.By += dy; c.Cy += dy; c.Dy += dy;
            return c;
        }

        // Height of the raised pavement above each drawn edge: the game's own pedestrian lane curves (NetLane.m_bezier;
        // lane y = curve y + Lane.m_verticalOffset, NetAI.cs:512) lying within the pavement beside that edge, minus the
        // edge height (curve y + m_surfaceLevel). 0 where there is no raised pavement or it falls outside PavementStep's window.
        private static void PavementLifts(ref NetSegment seg, NetInfo info, Bezier3 left, Bezier3 right, out float liftL, out float liftR)
        {
            RawPavementLifts(ref seg, info, left, right, null, out liftL, out liftR);
            liftL = PavementStep.Keep(liftL);
            liftR = PavementStep.Keep(liftR);
        }

        // The highest pedestrian-lane lift beside each edge, NaN when no pedestrian lane is beside it; each pedestrian lane
        // is described into `log` when given.
        private static void RawPavementLifts(ref NetSegment seg, NetInfo info, Bezier3 left, Bezier3 right, System.Text.StringBuilder log, out float liftL, out float liftR)
        {
            liftL = liftR = float.NaN;
            if (!(info.m_pavementWidth > 0f) || info.m_lanes == null) return;
            NetLane[] lanes = Singleton<NetManager>.instance.m_lanes.m_buffer;
            Vector3 lm = left.Position(0.5f), rm = right.Position(0.5f);
            uint lane = seg.m_lanes;
            for (int i = 0; i < info.m_lanes.Length && lane != 0; i++)
            {
                if ((info.m_lanes[i].m_laneType & NetInfo.LaneType.Pedestrian) != 0)
                {
                    Vector3 p = lanes[lane].m_bezier.Position(0.5f);
                    float dl = XZ(p, lm), dr = XZ(p, rm);
                    bool beside = Mathf.Min(dl, dr) <= info.m_pavementWidth + 1f;
                    if (beside)
                    {
                        if (dl < dr) liftL = float.IsNaN(liftL) ? p.y - lm.y : Mathf.Max(liftL, p.y - lm.y);
                        else liftR = float.IsNaN(liftR) ? p.y - rm.y : Mathf.Max(liftR, p.y - rm.y);
                    }
                    if (log != null)
                    {
                        log.Append("; pedestrian lane ").Append(i).Append(" mid y ").Append(p.y.ToString("0.00")).Append(", ")
                            .Append(dl.ToString("0.0")).Append(" m from left edge (y ").Append(lm.y.ToString("0.00")).Append("), ")
                            .Append(dr.ToString("0.0")).Append(" m from right edge (y ").Append(rm.y.ToString("0.00")).Append(")")
                            .Append(beside ? "" : ", not within m_pavementWidth + 1 m of either edge");
                    }
                }
                lane = lanes[lane].m_nextLane;
            }
        }

        private static float XZ(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        /// <summary>Length of one tunnel interval, in metres; shared with the drawn shell.</summary>
        public const float ProfileStep = StripStep;

        /// <summary>
        /// The cross-sections of a tunnel or slope piece between drawn edges <paramref name="l"/> and <paramref name="r"/>
        /// (<see cref="TunnelProfile.Build"/>), which the collision and the drawn shell both follow.
        /// </summary>
        public static int Profile(Bezier3D l, Bezier3D r, TunnelDims dims, bool tunnel, List<TunnelSection> into)
        {
            return TunnelProfile.Build(l, r, dims, tunnel, ProfileStep, into);
        }

        // The road slab of a tunnel or slope piece, between its inner walls.
        private static void Floor(Bezier3D l, Bezier3D r, TunnelDims dims, TriangleBuffer into)
        {
            Strip.Between(TunnelProfile.Inset(l, r, dims.InnerRatio), TunnelProfile.Inset(r, l, dims.InnerRatio), StripStep,
                GroundRoadDepth, RoadSurfaceFlag, into);
        }

        // Per interval of the profile: walls TunnelWallWidth thick outside each section's L and R up to the ceiling's top,
        // and a level ceiling slab whose underside is the section's Ceiling. On a slope's open intervals the walls stop at
        // the portal's ground level (the higher end).
        private static void Tube(Bezier3D l, Bezier3D r, List<TunnelSection> sections, bool tunnel, TunnelDims dims, TriangleBuffer into)
        {
            float width = Mathf.Sqrt((r.Ax - l.Ax) * (r.Ax - l.Ax) + (r.Az - l.Az) * (r.Az - l.Az));
            if (!(width > 0f)) return;
            float outer = dims.InnerRatio + 2f * TunnelWallWidth / width;
            float ground = Mathf.Max((l.Ay + r.Ay) * 0.5f, (l.Dy + r.Dy) * 0.5f);
            for (int k = 0; k + 1 < sections.Count; k++)
            {
                TunnelSection a = sections[k], b = sections[k + 1];
                Bezier3D lk = l.Cut(a.T, b.T), rk = r.Cut(a.T, b.T);
                float floorTop = Mathf.Max((lk.Ay + rk.Ay) * 0.5f, (lk.Dy + rk.Dy) * 0.5f);
                float wall = a.Covered
                    ? Mathf.Max(a.Ceiling - Mathf.Min(lk.Ay, rk.Ay), b.Ceiling - Mathf.Min(lk.Dy, rk.Dy)) + CeilingThickness
                    : tunnel ? dims.Lintel : Mathf.Min(dims.Lintel, ground - floorTop);
                if (wall > 0.5f)
                {
                    Slab(TunnelProfile.Inset(lk, rk, outer), TunnelProfile.Inset(rk, lk, outer), TunnelWallWidth, wall, wall, TunnelFlag, into);
                    Slab(TunnelProfile.Inset(rk, lk, outer), TunnelProfile.Inset(lk, rk, outer), TunnelWallWidth, wall, wall, TunnelFlag, into);
                }
                if (a.Covered)
                {
                    float ta = a.Ceiling + CeilingThickness, tb = b.Ceiling + CeilingThickness;
                    Strip.Between(Line(a.Lx, ta, a.Lz, b.Lx, tb, b.Lz), Line(a.Rx, ta, a.Rz, b.Rx, tb, b.Rz), StripStep,
                        CeilingThickness, TunnelFlag, into);
                }
            }
        }

        private static Bezier3D Line(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            float dx = (x1 - x0) / 3f, dy = (y1 - y0) / 3f, dz = (z1 - z0) / 3f;
            return new Bezier3D
            {
                Ax = x0, Ay = y0, Az = z0, Bx = x0 + dx, By = y0 + dy, Bz = z0 + dz,
                Cx = x1 - dx, Cy = y1 - dy, Cz = z1 - dz, Dx = x1, Dy = y1, Dz = z1,
            };
        }

        /// <summary>
        /// Whether the node is underground and joins exactly two tunnel or slope segments (a bend, or a 2-segment
        /// junction such as slope to tunnel), which one joint piece closes; <paramref name="info"/> is the tunnel
        /// segment's NetInfo, else the slope's, whose <see cref="Dims"/> and ground road the joint uses.
        /// </summary>
        public static bool UndergroundJoint(ushort nodeId, ref NetNode node, NetSegment[] segs, out NetInfo info)
        {
            info = null;
            if ((node.m_flags & NetNode.Flags.Underground) == 0 || (node.m_flags & NetNode.Flags.Middle) != 0) return false;
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                Kind kind = Classify(segs[sid].Info);
                if (kind != Kind.Tunnel && kind != Kind.Slope) return false;
                if (info == null || kind == Kind.Tunnel) info = segs[sid].Info;
                count++;
            }
            return count == 2;
        }

        /// <summary>
        /// The drawn edges across a node joining exactly two segments, from the first segment's trimmed end corners to the
        /// second's, as <c>NetNode.RefreshBendData</c> builds a bend (<c>CalculateCorner</c> with height offset, then
        /// <c>NetSegment.CalculateMiddlePoints</c> with the corner directions negated). False unless two segments connect.
        /// </summary>
        public static bool JointEdges(ushort nodeId, ref NetNode node, NetSegment[] segs, out Bezier3 left, out Bezier3 right)
        {
            Vector3 c1 = Vector3.zero, c2 = Vector3.zero, c3 = Vector3.zero, c4 = Vector3.zero;
            Vector3 d1 = Vector3.zero, d2 = Vector3.zero, d3 = Vector3.zero, d4 = Vector3.zero;
            bool first = false;
            int found = 0;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                bool isFirst = ++found == 1;
                bool start = segs[sid].m_startNode == nodeId;
                bool invert = (segs[sid].m_flags & NetSegment.Flags.Invert) != 0;
                bool smooth;
                if ((!isFirst && !first) || (isFirst && start == invert))
                {
                    segs[sid].CalculateCorner(sid, true, start, false, out c1, out d1, out smooth);
                    segs[sid].CalculateCorner(sid, true, start, true, out c2, out d2, out smooth);
                    first = true;
                }
                else
                {
                    segs[sid].CalculateCorner(sid, true, start, true, out c3, out d3, out smooth);
                    segs[sid].CalculateCorner(sid, true, start, false, out c4, out d4, out smooth);
                }
            }
            Vector3 m1, m2, m3, m4;
            NetSegment.CalculateMiddlePoints(c1, -d1, c3, -d3, true, true, out m1, out m2);
            NetSegment.CalculateMiddlePoints(c2, -d2, c4, -d4, true, true, out m3, out m4);
            left = new Bezier3(c1, m1, m2, c3);
            right = new Bezier3(c2, m3, m4, c4);
            return found == 2;
        }

        private static Bezier3D ToCore(Bezier3 c, float lift)
        {
            return new Bezier3D
            {
                Ax = c.a.x, Ay = c.a.y + lift, Az = c.a.z, Bx = c.b.x, By = c.b.y + lift, Bz = c.b.z,
                Cx = c.c.x, Cy = c.c.y + lift, Cz = c.c.z, Dx = c.d.x, Dy = c.d.y + lift, Dz = c.d.z,
            };
        }

        // Junctions and bends: a fan over the trimmed end corners of every connected surface segment (the outline the
        // game's node mesh fills). Dead ends: a disc of the road's half-width. Middle nodes: nothing, the segments meet.
        private void EmitJunction(ushort nodeId, NetSegment[] segs, NetNode[] nodes, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            NetNode node = nodes[nodeId];
            if ((node.m_flags & NetNode.Flags.Middle) != 0) return;
            NetInfo jointInfo;
            if (UndergroundJoint(nodeId, ref node, segs, out jointInfo))
            {
                Bezier3 jl, jr;
                JointEdges(nodeId, ref node, segs, out jl, out jr);
                Bezier3D l = ToCore(jl, 0f), r = ToCore(jr, 0f);
                if (!(Mathf.Max(l.Ax, l.Dx, r.Ax, r.Dx) < minX || Mathf.Min(l.Ax, l.Dx, r.Ax, r.Dx) > maxX
                    || Mathf.Max(l.Az, l.Dz, r.Az, r.Dz) < minZ || Mathf.Min(l.Az, l.Dz, r.Az, r.Dz) > maxZ))
                {
                    TunnelDims d = Dims(jointInfo);
                    Floor(l, r, d, into);
                    Profile(l, r, d, true, _sections);
                    Tube(l, r, _sections, true, d, into);
                }
                return;
            }
            int count = 0;
            float radius = 0f, sumY = 0f;
            bool allBridge = true;
            float ceiling = 0f;
            int roofs = 0;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                NetInfo info = segs[sid].Info;
                Kind kind = Classify(info);
                if (kind == Kind.Skip) continue;
                if (kind != Kind.Bridge) allBridge = false;
                if (info.m_halfWidth > radius) radius = info.m_halfWidth;
                Bezier3 left, right;
                segs[sid].GenerateBezier(sid, nodeId, out left, out right);
                if (kind == Kind.Tunnel || kind == Kind.Slope)
                {
                    TunnelDims d = Dims(info);
                    if (Profile(ToCore(left, 0f), ToCore(right, 0f), d, kind == Kind.Tunnel, _sections) > 0 && roofs + 2 <= 16)
                    {
                        TunnelSection m = _sections[0];
                        float top = m.Ceiling + CeilingThickness;
                        _roof[3 * roofs] = m.Lx; _roof[3 * roofs + 1] = top; _roof[3 * roofs + 2] = m.Lz;
                        _roof[3 * roofs + 3] = m.Rx; _roof[3 * roofs + 4] = top; _roof[3 * roofs + 5] = m.Rz;
                        roofs += 2;
                    }
                    if (kind == Kind.Tunnel) ceiling = Mathf.Max(ceiling, d.Lintel + CeilingThickness);
                }
                foreach (Vector3 c in new[] { left.a, right.a })
                {
                    _ring[3 * count] = c.x; _ring[3 * count + 1] = c.y; _ring[3 * count + 2] = c.z;
                    count++;
                    sumY += c.y;
                    x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.z); z1 = Mathf.Max(z1, c.z);
                }
            }
            if (count == 0) return;
            Vector3 p = node.m_position;
            float y = sumY / count, thickness = allBridge ? DeckThickness : GroundRoadDepth;
            ushort flags = allBridge ? BridgeDeckFlag : RoadSurfaceFlag;
            if (count == 2)
            {
                if (p.x + radius < minX || p.x - radius > maxX || p.z + radius < minZ || p.z - radius > maxZ) return;
                Disc.Fan(p.x, y, p.z, radius, DiscSegments, thickness, flags, into);
                if (ceiling > 0f) Disc.Fan(p.x, y + ceiling, p.z, radius, DiscSegments, CeilingThickness, TunnelFlag, into);
                return;
            }
            if (x1 < minX || x0 > maxX || z1 < minZ || z0 > maxZ) return;
            Disc.Polygon(p.x, y, p.z, _ring, count, thickness, flags, into);
            if (ceiling > 0f && roofs >= 3)
            {
                float mean = 0f;
                for (int i = 0; i < roofs; i++) mean += _roof[3 * i + 1] / roofs;
                Disc.Polygon(p.x, mean, p.z, _roof, roofs, CeilingThickness, TunnelFlag, into);
            }
        }

        /// <summary>
        /// Exported (ground or bridge) segment ids whose centre-curve midpoint lies within <paramref name="radius"/> of
        /// <paramref name="pos"/> in xz, nearest first.
        /// </summary>
        public List<ushort> FindSegments(Vector3 pos, float radius)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            var found = new List<KeyValuePair<float, ushort>>();
            for (int cz = Cell(pos.z - radius - GridMargin); cz <= Cell(pos.z + radius + GridMargin); cz++)
            {
                for (int cx = Cell(pos.x - radius - GridMargin); cx <= Cell(pos.x + radius + GridMargin); cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        if ((segs[id].m_flags & NetSegment.Flags.Created) != 0 && (segs[id].m_flags & NetSegment.Flags.Deleted) == 0
                            && Classify(segs[id].Info) != Kind.Skip)
                        {
                            Vector3 m = segs[id].GenerateBezier(id, segs[id].m_startNode).Position(0.5f);
                            float d = new Vector2(m.x - pos.x, m.z - pos.z).magnitude;
                            if (d <= radius) found.Add(new KeyValuePair<float, ushort>(d, id));
                        }
                        id = segs[id].m_nextGridSegment;
                    }
                }
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            var ids = new List<ushort>(found.Count);
            foreach (KeyValuePair<float, ushort> kv in found) ids.Add(kv.Value);
            return ids;
        }

        // The ground segment whose centre curve passes nearest to `pos` within 64 m (xz), 0 when none.
        private static ushort NearestGround(Vector3 pos, out float bestD)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            ushort[] grid = nm.m_segmentGrid;
            ushort best = 0;
            bestD = 64f;
            for (int cz = Cell(pos.z - GridMargin); cz <= Cell(pos.z + GridMargin); cz++)
            {
                for (int cx = Cell(pos.x - GridMargin); cx <= Cell(pos.x + GridMargin); cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < NetManager.MAX_SEGMENT_COUNT)
                    {
                        if ((segs[id].m_flags & NetSegment.Flags.Created) != 0 && Classify(segs[id].Info) == Kind.Ground)
                        {
                            Bezier3 c = segs[id].GenerateBezier(id, segs[id].m_startNode);
                            for (int i = 0; i <= 16; i++)
                            {
                                Vector3 q = c.Position(i / 16f);
                                float d = new Vector2(q.x - pos.x, q.z - pos.z).magnitude;
                                if (d < bestD) { bestD = d; best = id; }
                            }
                        }
                        id = segs[id].m_nextGridSegment;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// One log line about the raised pavement of the ground segment nearest to <paramref name="pos"/>: every pedestrian
        /// lane's mid height and distance to each drawn edge, and the step computed on each side with
        /// <see cref="PavementStep.Verdict"/> (kept, or why it was dropped).
        /// </summary>
        public static string DescribePavementSteps(Vector3 pos)
        {
            float d;
            ushort id = NearestGround(pos, out d);
            if (id == 0) return "pavement: no ground segment within 64 m of (" + Fmt(pos) + ")";
            NetSegment seg = Singleton<NetManager>.instance.m_segments.m_buffer[id];
            NetInfo info = seg.Info;
            Bezier3 left, right;
            seg.GenerateBezier(id, seg.m_startNode, out left, out right);
            var sb = new System.Text.StringBuilder();
            sb.Append("pavement of nearest ground segment ").Append(id).Append(" ('").Append(info.name).Append("', ").Append(d.ToString("0.0"))
                .Append(" m away): m_pavementWidth ").Append(info.m_pavementWidth.ToString("0.00")).Append(", m_surfaceLevel ").Append(info.m_surfaceLevel.ToString("0.00"));
            float l, r;
            RawPavementLifts(ref seg, info, left, right, sb, out l, out r);
            if (!(info.m_pavementWidth > 0f)) sb.Append("; m_pavementWidth is 0, no pavement emitted");
            sb.Append("; left step: ").Append(PavementStep.Verdict(l)).Append("; right step: ").Append(PavementStep.Verdict(r));
            return sb.ToString();
        }

        /// <summary>
        /// One log line about the ground segment nearest to <paramref name="pos"/> within 64 m: its id and NetInfo, node
        /// heights and height offsets, surface level, and at t = 0, 0.5, 1 the game's centre bezier y, the detail terrain
        /// height and the top y of the collision we emit there (the mean of the two edge samples).
        /// </summary>
        public string DescribeNearestGround(Vector3 pos)
        {
            NetManager nm = Singleton<NetManager>.instance;
            NetSegment[] segs = nm.m_segments.m_buffer;
            NetNode[] nodes = nm.m_nodes.m_buffer;
            float bestD;
            ushort best = NearestGround(pos, out bestD);
            if (best == 0) return "no ground segment within 64 m of (" + Fmt(pos) + ")";

            NetSegment s = segs[best];
            NetInfo info = s.Info;
            NetNode n0 = nodes[s.m_startNode], n1 = nodes[s.m_endNode];
            Bezier3 left, right;
            Bezier3 centre = s.GenerateBezier(best, s.m_startNode, out left, out right);
            var sb = new System.Text.StringBuilder();
            sb.Append("nearest ground segment ").Append(best).Append(" (").Append(info.name).Append(", ")
                .Append(bestD.ToString("0.0")).Append(" m away): node y ").Append(n0.m_position.y.ToString("0.00")).Append(" / ")
                .Append(n1.m_position.y.ToString("0.00")).Append(", m_heightOffset ").Append(n0.m_heightOffset).Append(" / ")
                .Append(n1.m_heightOffset).Append(", m_surfaceLevel ").Append(info.m_surfaceLevel.ToString("0.00"));
            TerrainManager tm = TerrainManager.instance;
            foreach (float t in new[] { 0f, 0.5f, 1f })
            {
                Vector3 c = centre.Position(t);
                float top = (left.Position(t).y + right.Position(t).y) * 0.5f;
                sb.Append("; t=").Append(t.ToString("0.0")).Append(" bezier y ").Append(c.y.ToString("0.00"))
                    .Append(" terrain ").Append(tm.SampleDetailHeightSmooth(c).ToString("0.00"))
                    .Append(" collision top ").Append(top.ToString("0.00"));
            }
            return sb.ToString();
        }

        private static string Fmt(Vector3 v)
        {
            return v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0");
        }
    }
}
