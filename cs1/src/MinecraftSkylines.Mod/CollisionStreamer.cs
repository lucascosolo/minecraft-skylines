using System;
using System.Diagnostics;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Geometry;
using Skylines.Core.Streaming;
using Skylines.Host;
using Skylines.Host.Geometry;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Streams terrain, roads and bridges around the player to Minecraft as COLLISION_REGION frames. Regions are 16 x 16
    /// Minecraft blocks; each is built in CS1 coordinates over the matching CS1 rectangle, converted to Minecraft
    /// coordinates and sent nearest first. Main thread only.
    /// </summary>
    public sealed class CollisionStreamer
    {
        private const float RegionSize = 16f;
        private const float TerrainStep = 2f;

        private readonly HostLog _log;
        private readonly RegionGrid _grid = new RegionGrid(RegionSize);
        private readonly RegionStreamPlanner _planner;
        private readonly TerrainSampler _terrain = new TerrainSampler();
        private readonly NetGeometry _net = new NetGeometry();
        private readonly TriangleBuffer _buffer = new TriangleBuffer();
        private int _regionsSent;
        private long _trianglesSent;
        private double _lastBuildMs;
        private const int MaxBuildAttempts = 3;
        private readonly System.Collections.Generic.Dictionary<long, int> _failures = new System.Collections.Generic.Dictionary<long, int>();

        public CollisionStreamer(HostLog log)
        {
            _log = log;
            _planner = new RegionStreamPlanner(_grid, 64f, 96f);
        }

        /// <summary>The current collision epoch (every region sent carries it).</summary>
        public uint Epoch { get { return _planner.Epoch; } }

        /// <summary>Forgets every sent region, starts a new epoch and returns the COLLISION_RESET payload to send.</summary>
        public byte[] Reset()
        {
            _planner.Reset();
            _failures.Clear();
            return new CollisionReset { Epoch = _planner.Epoch }.Encode();
        }

        /// <summary>
        /// Plans regions around the player (radius 64 m, evict 96 m), then builds and sends them nearest first until
        /// <paramref name="budgetMs"/> is used; always at least one region when one is due.
        /// </summary>
        public void Tick(Vector3 csFeet, BridgeHost host, double budgetMs)
        {
            float mcX = csFeet.x, mcZ = -csFeet.z;
            var clock = Stopwatch.StartNew();
            do
            {
                var due = _planner.Next(mcX, mcZ, 1);
                if (due.Count == 0) return;
                int rx, rz;
                RegionGrid.Unkey(due[0], out rx, out rz);
                if (!BuildAndSend(rx, rz, host))
                {
                    _planner.Invalidate(rx, rz);
                    return;
                }
            }
            while (clock.Elapsed.TotalMilliseconds < budgetMs);
        }

        /// <summary>True when every region within <paramref name="radiusRegions"/> regions of the player's region has been sent this epoch.</summary>
        public bool RegionsSentAround(Vector3 csFeet, int radiusRegions)
        {
            int cx, cz;
            _grid.RegionOf(csFeet.x, -csFeet.z, out cx, out cz);
            for (int x = cx - radiusRegions; x <= cx + radiusRegions; x++)
                for (int z = cz - radiusRegions; z <= cz + radiusRegions; z++)
                    if (!_planner.IsSent(x, z)) return false;
            return true;
        }

        /// <summary>One line for the overlay.</summary>
        public string Stats
        {
            get { return "collision: " + _regionsSent + " regions, " + _trianglesSent + " tris, last build " + _lastBuildMs.ToString("0.0") + " ms"; }
        }

        // Returns false when the region should be retried on a later frame (the frame could not be queued,
        // or the terrain failed to build and the region has not used up its attempts). Roads that fail to
        // build are dropped from that region only, so the player never loses ground collision for them.
        private bool BuildAndSend(int rx, int rz, BridgeHost host)
        {
            CollisionRegion region;
            long key = RegionGrid.Key(rx, rz);
            try
            {
                var clock = Stopwatch.StartNew();
                // MC z in [16rz, 16rz + 16) is CS1 z in (-16rz - 16, -16rz].
                float minX = rx * RegionSize, maxX = minX + RegionSize;
                float maxZ = -rz * RegionSize, minZ = maxZ - RegionSize;
                _buffer.Clear();
                Heightfield.Triangulate(_terrain.AsFunc(), minX, minZ, maxX, maxZ, TerrainStep, CollisionRegion.Terrain, _buffer);
                int terrainOnly = _buffer.Count;
                try
                {
                    _net.Emit(minX, minZ, maxX, maxZ, _buffer);
                }
                catch (Exception e)
                {
                    _log.Error("collision region (" + rx + "," + rz + "): roads failed to build, sending terrain only", e);
                    _buffer.Truncate(terrainOnly);
                }
                region = CollisionConversion.ToRegion(_buffer, _planner.Epoch, rx, rz);
                _lastBuildMs = clock.Elapsed.TotalMilliseconds;
            }
            catch (Exception e)
            {
                int failures;
                _failures.TryGetValue(key, out failures);
                _failures[key] = ++failures;
                if (failures < MaxBuildAttempts)
                {
                    _log.Error("collision region (" + rx + "," + rz + ") failed to build (attempt " + failures + "), will retry", e);
                    return false;
                }
                _log.Error("collision region (" + rx + "," + rz + ") failed to build " + failures + " times, giving up on it", e);
                return true;
            }
            if (!host.Send(AppProtocol.CollisionRegionType, region.Encode())) return false;
            _regionsSent++;
            _trianglesSent += region.TriangleCount;
            return true;
        }
    }
}
