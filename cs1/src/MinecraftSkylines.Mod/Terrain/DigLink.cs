using System;
using System.Collections.Generic;
using MinecraftSkylines.Protocol;
using Skylines.Core.Geometry;
using Skylines.Core.Voxels;
using Skylines.Host;
using Skylines.Host.Geometry;
using Skylines.Host.Terrain;
using UnityEngine;

namespace MinecraftSkylines.Mod.Terrain
{
    /// <summary>
    /// Milestone 5: ground the player dug (every edit at or below the terrain's solid top, <see cref="DugGround"/>) becomes
    /// a hole in CS1. The 4 m surface cells over open columns are clipped with the game's own surface Clip
    /// (<see cref="TerrainClipMask"/>, runtime only: <c>TerrainManager.Data</c> saves heights, never the surface); the undug
    /// remainder of each clipped cell and the skirts between the terrain surface and the block grid are drawn here; the
    /// cavity's walls, floors and ceilings are the guest's shadow blocks facing dug cells (SECTION_MESH). Collision
    /// regions get the same cut (<see cref="AddCollision"/>). Heights are never changed, so CS1's water never floods a pit.
    /// Main thread only.
    /// </summary>
    internal sealed class DigLink : IDisposable
    {
        public const string ClipGroup = "dig";
        private const float TerrainStep = 2f; // CollisionStreamer's terrain triangulation
        private const int MaxMeshVertices = 60000;
        private const double RebuildIntervalS = 0.25;

        /// <summary>The instance LinkService created; null while the mod is off.</summary>
        public static DigLink Current;

        private readonly HostLog _log;
        private readonly TerrainSampler _terrain = new TerrainSampler();
        private readonly DugGround _ground;
        private readonly HashSet<long> _dirtyRegions = new HashSet<long>();
        private readonly List<DugQuad> _quads = new List<DugQuad>();
        private readonly ConvexCut _cut = new ConvexCut();
        private readonly TriangleBuffer _tmp = new TriangleBuffer();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Material _material;
        private bool _materialTried;
        private bool _renderDirty;
        private float _lastRebuild = -1000f;
        private int _clipped;

        public DigLink(HostLog log)
        {
            _log = log;
            Func<float, float, float> f = _terrain.AsFunc();
            _ground = new DugGround((x, z) => Heightfield.HeightAt(f, x + 0.5f, -(z + 0.5f), TerrainStep));
        }

        /// <summary>The city's edits were (re)loaded: rebuild everything from them.</summary>
        public void Reload(List<VoxelEdit> edits)
        {
            _ground.Clear();
            foreach (VoxelEdit e in edits) _ground.Set(e.Pos.X, e.Pos.Y, e.Pos.Z, true);
            _renderDirty = true;
            _log.Info("dig: " + edits.Count + " edits loaded, " + _ground.DugCount + " dug cells");
        }

        /// <summary>One edit changed (the guest's BLOCK_EDITS).</summary>
        public void OnEdit(int x, int y, int z, bool edited)
        {
            _ground.Set(x, y, z, edited);
            for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                    _dirtyRegions.Add(Skylines.Core.Streaming.RegionGrid.Key((x + dx) >> 4, (z + dz) >> 4));
            _renderDirty = true;
        }

        /// <summary>Regions whose collision changed since the last call (the streamer re-sends them).</summary>
        public void TakeDirtyRegions(List<long> into)
        {
            into.AddRange(_dirtyRegions);
            _dirtyRegions.Clear();
        }

        /// <summary>Per frame: re-clips and rebuilds the surface patches at most every 0.25 s while edits change.</summary>
        public void Update(bool inCity)
        {
            if (!inCity || !_renderDirty || Time.realtimeSinceStartup - _lastRebuild < RebuildIntervalS) return;
            _renderDirty = false;
            _lastRebuild = Time.realtimeSinceStartup;
            try { Rebuild(); }
            catch (Exception e) { _log.Error("dig: rebuild", e); }
        }

        /// <summary>Per frame after the camera moved: draws the patches while a city is loaded.</summary>
        public void LateUpdate(bool inCity)
        {
            if (!inCity || _meshes.Count == 0 || Material == null) return;
            int layer = LayerMask.NameToLayer("Props");
            foreach (Mesh m in _meshes) Graphics.DrawMesh(m, Matrix4x4.identity, _material, layer >= 0 ? layer : 10);
        }

        /// <summary>The city unloads: forget everything; the game rebuilds the surface on the next load (no recompute).</summary>
        public void Unload()
        {
            _ground.Clear();
            _dirtyRegions.Clear();
            Restore(false);
        }

        /// <summary>The mod stops: give the game its surface back now.</summary>
        public void Dispose()
        {
            Restore(true);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;
        }

        public string OverlayText()
        {
            return "Dig: " + _ground.DugCount + " dug cells, " + _clipped + " clipped 4 m cells, " + _meshes.Count + " patch meshes";
        }

        private void Restore(bool recompute)
        {
            _renderDirty = false;
            if (TerrainClipMask.Exists && TerrainClipMask.Instance.SetGroup(ClipGroup, new List<Rect>(), recompute) > 0)
                _log.Info("dig: clips restored (recompute " + recompute + ")");
            _clipped = 0;
            DestroyMeshes();
        }

        private void DestroyMeshes()
        {
            foreach (Mesh m in _meshes) UnityEngine.Object.Destroy(m);
            _meshes.Clear();
        }

        private static long CellKey(int i, int j) { return ((long)i << 32) | (uint)j; }

        private static float CellMin(int index)
        {
            return (index - TerrainClipMask.CellsPerAxis / 2) * TerrainClipMask.CellSize;
        }

        private void Rebuild()
        {
            List<KeyValuePair<int, int>> open = _ground.OpenColumns(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
            var openSet = new HashSet<long>();
            var cells = new HashSet<long>();
            var rects = new List<Rect>();
            foreach (KeyValuePair<int, int> c in open)
            {
                openSet.Add(CellKey(c.Key, c.Value));
                int i = TerrainClipMask.CellIndex(c.Key + 0.5f), j = TerrainClipMask.CellIndex(-(c.Value + 0.5f));
                if (cells.Add(CellKey(i, j)))
                    rects.Add(Rect.MinMaxRect(CellMin(i), CellMin(j), CellMin(i) + TerrainClipMask.CellSize, CellMin(j) + TerrainClipMask.CellSize));
            }
            TerrainClipMask mask = TerrainClipMask.Instance;
            if (mask.OnError == null) mask.OnError = (where, e) => _log.Error("terrain clip mask " + where, e);
            mask.SetGroup(ClipGroup, rects, true);
            _clipped = rects.Count;

            DestroyMeshes();
            var mb = new MeshParts();
            float tiling = GrassTiling();
            foreach (Rect r in rects)
            {
                for (int a = 0; a < 4; a++)
                {
                    for (int b = 0; b < 4; b++)
                    {
                        float x0 = r.xMin + a, z0 = r.yMin + b;
                        // CS1 z in [z0, z0 + 1] is Minecraft column z = -z0 - 1.
                        if (openSet.Contains(CellKey((int)x0, -(int)z0 - 1))) continue;
                        Vector3 p00 = Ground(x0, z0), p01 = Ground(x0, z0 + 1), p11 = Ground(x0 + 1, z0 + 1), p10 = Ground(x0 + 1, z0);
                        // Clockwise seen from above (Unity's front face).
                        Flush(mb, 4);
                        mb.Quad(p00, p01, p11, p10, tiling, true);
                    }
                }
            }
            _quads.Clear();
            _ground.Skirts(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue, (x, z) => _terrain.Height(x, -z), _quads);
            foreach (DugQuad q in _quads)
            {
                Flush(mb, 4);
                // Minecraft A, B, C, D counter-clockwise from the open side; mirrored z makes it A, D, C, B in Unity.
                mb.Quad(new Vector3(q.Ax, q.Ay, -q.Az), new Vector3(q.Dx, q.Dy, -q.Dz), new Vector3(q.Cx, q.Cy, -q.Cz),
                    new Vector3(q.Bx, q.By, -q.Bz), tiling, false);
            }
            Flush(mb, MaxMeshVertices);
            _log.Info("dig: " + open.Count + " open columns, " + rects.Count + " clipped cells, " + _quads.Count + " skirts, "
                + _meshes.Count + " meshes");
        }

        private Vector3 Ground(float x, float z)
        {
            return new Vector3(x, _terrain.Height(x, z), z);
        }

        // Starts a new mesh when adding n more vertices would pass the limit (n = the limit flushes what is left).
        private void Flush(MeshParts mb, int n)
        {
            if (mb.Vertices.Count == 0 || mb.Vertices.Count + n <= MaxMeshVertices && n != MaxMeshVertices) return;
            var m = new Mesh { name = "MinecraftSkylines.DigPatches" };
            m.vertices = mb.Vertices.ToArray();
            m.uv = mb.Uvs.ToArray();
            m.triangles = mb.Triangles.ToArray();
            m.RecalculateNormals();
            m.RecalculateBounds();
            _meshes.Add(m);
            mb.Clear();
        }

        private static float GrassTiling()
        {
            TerrainProperties p = TerrainManager.exists ? TerrainManager.instance.m_properties : null;
            return p != null ? p.m_grassTiling : 0.029f;
        }

        private Material Material
        {
            get
            {
                if (_material != null || _materialTried) return _material;
                _materialTried = true;
                Shader shader = Shader.Find("Custom/Props/Prop/Default");
                TerrainProperties p = TerrainManager.exists ? TerrainManager.instance.m_properties : null;
                if (shader == null || p == null || p.m_grassDiffuse == null)
                {
                    _log.Warn("dig: prop shader or terrain grass texture unavailable; dug holes are drawn without surface patches");
                    return null;
                }
                _material = new Material(shader) { name = "MinecraftSkylines.DigPatches" };
                _material.SetTexture("_MainTex", p.m_grassDiffuse);
                _material.SetColor("_Color", Color.white);
                _material.SetTexture("_XYSMap", Skylines.Host.Rendering.TextureUtil.Solid(new Color32(128, 128, 255, 255), true, "MinecraftSkylines.DigXYS"));
                _material.SetTexture("_ACIMap", Skylines.Host.Rendering.TextureUtil.Solid(new Color32(0, 0, 0, 255), true, "MinecraftSkylines.DigACI"));
                return _material;
            }
        }

        /// <summary>
        /// Adds region (rx, rz)'s dig to its collision (CS1 coordinates): terrain triangles over open columns cut away
        /// exactly (with <paramref name="dugSurface"/>, the cut pieces are kept flagged <see cref="CollisionRegion.DugSurface"/>
        /// so the guest's shadow ground keeps its height), then the cavity faces and skirts as terrain.
        /// </summary>
        public void AddCollision(int rx, int rz, TriangleBuffer buffer, bool dugSurface)
        {
            int x0 = rx * 16, z0 = rz * 16;
            List<KeyValuePair<int, int>> open = _ground.OpenColumns(x0, z0, x0 + 16, z0 + 16);
            if (open.Count > 0)
            {
                _tmp.Clear();
                float[] p = buffer.Positions;
                for (int t = 0, o = 0; t < buffer.Count; t++, o += 9)
                    _tmp.Add(p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7], p[o + 8], buffer.Flags[t]);
                buffer.Clear();
                _cut.Clear();
                foreach (KeyValuePair<int, int> c in open)
                {
                    float zMax = -c.Value, zMin = zMax - 1;
                    _cut.Add(new[] { c.Key, zMin, c.Key + 1f, zMin, c.Key + 1f, zMax, c.Key, zMax }, 4);
                }
                float[] q = _tmp.Positions;
                for (int t = 0, o = 0; t < _tmp.Count; t++, o += 9)
                {
                    ushort f = _tmp.Flags[t];
                    if ((f & CollisionRegion.Terrain) == 0)
                    {
                        buffer.Add(q[o], q[o + 1], q[o + 2], q[o + 3], q[o + 4], q[o + 5], q[o + 6], q[o + 7], q[o + 8], f);
                        continue;
                    }
                    _cut.Apply(_tmp, t, t + 1, buffer);
                    if (!dugSurface) continue;
                    foreach (KeyValuePair<int, int> c in open)
                    {
                        RectClip.Triangle(q[o], q[o + 1], q[o + 2], q[o + 3], q[o + 4], q[o + 5], q[o + 6], q[o + 7], q[o + 8],
                            CollisionRegion.DugSurface, c.Key, -c.Value - 1f, c.Key + 1f, -c.Value, buffer);
                    }
                }
            }
            _quads.Clear();
            _ground.Faces(x0, z0, x0 + 16, z0 + 16, _quads);
            Func<float, float, float> f2 = _terrain.AsFunc();
            _ground.Skirts(x0, z0, x0 + 16, z0 + 16, (x, z) => Heightfield.HeightAt(f2, x, -z, TerrainStep), _quads);
            foreach (DugQuad d in _quads)
            {
                // Minecraft (A, B, C) and (A, C, D); mirroring z reverses the winding.
                buffer.Add(d.Ax, d.Ay, -d.Az, d.Cx, d.Cy, -d.Cz, d.Bx, d.By, -d.Bz, CollisionRegion.Terrain);
                buffer.Add(d.Ax, d.Ay, -d.Az, d.Dx, d.Dy, -d.Dz, d.Cx, d.Cy, -d.Cz, CollisionRegion.Terrain);
            }
        }

        private sealed class MeshParts
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public void Clear()
            {
                Vertices.Clear();
                Uvs.Clear();
                Triangles.Clear();
            }

            // Triangles (a, b, c) and (a, c, d). Top patches map world x, z; skirts map the edge and y.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float tiling, bool top)
            {
                int i = Vertices.Count;
                foreach (Vector3 v in new[] { a, b, c, d })
                {
                    Vertices.Add(v);
                    Uvs.Add(top ? new Vector2(v.x * tiling, v.z * tiling) : new Vector2((v.x + v.z) * tiling, v.y * tiling));
                }
                Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
                Triangles.Add(i); Triangles.Add(i + 2); Triangles.Add(i + 3);
            }
        }
    }
}
