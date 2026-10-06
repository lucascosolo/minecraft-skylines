using ColossalFramework;
using UnityEngine;

namespace Skylines.Host.Terrain
{
    /// <summary>
    /// Samples CS1's drawn water surface and the terrain under it over a grid of world positions. Main thread only.
    /// See docs/CS1-API-NOTES.md (water surface).
    /// </summary>
    public static class WaterSampler
    {
        /// <summary>
        /// For every (xs[i], zs[j]) in CS1 world coordinates, writes the water surface (the terrain where there is no
        /// water) and the terrain height to index j * xs.Length + i.
        /// </summary>
        public static void Sample(float[] xs, float[] zs, float[] surface, float[] ground)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            for (int j = 0; j < zs.Length; j++)
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    var p = new Vector3(xs[i], 0f, zs[j]);
                    int k = j * xs.Length + i;
                    surface[k] = terrain.SampleRawHeightSmoothWithWater(p, true, 0f);
                    ground[k] = terrain.SampleRawHeightSmooth(p);
                }
            }
        }
    }
}
