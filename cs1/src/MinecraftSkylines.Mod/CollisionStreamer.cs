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
    /// Streams terrain, roads, bridges, tunnels, buildings, trees and props around the player to Minecraft as COLLISION_REGION frames. Regions are 16 x 16
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
        private readonly ObstacleGeometry _obstacles = new ObstacleGeometry();
        private readonly TriangleBuffer _buffer = new TriangleBuffer();
        private readonly System.Collections.Generic.List<TreeSample> _treeSamples = new System.Collections.Generic.List<TreeSample>();
        private readonly TriangleBuffer _spawnTris = new TriangleBuffer();
        private readonly System.Collections.Generic.List<long> _dug = new System.Collections.Generic.List<long>();
        private static readonly System.Collections.Generic.List<long> s_resend = new System.Collections.Generic.List<long>();
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
            _dug.Clear();
            lock (s_resend)
            {
                _dug.AddRange(s_resend);
                s_resend.Clear();
            }
            foreach (long k in _dug)
            {
                int tx, tz;
                RegionGrid.Unkey(k, out tx, out tz);
                _planner.Invalidate(tx, tz);
            }
            if (Terrain.DigLink.Current != null)
            {
                _dug.Clear();
                Terrain.DigLink.Current.TakeDirtyRegions(_dug);
                foreach (long k in _dug)
                {
                    int dx, dz;
                    RegionGrid.Unkey(k, out dx, out dz);
                    _planner.Invalidate(dx, dz);
                }
            }
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

        /// <summary>Thread-safe: asks for the region holding CS1 point (x, z) to be sent again (a tree grew there).</summary>
        public static void RequestResendAtCs(float x, float z)
        {
            int rx, rz;
            RegionOfCs(x, z, out rx, out rz);
            lock (s_resend) s_resend.Add(RegionGrid.Key(rx, rz));
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
        /// game clipped its surface inside a tunnel slope's footprint, <see cref="NetGeometry.PortalFootprint"/>), then roads, then buildings when <paramref name="buildings"/> is given. If roads or
        /// buildings throw, the region keeps its terrain only and the exception is returned (else null); if the portal
        /// footprint throws, the terrain has no holes and that exception is returned. The streamer and
        /// the self-test both build through here.
        /// </summary>
        public static Exception BuildCs(TerrainSampler terrain, NetGeometry net, BuildingGeometry buildings, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            return BuildCs(terrain, net, buildings, null, minX, minZ, maxX, maxZ, into);
        }

        /// <summary>
        /// As the overload above, then trees, bushes and props when <paramref name="obstacles"/> is given; if they throw, the
        /// region keeps terrain, roads and buildings and the exception is returned.
        /// </summary>
        private static readonly ConvexCut s_cut = new ConvexCut();
        private static readonly AreaBoundary s_boundary = new AreaBoundary();
        private static readonly TriangleBuffer s_uncut = new TriangleBuffer();

        private static void Copy(TriangleBuffer from, TriangleBuffer to)
        {
            to.Clear();
            float[] p = from.Positions;
            for (int t = 0, o = 0; t < from.Count; t++, o += 9)
            {
                to.Add(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7], p[o + 8], from.Flags[t]);
            }
        }

        public static Exception BuildCs(TerrainSampler terrain, NetGeometry net, BuildingGeometry buildings, ObstacleGeometry obstacles, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            into.Clear();
            Exception failed = null;
            StripFootprint portals = null;
            try { portals = net.PortalFootprint(minX, minZ, maxX, maxZ); }
            catch (Exception e) { failed = e; }
            Heightfield.Triangulate(terrain.AsFunc(), terrain.HoleFunc(TerrainStep, portals), minX, minZ, maxX, maxZ, TerrainStep, CollisionRegion.Terrain, into);
            // Terrain under sunken road surfaces (Basic Road: 0.3 m below the flattened ground) is cut away, so the road
            // is walked at its drawn height and the pavements are a step up. Uncut terrain is kept to fall back on.
            bool cutTerrain = false, copied = false;
            try
            {
                s_cut.Clear();
                net.SunkenRoadCuts(minX, minZ, maxX, maxZ, s_cut);
                if (s_cut.Count > 0)
                {
                    Copy(into, s_uncut);
                    copied = true;
                    into.Clear();
                    s_cut.Apply(s_uncut, 0, s_uncut.Count, into);
                    cutTerrain = true;
                }
            }
            catch (Exception e)
            {
                if (copied) Copy(s_uncut, into);
                cutTerrain = false;
                if (failed == null) failed = e;
            }
            int terrainOnly = into.Count;
            try
            {
                net.Emit(minX, minZ, maxX, maxZ, into);
                if (buildings != null) buildings.Emit(terrain, minX, minZ, maxX, maxZ, into);
            }
            catch (Exception e)
            {
                // Without the roads the cut would leave holes: fall back to the whole terrain.
                if (cutTerrain) Copy(s_uncut, into);
                else into.Truncate(terrainOnly);
                return e;
            }
            // Invisible walls at the edge of the owned land (owner, 2026-10-06: "so the player can't wander off").
            try { s_boundary.Emit(minX, minZ, maxX, maxZ, into); }
            catch (Exception e) { if (failed == null) failed = e; }
            int solid = into.Count;
            try
            {
                if (obstacles != null) obstacles.Emit(minX, minZ, maxX, maxZ, into);
                return failed;
            }
            catch (Exception e)
            {
                into.Truncate(solid);
                return e;
            }
        }

        /// <summary>Metres above the terrain the spawn probe starts, the lowest normal y counted as floor, and the gap left above it.</summary>
        public const float SpawnProbeHeight = 60f, WalkableNormalY = 0.7f, SpawnClearance = 0.05f;

        /// <summary>
        /// Feet height for a spawn at CS1 (x, z): the collision of its region and the 8 around it is built as it will be
        /// streamed (<see cref="BuildCs(TerrainSampler, NetGeometry, BuildingGeometry, float, float, float, float, TriangleBuffer)"/>),
        /// a vertical ray from terrain + <see cref="SpawnProbeHeight"/> picks the highest walkable surface not inside a
        /// building (<see cref="VerticalRay.HighestWalkable"/>), and the feet go <see cref="SpawnClearance"/> above it;
        /// terrain + <see cref="SpawnClearance"/> when nothing is hit. <paramref name="how"/> describes the choice for the log.
        /// </summary>
        public float SpawnFeetY(float x, float z, out string how)
        {
            float ground = _terrain.Height(x, z);
            _spawnTris.Clear();
            int rx, rz;
            RegionOfCs(x, z, out rx, out rz);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    float minX, minZ, maxX, maxZ;
                    RegionRectCs(rx + dx, rz + dz, out minX, out minZ, out maxX, out maxZ);
                    Exception e = BuildCs(_terrain, _net, _buildings, _obstacles, minX, minZ, maxX, maxZ, _buffer);
                    if (e != null) _log.Error("spawn probe region (" + (rx + dx) + "," + (rz + dz) + ")", e);
                    float[] p = _buffer.Positions;
                    ushort[] f = _buffer.Flags;
                    for (int i = 0, o = 0; i < _buffer.Count; i++, o += 9)
                        _spawnTris.Add(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7], p[o + 8], f[i]);
                }
            }
            float y;
            if (VerticalRay.HighestWalkable(_spawnTris, x, z, ground + SpawnProbeHeight, WalkableNormalY, BuildingGeometry.BuildingFlag, out y))
            {
                how = "surface at y " + y.ToString("0.00") + " (terrain " + ground.ToString("0.00") + ", " + _spawnTris.Count + " tris probed)";
                return y + SpawnClearance;
            }
            how = "no walkable surface hit, terrain y " + ground.ToString("0.00") + " (" + _spawnTris.Count + " tris probed)";
            return ground + SpawnClearance;
        }

        private byte[] TreesPayload(int rx, int rz)
        {
            int n = Math.Min(_treeSamples.Count, Trees.MaxCount);
            var items = new TreeRecord[n];
            for (int i = 0; i < n; i++)
            {
                TreeSample t = _treeSamples[i];
                items[i] = new TreeRecord
                {
                    Id = t.Id, X = t.Position.x, Y = t.Position.y, Z = -t.Position.z,
                    Height = t.Height, Radius = t.Radius, Kind = TreeRecord.KindOf(t.Name, t.Height)
                };
            }
            return new Trees { Epoch = _planner.Epoch, RegionX = rx, RegionZ = rz, Items = items }.Encode();
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
                _obstacles.TreeSamples = _treeSamples;
                _buildings.ExtraFlags = host.NegotiatedAppMinor >= 20 ? City.CityShops.TraderFlags : null;
                Exception roads;
                try { roads = BuildCs(_terrain, _net, _buildings, _obstacles, minX, minZ, maxX, maxZ, _buffer); }
                finally { _obstacles.TreeSamples = null; }
                if (roads != null)
                {
                    _log.Error("collision region (" + rx + "," + rz + "): roads, buildings, trees or props failed to build, sending what built", roads);
                }
                // Milestone 5: dug ground opens the terrain (minor 13 guests also get the cut surface as bit 9).
                if (Terrain.DigLink.Current != null)
                {
                    try { Terrain.DigLink.Current.AddCollision(rx, rz, _buffer, host.NegotiatedAppMinor >= 13); }
                    catch (Exception e) { _log.Error("collision region (" + rx + "," + rz + "): dug ground failed", e); }
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
            // Minor 12: the trees of this region, right after its geometry. A failed send only loses the list.
            if (host.NegotiatedAppMinor >= 12) host.Send(AppProtocol.TreesType, TreesPayload(rx, rz));
            return true;
        }
    }
}
