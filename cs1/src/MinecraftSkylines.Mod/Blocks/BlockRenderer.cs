using System;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Host;
using Skylines.Host.Rendering;
using UnityEngine;
using UInput = UnityEngine.Input;

namespace MinecraftSkylines.Mod.Blocks
{
    /// <summary>
    /// Draws the Minecraft section meshes (protocol 1.2) in the city scene on the Props layer, in city and
    /// Minecraft mode. The material is an open experiment: launch.cfg <c>block_material</c> picks the start
    /// variant, Ctrl+Shift+B cycles it. Cutout/translucent vertex flags are ignored for now. Main thread only.
    /// </summary>
    internal sealed class BlockRenderer : IDisposable
    {
        public const int VariantCount = 4;
        private const double BuildBudgetMs = 4.0;
        private static readonly string[] VariantNames =
        {
            "prop shader, atlas only", "prop shader + neutral XYS/ACI", "building shader + neutral XYS/ACI", "Unity Diffuse",
        };

        private readonly HostLog _log;
        private readonly MeshStore _store;
        private readonly Material[] _materials = new Material[VariantCount];
        private readonly bool[] _materialFailed = new bool[VariantCount];
        private Texture2D _atlas, _xys, _aci;

        /// <summary>Raised per SECTION_MESH after it is queued: sx, sy, sz, vertex count.</summary>
        public event Action<int, int, int, int> SectionReceived;

        public BlockRenderer(HostLog log, int variant)
        {
            _log = log;
            int layer = LayerMask.NameToLayer("Props");
            _store = new MeshStore(layer >= 0 ? layer : 10, BuildBudgetMs);
            Variant = Math.Max(0, Math.Min(VariantCount - 1, variant));
            _log.Info("blocks: Props layer " + layer + ", material variant " + Variant + " (" + VariantNames[Variant] + ")");
        }

        public int Variant { get; private set; }
        public string VariantName { get { return VariantNames[Variant]; } }
        public MeshStore Store { get { return _store; } }
        public int AtlasWidth { get; private set; }
        public int AtlasHeight { get; private set; }
        public long SectionsReceived { get; private set; }
        public long VerticesReceived { get; private set; }

        public static long Key(int sx, int sy, int sz)
        {
            // 24 bits for x and z (|s| < 2^23 sections), 16 for y.
            return ((long)(sx & 0xFFFFFF) << 40) | ((long)(sz & 0xFFFFFF) << 16) | (long)(sy & 0xFFFF);
        }

        /// <summary>Handles 1.2 guest messages; returns false for any other type.</summary>
        public bool Handle(ushort type, byte[] payload)
        {
            switch (type)
            {
                case AppProtocol.BlockAtlasType:
                    OnAtlas(BlockAtlas.Decode(payload));
                    return true;
                case AppProtocol.SectionMeshType:
                    OnSection(SectionMesh.Decode(payload));
                    return true;
                case AppProtocol.SectionsClearType:
                    _store.Clear();
                    _log.Info("blocks: SECTIONS_CLEAR");
                    return true;
                case AppProtocol.AtlasRegionType:
                    return true; // optional in 1.2; animated sprites stay on their first frame
                default:
                    return false;
            }
        }

        public void SetVariant(int v)
        {
            Variant = ((v % VariantCount) + VariantCount) % VariantCount;
            _store.Material = MaterialFor(Variant);
            _log.Info("blocks: material variant " + Variant + " (" + VariantName + ")" + (_store.Material == null ? " unavailable, blocks hidden" : ""));
        }

        // Sun-contrast softening for blocks (owner, 2026-10-06: sunlit sides too bright, shaded sides too dark,
        // but the light should still come from the sun). Ctrl+Shift+L cycles; Ctrl+Shift+L is not forwarded to
        // Minecraft (PlayerMode), where plain L would open the advancements screen.
        private static readonly float[] SoftenSteps = { 0f, 0.3f, 0.5f, 0.7f };
        private int _soften = 2;

        public string SoftenText { get { return "softening " + SoftenSteps[_soften].ToString("0.0") + "  [Ctrl+Shift+L]"; } }

        public void Update()
        {
            _store.NormalUpBend = SoftenSteps[_soften];
            if (UInput.GetKeyDown(KeyCode.L)
                && (UInput.GetKey(KeyCode.LeftControl) || UInput.GetKey(KeyCode.RightControl))
                && (UInput.GetKey(KeyCode.LeftShift) || UInput.GetKey(KeyCode.RightShift))
                && !ColossalFramework.UI.UIView.HasInputFocus())
            {
                _soften = (_soften + 1) % SoftenSteps.Length;
                _log.Info("blocks: lighting softening " + SoftenSteps[_soften].ToString("0.0") + " (normals bent toward up)");
            }
            if (UInput.GetKeyDown(KeyCode.B)
                && (UInput.GetKey(KeyCode.LeftControl) || UInput.GetKey(KeyCode.RightControl))
                && (UInput.GetKey(KeyCode.LeftShift) || UInput.GetKey(KeyCode.RightShift))
                && !ColossalFramework.UI.UIView.HasInputFocus())
                SetVariant(Variant + 1);
        }

        /// <summary>Exceptions caught while building or drawing, and the last message.</summary>
        public int Errors { get; private set; }
        public string LastError { get; private set; }

        public void LateUpdate(bool draw)
        {
            try
            {
                if (_store.Material == null && _atlas != null) _store.Material = MaterialFor(Variant);
                _store.LateUpdate(draw);
            }
            catch (Exception e)
            {
                if (Errors++ == 0) _log.Error("blocks: build/draw", e);
                LastError = e.Message;
            }
        }

        public string OverlayText()
        {
            if (SectionsReceived == 0 && _atlas == null) return "";
            return "Blocks: " + _store.Count + " sections, " + _store.VertexCount + " vertices, atlas " + AtlasWidth + "x" + AtlasHeight
                + "; material " + Variant + " (" + VariantName + ")  [Ctrl+Shift+B]; " + SoftenText;
        }

        public void Dispose()
        {
            _store.Dispose();
            for (int i = 0; i < VariantCount; i++) TextureUtil.Replace(ref _materials[i], null);
            TextureUtil.Replace(ref _atlas, null);
            TextureUtil.Replace(ref _xys, null);
            TextureUtil.Replace(ref _aci, null);
        }

        private void OnAtlas(BlockAtlas a)
        {
            if (a.Format != BlockAtlas.FormatPng) { _log.Warn("blocks: atlas format " + a.Format + " ignored"); return; }
            Texture2D t = TextureUtil.LoadPng(a.Data, "MinecraftSkylines.BlockAtlas");
            if (t == null) { _log.Warn("blocks: atlas PNG (" + a.Data.Length + " bytes) did not decode"); return; }
            TextureUtil.Replace(ref _atlas, t);
            AtlasWidth = t.width;
            AtlasHeight = t.height;
            foreach (Material m in _materials)
                if (m != null) m.SetTexture("_MainTex", _atlas);
            _log.Info("blocks: atlas " + t.width + "x" + t.height + " (" + a.Width + "x" + a.Height + " announced), " + a.Data.Length + " PNG bytes");
        }

        private void OnSection(SectionMesh m)
        {
            SectionsReceived++;
            VerticesReceived += m.VertexCount;
            long key = Key(m.Sx, m.Sy, m.Sz);
            if (m.VertexCount == 0) _store.Remove(key);
            else
            {
                CsMeshData d = BlockMeshConversion.Convert(m);
                _store.Put(key, new Vector3((float)d.OriginX, (float)d.OriginY, (float)d.OriginZ), d.Positions, d.Uvs, d.Colors, d.Indices);
            }
            Action<int, int, int, int> h = SectionReceived;
            if (h != null) h(m.Sx, m.Sy, m.Sz, m.VertexCount);
        }

        private Material MaterialFor(int v)
        {
            if (_materials[v] != null || _materialFailed[v]) return _materials[v];
            string shaderName = v == 2 ? "Custom/Buildings/Building/Default" : v == 3 ? "Diffuse" : "Custom/Props/Prop/Default";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                _materialFailed[v] = true;
                _log.Warn("blocks: Shader.Find(\"" + shaderName + "\") returned null; variant " + v + " cannot draw");
                return null;
            }
            var m = new Material(shader) { name = "MinecraftSkylines.Blocks." + v };
            if (_atlas != null) m.SetTexture("_MainTex", _atlas);
            if (v != 3) m.SetColor("_Color", Color.white);
            if (v == 1 || v == 2)
            {
                // Neutral maps as the game's asset importer builds them when no source map exists
                // (AssetImporterTextureLoader.CombineChannels with its ResultDefaults): XYS = flat normal
                // (0.5, 0.5) and B = 1 - specular = 1; ACI = R 1 - alpha = 0 (opaque), G 1 - colour mask = 0,
                // B illumination = 0. Both linear, as the importer's ResultLinear marks them.
                if (_xys == null) _xys = TextureUtil.Solid(new Color32(128, 128, 255, 255), true, "MinecraftSkylines.NeutralXYS");
                if (_aci == null) _aci = TextureUtil.Solid(new Color32(0, 0, 0, 255), true, "MinecraftSkylines.NeutralACI");
                m.SetTexture("_XYSMap", _xys);
                m.SetTexture("_ACIMap", _aci);
            }
            _materials[v] = m;
            return m;
        }
    }
}
