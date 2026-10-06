using System.Collections.Generic;
using ColossalFramework;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Tunnel portal meshes: built-in net meshes whose cached geometry has a black end wall (<see cref="PortalCap"/>), with
    /// that wall's triangles left out. Looked up once per mesh; a replacement mesh made from a portal is registered under
    /// the same shape. Also emits a slope segment's portal structure as collision. Main thread only.
    /// </summary>
    public static class PortalShapes
    {
        /// <summary>Portal triangles whose highest point is below this, in metres above the road surface, are not collision (the floor comes from the tube).</summary>
        public const float SolidAbove = 0.5f;

        /// <summary>A portal mesh's cached geometry and its triangle list without the end wall.</summary>
        public sealed class Shape
        {
            /// <summary>The cached mesh (positions, indices, channels).</summary>
            public CachedMesh Mesh;
            /// <summary>Triangle list without the end wall.</summary>
            public int[] Kept;
            /// <summary>End wall triangles left out.</summary>
            public int CapTriangles;
        }

        private static readonly Dictionary<Mesh, Shape> s_shapes = new Dictionary<Mesh, Shape>();

        /// <summary>The portal shape of <paramref name="mesh"/>, or false when it is not a cached mesh with an end wall.</summary>
        public static bool TryGet(Mesh mesh, out Shape shape)
        {
            shape = null;
            if (mesh == null) return false;
            if (!s_shapes.TryGetValue(mesh, out shape))
            {
                CachedMesh cached;
                if (BuiltInMeshes.TryGetMesh(mesh, out cached))
                {
                    int caps;
                    int[] kept = PortalCap.Remove(cached.Positions, cached.Indices, mesh.bounds.center.x, out caps);
                    if (caps > 0) shape = new Shape { Mesh = cached, Kept = kept, CapTriangles = caps };
                }
                s_shapes[mesh] = shape;
            }
            return shape != null;
        }

        /// <summary>Makes <paramref name="replacement"/> resolve to <paramref name="shape"/>, the shape of the mesh it replaces.</summary>
        public static void Register(Mesh replacement, Shape shape)
        {
            if (replacement != null) s_shapes[replacement] = shape;
        }

        /// <summary>Forgets <paramref name="replacement"/> before it is destroyed.</summary>
        public static void Unregister(Mesh replacement)
        {
            if (replacement != null) s_shapes.Remove(replacement);
        }

        /// <summary>
        /// Appends the portal structure of slope segment <paramref name="id"/> (every segment mesh of its NetInfo that is a
        /// portal, triangles reaching <see cref="SolidAbove"/>) bent along the segment exactly as
        /// <c>NetSegment.RenderInstance</c> bends it for drawing. Returns the triangles added.
        /// </summary>
        public static int EmitCollision(ushort id, ref NetSegment seg, NetInfo info, ushort flags, Skylines.Core.Geometry.TriangleBuffer into)
        {
            if (info.m_segments == null || !(info.m_halfWidth > 0f) || !(info.m_segmentLength > 0f)) return 0;
            int added = 0;
            bool edges = false;
            Bezier3D left = default(Bezier3D), right = default(Bezier3D);
            foreach (NetInfo.Segment s in info.m_segments)
            {
                bool turnAround;
                Shape shape;
                if (s == null || s.m_requireHeightMap || !s.CheckFlags(seg.m_flags, seg.m_flags2, out turnAround)) continue;
                if (!TryGet(s.m_segmentMesh, out shape)) continue;
                if (info.m_netAI.CanAutoRenderInverted()) turnAround = (seg.m_flags & NetSegment.Flags.Invert) != 0;
                if (!edges)
                {
                    RenderEdges(id, ref seg, out left, out right);
                    edges = true;
                }
                float sx = 0.5f / info.m_halfWidth, sz = 1f / info.m_segmentLength;
                if (turnAround)
                {
                    sx = -sx;
                    sz = -sz;
                }
                added += NetBend.Triangles(shape.Mesh.Positions, shape.Kept, left, right, sx, sz, SolidAbove, flags, into);
            }
            return added;
        }

        // NetSegment.RenderInstance: left edge from the start-left corner to the end-right one (the end node's left), right
        // edge from start-right to end-left, middle points from CalculateMiddlePoints.
        private static void RenderEdges(ushort id, ref NetSegment seg, out Bezier3D left, out Bezier3D right)
        {
            Vector3 c1, d1, c2, d2, c3, d3, c4, d4, m1, m2, m3, m4;
            bool s1, s2;
            seg.CalculateCorner(id, true, true, true, out c1, out d1, out s1);
            seg.CalculateCorner(id, true, false, true, out c2, out d2, out s2);
            seg.CalculateCorner(id, true, true, false, out c3, out d3, out s1);
            seg.CalculateCorner(id, true, false, false, out c4, out d4, out s2);
            NetSegment.CalculateMiddlePoints(c1, d1, c4, d4, s1, s2, out m1, out m2);
            NetSegment.CalculateMiddlePoints(c3, d3, c2, d2, s1, s2, out m3, out m4);
            left = Curve(c1, m1, m2, c4);
            right = Curve(c3, m3, m4, c2);
        }

        private static Bezier3D Curve(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            return new Bezier3D { Ax = a.x, Ay = a.y, Az = a.z, Bx = b.x, By = b.y, Bz = b.z, Cx = c.x, Cy = c.y, Cz = c.z, Dx = d.x, Dy = d.y, Dz = d.z };
        }
    }
}
