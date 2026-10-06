using System;
using Skylines.Host;
using UnityEngine;

namespace MinecraftSkylines.Mod.Underground
{
    /// <summary>
    /// Applies an <see cref="UndergroundMode"/> to the main camera and TransportManager each frame while in Minecraft
    /// mode. <see cref="Begin"/> records the camera's culling mask and TunnelsVisible; <see cref="End"/> restores both
    /// exactly, is idempotent and never throws. Main thread only.
    /// </summary>
    internal sealed class UndergroundRenderer
    {
        private const double LogGapMs = 1000;

        private readonly HostLog _log;
        private Camera _camera;
        private int _originalMask;
        // World-space overlays that belong to the city-builder view, not a first-person view (owner, 2026-10-06:
        // "the whole screen will just be my first person view of the world with the Minecraft overlay"):
        // problem icons over buildings, traffic direction arrows, markers. Missing layer names are skipped.
        private static readonly string[] FirstPersonHiddenLayers = { "Notifications", "DirectionArrows", "Markers", "ScenarioMarkers" };
        private int _hiddenMask;
        private bool _originalTunnels;
        private int _layer = -1;
        private bool _active;
        private bool _underground;
        private double _lastLogMs = -1e9;
        private int _suppressed;

        public UndergroundRenderer(HostLog log, int mode)
        {
            _log = log;
            Mode = mode;
        }

        public int Mode { get; private set; }

        /// <summary>True while the eye is underground (as of the last <see cref="Apply"/>).</summary>
        public bool Underground { get { return _underground; } }

        public void SetMode(int mode)
        {
            if (!UndergroundMode.IsValid(mode)) return;
            Mode = mode;
            _log.Info("underground: mode " + UndergroundMode.Describe(mode));
        }

        /// <summary>Records the main camera's mask and TunnelsVisible. Idempotent.</summary>
        public void Begin()
        {
            if (_active) return;
            _camera = Camera.main;
            _layer = LayerMask.NameToLayer("MetroTunnels");
            _originalMask = _camera == null ? 0 : _camera.cullingMask;
            _hiddenMask = 0;
            foreach (string name in FirstPersonHiddenLayers)
            {
                int l = LayerMask.NameToLayer(name);
                if (l >= 0) _hiddenMask |= 1 << l;
            }
            _originalTunnels = TransportManager.exists && TransportManager.instance.TunnelsVisible;
            _underground = false;
            _active = true;
            if (_layer < 0) _log.Warn("underground: layer MetroTunnels not found");
        }

        /// <summary>Sets this frame's camera mask and tunnel visibility for an eye at <paramref name="eye"/>.</summary>
        public void Apply(Vector3 eye, double nowMs)
        {
            if (!_active || _camera == null) return;
            bool under = TerrainManager.exists
                && UndergroundMode.IsUnderground(eye.y, TerrainManager.instance.SampleDetailHeightSmooth(eye));
            if (under != _underground)
            {
                _underground = under;
                Transition(nowMs, eye.y);
            }
            int mask = _originalMask & ~_hiddenMask;
            if (_layer >= 0 && UndergroundMode.WantsLayer(Mode, under)) mask |= 1 << _layer;
            _camera.cullingMask = mask;
            if (TransportManager.exists)
            {
                // Mode 3 drives the game's view; otherwise leave whatever was there at Begin.
                TransportManager.instance.TunnelsVisible = Mode == 3 ? UndergroundMode.WantsTunnelsVisible(Mode, under) : _originalTunnels;
            }
        }

        /// <summary>Restores the camera mask and TunnelsVisible recorded by <see cref="Begin"/>. Never throws.</summary>
        public void End()
        {
            if (!_active) return;
            _active = false;
            _underground = false;
            try { if (_camera != null) _camera.cullingMask = _originalMask; }
            catch (Exception e) { _log.Error("underground: restore culling mask", e); }
            try { if (TransportManager.exists) TransportManager.instance.TunnelsVisible = _originalTunnels; }
            catch (Exception e) { _log.Error("underground: restore TunnelsVisible", e); }
            _camera = null;
        }

        public string OverlayText()
        {
            return "Underground [Ctrl+Shift+N]: mode " + UndergroundMode.Describe(Mode) + "; player underground: " + (_underground ? "yes" : "no");
        }

        private void Transition(double nowMs, float eyeY)
        {
            if (nowMs - _lastLogMs < LogGapMs)
            {
                _suppressed++;
                return;
            }
            _log.Info("underground: eye y " + eyeY.ToString("0.0") + " is now " + (_underground ? "underground" : "above ground")
                + (_suppressed > 0 ? " (" + _suppressed + " quick transitions not logged)" : ""));
            _lastLogMs = nowMs;
            _suppressed = 0;
        }
    }
}
