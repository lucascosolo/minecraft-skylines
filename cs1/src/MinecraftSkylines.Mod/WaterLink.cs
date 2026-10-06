using System;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Water;
using Skylines.Host;
using Skylines.Host.Terrain;
using UnityEngine;
using Vec3d = MinecraftSkylines.Protocol.Vec3d;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// While the player is in Minecraft, samples the city's water over the 64 x 64 block columns around the player once a
    /// second and sends WATER_SURFACE (minor 10) when the grid changed, so the player swims in the city's water. Main thread only.
    /// </summary>
    internal sealed class WaterLink
    {
        private const double IntervalSeconds = 1.0;
        private const int Size = 64;

        private readonly HostLog _log;
        private readonly float[] _xs = new float[Size], _zs = new float[Size];
        private readonly float[] _surface = new float[Size * Size], _bottom = new float[Size * Size];
        private double _lastSeconds = double.NegativeInfinity;
        private WaterSurface _sent;

        public WaterLink(HostLog log)
        {
            _log = log;
        }

        public void Update(BridgeHost host, PlayerMode player, double nowSeconds)
        {
            Vector3 feet;
            uint flags;
            double ageMs;
            if (host == null || host.State != BridgeState.Connected || host.NegotiatedAppMinor < 10 || !player.IsActive
                || !player.TryLatestState(out feet, out flags, out ageMs))
            {
                // The guest drops its grid on exit and link loss, so the next session starts with a fresh send.
                _lastSeconds = double.NegativeInfinity;
                _sent = null;
                return;
            }
            if (nowSeconds - _lastSeconds < IntervalSeconds) return;
            _lastSeconds = nowSeconds;

            Vec3d mcFeet = MinecraftFrame.CsToMc(new Vec3d { X = feet.x, Y = feet.y, Z = feet.z });
            int originX = WaterColumns.Origin(mcFeet.X, Size), originZ = WaterColumns.Origin(mcFeet.Z, Size);
            for (int i = 0; i < Size; i++)
            {
                // Column centres; CS1 z is Minecraft -z, so row dz samples CS1 z = -(originZ + dz + 0.5).
                _xs[i] = originX + i + 0.5f;
                _zs[i] = -(originZ + i + 0.5f);
            }
            try
            {
                WaterSampler.Sample(_xs, _zs, _surface, _bottom);
            }
            catch (Exception e)
            {
                _log.Error("water: sampling the city's water failed", e);
                return;
            }
            float yOffset = (float)MinecraftFrame.YOffset;
            for (int k = 0; k < _surface.Length; k++)
            {
                _surface[k] += yOffset;
                _bottom[k] += yOffset;
            }
            WaterColumns.Settle(_surface, _bottom, WaterColumns.MinDepth);
            if (_sent != null && _sent.OriginX == originX && _sent.OriginZ == originZ
                && WaterColumns.SameValues(_sent.Surface, _surface) && WaterColumns.SameValues(_sent.Bottom, _bottom)) return;

            var msg = new WaterSurface
            {
                OriginX = originX, OriginZ = originZ, Size = Size,
                Surface = (float[])_surface.Clone(), Bottom = (float[])_bottom.Clone(),
            };
            if (!host.Send(AppProtocol.WaterSurfaceType, msg.Encode())) return;
            int wet = 0;
            for (int k = 0; k < msg.Surface.Length; k++) if (msg.Surface[k] > msg.Bottom[k]) wet++;
            if (_sent == null) _log.Info("water: WATER_SURFACE " + Size + "x" + Size + " at " + originX + "," + originZ + ", " + wet + " wet columns");
            _sent = msg;
        }
    }
}
