using System;
using Skylines.Host;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Slows the city simulation while in Minecraft mode, so walkers and cars move at about real speed in first person
    /// (owner, 2026-10-06: "all the people are sprinting by 2-3x faster than a normal person despite their animation
    /// looking like a casual stroll"). CS1 advances its simulation by Time.deltaTime / Time.fixedDeltaTime frames per
    /// frame (SimulationManager.Update), so stretching Time.fixedDeltaTime by 1 / rate slows everything the simulation
    /// moves, the day clock included, while animations keep their own speed. <see cref="End"/> restores the recorded
    /// value exactly; idempotent and never throws. Main thread only.
    /// </summary>
    internal sealed class SimRate
    {
        private readonly HostLog _log;
        private float _saved;
        private bool _active;

        public SimRate(HostLog log)
        {
            _log = log;
        }

        public void Begin(float rate)
        {
            if (_active) return;
            if (!(rate >= 0.1f && rate < 0.999f)) return;
            try
            {
                _saved = Time.fixedDeltaTime;
                Time.fixedDeltaTime = _saved / rate;
                _active = true;
                _log.Info("sim rate: city simulation at " + rate.ToString("0.00") + "x in Minecraft mode (fixed step " + _saved.ToString("0.0000") + " -> " + Time.fixedDeltaTime.ToString("0.0000") + " s)");
            }
            catch (Exception e)
            {
                _log.Error("sim rate: begin", e);
            }
        }

        public void End()
        {
            if (!_active) return;
            _active = false;
            try
            {
                Time.fixedDeltaTime = _saved;
                _log.Info("sim rate: normal speed restored");
            }
            catch (Exception e)
            {
                _log.Error("sim rate: restore", e);
            }
        }
    }
}
