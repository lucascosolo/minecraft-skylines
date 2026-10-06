using System;
using System.Diagnostics;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Geometry;
using Skylines.Core.Streaming;
using Skylines.Host;
using Skylines.Host.Geometry;
using MinecraftSkylines.Mod.Diagnostics;
using UnityEngine;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Streams terrain, roads, bridges, tunnels and buildings around the player to Minecraft as COLLISION_REGION frames. Regions are 16 x 16
    /// Minecraft blocks; each is built in CS1 coordinates over the matching CS1 rectangle, converted to Minecraft
    /// coordinates and sent nearest first. Main thread only.
    /// </summary>
    internal sealed class CollisionStreamer
    {
        private const float RegionSize = 16f;
        private const float TerrainStep = 2f;

        private readonly HostLog _log;
        private readonly RegionGrid _grid = new RegionGrid(RegionSize);
        private readonly RegionStreamPlanner _planner;
        private readonly TerrainSampler _terrain = new TerrainSampler();
        private readonly NetGeometry _net = new NetGeometry();
        private readonly BuildingGeometry _buildings = new BuildingGeometry();
        private readonly TriangleBuffer _buffer = new TriangleBuffer();
        private int _regionsSent;
        private long _trianglesSent;
        private double _lastBuildMs;
        private const int MaxBuildAttempts = 3;
        private readonly System.Collections.Generic.Dictionary<long, int> _failures = new System.Collections.Generic.Dictionary<long, int>();

        public CollisionStreamer(HostLog log)
        {
            _log = log;
            Viewer = new CollisionViewer(log);
            _planner = new RegionStreamPlanner(_grid, 64f, 96f);
        }

        /// <summary>Keeps a CS1-coordinate copy of every region built, for the Ctrl+Shift+G debug wireframe.</summary>
        public CollisionViewer Viewer { get; private set; }

        /// <summary>Diagnostic line about the ground road nearest to <paramref name="cs"/> (see <see cref="NetGeometry.DescribeNearestGround"/>).</summary>
        public string DescribeNearestGround(Vector3 cs)
        {
            return _net.DescribeNearestGround(cs);
        }

        /// <summary>The current collision epoch (every region sent carries it).</summary>
        public uint Epoch { get { return _planner.Epoch; } }

        /// <summary>Forgets every sent region, starts a new epoch and returns the COLLISION_RESET payload to send.</summary>
        public byte[] Reset()
        {
            _planner.Reset();
            _failures.Clear();
            Viewer.Clear();
            return new CollisionReset { Epoch = _planner.Epoch }.Encode();
        }

        /// <summary>
        /// Plans regions around the player (radius 64 m, evict 96 m), then builds and sends them nearest first until
        /// <paramref name="budgetMs"/> is used; always at least one region when one is due.
        /// </summary>
        public void Tick(Vector3 csFeet, BridgeHost host, double budgetMs)
        {
            float mcX = csFeet.x, mcZ = -csFeet.z;
            Viewer.Track(csFeet);
            var clock = Stopwatch.StartNew();
            do
            {
                var due = _planner.Next(mcX, mcZ, 1);
                if (due.Count == 0) return;
                int rx, rz;
                RegionGrid.Unkey(due[0], out rx, out rz);
                if (!BuildAndSend(rx, rz, host, csFeet))
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

        /// <summary>The CS1 rectangle of Minecraft region (rx, rz): MC z in [16rz, 16rz + 16) is CS1 z in (-16rz - 16, -16rz].</summary>
        public static void RegionRectCs(int rx, int rz, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = rx * RegionSize;
            maxX = minX + RegionSize;
            maxZ = -rz * RegionSize;
            minZ = maxZ - RegionSize;
        }

        /// <summary>The Minecraft region containing CS1 point (x, z).</summary>
        public static void RegionOfCs(float x, float z, out int rx, out int rz)
        {
            rx = (int)Math.Floor(x / RegionSize);
            rz = (int)Math.Floor(-z / RegionSize);
        }

        /// <summary>Builds one region without buildings (see the other overload).</summary>
        public static Exception BuildCs(TerrainSampler terrain, NetGeometry net, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            return BuildCs(terrain, net, null, minX, minZ, maxX, maxZ, into);
        }

        /// <summary>
        /// Builds one region's collision in CS1 coordinates into <paramref name="into"/>: terrain (with holes where the
        /// game clipped its surface), then roads, then buildings when <paramref name="buildings"/> is given. If roads or
        /// buildings throw, the region keeps its terrain only and the exception is returned (else null). The streamer and
        /// the self-test both build through here.
        /// </summary>
        public static Exception BuildCs(TerrainSampler terrain, NetGeometry net, BuildingGeometry buildings, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            into.Clear();
            Heightfield.Triangulate(terrain.AsFunc(), terrain.HoleFunc(TerrainStep), minX, minZ, maxX, maxZ, TerrainStep, CollisionRegion.Terrain, into);
            int terrainOnly = into.Count;
            try
            {
                net.Emit(minX, minZ, maxX, maxZ, into);
                if (buildings != null) buildings.Emit(terrain, minX, minZ, maxX, maxZ, into);
                return null;
            }
            catch (Exception e)
            {
                into.Truncate(terrainOnly);
                return e;
            }
        }

        // Returns false when the region should be retried on a later frame (the frame could not be queued,
        // or the terrain failed to build and the region has not used up its attempts). Roads that fail to
        // build are dropped from that region only, so the player never loses ground collision for them.
        private bool BuildAndSend(int rx, int rz, BridgeHost host, Vector3 csFeet)
        {
            CollisionRegion region;
            long key = RegionGrid.Key(rx, rz);
            try
            {
                var clock = Stopwatch.StartNew();
                float minX, minZ, maxX, maxZ;
                RegionRectCs(rx, rz, out minX, out minZ, out maxX, out maxZ);
                Exception roads = BuildCs(_terrain, _net, _buildings, minX, minZ, maxX, maxZ, _buffer);
                if (roads != null)
                {
                    _log.Error("collision region (" + rx + "," + rz + "): roads or buildings failed to build, sending terrain only", roads);
                }
                Viewer.Store(key, minX, minZ, maxX, maxZ, _buffer, csFeet);
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
