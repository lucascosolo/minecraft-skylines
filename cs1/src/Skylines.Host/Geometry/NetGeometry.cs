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
    /// railing along each edge; underground segments are skipped. Main thread only.
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
        }

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
            if (ai.IsUnderground()) return Kind.Skip;
            return ai.IsBridge() ? Kind.Bridge : Kind.Ground;
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
            Strip.Between(ToCore(left, 0f), ToCore(right, 0f), StripStep, deck ? DeckThickness : GroundRoadDepth, deck ? BridgeDeckFlag : RoadSurfaceFlag, into);
            if (deck)
            {
                Railing(left, right, into);
                Railing(right, left, into);
            }
            _nodes.Add(seg.m_startNode);
            _nodes.Add(seg.m_endNode);
            LastSegmentCount++;
        }

        // A RailingWidth-wide, RailingHeight-tall wall standing on the deck along `edge`, inset towards `other`.
        private static void Railing(Bezier3 edge, Bezier3 other, TriangleBuffer into)
        {
            float width = Vector3.Distance(Vector3.Lerp(edge.a, edge.d, 0.5f), Vector3.Lerp(other.a, other.d, 0.5f));
            if (width <= RailingWidth) return;
            float f = RailingWidth / width;
            var inset = new Bezier3(Vector3.Lerp(edge.a, other.a, f), Vector3.Lerp(edge.b, other.b, f),
                Vector3.Lerp(edge.c, other.c, f), Vector3.Lerp(edge.d, other.d, f));
            Strip.Between(ToCore(edge, RailingHeight), ToCore(inset, RailingHeight), StripStep, RailingHeight, RailingFlag, into);
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
            if ((node.m_flags & (NetNode.Flags.Underground | NetNode.Flags.Middle)) != 0) return;
            int count = 0;
            float radius = 0f, sumY = 0f;
            bool allBridge = true;
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
                return;
            }
            if (x1 < minX || x0 > maxX || z1 < minZ || z0 > maxZ) return;
            Disc.Polygon(p.x, y, p.z, _ring, count, thickness, flags, into);
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
