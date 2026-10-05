using System.Collections.Generic;
using ColossalFramework;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Road, rail and path surfaces of the network overlapping a world-space rectangle, as collision triangles in CS1
    /// coordinates. Ground segments are flat ribbons; bridge and elevated segments are 1 m slabs (walkable underneath);
    /// underground segments are skipped. Main thread only.
    /// </summary>
    public sealed class NetGeometry
    {
        /// <summary>Flags value of a ground road surface triangle (bit 1).</summary>
        public const ushort RoadSurfaceFlag = 1 << 1;
        /// <summary>Flags value of a bridge or elevated deck triangle (bit 2).</summary>
        public const ushort BridgeDeckFlag = 1 << 2;
        /// <summary>Deck thickness in metres.</summary>
        public const float DeckThickness = 1.0f;

        private const float StripStep = 4f;
        private const int DiscSegments = 16;
        private const float GridCell = 64f;       // NetManager.InitializeSegment: (int)(x / 64f + 135f)
        private const int GridSize = 270;         // NetManager.NODEGRID_RESOLUTION
        private const float GridMargin = 256f;    // segments are filed under their midpoint; searched with slack, then bounds-tested

        private enum Kind { Skip, Ground, Bridge }

        private readonly HashSet<ushort> _nodes = new HashSet<ushort>();

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

            foreach (ushort n in _nodes) EmitJunction(nodes[n], segs, nodes, minX, minZ, maxX, maxZ, into);
        }

        private static int Cell(float v)
        {
            return Mathf.Clamp((int)(v / GridCell + 135f), 0, GridSize - 1);
        }

        private static Kind Classify(NetInfo info)
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

            Vector3 p0 = nodes[seg.m_startNode].m_position, p1 = nodes[seg.m_endNode].m_position;
            p0.y += info.m_surfaceLevel;
            p1.y += info.m_surfaceLevel;
            Vector3 m0, m1;
            NetSegment.CalculateMiddlePoints(p0, seg.m_startDirection, p1, seg.m_endDirection, true, true, out m0, out m1);
            var curve = new Bezier3D
            {
                Ax = p0.x, Ay = p0.y, Az = p0.z, Bx = m0.x, By = m0.y, Bz = m0.z,
                Cx = m1.x, Cy = m1.y, Cz = m1.z, Dx = p1.x, Dy = p1.y, Dz = p1.z,
            };
            bool deck = kind == Kind.Bridge;
            Strip.Road(curve, info.m_halfWidth, StripStep, deck ? DeckThickness : 0f, deck ? BridgeDeckFlag : RoadSurfaceFlag, into);
            _nodes.Add(seg.m_startNode);
            _nodes.Add(seg.m_endNode);
            LastSegmentCount++;
        }

        private static void EmitJunction(NetNode node, NetSegment[] segs, NetNode[] nodes, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            if ((node.m_flags & NetNode.Flags.Underground) != 0) return;
            float radius = 0f;
            bool allBridge = true;
            for (int i = 0; i < 8; i++)
            {
                ushort sid = node.GetSegment(i);
                if (sid == 0) continue;
                NetInfo info = segs[sid].Info;
                Kind kind = Classify(info);
                if (kind == Kind.Skip) continue;
                if (info.m_halfWidth > radius) radius = info.m_halfWidth;
                if (kind != Kind.Bridge) allBridge = false;
            }
            if (radius <= 0f) return;
            Vector3 p = node.m_position;
            if (p.x + radius < minX || p.x - radius > maxX || p.z + radius < minZ || p.z - radius > maxZ) return;
            Disc.Fan(p.x, p.y, p.z, radius, DiscSegments, allBridge ? DeckThickness : 0f, allBridge ? BridgeDeckFlag : RoadSurfaceFlag, into);
        }
    }
}
