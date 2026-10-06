using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>
    /// Keeps a first-person eye outside decal volumes. CS1 draws a decal prop up close as <c>PropInfo.m_mesh</c> (a box,
    /// typically 4 m tall around the ground) with a projecting shader (PropInstance.RenderInstance, PropInstance.cs:494),
    /// and the paint disappears while the camera is inside that box (owner, 2026-10-06: brick "Tiles" in front of houses
    /// vanished when walked on). <see cref="Begin"/> swaps each tall decal's mesh for a box with the same footprint whose
    /// top is <c>top</c> metres above the pivot; <see cref="End"/> puts every original back. Main thread only; never throws.
    /// </summary>
    public sealed class DecalHeight
    {
        private readonly HostLog _log;
        private readonly List<KeyValuePair<PropInfo, Mesh>> _swapped = new List<KeyValuePair<PropInfo, Mesh>>();
        private readonly Dictionary<Mesh, Mesh> _boxes = new Dictionary<Mesh, Mesh>();

        /// <summary>Creates the helper; nothing changes until <see cref="Begin"/>.</summary>
        public DecalHeight(HostLog log)
        {
            _log = log;
        }

        /// <summary>Lowers the top of every taller decal volume to <paramref name="top"/> metres above its pivot. Idempotent.</summary>
        public void Begin(float top)
        {
            if (_swapped.Count > 0) return;
            try
            {
                int n = PrefabCollection<PropInfo>.LoadedCount();
                for (uint i = 0; i < n; i++)
                {
                    PropInfo info = PrefabCollection<PropInfo>.GetLoaded(i);
                    if (info == null || !info.m_isDecal || info.m_mesh == null) continue;
                    Bounds b = info.m_mesh.bounds;
                    if (!(b.max.y > top + 0.01f) || !(b.min.y < top)) continue;
                    Mesh box;
                    if (!_boxes.TryGetValue(info.m_mesh, out box))
                    {
                        box = Box(b.min.x, b.max.x, b.min.y, top, b.min.z, b.max.z, info.m_mesh.name);
                        _boxes[info.m_mesh] = box;
                    }
                    _swapped.Add(new KeyValuePair<PropInfo, Mesh>(info, info.m_mesh));
                    info.m_mesh = box;
                }
                if (_swapped.Count > 0) _log.Info("decals: " + _swapped.Count + " decal volumes lowered to " + top.ToString("0.0") + " m above ground");
            }
            catch (Exception e)
            {
                _log.Error("decals: lower", e);
                End();
            }
        }

        /// <summary>Restores every swapped decal mesh. Idempotent.</summary>
        public void End()
        {
            if (_swapped.Count == 0) return;
            foreach (KeyValuePair<PropInfo, Mesh> s in _swapped)
            {
                try { if (s.Key != null) s.Key.m_mesh = s.Value; }
                catch (Exception e) { _log.Error("decals: restore", e); }
            }
            _swapped.Clear();
            _log.Info("decals: original decal volumes restored");
        }

        private static Mesh Box(float x0, float x1, float y0, float y1, float z0, float z1, string name)
        {
            var v = new[]
            {
                new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1),
                new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1),
            };
            int[] t =
            {
                0, 2, 1, 0, 3, 2, // bottom
                4, 5, 6, 4, 6, 7, // top
                0, 1, 5, 0, 5, 4, // z0
                2, 3, 7, 2, 7, 6, // z1
                1, 2, 6, 1, 6, 5, // x1
                3, 0, 4, 3, 4, 7, // x0
            };
            var m = new Mesh { name = name + " (lowered)", hideFlags = HideFlags.HideAndDontSave, vertices = v, triangles = t, uv = new Vector2[8] };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
