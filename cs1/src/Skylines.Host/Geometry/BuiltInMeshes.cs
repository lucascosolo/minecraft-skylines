using System;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Geometry of GPU-only built-in meshes from the extracted <see cref="MeshCache"/> under $HOME, matched by
    /// <c>Mesh.name</c>, <c>Mesh.vertexCount</c> and <c>Mesh.bounds</c> (both work without read access). Opened on first
    /// use; its status is logged once. Main thread only.
    /// </summary>
    public static class BuiltInMeshes
    {
        private static MeshCache s_cache;

        /// <summary>The cache's vertices and triangles for <paramref name="mesh"/>, or false when it has none.</summary>
        public static bool TryGet(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
            vertices = null;
            triangles = null;
            if (mesh == null) return false;
            if (s_cache == null)
            {
                s_cache = MeshCache.Open(MeshCache.DefaultPath(Environment.GetEnvironmentVariable("HOME")));
                Debug.Log("[MinecraftSkylines] built-in mesh cache: " + s_cache.Status);
            }
            if (!s_cache.Available) return false;
            Bounds b = mesh.bounds;
            Vector3 c = b.center, e = b.extents;
            float[] p;
            if (!s_cache.TryGet(mesh.name, mesh.vertexCount, c.x, c.y, c.z, e.x, e.y, e.z, out p, out triangles)) return false;
            vertices = new Vector3[p.Length / 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(p[3 * i], p[3 * i + 1], p[3 * i + 2]);
            return true;
        }
    }
}
