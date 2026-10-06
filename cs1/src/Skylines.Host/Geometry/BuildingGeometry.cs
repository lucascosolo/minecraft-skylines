using ColossalFramework;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Buildings overlapping a world-space rectangle as closed boxes in CS1 coordinates: the box the game itself
    /// ray-casts against (<c>Building.RayCast</c>: half extents <c>Width * 4</c> by <c>Length * 4</c> m about
    /// <c>m_position</c>, rotated by <c>m_angle</c>, height <c>BuildingInfo.m_size.y</c>), with its bottom 1 m below the
    /// lower of the terrain and the building's base. Main thread only.
    /// </summary>
    public sealed class BuildingGeometry
    {
        /// <summary>Flags value of a building triangle (bit 3).</summary>
        public const ushort BuildingFlag = 1 << 3;
        /// <summary>How far below the ground the box reaches, in metres.</summary>
        public const float Sink = 1f;

        private const float GridCell = 64f;       // BuildingManager.BUILDINGGRID_CELL_SIZE
        private const int GridSize = 270;         // BuildingManager.BUILDINGGRID_RESOLUTION
        private const float GridMargin = 128f;    // filed under m_position (BuildingManager.cs:5290); largest lot 128 x 64 m

        /// <summary>Number of buildings emitted by the last <see cref="Emit"/>.</summary>
        public int LastBuildingCount { get; private set; }

        /// <summary>Appends a box for every solid building overlapping the rectangle to <paramref name="into"/>.</summary>
        public void Emit(TerrainSampler terrain, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Building[] buildings = bm.m_buildings.m_buffer;
            ushort[] grid = bm.m_buildingGrid;
            LastBuildingCount = 0;
            int cx0 = Cell(minX - GridMargin), cx1 = Cell(maxX + GridMargin);
            int cz0 = Cell(minZ - GridMargin), cz1 = Cell(maxZ + GridMargin);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    ushort id = grid[cz * GridSize + cx];
                    int guard = 0;
                    while (id != 0 && guard++ < BuildingManager.MAX_BUILDING_COUNT)
                    {
                        if (EmitBuilding(ref buildings[id], terrain, minX, minZ, maxX, maxZ, into)) LastBuildingCount++;
                        id = buildings[id].m_nextGridBuilding;
                    }
                }
            }
        }

        private static int Cell(float v)
        {
            return Mathf.Clamp((int)(v / GridCell + 135f), 0, GridSize - 1);
        }

        /// <summary>
        /// Whether a building is exported: created, not deleted, hidden or collapsed, and not an intersection asset
        /// (its footprint is road, already exported) or a wildlife spawn point (an invisible marker).
        /// </summary>
        public static bool IsSolid(ref Building b)
        {
            const Building.Flags off = Building.Flags.Deleted | Building.Flags.Hidden | Building.Flags.Collapsed;
            if ((b.m_flags & Building.Flags.Created) == 0 || (b.m_flags & off) != 0) return false;
            BuildingInfo info = b.Info;
            if (info == null || !(info.m_size.y > 0f)) return false;
            return !(info.m_buildingAI is IntersectionAI || info.m_buildingAI is WildlifeSpawnPointAI);
        }

        private static bool EmitBuilding(ref Building b, TerrainSampler terrain, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            if (!IsSolid(ref b)) return false;
            float hx = b.Width * 4f, hz = b.Length * 4f;
            if (!(hx > 0f && hz > 0f)) return false;
            Vector3 p = b.m_position;
            float r = Mathf.Sqrt(hx * hx + hz * hz);
            if (p.x + r < minX || p.x - r > maxX || p.z + r < minZ || p.z - r > maxZ) return false;
            float bottom = Mathf.Min(terrain.Height(p.x, p.z), p.y) - Sink;
            Box.Oriented(p.x, p.z, b.m_angle, hx, hz, bottom, p.y + b.Info.m_size.y, BuildingFlag, into);
            return true;
        }
    }
}
