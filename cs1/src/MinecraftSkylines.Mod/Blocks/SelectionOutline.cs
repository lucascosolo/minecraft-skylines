using System;
using MinecraftSkylines.Protocol;
using UnityEngine;
using UnityEngine.Rendering;

namespace MinecraftSkylines.Mod.Blocks
{
    /// <summary>
    /// Draws Minecraft's block outline (BLOCK_SELECTION) as a thin wireframe box over the main camera while in
    /// Minecraft mode: black like Minecraft's, a grid cell on city geometry slightly lighter. Depth-tested, so
    /// walls hide it. Main thread only.
    /// </summary>
    internal sealed class SelectionOutline
    {
        // Minecraft draws its outline 0.002 outside the shape so it does not z-fight with the faces.
        private const float Grow = 0.002f;
        private static readonly Color BlockColor = new Color(0f, 0f, 0f, 0.4f);
        private static readonly Color CellColor = new Color(0.3f, 0.3f, 0.3f, 0.55f);

        private readonly Func<bool> _shown;
        private readonly Vector3[] _corners = new Vector3[8];
        private Material _material;
        private bool _visible;
        private Color _color;

        public SelectionOutline(Func<bool> shown)
        {
            _shown = shown;
            Camera.onPostRender += Draw;
        }

        /// <summary>Latest BLOCK_SELECTION; Minecraft (x, y, z) becomes CS1 (x, y - YOffset, -z).</summary>
        public void Set(BlockSelection s)
        {
            _visible = s.Visible;
            if (!s.Visible) return;
            _color = s.Kind == BlockSelection.KindPlacement ? CellColor : BlockColor;
            float y = (float)MinecraftFrame.YOffset;
            var min = new Vector3(s.MinX - Grow, s.MinY - y - Grow, -s.MaxZ - Grow);
            var max = new Vector3(s.MaxX + Grow, s.MaxY - y + Grow, -s.MinZ + Grow);
            for (int i = 0; i < 8; i++)
                _corners[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
        }

        /// <summary>Link lost: nothing is targeted any more.</summary>
        public void Hide()
        {
            _visible = false;
        }

        public void Dispose()
        {
            Camera.onPostRender -= Draw;
            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;
        }

        private void Draw(Camera cam)
        {
            if (!_visible || cam != Camera.main || !_shown()) return;
            if (_material == null)
            {
                _material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_Cull", (int)CullMode.Off);
                _material.SetInt("_ZWrite", 0);
                _material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            }
            _material.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
            GL.Color(_color);
            // The 12 edges: corner pairs differing in exactly one bit.
            for (int a = 0; a < 8; a++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((a & bit) == 0)
                    {
                        Vector3 p = _corners[a], q = _corners[a | bit];
                        GL.Vertex3(p.x, p.y, p.z);
                        GL.Vertex3(q.x, q.y, q.z);
                    }
            GL.End();
            GL.PopMatrix();
        }
    }
}
