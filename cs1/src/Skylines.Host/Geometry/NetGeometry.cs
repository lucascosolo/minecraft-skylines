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
    /// a ceiling where the slope is deep enough to be roofed. Other underground segments are skipped. Main thread only.
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
        /// <summary>Tunnel clearance when the NetInfo gives none, wall width and ceiling thickness, in metres.</summary>
        public const float DefaultTunnelClearance = 6f, TunnelWallWidth = 0.5f, CeilingThickness = 1f;

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
        private static int s_infoCount = -1;

        private readonly HashSet<ushort> _nodes = new HashSet<ushort>();
        private readonly float[] _ring = new float[3 * 16];

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
            for (uint i = 0; i < n; i++)
            {
                NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                RoadAI road = info == null ? null : info.m_netAI as RoadAI;
                if (road == null) continue;
                if (road.m_elevatedInfo != null) s_elevatedInfos.Add(road.m_elevatedInfo);
                if (road.m_bridgeInfo != null) s_elevatedInfos.Add(road.m_bridgeInfo);
                if (road.m_slopeInfo != null) s_slopeInfos.Add(road.m_slopeInfo);
            }
        }

        /// <summary>
        /// Tunnel floor to ceiling-top height: <c>NetInfo.m_maxHeight</c> (how far the game keeps terrain above a tunnel,
        /// NetSegment.cs:1477) when between 3 and 12 m, else 6 m.
        /// </summary>
        public static float Clearance(NetInfo info)
        {
            return info.m_maxHeight >= 3f && info.m_maxHeight <= 12f ? info.m_maxHeight : DefaultTunnelClearance;
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
            Strip.Between(l, r, StripStep, depth, deck ? BridgeDeckFlag : RoadSurfaceFlag, into);
            if (kind == Kind.Tunnel || kind == Kind.Slope)
            {
                Tube(l, r, Clearance(info), kind == Kind.Tunnel, into);
            }
            else
            {
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
        // edge height (curve y + m_surfaceLevel). 0 where there is no raised pavement.
        private static void PavementLifts(ref NetSegment seg, NetInfo info, Bezier3 left, Bezier3 right, out float liftL, out float liftR)
        {
            liftL = liftR = 0f;
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
                    if (Mathf.Min(dl, dr) <= info.m_pavementWidth + 1f)
                    {
                        if (dl < dr) liftL = Mathf.Max(liftL, p.y - lm.y);
                        else liftR = Mathf.Max(liftR, p.y - rm.y);
                    }
                }
                lane = lanes[lane].m_nextLane;
            }
            if (liftL < 0.05f || liftL > 1.5f) liftL = 0f;
            if (liftR < 0.05f || liftR > 1.5f) liftR = 0f;
        }

        private static float XZ(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        // Walls inside each edge and a ceiling slab whose top is `clearance` above the floor, per StripStep interval. A
        // tunnel is closed throughout; on a slope the walls stop at the portal's ground level (the slope's higher end)
        // and the ceiling covers only the intervals deep enough for its top to stay at or below that level.
        private static void Tube(Bezier3D l, Bezier3D r, float clearance, bool tunnel, TriangleBuffer into)
        {
            float dx = l.Dx - l.Ax, dy = l.Dy - l.Ay, dz = l.Dz - l.Az;
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dy * dy + dz * dz) / StripStep));
            float ground = Mathf.Max((l.Ay + r.Ay) * 0.5f, (l.Dy + r.Dy) * 0.5f);
            for (int k = 0; k < n; k++)
            {
                float t0 = k / (float)n, t1 = (k + 1) / (float)n;
                Bezier3D lk = l.Cut(t0, t1), rk = r.Cut(t0, t1);
                float floorTop = Mathf.Max((lk.Ay + rk.Ay) * 0.5f, (lk.Dy + rk.Dy) * 0.5f);
                float wall = tunnel ? clearance : Mathf.Min(clearance, ground - floorTop);
                if (wall > 0.5f)
                {
                    Slab(lk, rk, TunnelWallWidth, wall, wall, TunnelFlag, into);
                    Slab(rk, lk, TunnelWallWidth, wall, wall, TunnelFlag, into);
                }
                if (tunnel || floorTop + clearance <= ground)
                {
                    Strip.Between(Lift(lk, clearance), Lift(rk, clearance), StripStep, CeilingThickness, TunnelFlag, into);
                }
            }
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
            int count = 0;
            float radius = 0f, sumY = 0f;
            bool allBridge = true;
            float ceiling = 0f;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                NetInfo info = segs[sid].Info;
                Kind kind = Classify(info);
                if (kind == Kind.Skip) continue;
                if (kind != Kind.Bridge) allBridge = false;
                if (kind == Kind.Tunnel) ceiling = Mathf.Max(ceiling, Clearance(info));
                if (info.m_halfWidth > radius) radius = info.m_halfWidth;
                Bezier3 left, right;
                segs[sid].GenerateBezier(sid, nodeId, out left, out right);
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
            if (ceiling > 0f)
            {
                for (int i = 0; i < count; i++) _ring[3 * i + 1] += ceiling;
                Disc.Polygon(p.x, y + ceiling, p.z, _ring, count, CeilingThickness, TunnelFlag, into);
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
            ushort[] grid = nm.m_segmentGrid;
            ushort best = 0;
            float bestD = 64f;
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
