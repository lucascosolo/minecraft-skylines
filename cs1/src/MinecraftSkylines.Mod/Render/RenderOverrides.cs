using System;
using Skylines.Host;
using Skylines.Host.Camera;
using UnityEngine;

namespace MinecraftSkylines.Mod.Render
{
    /// <summary>
    /// What Minecraft mode changes in the renderer besides the camera pose, while CameraController (which normally
    /// sets all of it each frame) is disabled: clip planes from the <see cref="ClipPreset"/>, RenderManager.CameraHeight
    /// (feeds vehicle and citizen render distance; the orbit formula gives a tiny value at eye height) and
    /// QualitySettings.shadowDistance. <see cref="Begin"/> records CameraHeight and shadowDistance; <see cref="End"/> restores
    /// them, is idempotent and never throws. Near/far are restored by <see cref="CameraTakeover"/>. Main thread only.
    /// </summary>
    internal sealed class RenderOverrides
    {
        private const float CameraHeight = 300f;
        private const float ShadowDistance = 250f;

        private readonly HostLog _log;
        private bool _active;
        private float _originalCameraHeight;
        private float _originalShadowDistance;

        public RenderOverrides(HostLog log, int preset)
        {
            _log = log;
            Preset = preset;
        }

        public int Preset { get; private set; }

        public void SetPreset(int preset)
        {
            if (!ClipPreset.IsValid(preset)) return;
            Preset = preset;
        }

        public void Begin()
        {
            if (_active) return;
            _originalCameraHeight = RenderManager.exists ? RenderManager.instance.CameraHeight : 0f;
            _originalShadowDistance = QualitySettings.shadowDistance;
            _active = true;
        }

        /// <summary>Applies this frame's overrides; call after <see cref="CameraTakeover.Drive"/>.</summary>
        public void Apply(CameraTakeover camera)
        {
            if (!_active) return;
            camera.SetClip(ClipPreset.Near(Preset), ClipPreset.Far(Preset, camera.CityFar));
            if (RenderManager.exists) RenderManager.instance.CameraHeight = CameraHeight;
            QualitySettings.shadowDistance = ShadowDistance;
        }

        public string OverlayText(CameraTakeover camera)
        {
            return "Clip [Ctrl+Shift+F]: preset " + Preset + ", " + ClipPreset.Describe(Preset, camera.CityFar);
        }

        public void End()
        {
            if (!_active) return;
            _active = false;
            try { if (RenderManager.exists) RenderManager.instance.CameraHeight = _originalCameraHeight; }
            catch (Exception e) { _log.Error("render: restore CameraHeight", e); }
            try { QualitySettings.shadowDistance = _originalShadowDistance; }
            catch (Exception e) { _log.Error("render: restore shadowDistance", e); }
        }
    }
}
