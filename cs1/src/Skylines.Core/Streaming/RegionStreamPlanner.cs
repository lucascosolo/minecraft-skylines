using System.Collections.Generic;

namespace Skylines.Core.Streaming
{
    /// <summary>Decides which regions to send next around a moving viewer, and forgets regions that fell behind.</summary>
    public sealed class RegionStreamPlanner
    {
        private readonly RegionGrid _grid;
        private readonly float _radius, _evictRadius;
        private readonly HashSet<long> _sent = new HashSet<long>();

        /// <summary>Regions within <paramref name="radius"/> are sent; sent regions beyond <paramref name="evictRadius"/> are forgotten.</summary>
        public RegionStreamPlanner(RegionGrid grid, float radius, float evictRadius)
        {
            _grid = grid;
            _radius = radius;
            _evictRadius = evictRadius;
            Epoch = 1;
        }

        /// <summary>Starts at 1; <see cref="Reset"/> increments it.</summary>
        public uint Epoch { get; private set; }

        /// <summary>Evicts far regions, then returns up to <paramref name="budget"/> not-yet-sent regions, nearest first, and marks them sent.</summary>
        public List<long> Next(float x, float z, int budget)
        {
            var evict = new List<long>();
            foreach (long k in _sent)
            {
                int rx, rz;
                RegionGrid.Unkey(k, out rx, out rz);
                if (_grid.DistanceTo(rx, rz, x, z) > _evictRadius) evict.Add(k);
            }
            foreach (long k in evict) _sent.Remove(k);

            var result = new List<long>();
            foreach (long k in _grid.RegionsWithin(x, z, _radius))
            {
                if (result.Count >= budget) break;
                if (_sent.Add(k)) result.Add(k);
            }
            return result;
        }

        /// <summary>Makes one region due again.</summary>
        public void Invalidate(int rx, int rz)
        {
            _sent.Remove(RegionGrid.Key(rx, rz));
        }

        /// <summary>Forgets every region and starts a new epoch.</summary>
        public void Reset()
        {
            _sent.Clear();
            Epoch++;
        }

        /// <summary>True if the region has been returned by <see cref="Next"/> and not evicted or invalidated since.</summary>
        public bool IsSent(int rx, int rz)
        {
            return _sent.Contains(RegionGrid.Key(rx, rz));
        }
    }
}
