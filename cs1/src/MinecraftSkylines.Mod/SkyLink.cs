using System;
using System.Diagnostics;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Sky;
using Skylines.Host;
using Skylines.Host.Rendering;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Minecraft's sky in Minecraft mode (protocol 1.9; owner, 2026-10-06): keeps the newest SKY_STATE and the
    /// SKY_TEXTURES images and, while the player is in Minecraft with a fresh state, has <see cref="SkyRenderer"/> draw
    /// it in place of CS1's sky, sun and moon at CS1's own light directions. Main thread only.
    /// </summary>
    internal sealed class SkyLink : IDisposable
    {
        private readonly HostLog _log;
        private readonly SkyRenderer _renderer;
        private readonly Stopwatch _age = new Stopwatch();
        private readonly SkyFrame _frame = new SkyFrame();
        private SkyState _state;

        public SkyLink(HostLog log)
        {
            _log = log;
            _renderer = new SkyRenderer(log);
        }

        /// <summary>Handles SKY_STATE and SKY_TEXTURES; false for any other type.</summary>
        public bool Handle(ushort type, byte[] payload)
        {
            if (type == AppProtocol.SkyStateType)
            {
                if (_state == null) _log.Info("sky: first SKY_STATE");
                _state = SkyState.Decode(payload);
                _age.Reset();
                _age.Start();
                return true;
            }
            if (type != AppProtocol.SkyTexturesType) return false;
            SkyTextures m = SkyTextures.Decode(payload);
            _renderer.ClearTextures();
            int loaded = 0;
            foreach (SkyTexture t in m.Textures)
            {
                int slot = SkyMath.TextureSlot(t.Kind, t.Phase);
                if (slot < 0 || t.Format != SkyTextures.FormatPng) continue;
                Texture2D tex = TextureUtil.LoadPng(t.Data, "MinecraftSkylines.Sky." + slot);
                if (tex == null) { _log.Warn("sky: texture kind " + t.Kind + " phase " + t.Phase + " did not decode"); continue; }
                _renderer.SetTexture(slot, tex);
                loaded++;
            }
            _log.Info("sky: SKY_TEXTURES " + loaded + " of " + m.Textures.Length + " images loaded");
            return true;
        }

        public void LateUpdate(BridgeHost host, bool minecraftMode)
        {
            SkyState s = _state;
            bool draw = minecraftMode && s != null && (s.Flags & SkyState.FlagSky) != 0
                && host != null && host.State == BridgeState.Connected && host.NegotiatedAppMinor >= 9
                && SkyMath.IsFresh(_age.Elapsed.TotalSeconds);
            if (!draw)
            {
                _renderer.Hide();
                return;
            }
            try
            {
                Array.Copy(s.SkyColor, _frame.SkyColor, 3);
                Array.Copy(s.FogColor, _frame.FogColor, 3);
                Array.Copy(s.SunriseColor, _frame.SunriseColor, 4);
                Array.Copy(s.CloudColor, _frame.CloudColor, 4);
                _frame.StarAlpha = SkyMath.StarAlpha(s.StarBrightness, s.RainLevel);
                _frame.CelestialAlpha = SkyMath.CelestialAlpha(s.RainLevel);
                _frame.MoonSlot = SkyMath.MoonSlot(s.MoonPhase);
                _frame.Clouds = (s.Flags & SkyState.FlagClouds) != 0;
                _frame.CloudHeight = s.CloudHeight - (float)MinecraftFrame.YOffset;
                _frame.CloudOffset = SkyMath.CloudOffsetAt(s.CloudOffset, s.CloudSpeed, _age.Elapsed.TotalSeconds);
                _renderer.Draw(_frame);
            }
            catch (Exception e)
            {
                _log.Error("sky: draw", e);
                _renderer.Hide();
            }
        }

        /// <summary>Gives CS1 its sky back and forgets the state (level unload, link lost).</summary>
        public void Reset()
        {
            _renderer.Hide();
            _state = null;
        }

        public void Dispose()
        {
            _renderer.Dispose();
            _state = null;
        }
    }
}
