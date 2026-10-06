using System;

namespace Skylines.Core.Water
{
    /// <summary>Pure helpers for a water-surface grid over block columns.</summary>
    public static class WaterColumns
    {
        /// <summary>Shallower water than this (metres) is not water; smoothing leaves a film along shores.</summary>
        public const float MinDepth = 0.05f;

        /// <summary>Sets each column's surface to its ground where the water is shallower than <paramref name="minDepth"/> (or NaN).</summary>
        public static void Settle(float[] surface, float[] ground, float minDepth)
        {
            for (int i = 0; i < surface.Length; i++)
            {
                if (!(surface[i] - ground[i] >= minDepth)) surface[i] = ground[i];
            }
        }

        /// <summary>The first column of a <paramref name="size"/>-wide grid around <paramref name="feet"/>: floor(feet) - size / 2.</summary>
        public static int Origin(double feet, int size)
        {
            return (int)Math.Floor(feet) - size / 2;
        }

        /// <summary>True when both arrays have the same length and bit-identical values.</summary>
        public static bool SameValues(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.ToInt32(BitConverter.GetBytes(a[i]), 0) != BitConverter.ToInt32(BitConverter.GetBytes(b[i]), 0)) return false;
            }
            return true;
        }
    }
}
