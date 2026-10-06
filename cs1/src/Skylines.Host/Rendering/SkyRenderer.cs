using System;
using System.Reflection;
using Skylines.Core.Sky;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>What <see cref="SkyRenderer"/> draws this frame. Colours are sRGB 0-1; heights in CS1 metres.</summary>
    public sealed class SkyFrame
    {
        /// <summary>Draw the cloud layer.</summary>
        public bool Clouds;
        /// <summary>Overhead colour (r, g, b).</summary>
        public float[] SkyColor = new float[3];
        /// <summary>Horizon colour (r, g, b), also the camera's clear colour.</summary>
        public float[] FogColor = new float[3];
        /// <summary>Glow towards the sun (r, g, b, a).</summary>
        public float[] SunriseColor = new float[4];
        /// <summary>Star opacity 0-1.</summary>
        public float StarAlpha;
        /// <summary>Sun and moon opacity 0-1.</summary>
        public float CelestialAlpha;
        /// <summary>Texture slot of the moon (1-8).</summary>
        public int MoonSlot = 1;
        /// <summary>Cloud colour (r, g, b, a).</summary>
        public float[] CloudColor = new float[4];
        /// <summary>World Y of the cloud layer.</summary>
        public float CloudHeight;
        /// <summary>Cloud scroll along the texture's x axis, in world units.</summary>
        public double CloudOffset;
    }

    /// <summary>
    /// Replaces CS1's sky on the main camera with a block-game sky: a gradient dome with a sunrise glow, stars, textured
    /// sun and moon at CS1's own sun and moon light directions, and a flat tiled cloud layer. CS1's skybox and star field
    /// are hidden per render of the main camera only (clear flags and the star material's intensity are overridden in
    /// <c>Camera.onPreCull</c> and restored in <c>Camera.onPostRender</c>), so nothing persists when drawing stops.
    /// Textures in slots (<see cref="SkyMath.TextureSlot"/>) are owned and destroyed here. Main thread only.
    /// </summary>
    public sealed class SkyRenderer : IDisposable
    {
        private const int Rings = 16, Segments = 32, StarCount = 1500, CloudGrid = 32;
        private const float SunHalf = 0.30f, MoonHalf = 0.20f, CloudRadius = 2048f;
        private static readonly int StarIntensityId = Shader.PropertyToID("_StarIntensity");

        private readonly HostLog _log;
        private readonly Texture2D[] _textures = new Texture2D[SkyMath.Slots];
        private readonly FrameOverride<CameraClearFlags> _clear = new FrameOverride<CameraClearFlags>();
        private readonly FrameOverride<Color> _background = new FrameOverride<Color>();
        private readonly FrameOverride<float> _stars = new FrameOverride<float>();
        private readonly int _layer;
        private UnityEngine.Camera _camera, _overriddenCamera;
        private Material _cs1Stars, _overriddenStars;
        private Material _domeMat, _starMat, _sunMat, _moonMat, _cloudMat;
        private Mesh _dome, _starMesh, _quad, _cloudMesh;
        private Vector3[] _domeDirs;
        private Color[] _domeColours;
        private Vector3[] _cloudVerts;
        private Vector2[] _cloudUv;
        private Color[] _cloudColours;
        private float _starAlphaShown = -1f;
        private Color _fog;
        private bool _hooked, _materialsTried;
        private readonly float[] _rgb = new float[3];

        /// <summary>Creates nothing in Unity until the first <see cref="Draw"/>.</summary>
        public SkyRenderer(HostLog log)
        {
            _log = log;
            int props = LayerMask.NameToLayer("Props");
            _layer = props >= 0 ? props : 10;
        }

        /// <summary>True while CS1's sky is replaced.</summary>
        public bool Active { get { return _hooked; } }

        /// <summary>Stores a texture in a slot (sun 0, moon phases 1-8, clouds 9), destroying the one it replaces.</summary>
        public void SetTexture(int slot, Texture2D texture)
        {
            if (slot < 0 || slot >= _textures.Length) { if (texture != null) UnityEngine.Object.Destroy(texture); return; }
            if (texture != null && slot == 9) texture.wrapMode = TextureWrapMode.Repeat;
            TextureUtil.Replace(ref _textures[slot], texture);
        }

        /// <summary>Destroys every slot's texture.</summary>
        public void ClearTextures()
        {
            for (int i = 0; i < _textures.Length; i++) TextureUtil.Replace(ref _textures[i], null);
        }

        /// <summary>Draws this frame's sky on the main camera (call from LateUpdate, after the camera is placed).</summary>
        public void Draw(SkyFrame f)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam == null) { Hide(); return; }
            EnsureResources();
            if (_domeMat == null) return;
            if (!_hooked)
            {
                UnityEngine.Camera.onPreCull += PreCull;
                UnityEngine.Camera.onPostRender += PostRender;
                _hooked = true;
                _log.Info("sky: CS1 sky replaced on the main camera");
            }
            _camera = cam;
            _cs1Stars = FindCs1StarMaterial();
            _fog = Colour(f.FogColor[0], f.FogColor[1], f.FogColor[2], 1f);

            Vector3 eye = cam.transform.position;
            float far = cam.farClipPlane;
            DayNightProperties dn = DayNightProperties.instance;
            Transform sunT = dn != null ? dn.m_SunLight : null;
            Transform moonT = dn != null ? dn.m_MoonLight : null;
            Vector3 sunDir = dn != null ? dn.sunDir : Vector3.up;

            ColourDome(f, sunDir);
            Graphics.DrawMesh(_dome, Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one * (0.9f * far)), _domeMat, _layer, cam);

            if (_starMat != null && f.StarAlpha > 0.002f)
            {
                SetStarAlpha(f.StarAlpha);
                Quaternion turn = sunT != null ? sunT.rotation : Quaternion.identity;
                Graphics.DrawMesh(_starMesh, Matrix4x4.TRS(eye, turn, Vector3.one * (0.85f * far)), _starMat, _layer, cam);
            }
            Color celestial = new Color(1f, 1f, 1f, f.CelestialAlpha);
            if (sunT != null) DrawDisc(_sunMat, _textures[0], -sunT.forward, sunT.up, SunHalf, celestial, eye, far, cam);
            int moon = f.MoonSlot >= 1 && f.MoonSlot <= 8 ? f.MoonSlot : 1;
            if (moonT != null) DrawDisc(_moonMat, _textures[moon], -moonT.forward, moonT.up, MoonHalf, celestial, eye, far, cam);
            if (f.Clouds && _cloudMat != null && _textures[9] != null) DrawClouds(f, eye, far, cam);
        }

        /// <summary>Stops drawing and gives CS1 its sky back. Idempotent.</summary>
        public void Hide()
        {
            if (!_hooked) return;
            UnityEngine.Camera.onPreCull -= PreCull;
            UnityEngine.Camera.onPostRender -= PostRender;
            _hooked = false;
            RestoreAll();
            _camera = null;
            _log.Info("sky: CS1 sky restored");
        }

        /// <summary>Hides and destroys everything this class made.</summary>
        public void Dispose()
        {
            Hide();
            ClearTextures();
            TextureUtil.Replace(ref _domeMat, null);
            TextureUtil.Replace(ref _starMat, null);
            TextureUtil.Replace(ref _sunMat, null);
            TextureUtil.Replace(ref _moonMat, null);
            TextureUtil.Replace(ref _cloudMat, null);
            TextureUtil.Replace(ref _dome, null);
            TextureUtil.Replace(ref _starMesh, null);
            TextureUtil.Replace(ref _quad, null);
            TextureUtil.Replace(ref _cloudMesh, null);
        }

        private void PreCull(UnityEngine.Camera cam)
        {
            if (cam == null || cam != _camera) return;
            try
            {
                _overriddenCamera = cam;
                _clear.Apply(() => cam.clearFlags, v => cam.clearFlags = v, CameraClearFlags.SolidColor);
                _background.Apply(() => cam.backgroundColor, v => cam.backgroundColor = v, _fog);
                Material stars = _cs1Stars;
                if (stars != null && stars.HasProperty(StarIntensityId))
                {
                    _overriddenStars = stars;
                    _stars.Apply(() => stars.GetFloat(StarIntensityId), v => stars.SetFloat(StarIntensityId, v), 0f);
                }
            }
            catch (Exception e)
            {
                _log.Error("sky: pre-cull override", e);
                RestoreAll();
            }
        }

        private void PostRender(UnityEngine.Camera cam)
        {
            if (cam != null && cam == _overriddenCamera) RestoreAll();
        }

        private void RestoreAll()
        {
            UnityEngine.Camera cam = _overriddenCamera;
            Material stars = _overriddenStars;
            try
            {
                _clear.Restore(v => { if (cam != null) cam.clearFlags = v; });
                _background.Restore(v => { if (cam != null) cam.backgroundColor = v; });
                _stars.Restore(v => { if (stars != null) stars.SetFloat(StarIntensityId, v); });
            }
            catch (Exception e)
            {
                _log.Error("sky: restore", e);
            }
            _overriddenCamera = null;
            _overriddenStars = null;
        }

        // DayNightProperties draws its star field with Graphics.DrawMesh in LateUpdate (DayNightProperties.cs:873-878)
        // using the private m_StarsMaterial, whose _StarIntensity it sets every Update (:827).
        private static readonly FieldInfo s_starsField = typeof(DayNightProperties).GetField("m_StarsMaterial", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Material FindCs1StarMaterial()
        {
            DayNightProperties dn = DayNightProperties.instance;
            return dn != null && s_starsField != null ? s_starsField.GetValue(dn) as Material : null;
        }

        private void DrawDisc(Material mat, Texture2D tex, Vector3 dir, Vector3 up, float half, Color colour, Vector3 eye, float far, UnityEngine.Camera cam)
        {
            if (mat == null || tex == null || dir.sqrMagnitude < 1e-6f) return;
            float d = 0.8f * far;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", tex);
            block.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * colour.a));
            Matrix4x4 m = Matrix4x4.TRS(eye + dir.normalized * d, Quaternion.LookRotation(dir, up), Vector3.one * (2f * half * d));
            Graphics.DrawMesh(_quad, m, mat, _layer, cam, 0, block);
        }

        private void DrawClouds(SkyFrame f, Vector3 eye, float far, UnityEngine.Camera cam)
        {
            Texture2D tex = _textures[9];
            float radius = Mathf.Min(CloudRadius, 0.9f * far);
            float step = 2f * radius / CloudGrid;
            float u0, v0;
            SkyMath.CloudUv(eye.x, -eye.z, f.CloudOffset, tex.width, tex.height, out u0, out v0);
            Color c = Colour(f.CloudColor[0], f.CloudColor[1], f.CloudColor[2], 1f);
            float baseAlpha = SkyMath.Clamp01(f.CloudColor[3]);
            int n = CloudGrid + 1;
            for (int iz = 0; iz < n; iz++)
            {
                for (int ix = 0; ix < n; ix++)
                {
                    int i = iz * n + ix;
                    float dx = -radius + ix * step, dz = -radius + iz * step;
                    _cloudVerts[i] = new Vector3(dx, 0f, dz);
                    _cloudUv[i] = new Vector2(u0 + dx / (SkyMath.CloudCellBlocks * tex.width), v0 + dz / (SkyMath.CloudCellBlocks * tex.height));
                    float r = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                    c.a = baseAlpha * (1f - Mathf.SmoothStep(0f, 1f, (r - 0.6f) / 0.4f));
                    _cloudColours[i] = c;
                }
            }
            _cloudMesh.vertices = _cloudVerts;
            _cloudMesh.uv = _cloudUv;
            _cloudMesh.colors = _cloudColours;
            _cloudMesh.RecalculateBounds();
            _cloudMat.mainTexture = tex;
            Graphics.DrawMesh(_cloudMesh, Matrix4x4.TRS(new Vector3(eye.x, f.CloudHeight, eye.z), Quaternion.identity, Vector3.one), _cloudMat, _layer, cam);
        }

        private void ColourDome(SkyFrame f, Vector3 sunDir)
        {
            var sunH = new Vector2(sunDir.x, sunDir.z);
            bool hasAzimuth = sunH.sqrMagnitude > 1e-6f;
            if (hasAzimuth) sunH.Normalize();
            for (int i = 0; i < _domeDirs.Length; i++)
            {
                Vector3 d = _domeDirs[i];
                var h = new Vector2(d.x, d.z);
                float cosAz = hasAzimuth && h.sqrMagnitude > 1e-6f ? Vector2.Dot(h.normalized, sunH) : 0f;
                SkyMath.DomeColour(d.y, cosAz, f.SkyColor, f.FogColor, f.SunriseColor, _rgb);
                _domeColours[i] = Colour(_rgb[0], _rgb[1], _rgb[2], 1f);
            }
            _dome.colors = _domeColours;
        }

        private void SetStarAlpha(float alpha)
        {
            if (Mathf.Abs(alpha - _starAlphaShown) < 1f / 255f) return;
            _starAlphaShown = alpha;
            Color[] cs = new Color[_starMesh.vertexCount];
            var c = new Color(1f, 1f, 1f, alpha);
            for (int i = 0; i < cs.Length; i++) cs[i] = c;
            _starMesh.colors = cs;
        }

        // Vertex colours are not converted by Unity in linear colour space, unlike material colours.
        private static Color Colour(float r, float g, float b, float a)
        {
            var c = new Color(SkyMath.Clamp01(r), SkyMath.Clamp01(g), SkyMath.Clamp01(b), a);
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        private void EnsureResources()
        {
            if (_materialsTried) return;
            _materialsTried = true;
            Shader colored = Shader.Find("Hidden/Internal-Colored");
            if (colored == null)
            {
                _log.Warn("sky: Shader.Find(\"Hidden/Internal-Colored\") returned null; CS1's sky stays");
                return;
            }
            _domeMat = ColoredMaterial(colored, 1000, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero);
            _starMat = ColoredMaterial(colored, 1001, UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.One);
            _sunMat = ParticleMaterial("Particles/Additive", 1002);
            _moonMat = ParticleMaterial("Particles/Additive", 1003);
            _cloudMat = ParticleMaterial("Particles/Alpha Blended", 1004);
            BuildDome();
            BuildStars();
            BuildQuad();
            int n = (CloudGrid + 1) * (CloudGrid + 1);
            _cloudVerts = new Vector3[n];
            _cloudUv = new Vector2[n];
            _cloudColours = new Color[n];
            _cloudMesh = new Mesh { name = "Skylines.Sky.Clouds", hideFlags = HideFlags.HideAndDontSave };
            _cloudMesh.MarkDynamic();
            _cloudMesh.vertices = _cloudVerts;
            _cloudMesh.triangles = GridTriangles(CloudGrid);
            _log.Info("sky: materials dome/stars " + (_domeMat != null) + "/" + (_starMat != null) + ", sun/moon " + (_sunMat != null) + "/" + (_moonMat != null) + ", clouds " + (_cloudMat != null));
        }

        private static Material ColoredMaterial(Shader s, int queue, UnityEngine.Rendering.BlendMode src, UnityEngine.Rendering.BlendMode dst)
        {
            var m = new Material(s) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            m.SetInt("_SrcBlend", (int)src);
            m.SetInt("_DstBlend", (int)dst);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            return m;
        }

        private Material ParticleMaterial(string shaderName, int queue)
        {
            Shader s = Shader.Find(shaderName);
            if (s == null)
            {
                _log.Warn("sky: Shader.Find(\"" + shaderName + "\") returned null; that part of the sky is not drawn");
                return null;
            }
            var m = new Material(s) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }

        private void BuildDome()
        {
            int n = (Rings + 1) * (Segments + 1);
            _domeDirs = new Vector3[n];
            _domeColours = new Color[n];
            for (int r = 0; r <= Rings; r++)
            {
                float el = Mathf.PI * ((float)r / Rings - 0.5f);
                for (int s = 0; s <= Segments; s++)
                {
                    float az = 2f * Mathf.PI * s / Segments;
                    _domeDirs[r * (Segments + 1) + s] = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
                }
            }
            _dome = new Mesh { name = "Skylines.Sky.Dome", hideFlags = HideFlags.HideAndDontSave };
            _dome.MarkDynamic();
            _dome.vertices = _domeDirs;
            _dome.colors = _domeColours;
            _dome.triangles = Triangles(Rings, Segments);
            _dome.RecalculateBounds();
        }

        private void BuildStars()
        {
            var dirs = new float[3 * StarCount];
            var sizes = new float[StarCount];
            SkyMath.Stars(StarCount, 10842u, dirs, sizes);
            var v = new Vector3[4 * StarCount];
            var tris = new int[6 * StarCount];
            for (int i = 0; i < StarCount; i++)
            {
                var d = new Vector3(dirs[3 * i], dirs[3 * i + 1], dirs[3 * i + 2]);
                Vector3 a = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized * sizes[i];
                Vector3 b = Vector3.Cross(d, a).normalized * sizes[i];
                v[4 * i] = d - a - b;
                v[4 * i + 1] = d + a - b;
                v[4 * i + 2] = d + a + b;
                v[4 * i + 3] = d - a + b;
                int t = 6 * i, k = 4 * i;
                tris[t] = k; tris[t + 1] = k + 1; tris[t + 2] = k + 2;
                tris[t + 3] = k; tris[t + 4] = k + 2; tris[t + 5] = k + 3;
            }
            _starMesh = new Mesh { name = "Skylines.Sky.Stars", hideFlags = HideFlags.HideAndDontSave };
            _starMesh.vertices = v;
            _starMesh.triangles = tris;
            _starMesh.RecalculateBounds();
            _starAlphaShown = -1f;
        }

        private void BuildQuad()
        {
            _quad = new Mesh { name = "Skylines.Sky.Disc", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
        }

        private static int[] GridTriangles(int cells)
        {
            return Triangles(cells, cells);
        }

        private static int[] Triangles(int rows, int cols)
        {
            var t = new int[rows * cols * 6];
            int k = 0, w = cols + 1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int a = r * w + c;
                    t[k++] = a; t[k++] = a + w; t[k++] = a + 1;
                    t[k++] = a + 1; t[k++] = a + w; t[k++] = a + w + 1;
                }
            }
            return t;
        }
    }
}
