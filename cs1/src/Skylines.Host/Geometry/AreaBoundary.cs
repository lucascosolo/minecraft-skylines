using System;
using ColossalFramework;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Invisible walls along the edge of the land the player owns (<c>GameAreaManager.PointOutOfArea</c>, 1920 m tiles with
    /// borders at 960 + k * 1920, GameAreaManager.cs:1244), so a first-person player stays inside the city. Main thread only.
    /// </summary>
    public sealed class AreaBoundary
    {
        /// <summary>Flags value of a boundary wall triangle (bit 8).</summary>
        public const ushort BoundaryFlag = 1 << 8;
        /// <summary>Wall bottom and top, metres: below any dig and above anything a player can build or fly to.</summary>
        public const float Bottom = -200f, Top = 2200f;
        /// <summary>How far past the region's edges walls are kept, in metres (as the other emitters).</summary>
        public const float ClipMargin = 0.05f;

        private readonly Func<float, float, bool> _outside = Outside;

        /// <summary>Walls emitted by the last <see cref="Emit"/>.</summary>
        public int LastWallCount { get; private set; }

        /// <summary>Appends the walls overlapping the rectangle, clipped to it.</summary>
        public void Emit(float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            LastWallCount = AreaWalls.Emit(_outside, GameAreaManager.AREAGRID_CELL_SIZE / 2, GameAreaManager.AREAGRID_CELL_SIZE,
                minX, minZ, maxX, maxZ, ClipMargin, Bottom, Top, BoundaryFlag, into);
        }

        /// <summary>True when (x, z) lies outside the land the player owns.</summary>
        public static bool Outside(float x, float z)
        {
            return !GameAreaManager.exists || Singleton<GameAreaManager>.instance.PointOutOfArea(new Vector3(x, 0f, z));
        }
    }
}
