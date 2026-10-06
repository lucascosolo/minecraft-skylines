using System;
using ColossalFramework;
using Skylines.Host.Terrain;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>CS1's own smooth terrain height at world (x, z), the sampler objects use. Main thread only.</summary>
    public sealed class TerrainSampler
    {
        /// <summary>Terrain height in metres at the world position.</summary>
        public float Height(float x, float z)
        {
            return Singleton<TerrainManager>.instance.SampleDetailHeightSmooth(new Vector3(x, 0f, z));
        }

        /// <summary>A detail surface cell counts as clipped from this <c>SurfaceCell.m_clipped</c> value (0-255) up.</summary>
        public const byte ClipThreshold = 128;

        /// <summary>
        /// Whether the game has clipped its terrain surface at the world position (tunnel portals, buildings and roads
        /// with <c>m_clipTerrain</c>): <c>TerrainManager.GetSurfaceCell</c> on the 4 m detail cell, which falls back
        /// to the interpolated raw surface where the patch has no detail.
        /// </summary>
        public bool IsClipped(float x, float z)
        {
            TerrainManager.SurfaceCell c = Singleton<TerrainManager>.instance.GetSurfaceCell(TerrainClipMask.CellIndex(x), TerrainClipMask.CellIndex(z));
            return c.m_clipped >= ClipThreshold;
        }

        /// <summary>
        /// Skip test for <c>Heightfield.Triangulate</c>: a terrain cell of edge <paramref name="step"/> is dropped when the
        /// surface is clipped at its centre and at all four corners, so a hole never reaches past the game's own cut.
        /// </summary>
        public Func<float, float, bool> HoleFunc(float step)
        {
            float h = step * 0.5f;
            return (x, z) => IsClipped(x, z) && IsClipped(x - h, z - h) && IsClipped(x + h, z - h) && IsClipped(x - h, z + h) && IsClipped(x + h, z + h);
        }

        /// <summary>The sampler as a delegate for <c>Skylines.Core.Geometry.Heightfield</c>.</summary>
        public Func<float, float, float> AsFunc()
        {
            return Height;
        }
    }
}
