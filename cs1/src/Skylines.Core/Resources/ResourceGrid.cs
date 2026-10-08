using System;

namespace Skylines.Core.Resources
{
    /// <summary>CS1's natural resource grid: 512 x 512 cells of 33.75 m centred on the map's origin.</summary>
    public static class ResourceGrid
    {
        /// <summary>Cell edge, m.</summary>
        public const float CellSize = 33.75f;
        /// <summary>Cells per side.</summary>
        public const int Resolution = 512;

        /// <summary>The cell index along one axis of a CS1 coordinate, clamped to the grid.</summary>
        public static int Cell(float cs)
        {
            int c = (int)Math.Floor(cs / (double)CellSize + Resolution / 2);
            return Math.Max(0, Math.Min(Resolution - 1, c));
        }

        /// <summary>A resource byte after taking <paramref name="units"/> from it, never below 0.</summary>
        public static byte Deplete(byte current, int units)
        {
            return (byte)Math.Max(0, current - Math.Max(0, units));
        }
    }
}
