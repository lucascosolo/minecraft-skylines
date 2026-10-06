using System;
using System.Collections.Generic;
using ColossalFramework;
using Skylines.Core.Geometry;
using UnityEngine;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Buildings overlapping a world-space rectangle as triangles in CS1 coordinates, clipped to the rectangle. Each building
    /// is the mesh the game itself ray-casts against (<c>BuildingInfoBase.m_lodMeshData</c>, Building.cs:1578-1597) plus its
    /// active sub-meshes, placed with the renderer's matrix (BuildingAI.cs:78-86, 350; BuildingInfo.cs:649) and extended
    /// <c>m_baseHeight + </c><see cref="Sink"/> m below its ground line like the game's foundation. A building without LOD
    /// mesh data falls back to its lot box (<c>Building.RayCast</c>'s box). Main thread only.
    /// </summary>
    public sealed class BuildingGeometry
    {
        /// <summary>Flags value of a building triangle (bit 3).</summary>
        public const ushort BuildingFlag = 1 << 3;
        /// <summary>How far below the ground a building reaches, in metres.</summary>
        public const float Sink = 1f;
        /// <summary>Most building triangles one region carries; beyond it the smallest are dropped.</summary>
        public const int RegionBudget = 4000;
        /// <summary>How far past the region's edges building triangles are kept, in metres.</summary>
        public const float ClipMargin = 0.05f;

        private const float GridCell = 64f;       // BuildingManager.BUILDINGGRID_CELL_SIZE
        private const int GridSize = 270;         // BuildingManager.BUILDINGGRID_RESOLUTION
        private const float GridMargin = 128f;    // filed under m_position (BuildingManager.cs:5290); largest lot 128 x 64 m
        private const int CacheLimit = 1024;

        private readonly Dictionary<ushort, Shape> _cache = new Dictionary<ushort, Shape>();
        private readonly TriangleBuffer _local = new TriangleBuffer();

        /// <summary>Number of buildings emitted by the last <see cref="Emit"/>.</summary>
        public int LastBuildingCount { get; private set; }
        /// <summary>Of those, how many fell back to the lot box.</summary>
        public int LastBoxCount { get; private set; }
        /// <summary>Triangles the last <see cref="Emit"/> dropped to stay within <see cref="RegionBudget"/>.</summary>
        public int LastDropped { get; private set; }

        /// <summary>Appends every solid building's triangles inside the rectangle to <paramref name="into"/>.</summary>
        public void Emit(TerrainSampler terrain, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Building[] buildings = bm.m_buildings.m_buffer;
            ushort[] grid = bm.m_buildingGrid;
            LastBuildingCount = 0;
            LastBoxCount = 0;
            if (_cache.Count > CacheLimit) _cache.Clear();
            int start = into.Count;
            float x0 = minX - ClipMargin, z0 = minZ - ClipMargin, x1 = maxX + ClipMargin, z1 = maxZ + ClipMargin;
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
                        if (IsSolid(ref buildings[id]))
                        {
                            Shape s = ShapeOf(id, ref buildings[id], terrain);
                            if (s.Clip(x0, z0, x1, z1, into) > 0)
                            {
                                LastBuildingCount++;
                                if (s.IsBox) LastBoxCount++;
                            }
                        }
                        id = buildings[id].m_nextGridBuilding;
                    }
                }
            }
            LastDropped = TriangleBudget.KeepLargest(into, start, RegionBudget);
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

        private Shape ShapeOf(ushort id, ref Building b, TerrainSampler terrain)
        {
            Shape s;
            if (_cache.TryGetValue(id, out s) && s.Matches(ref b)) return s;
            s = new Shape(ref b);
            _local.Clear();
            if (!AddMeshes(ref b, terrain, _local))
            {
                _local.Clear();
                s.IsBox = true;
                float hx = b.Width * 4f, hz = b.Length * 4f;
                Vector3 p = b.m_position;
                if (hx > 0f && hz > 0f)
                {
                    float bottom = Mathf.Min(terrain.Height(p.x, p.z), p.y) - Sink;
                    Box.Oriented(p.x, p.z, b.m_angle, hx, hz, bottom, p.y + b.Info.m_size.y, BuildingFlag, _local);
                }
            }
            s.Take(_local);
            _cache[id] = s;
            return s;
        }

        // The main LOD mesh and every sub-mesh the renderer would draw for the building's flags (BuildingAI.RenderMeshes,
        // BuildingAI.cs:208-245). False when the main mesh has no usable LOD data.
        private static bool AddMeshes(ref Building b, TerrainSampler terrain, TriangleBuffer into)
        {
            BuildingInfo info = b.Info;
            Vector3 meshPosition;
            Quaternion meshRotation;
            b.CalculateMeshPosition(out meshPosition, out meshRotation);
            Matrix4x4 root = Matrix4x4.TRS(meshPosition, meshRotation, Vector3.one);
            float depth = b.m_baseHeight + Sink;
            if (!AddMesh(info, root, false, depth, terrain, into)) return false;
            BuildingInfo.MeshInfo[] subs = info.m_subMeshes;
            if (subs == null) return true;
            for (int i = 0; i < subs.Length; i++)
            {
                BuildingInfo.MeshInfo mi = subs[i];
                if (!Active(mi, ref b, true)) continue;
                BuildingInfoSub sub = mi.m_subInfo as BuildingInfoSub;
                if (sub == null) continue;
                Matrix4x4 m = root * mi.m_matrix;
                if (sub.m_subMeshes != null && sub.m_subMeshes.Length != 0)
                {
                    for (int j = 0; j < sub.m_subMeshes.Length; j++)
                    {
                        BuildingInfo.MeshInfo mj = sub.m_subMeshes[j];
                        if (Active(mj, ref b, false)) AddMesh(mj.m_subInfo, m, mj.m_followTerrain, depth, terrain, into);
                    }
                }
                else
                {
                    AddMesh(sub, m, mi.m_followTerrain, depth, terrain, into);
                }
            }
            return true;
        }

        private static bool Active(BuildingInfo.MeshInfo mi, ref Building b, bool checkFlags2)
        {
            if (mi == null) return false;
            if (((mi.m_flagsRequired | mi.m_flagsForbidden) & b.m_flags) != mi.m_flagsRequired) return false;
            return !checkFlags2 || ((mi.m_flagsRequired2 | mi.m_flagsForbidden2) & b.m_flags2) == mi.m_flagsRequired2;
        }

        // Appends one LOD mesh, with its foundation skirt, in world space. False when it has no usable triangles.
        private static bool AddMesh(BuildingInfoBase info, Matrix4x4 m, bool followTerrain, float depth, TerrainSampler terrain, TriangleBuffer into)
        {
            if (info == null) return false;
            RenderGroup.MeshData data = info.m_lodMeshData;
            if (data == null || data.m_vertices == null || data.m_triangles == null) return false;
            Vector3[] v = data.m_vertices;
            int[] t = data.m_triangles;
            if (t.Length < 3) return false;
            int start = into.Count;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = t[i], c1 = t[i + 1], c2 = t[i + 2];
                if ((uint)a >= (uint)v.Length || (uint)c1 >= (uint)v.Length || (uint)c2 >= (uint)v.Length)
                {
                    into.Truncate(start);
                    return false;
                }
                into.Add(v[a].x, v[a].y, v[a].z, v[c1].x, v[c1].y, v[c1].z, v[c2].x, v[c2].y, v[c2].z, BuildingFlag);
            }
            Skirt.Append(into, start, 0f, depth, BuildingFlag);
            if (followTerrain) m.m13 = terrain.Height(m.m03, m.m23); // BuildingAI.RenderMesh, BuildingAI.cs:351-354
            bool onTerrain = info.m_requireHeightMap; // vertex y is relative to the terrain (BuildingAI.cs:2165-2168)
            float[] p = into.Positions;
            for (int o = 9 * start; o < 9 * into.Count; o += 3)
            {
                Vector3 w = m.MultiplyPoint3x4(new Vector3(p[o], p[o + 1], p[o + 2]));
                if (onTerrain) w.y = p[o + 1] + terrain.Height(w.x, w.z);
                p[o] = w.x; p[o + 1] = w.y; p[o + 2] = w.z;
            }
            return true;
        }

        private sealed class Shape
        {
            private readonly BuildingInfo _info;
            private readonly Vector3 _position;
            private readonly float _angle;
            private readonly Building.Flags _flags;
            private readonly Building.Flags2 _flags2;
            private readonly int _length;
            private readonly byte _baseHeight;
            private float[] _tris = new float[0];
            private ushort[] _triFlags = new ushort[0];
            private float _minX, _minZ, _maxX, _maxZ;

            public bool IsBox;

            public Shape(ref Building b)
            {
                _info = b.Info;
                _position = b.m_position;
                _angle = b.m_angle;
                _flags = b.m_flags;
                _flags2 = b.m_flags2;
                _length = b.Length;
                _baseHeight = b.m_baseHeight;
            }

            public bool Matches(ref Building b)
            {
                return ReferenceEquals(_info, b.Info) && _position == b.m_position && _angle == b.m_angle && _flags == b.m_flags
                    && _flags2 == b.m_flags2 && _length == b.Length && _baseHeight == b.m_baseHeight;
            }

            public void Take(TriangleBuffer local)
            {
                _tris = new float[9 * local.Count];
                _triFlags = new ushort[local.Count];
                Array.Copy(local.Positions, _tris, _tris.Length);
                Array.Copy(local.Flags, _triFlags, _triFlags.Length);
                _minX = _minZ = float.MaxValue;
                _maxX = _maxZ = float.MinValue;
                for (int o = 0; o < _tris.Length; o += 3)
                {
                    _minX = Math.Min(_minX, _tris[o]); _maxX = Math.Max(_maxX, _tris[o]);
                    _minZ = Math.Min(_minZ, _tris[o + 2]); _maxZ = Math.Max(_maxZ, _tris[o + 2]);
                }
            }

            public int Clip(float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
            {
                if (_maxX < minX || _minX > maxX || _maxZ < minZ || _minZ > maxZ) return 0;
                float[] p = _tris;
                int added = 0;
                for (int i = 0; i < _triFlags.Length; i++)
                {
                    int o = 9 * i;
                    if (Math.Max(p[o], Math.Max(p[o + 3], p[o + 6])) < minX || Math.Min(p[o], Math.Min(p[o + 3], p[o + 6])) > maxX
                        || Math.Max(p[o + 2], Math.Max(p[o + 5], p[o + 8])) < minZ || Math.Min(p[o + 2], Math.Min(p[o + 5], p[o + 8])) > maxZ)
                    {
                        continue;
                    }
                    added += RectClip.Triangle(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7], p[o + 8],
                        _triFlags[i], minX, minZ, maxX, maxZ, into);
                }
                return added;
            }
        }
    }
}
