using System;
using ColossalFramework;
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

        /// <summary>The sampler as a delegate for <c>Skylines.Core.Geometry.Heightfield</c>.</summary>
        public Func<float, float, float> AsFunc()
        {
            return Height;
        }
    }
}
