using System;
using System.Collections.Generic;

namespace Skylines.Host.Rendering
{
    /// <summary>One part of a split mesh.</summary>
    public sealed class MeshPart
    {
        /// <summary>Part vertex k comes from source vertex VertexMap[k].</summary>
        public int[] VertexMap;
        /// <summary>Triangle list into the part's vertices.</summary>
        public int[] Indices;
    }

    /// <summary>
    /// Splits a triangle list so every part fits Unity 5.6's 16-bit index buffers (Mesh.indexFormat arrived in
    /// 2017.3). Unity-free.
    /// </summary>
    public static class MeshSplit
    {
        /// <summary>Vertex cap per part, below 65535 with headroom.</summary>
        public const int MaxVertices = 65000;

        /// <summary>Splits <paramref name="indices"/> (a triangle list over <paramref name="vertexCount"/> vertices) into parts of at most <paramref name="maxVertices"/> vertices.</summary>
        public static List<MeshPart> Split(int[] indices, int vertexCount, int maxVertices)
        {
            if (maxVertices < 3) throw new ArgumentOutOfRangeException("maxVertices");
            if (indices.Length % 3 != 0) throw new ArgumentException("indices must hold whole triangles");
            foreach (int i in indices)
                if (i < 0 || i >= vertexCount) throw new ArgumentOutOfRangeException("indices", "index " + i + " outside 0.." + (vertexCount - 1));
            var parts = new List<MeshPart>();
            if (vertexCount == 0) return parts;
            if (vertexCount <= maxVertices)
            {
                var identity = new int[vertexCount];
                for (int k = 0; k < vertexCount; k++) identity[k] = k;
                parts.Add(new MeshPart { VertexMap = identity, Indices = (int[])indices.Clone() });
                return parts;
            }

            var map = new Dictionary<int, int>();
            var verts = new List<int>();
            var tris = new List<int>();
            for (int t = 0; t < indices.Length; t += 3)
            {
                int fresh = 0;
                for (int k = 0; k < 3; k++)
                {
                    int v = indices[t + k];
                    if (!map.ContainsKey(v) && (k == 0 || v != indices[t]) && (k < 2 || v != indices[t + 1])) fresh++;
                }
                if (verts.Count + fresh > maxVertices)
                {
                    parts.Add(new MeshPart { VertexMap = verts.ToArray(), Indices = tris.ToArray() });
                    map.Clear();
                    verts.Clear();
                    tris.Clear();
                }
                for (int k = 0; k < 3; k++)
                {
                    int v = indices[t + k], local;
                    if (!map.TryGetValue(v, out local))
                    {
                        local = verts.Count;
                        map[v] = local;
                        verts.Add(v);
                    }
                    tris.Add(local);
                }
            }
            if (tris.Count > 0) parts.Add(new MeshPart { VertexMap = verts.ToArray(), Indices = tris.ToArray() });
            return parts;
        }
    }
}
