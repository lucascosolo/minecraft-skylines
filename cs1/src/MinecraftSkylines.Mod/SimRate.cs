using System;
using ColossalFramework;
using Skylines.Host;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Slows the city simulation while in Minecraft mode, so walkers and cars move at about real speed in first person
    /// (owner, 2026-10-06: "all the people are sprinting by 2-3x faster than a normal person despite their animation
    /// looking like a casual stroll"). CS1 advances its simulation by Time.deltaTime / Time.fixedDeltaTime frames per
    /// frame (SimulationManager.Update), so stretching Time.fixedDeltaTime by 1 / rate slows everything the simulation
    /// moves, while animations keep their own speed. The sun and the calendar keep CS1's normal pace (owner, same day:
    /// "I would like the cities skylines days to keep progressing at a natural rate even when I'm in Minecraft mode"):
    /// <see cref="Tick"/> adds the frames the slowdown withheld to SimulationManager's day-time and date offsets
    /// (m_dayTimeOffsetFrames, m_timeOffsetTicks, from which it derives both every frame, SimulationManager.cs:664-674).
    /// <see cref="End"/> restores the fixed step exactly; idempotent and never throws. Main thread only.
    /// </summary>
    internal sealed class SimRate
    {
        private readonly HostLog _log;
        private float _saved;
        private bool _active;
        private float _rate;
        private uint _startFrame;
        private uint _added;

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
                _rate = rate;
                _startFrame = SimulationManager.exists ? Singleton<SimulationManager>.instance.m_referenceFrameIndex : 0u;
                _added = 0;
                _active = true;
                _log.Info("sim rate: city simulation at " + rate.ToString("0.00") + "x in Minecraft mode (fixed step " + _saved.ToString("0.0000") + " -> " + Time.fixedDeltaTime.ToString("0.0000") + " s)");
            }
            catch (Exception e)
            {
                _log.Error("sim rate: begin", e);
            }
        }

        /// <summary>Per frame while active: keeps the sun and the date at normal pace.</summary>
        public void Tick()
        {
            if (!_active || !SimulationManager.exists) return;
            try
            {
                SimulationManager sim = Singleton<SimulationManager>.instance;
                uint ran = sim.m_referenceFrameIndex - _startFrame;
                uint owed = (uint)(ran / (double)_rate) - ran;
                if (owed <= _added) return;
                uint extra = owed - _added;
                _added = owed;
                sim.m_dayTimeOffsetFrames += extra;
                sim.m_timeOffsetTicks += extra * sim.m_timePerFrame.Ticks;
            }
            catch (Exception e)
            {
                _log.Error("sim rate: day clock", e);
                _active = false;
                Time.fixedDeltaTime = _saved;
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
