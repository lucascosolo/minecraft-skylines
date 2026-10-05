using System.Collections.Generic;
using ColossalFramework.UI;
using MinecraftSkylines.Protocol;
using Skylines.Core.Geometry;
using Skylines.Host;
using Skylines.Host.Geometry;
using UnityEngine;
using UnityEngine.Rendering;
using UInput = UnityEngine.Input;

namespace MinecraftSkylines.Mod.Diagnostics
{
    /// <summary>
    /// Debug view of the streamed collision: Ctrl+Shift+G toggles a wireframe of the regions last streamed within
    /// 32 m of the player, drawn over the main camera in city view and in Minecraft mode (terrain green, road yellow,
    /// bridge deck and railing red). Lines hidden behind scene geometry are drawn faint, visible ones solid. While off,
    /// nothing is subscribed to the camera. Main thread only.
    /// </summary>
    internal sealed class CollisionViewer
    {
        private const float KeepRadius = 64f;
        private const float DrawRadius = 32f;
        private const float HiddenAlpha = 0.25f;

        private struct Region
        {
            public float MinX, MinZ, MaxX, MaxZ;
            public float[] Positions;
            public ushort[] Flags;
        }

        private readonly HostLog _log;
        private readonly Dictionary<long, Region> _regions = new Dictionary<long, Region>();
        private readonly List<long> _evict = new List<long>();
        private Material _material;
        private Vector3 _player;
        private bool _on;

        public CollisionViewer(HostLog log)
        {
            _log = log;
        }

        /// <summary>Copies a region's CS1 triangles, drops regions farther than 64 m from the player.</summary>
        public void Store(long key, float minX, float minZ, float maxX, float maxZ, TriangleBuffer cs, Vector3 player)
        {
            _player = player;
            var r = new Region { MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ, Positions = new float[9 * cs.Count], Flags = new ushort[cs.Count] };
            System.Array.Copy(cs.Positions, r.Positions, r.Positions.Length);
            System.Array.Copy(cs.Flags, r.Flags, r.Flags.Length);
            _regions[key] = r;
            _evict.Clear();
            foreach (KeyValuePair<long, Region> kv in _regions)
                if (Distance(kv.Value, player) > KeepRadius) _evict.Add(kv.Key);
            foreach (long k in _evict) _regions.Remove(k);
        }

        /// <summary>Forgets every region (new collision epoch).</summary>
        public void Clear()
        {
            _regions.Clear();
        }

        /// <summary>The player moved (regions are kept or drawn relative to it).</summary>
        public void Track(Vector3 player)
        {
            _player = player;
        }

        /// <summary>Per frame from the pump's Update: the toggle key.</summary>
        public void Update()
        {
            if (!UInput.GetKeyDown(KeyCode.G)
                || !(UInput.GetKey(KeyCode.LeftControl) || UInput.GetKey(KeyCode.RightControl))
                || !(UInput.GetKey(KeyCode.LeftShift) || UInput.GetKey(KeyCode.RightShift))
                || UIView.HasModalInput() || UIView.HasInputFocus()) return;
            SetOn(!_on);
            _log.Info("collision viewer " + (_on ? "on" : "off") + ", " + _regions.Count + " regions kept");
        }

        /// <summary>Whether the wireframe is drawn (the self-test turns it on for its screenshots).</summary>
        public bool On
        {
            get { return _on; }
            set { SetOn(value); }
        }

        /// <summary>Unsubscribes from the camera and releases the material (mod disable).</summary>
        public void Dispose()
        {
            SetOn(false);
            if (_material != null) Object.Destroy(_material);
            _material = null;
        }

        private void SetOn(bool on)
        {
            if (on == _on) return;
            _on = on;
            if (on)
            {
                if (_material == null)
                {
                    _material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                    _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    _material.SetInt("_Cull", (int)CullMode.Off);
                    _material.SetInt("_ZWrite", 0);
                }
                Camera.onPostRender += Draw;
            }
            else
            {
                Camera.onPostRender -= Draw;
            }
        }

        private void Draw(Camera cam)
        {
            if (cam != Camera.main) return;
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            Pass(CompareFunction.Greater, HiddenAlpha);
            Pass(CompareFunction.LessEqual, 1f);
            GL.PopMatrix();
        }

        private void Pass(CompareFunction ztest, float alpha)
        {
            _material.SetInt("_ZTest", (int)ztest);
            _material.SetPass(0);
            GL.Begin(GL.LINES);
            foreach (Region r in _regions.Values)
            {
                if (Distance(r, _player) > DrawRadius) continue;
                float[] p = r.Positions;
                for (int i = 0; i < r.Flags.Length; i++)
                {
                    GL.Color(ColorOf(r.Flags[i], alpha));
                    int o = 9 * i;
                    Line(p, o, o + 3);
                    Line(p, o + 3, o + 6);
                    Line(p, o + 6, o);
                }
            }
            GL.End();
        }

        private static void Line(float[] p, int a, int b)
        {
            GL.Vertex3(p[a], p[a + 1], p[a + 2]);
            GL.Vertex3(p[b], p[b + 1], p[b + 2]);
        }

        private static Color ColorOf(ushort flags, float alpha)
        {
            if ((flags & (NetGeometry.BridgeDeckFlag | NetGeometry.RailingFlag)) != 0) return new Color(1f, 0.15f, 0.1f, alpha);
            if ((flags & NetGeometry.RoadSurfaceFlag) != 0) return new Color(1f, 0.9f, 0.1f, alpha);
            if ((flags & CollisionRegion.Terrain) != 0) return new Color(0.2f, 0.9f, 0.2f, alpha);
            return new Color(1f, 1f, 1f, alpha);
        }

        // Horizontal distance from the player to the nearest point of the region's rectangle.
        private static float Distance(Region r, Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(r.MinX - p.x, p.x - r.MaxX));
            float dz = Mathf.Max(0f, Mathf.Max(r.MinZ - p.z, p.z - r.MaxZ));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
