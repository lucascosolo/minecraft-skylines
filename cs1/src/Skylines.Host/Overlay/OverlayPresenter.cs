using System;
using System.Diagnostics;
using UnityEngine;

namespace Skylines.Host.Overlay
{
    /// <summary>
    /// Shows frames from a <see cref="SharedOverlayReader"/> full-screen over the game: uploads the front slot
    /// straight from the mapping into an RGBA32 texture (no managed copy, no per-pixel loop) and draws it from
    /// OnGUI. Pixels are premultiplied, so drawing uses Blend One OneMinusSrcAlpha through the built-in
    /// <see cref="PremultipliedShader"/> when the build contains it; otherwise GUI.DrawTexture's straight-alpha
    /// blend (translucent pixels come out darker). Row order is handled by flipping the UVs. Main thread only.
    /// </summary>
    public sealed class OverlayPresenter : IDisposable
    {
        /// <summary>Unity built-in: Blend One OneMinusSrcAlpha, output = vertex colour * texture * vertex alpha.</summary>
        public const string PremultipliedShader = "Particles/Alpha Blended Premultiply";

        private readonly Stopwatch _watch = new Stopwatch();
        private Texture2D _tex;
        private Material _mat;
        private bool _shaderTried;
        private bool _bottomUp;

        /// <summary>Set to true to force the straight-alpha GUI.DrawTexture path.</summary>
        public bool ForceStraightAlpha;

        /// <summary>Frames uploaded.</summary>
        public long Uploads { get; private set; }
        /// <summary>Upload time of the latest frame, milliseconds.</summary>
        public double LastUploadMs { get; private set; }
        /// <summary>Longest upload, milliseconds.</summary>
        public double MaxUploadMs { get; private set; }
        /// <summary>Sum of upload times, milliseconds.</summary>
        public double TotalUploadMs { get; private set; }
        /// <summary>Size of the latest frame.</summary>
        public int Width { get; private set; }
        /// <summary>Size of the latest frame.</summary>
        public int Height { get; private set; }
        /// <summary>The latest frame's id.</summary>
        public ulong FrameId { get; private set; }
        /// <summary>True once a frame was uploaded and not cleared since.</summary>
        public bool HasFrame { get; private set; }

        /// <summary>How the overlay is blended, for diagnostics.</summary>
        public string BlendDescription
        {
            get { return _mat != null && !ForceStraightAlpha ? "premultiplied (" + PremultipliedShader + ")" : "straight alpha (GUI.DrawTexture fallback)"; }
        }

        /// <summary>Copies the frame into the texture; call right after Acquire returned true, before the next Acquire.</summary>
        public void Upload(OverlayFrame frame)
        {
            _watch.Reset();
            _watch.Start();
            if (_tex == null || _tex.width != frame.Width || _tex.height != frame.Height)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                // Point filtering: the guest renders at exactly the host's viewport size, so texels map 1:1 to
                // screen pixels and GUI text stays crisp; bilinear would only blur during a resize.
                _tex = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false);
                _tex.filterMode = FilterMode.Point;
                _tex.wrapMode = TextureWrapMode.Clamp;
                _tex.hideFlags = HideFlags.HideAndDontSave;
            }
            _tex.LoadRawTextureData(frame.Pixels, frame.ByteCount);
            _tex.Apply(false, false);
            _bottomUp = frame.RowsBottomUp;
            Width = frame.Width;
            Height = frame.Height;
            FrameId = frame.FrameId;
            HasFrame = true;
            _watch.Stop();
            LastUploadMs = _watch.Elapsed.TotalMilliseconds;
            TotalUploadMs += LastUploadMs;
            if (LastUploadMs > MaxUploadMs) MaxUploadMs = LastUploadMs;
            Uploads++;
        }

        /// <summary>Stops showing the current frame (the texture is kept for reuse).</summary>
        public void Clear()
        {
            HasFrame = false;
        }

        /// <summary>Draws the latest frame over the whole screen. Call from OnGUI; draws on Repaint only.</summary>
        public void Draw()
        {
            if (!HasFrame || _tex == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            if (!_shaderTried)
            {
                _shaderTried = true;
                Shader s = Shader.Find(PremultipliedShader);
                if (s != null)
                {
                    _mat = new Material(s);
                    _mat.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            // Texture row 0 is the bottom. Top-down data (flag clear) lands upside down, so flip the V range.
            var uv = _bottomUp ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 1f, 1f, -1f);
            if (_mat != null && !ForceStraightAlpha) Graphics.DrawTexture(screen, _tex, uv, 0, 0, 0, 0, Color.white, _mat);
            else GUI.DrawTextureWithTexCoords(screen, _tex, uv, true);
        }

        /// <summary>Destroys the texture and material (Unity objects, not files).</summary>
        public void Dispose()
        {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            _tex = null;
            _mat = null;
            HasFrame = false;
        }
    }
}
