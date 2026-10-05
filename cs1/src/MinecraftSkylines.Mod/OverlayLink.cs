using System;
using System.Text;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Host;
using Skylines.Host.Overlay;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Minecraft's GUI overlay (app minor 3): sends VIEWPORT, maps the file of each OVERLAY_OFFER, takes new
    /// frames once per host frame while in Minecraft mode and draws them full-screen. Main thread only.
    /// </summary>
    internal sealed class OverlayLink : IDisposable
    {
        private readonly HostLog _log;
        private readonly OverlayPresenter _presenter = new OverlayPresenter();
        private SharedOverlayReader _reader;
        private int _sentWidth = -1, _sentHeight = -1;
        private bool _wasPlayerOn;
        private bool _visible;
        private string _note = "";

        public OverlayLink(HostLog log)
        {
            _log = log;
            _presenter.ForceStraightAlpha = Environment.GetEnvironmentVariable("MCSKYLINES_OVERLAY_BLEND") == "straight";
        }

        public long Offers { get; private set; }
        public bool Mapped { get { return _reader != null; } }
        public long FramesAcquired { get { return _reader == null ? 0 : _reader.FramesAcquired; } }
        public OverlayPresenter Presenter { get { return _presenter; } }

        /// <summary>Per frame from the pump's Update, after bridge events.</summary>
        public void Tick(BridgeHost host, bool playerOn)
        {
            if (host != null && host.State == BridgeState.Connected && host.NegotiatedAppMinor >= 3)
            {
                int w = Screen.width, h = Screen.height;
                if (w != _sentWidth || h != _sentHeight || (playerOn && !_wasPlayerOn))
                {
                    if (host.Send(AppProtocol.ViewportType, new Viewport { Width = (uint)w, Height = (uint)h }.Encode()))
                    {
                        _sentWidth = w;
                        _sentHeight = h;
                    }
                }
            }
            _wasPlayerOn = playerOn;
            if (playerOn && _reader != null)
            {
                try
                {
                    if (_reader.Acquire()) _presenter.Upload(_reader.Front);
                }
                catch (Exception e)
                {
                    _log.Error("overlay frame", e);
                    Unmap("error: " + e.Message);
                }
            }
            _visible = playerOn && _reader != null && _presenter.HasFrame;
        }

        /// <summary>From the pump's OnGUI, before the status box.</summary>
        public void Draw()
        {
            if (_visible) _presenter.Draw();
        }

        public void OnOffer(OverlayOffer offer)
        {
            Offers++;
            Unmap(null);
            try
            {
                var r = SharedOverlayReader.Open(offer.Path);
                if (r.Generation != offer.Generation || r.MaxWidth != offer.MaxWidth || r.MaxHeight != offer.MaxHeight || offer.SlotCount != 3)
                {
                    r.Close();
                    _note = "offer does not match its file (generation " + offer.Generation + ")";
                    _log.Warn("overlay: " + _note);
                    return;
                }
                _reader = r;
                _note = "";
                _log.Info("overlay: mapped " + offer.Path + " (" + r.MaxWidth + "x" + r.MaxHeight + ", generation " + r.Generation
                    + "); blend " + _presenter.BlendDescription);
            }
            catch (Exception e)
            {
                _note = "cannot map " + offer.Path + ": " + e.Message;
                _log.Warn("overlay: " + _note);
            }
        }

        public void OnStop()
        {
            Unmap("guest stopped the overlay");
        }

        /// <summary>Link lost: unmap, and send VIEWPORT again on the next connection.</summary>
        public void OnDisconnect()
        {
            Unmap("link lost");
            _sentWidth = _sentHeight = -1;
        }

        public string OverlayText()
        {
            if (_reader == null && _note.Length == 0) return "";
            var sb = new StringBuilder("GUI overlay: ");
            if (_reader != null)
            {
                sb.Append(_presenter.Width).Append('x').Append(_presenter.Height).Append(", ").Append(_reader.FramesAcquired)
                  .Append(" frames, upload avg ").Append((_presenter.Uploads == 0 ? 0 : _presenter.TotalUploadMs / _presenter.Uploads).ToString("0.00"))
                  .Append(" ms, max ").Append(_presenter.MaxUploadMs.ToString("0.00")).Append(" ms");
            }
            else sb.Append("off");
            if (_note.Length > 0) sb.Append("  (").Append(_note).Append(')');
            return sb.ToString();
        }

        public void Dispose()
        {
            Unmap(null);
            _presenter.Dispose();
        }

        private void Unmap(string why)
        {
            _presenter.Clear();
            _visible = false;
            if (_reader == null) return;
            _reader.Close();
            _reader = null;
            if (why != null)
            {
                _note = why;
                _log.Info("overlay: unmapped (" + why + ")");
            }
        }
    }
}
