using System;
using System.Collections.Generic;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Host;
using Skylines.Host.Geometry;
using UnityEngine;
using Vec3d = MinecraftSkylines.Protocol.Vec3d;
using McLight = MinecraftSkylines.Protocol.LightSource;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// While the player is in Minecraft, sends the props' lights that are on within 64 m as LIGHT_SOURCES (minor 8) once a
    /// second, so Minecraft's light engine lights the player's hand and blocks under the city's street lamps. Main thread only.
    /// </summary>
    internal sealed class LightLink
    {
        private const double IntervalSeconds = 1.0;
        private const float Radius = 64f;

        private readonly HostLog _log;
        private readonly ObstacleGeometry _geometry = new ObstacleGeometry();
        private readonly List<ObstacleGeometry.PropLight> _found = new List<ObstacleGeometry.PropLight>();
        private readonly List<McLight> _candidates = new List<McLight>();
        private double _lastSeconds = double.NegativeInfinity;
        private int _lastCount = -1;

        public LightLink(HostLog log)
        {
            _log = log;
        }

        public void Update(BridgeHost host, PlayerMode player, double nowSeconds)
        {
            Vector3 feet;
            uint flags;
            double ageMs;
            if (host == null || host.State != BridgeState.Connected || host.NegotiatedAppMinor < 8 || !player.IsActive
                || !player.TryLatestState(out feet, out flags, out ageMs))
            {
                _lastSeconds = double.NegativeInfinity;
                return;
            }
            if (nowSeconds - _lastSeconds < IntervalSeconds) return;
            _lastSeconds = nowSeconds;

            _found.Clear();
            _candidates.Clear();
            try
            {
                _geometry.CollectLights(feet.x - Radius, feet.z - Radius, feet.x + Radius, feet.z + Radius, _found);
            }
            catch (Exception e)
            {
                _log.Error("lights: collecting the city's lights failed", e);
                return;
            }
            foreach (ObstacleGeometry.PropLight l in _found)
            {
                float dx = l.Position.x - feet.x, dz = l.Position.z - feet.z;
                int level = LightLevels.FromRange(l.Range, l.Intensity);
                if (dx * dx + dz * dz > Radius * Radius || level == 0) continue;
                Vec3d mc = MinecraftFrame.CsToMc(new Vec3d { X = l.Position.x, Y = l.Position.y, Z = l.Position.z });
                _candidates.Add(new McLight
                {
                    X = (int)Math.Floor(mc.X), Y = (int)Math.Floor(mc.Y), Z = (int)Math.Floor(mc.Z), Level = (byte)level,
                });
            }
            var msg = new LightSources { Lights = LightLevels.Merge(_candidates) };
            if (host.Send(AppProtocol.LightSourcesType, msg.Encode()) && msg.Lights.Length != _lastCount)
            {
                // Logged when the count changes (dusk, dawn, walking into a lit street), not every second.
                _lastCount = msg.Lights.Length;
                _log.Info("lights: LIGHT_SOURCES " + msg.Lights.Length + " lit positions within " + Radius + " m (" + _found.Count + " lights on in the search box)");
            }
        }
    }
}
