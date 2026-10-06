using System;
using System.Collections.Generic;
using Skylines.Host.Geometry;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>
    /// Takes the black end wall out of road tunnel portals: <see cref="Apply"/> sets <c>NetInfo.Segment.m_segmentMesh</c>
    /// of every road slope NetInfo whose mesh is a <see cref="PortalShapes"/> portal to a copy built from the mesh cache
    /// (every vertex and vertex channel kept, the wall's triangles left out); <see cref="Restore"/> puts the originals
    /// back exactly and destroys the copies. Needs a version 2 mesh cache (vertex channels). Main thread only.
    /// </summary>
    public sealed class PortalMeshes
    {
        private sealed class Swap
        {
            public NetInfo.Segment Segment;
            public Mesh Original, Replacement;
        }

        private readonly HostLog _log;
        private readonly List<Swap> _swaps = new List<Swap>();
        private bool _active;

        /// <summary>Creates a swapper that logs to <paramref name="log"/>.</summary>
        public PortalMeshes(HostLog log)
        {
            _log = log;
        }

        /// <summary>Portal meshes currently replaced.</summary>
        public int Swapped { get { return _swaps.Count; } }

        /// <summary>Replaces every portal mesh. Idempotent; never throws.</summary>
        public void Apply()
        {
            if (_active) return;
            _active = true;
            int noChannels = 0;
            try
            {
                int n = PrefabCollection<NetInfo>.LoadedCount();
                for (uint i = 0; i < n; i++)
                {
                    NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                    if (info == null || info.m_segments == null || NetGeometry.Classify(info) != NetGeometry.Kind.Slope) continue;
                    foreach (NetInfo.Segment s in info.m_segments)
                    {
                        PortalShapes.Shape shape;
                        if (s == null || !PortalShapes.TryGet(s.m_segmentMesh, out shape)) continue;
                        Mesh copy = Build(s.m_segmentMesh, shape);
                        if (copy == null)
                        {
                            noChannels++;
                            continue;
                        }
                        PortalShapes.Register(copy, shape);
                        _swaps.Add(new Swap { Segment = s, Original = s.m_segmentMesh, Replacement = copy });
                        s.m_segmentMesh = copy;
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error("portals: apply", e);
            }
            _log.Info("portals: " + _swaps.Count + " tunnel portal meshes without their end wall"
                + (noChannels > 0 ? "; " + noChannels + " left as they are (mesh cache has no vertex channels: rerun tools/extract-cs1-meshes.sh)" : ""));
        }

        /// <summary>Puts every original mesh back and destroys the copies. Idempotent; never throws.</summary>
        public void Restore()
        {
            if (!_active) return;
            _active = false;
            foreach (Swap w in _swaps)
            {
                try
                {
                    if (w.Segment.m_segmentMesh == w.Replacement) w.Segment.m_segmentMesh = w.Original;
                    else _log.Warn("portals: a segment mesh was changed by something else; left as it is");
                    PortalShapes.Unregister(w.Replacement);
                    UnityEngine.Object.Destroy(w.Replacement);
                }
                catch (Exception e)
                {
                    _log.Error("portals: restore", e);
                }
            }
            _swaps.Clear();
        }

        // The original's vertices and channels with the kept triangles; null without vertex channels in the cache.
        private static Mesh Build(Mesh original, PortalShapes.Shape shape)
        {
            CachedMesh c = shape.Mesh;
            if (c.Normals == null && c.Uv == null) return null;
            int n = c.Positions.Length / 3;
            var m = new Mesh { name = original.name };
            m.vertices = V3(c.Positions, n);
            if (c.Normals != null) m.normals = V3(c.Normals, n);
            if (c.Tangents != null)
            {
                var t = new Vector4[n];
                for (int i = 0; i < n; i++) t[i] = new Vector4(c.Tangents[4 * i], c.Tangents[4 * i + 1], c.Tangents[4 * i + 2], c.Tangents[4 * i + 3]);
                m.tangents = t;
            }
            if (c.Colors != null)
            {
                var k = new Color32[n];
                for (int i = 0; i < n; i++) k[i] = new Color32(c.Colors[4 * i], c.Colors[4 * i + 1], c.Colors[4 * i + 2], c.Colors[4 * i + 3]);
                m.colors32 = k;
            }
            if (c.Uv != null) m.uv = V2(c.Uv, n);
            if (c.Uv2 != null) m.uv2 = V2(c.Uv2, n);
            if (c.Uv3 != null) m.uv3 = V2(c.Uv3, n);
            if (c.Uv4 != null) m.uv4 = V2(c.Uv4, n);
            m.triangles = shape.Kept;
            m.bounds = original.bounds;
            return m;
        }

        private static Vector3[] V3(float[] f, int n)
        {
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2]);
            return v;
        }

        private static Vector2[] V2(float[] f, int n)
        {
            var v = new Vector2[n];
            for (int i = 0; i < n; i++) v[i] = new Vector2(f[2 * i], f[2 * i + 1]);
            return v;
        }
    }
}
