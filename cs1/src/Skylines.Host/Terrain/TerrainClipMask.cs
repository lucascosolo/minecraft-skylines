using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace Skylines.Host.Terrain
{
    /// <summary>
    /// Runtime-only clip mask over world-space XZ rectangles, applied through CS1's own surface pass.
    /// <c>TerrainModify.UpdateAreaImplementation</c> zeroes the surface of every recomputed area and
    /// rebuilds it from each registered <see cref="ITerrainManager.TerrainUpdated"/>, so a clip only
    /// lasts if it is re-applied there. This object registers once per process and re-applies every
    /// rectangle on each recompute. Nothing reaches a save: <c>TerrainManager.Data</c> serializes
    /// heights only, the surface is rebuilt on load. See docs/MILESTONES.md, Spike T1 "Tool route".
    /// </summary>
    public sealed class TerrainClipMask : ITerrainManager
    {
        /// <summary>Edge of one detail surface cell in metres.</summary>
        public const float CellSize = 4f;

        /// <summary>Detail cells per axis over the whole 17280 m map.</summary>
        public const int CellsPerAxis = 4320;

        /// <summary>Detail cells per axis in one terrain patch (9×9 patches).</summary>
        public const int CellsPerPatch = 480;

        private static TerrainClipMask s_instance;

        private readonly object _sync = new object();
        private readonly List<Rect> _areas = new List<Rect>();

        private TerrainClipMask()
        {
        }

        /// <summary>The process-wide mask; registers with TerrainManager on first use (main thread).</summary>
        public static TerrainClipMask Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = new TerrainClipMask();
                    // There is no unregister API; an empty mask is inert.
                    TerrainManager.RegisterTerrainManager(s_instance);
                }
                return s_instance;
            }
        }

        /// <summary>Whether <see cref="Instance"/> has been created (and registered).</summary>
        public static bool Exists
        {
            get { return s_instance != null; }
        }

        /// <summary>Receives exceptions caught on the simulation thread.</summary>
        public Action<string, Exception> OnError;

        /// <summary>Number of clipped rectangles.</summary>
        public int Count
        {
            get { lock (_sync) { return _areas.Count; } }
        }

        /// <summary>Detail cell index containing world coordinate <paramref name="world"/> (x or z).</summary>
        public static int CellIndex(float world)
        {
            return Mathf.Clamp(Mathf.FloorToInt(world / CellSize + CellsPerAxis / 2), 0, CellsPerAxis - 1);
        }

        /// <summary>World coordinate of the centre of detail cell <paramref name="index"/>.</summary>
        public static float CellCentre(int index)
        {
            return index * CellSize - CellsPerAxis * CellSize / 2f + CellSize / 2f;
        }

        /// <summary>Clips the rectangle and asks the simulation thread to recompute its surface.</summary>
        public void Add(float minX, float minZ, float maxX, float maxZ)
        {
            var r = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            lock (_sync)
            {
                _areas.Add(r);
            }
            RequestRecompute(r);
        }

        /// <summary>
        /// Forgets every rectangle and returns how many there were. With <paramref name="recompute"/>
        /// the game rebuilds those areas without the clip; skip it while the level is unloading.
        /// </summary>
        public int Clear(bool recompute)
        {
            Rect[] old;
            lock (_sync)
            {
                old = _areas.ToArray();
                _areas.Clear();
            }
            if (recompute)
            {
                foreach (Rect r in old)
                {
                    RequestRecompute(r);
                }
            }
            return old.Length;
        }

        private void RequestRecompute(Rect r)
        {
            if (!SimulationManager.exists)
            {
                return;
            }
            SimulationManager.instance.AddAction(() =>
            {
                try
                {
                    TerrainModify.UpdateArea(r.xMin, r.yMin, r.xMax, r.yMax, false, true, false);
                }
                catch (Exception e)
                {
                    Report("UpdateArea", e);
                }
            });
        }

        /// <inheritdoc />
        public void TerrainUpdated(TerrainArea heightArea, TerrainArea surfaceArea, TerrainArea zoneArea)
        {
            try
            {
                lock (_sync)
                {
                    // ApplyQuad clamps to the area being recomputed and returns unless the surface is.
                    // Corner order a(min,min) b(min,max) c(max,max) d(max,min) keeps the inside on the
                    // non-negative side of every edge test; Edges.None gives full clip (255), no fade.
                    foreach (Rect r in _areas)
                    {
                        TerrainModify.ApplyQuad(
                            new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMin, 0f, r.yMax),
                            new Vector3(r.xMax, 0f, r.yMax), new Vector3(r.xMax, 0f, r.yMin),
                            TerrainModify.Edges.None, TerrainModify.Heights.None, TerrainModify.Surface.Clip);
                    }
                }
            }
            catch (Exception e)
            {
                Report("TerrainUpdated", e);
            }
        }

        /// <inheritdoc />
        public void AfterTerrainUpdate(TerrainArea heightArea, TerrainArea surfaceArea, TerrainArea zoneArea)
        {
        }

        private void Report(string where, Exception e)
        {
            try
            {
                if (OnError != null)
                {
                    OnError(where, e);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
