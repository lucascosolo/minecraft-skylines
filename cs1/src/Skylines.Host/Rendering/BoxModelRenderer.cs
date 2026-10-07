using System;
using System.Collections.Generic;
using Skylines.Core.Models;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>One textured, posed model of an entity: matrices in a right-handed frame (the guest's), metres.</summary>
    public sealed class BoxDraw
    {
        /// <summary>Model id.</summary>
        public uint Model;
        /// <summary>Texture id.</summary>
        public uint Texture;
        /// <summary>Colour multiplied with the texture.</summary>
        public Color32 Tint;
        /// <summary>Row-major 3x4 from model space to the entity's frame, relative to its position.</summary>
        public float[] Matrix;
        /// <summary>9 floats per part: px, py, pz (1/16 m), xRot, yRot, zRot (radians), xScale, yScale, zScale.</summary>
        public float[] Poses;
        /// <summary>One byte per part: bit 0 hidden with children, bit 1 own quads skipped.</summary>
        public byte[] Flags;
    }

    /// <summary>An entity at a position of the guest's right-handed frame (CS1 = z mirrored).</summary>
    public sealed class BoxEntity
    {
        /// <summary>Entity id.</summary>
        public uint Id;
        /// <summary>Position, guest frame.</summary>
        public float X, Y, Z;
        /// <summary>Models drawn for this entity.</summary>
        public BoxDraw[] Draws;
    }

    /// <summary>
    /// Draws "skinned box models" (parts of textured quads with per-part transforms) in the CS1 scene with
    /// Graphics.DrawMesh, interpolating every entity between the last two snapshots. Main thread only.
    /// </summary>
    public sealed class BoxModelRenderer : IDisposable
    {
        private sealed class Model
        {
            public int[] Parents;
            public Mesh[] Meshes;
        }

        private sealed class Track
        {
            public BoxEntity Previous, Latest;
            public float PreviousTime, LatestTime;
        }

        private readonly int _layer;
        private readonly Func<Texture2D, Material> _materialFor;
        private readonly Dictionary<uint, Model> _models = new Dictionary<uint, Model>();
        private readonly Dictionary<uint, Material> _materials = new Dictionary<uint, Material>();
        private readonly Dictionary<uint, Texture2D> _cutouts = new Dictionary<uint, Texture2D>();
        private readonly Dictionary<uint, Texture2D> _textures = new Dictionary<uint, Texture2D>();
        private Dictionary<uint, Track> _tracks = new Dictionary<uint, Track>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        /// <summary>Draws on <paramref name="layer"/>; <paramref name="materialFor"/> makes the material for a texture.</summary>
        public BoxModelRenderer(int layer, Func<Texture2D, Material> materialFor)
        {
            _layer = layer;
            _materialFor = materialFor;
        }

        /// <summary>Models held.</summary>
        public int ModelCount { get { return _models.Count; } }
        /// <summary>Textures held.</summary>
        public int TextureCount { get { return _textures.Count; } }
        /// <summary>Entities in the latest snapshot.</summary>
        public int EntityCount { get { return _tracks.Count; } }
        /// <summary>Part meshes queued in the last LateUpdate.</summary>
        public int DrawnLastFrame { get; private set; }

        /// <summary>Builds (or replaces) a model: parent index per part (-1 none) and 23 floats per quad per part.</summary>
        public void SetModel(uint id, int[] parents, float[][] quadsPerPart)
        {
            Model old;
            if (_models.TryGetValue(id, out old)) DestroyMeshes(old);
            var m = new Model { Parents = parents, Meshes = new Mesh[parents.Length] };
            for (int i = 0; i < parents.Length; i++)
                if (quadsPerPart[i].Length > 0) m.Meshes[i] = ToMesh(BoxModelMath.BuildMesh(quadsPerPart[i]), "MinecraftSkylines.BoxModel." + id + "." + i);
            _models[id] = m;
        }

        /// <summary>False when the PNG does not decode.</summary>
        public bool SetTexture(uint id, byte[] png)
        {
            Texture2D t = TextureUtil.LoadPng(png, "MinecraftSkylines.BoxTexture." + id);
            if (t == null) return false;
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            Material old;
            if (_materials.TryGetValue(id, out old) && old != null) UnityEngine.Object.Destroy(old);
            Texture2D oldTex;
            if (_textures.TryGetValue(id, out oldTex) && oldTex != null) UnityEngine.Object.Destroy(oldTex);
            _textures[id] = t;
            Material material = _materialFor(t);
            _materials[id] = material;
            // CS1's prop and building shaders cut out pixels by the ACI map's red channel (1 - alpha, as the game's
            // asset importer builds it from an alpha map); a neutral ACI drew transparent pixels as black squares.
            Texture2D oldCutout;
            if (_cutouts.TryGetValue(id, out oldCutout) && oldCutout != null) UnityEngine.Object.Destroy(oldCutout);
            _cutouts.Remove(id);
            if (material != null && material.HasProperty("_ACIMap"))
            {
                Texture2D cutout = TextureUtil.CutoutAci(t, "MinecraftSkylines.BoxCutout." + id);
                material.SetTexture("_ACIMap", cutout);
                _cutouts[id] = cutout;
            }
            return true;
        }

        /// <summary>The complete set of entities as of <paramref name="time"/> (seconds, any monotonic clock).</summary>
        public void SetFrame(IList<BoxEntity> entities, float time)
        {
            var next = new Dictionary<uint, Track>(entities.Count);
            foreach (BoxEntity e in entities)
            {
                Track t;
                if (_tracks.TryGetValue(e.Id, out t)) { t.Previous = t.Latest; t.PreviousTime = t.LatestTime; }
                else t = new Track { Previous = e, PreviousTime = time - 0.05f };
                t.Latest = e;
                t.LatestTime = time;
                next[e.Id] = t;
            }
            _tracks = next;
        }

        /// <summary>Drops the entities (models and textures stay).</summary>
        public void ClearEntities()
        {
            _tracks = new Dictionary<uint, Track>();
        }

        /// <summary>Queues every entity within <paramref name="maxDistance"/> of <paramref name="camera"/> (CS1 frame) for this frame.</summary>
        public void LateUpdate(bool draw, float time, Vector3 camera, float maxDistance)
        {
            DrawnLastFrame = 0;
            if (!draw) return;
            float max2 = maxDistance * maxDistance;
            foreach (Track t in _tracks.Values)
            {
                float interval = Mathf.Clamp(t.LatestTime - t.PreviousTime, 0.02f, 0.25f);
                float a = Mathf.Clamp01((time - t.LatestTime) / interval);
                BoxEntity p = t.Previous, l = t.Latest;
                float x = p.X + (l.X - p.X) * a, y = p.Y + (l.Y - p.Y) * a, z = p.Z + (l.Z - p.Z) * a;
                float dx = x - camera.x, dy = y - camera.y, dz = -z - camera.z;
                if (dx * dx + dy * dy + dz * dz > max2) continue;
                for (int d = 0; d < l.Draws.Length; d++)
                {
                    BoxDraw ld = l.Draws[d];
                    BoxDraw pd = d < p.Draws.Length && p.Draws[d].Model == ld.Model && p.Draws[d].Poses.Length == ld.Poses.Length ? p.Draws[d] : ld;
                    DrawOne(pd, ld, a, x, y, z);
                }
            }
        }

        private void DrawOne(BoxDraw p, BoxDraw l, float a, float x, float y, float z)
        {
            Model model;
            Material material;
            if (!_models.TryGetValue(l.Model, out model) || !_materials.TryGetValue(l.Texture, out material) || material == null) return;
            int n = model.Parents.Length;
            if (l.Flags.Length != n || l.Poses.Length != n * 9) return;
            var locals = new float[n][];
            for (int i = 0; i < n; i++)
            {
                int o = i * 9;
                locals[i] = BoxModelMath.PartLocal(
                    Lerp(p.Poses[o], l.Poses[o], a), Lerp(p.Poses[o + 1], l.Poses[o + 1], a), Lerp(p.Poses[o + 2], l.Poses[o + 2], a),
                    BoxModelMath.LerpAngle(p.Poses[o + 3], l.Poses[o + 3], a), BoxModelMath.LerpAngle(p.Poses[o + 4], l.Poses[o + 4], a),
                    BoxModelMath.LerpAngle(p.Poses[o + 5], l.Poses[o + 5], a),
                    Lerp(p.Poses[o + 6], l.Poses[o + 6], a), Lerp(p.Poses[o + 7], l.Poses[o + 7], a), Lerp(p.Poses[o + 8], l.Poses[o + 8], a));
            }
            float[][] chain = BoxModelMath.Chain(model.Parents, locals);
            bool[] drawn = BoxModelMath.Drawn(model.Parents, l.Flags);
            float[] root = BoxModelMath.Multiply(BoxModelMath.Translation(x, y, z),
                BoxModelMath.FromAffine(BoxModelMath.Lerp(p.Matrix, l.Matrix, a)));
            _block.Clear();
            _block.SetColor("_Color", l.Tint);
            for (int i = 0; i < n; i++)
            {
                if (!drawn[i] || model.Meshes[i] == null) continue;
                float[] m = BoxModelMath.MirrorZ(BoxModelMath.Multiply(root, chain[i]));
                var u = new Matrix4x4();
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++)
                        u[r, c] = m[r * 4 + c];
                Graphics.DrawMesh(model.Meshes[i], u, material, _layer, null, 0, _block);
                DrawnLastFrame++;
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static Mesh ToMesh(BoxMesh b, string name)
        {
            int vc = b.Positions.Length / 3;
            var v = new Vector3[vc];
            var nrm = new Vector3[vc];
            var uv = new Vector2[vc];
            for (int i = 0; i < vc; i++)
            {
                v[i] = new Vector3(b.Positions[i * 3], b.Positions[i * 3 + 1], b.Positions[i * 3 + 2]);
                nrm[i] = new Vector3(b.Normals[i * 3], b.Normals[i * 3 + 1], b.Normals[i * 3 + 2]);
                uv[i] = new Vector2(b.Uvs[i * 2], b.Uvs[i * 2 + 1]);
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = v;
            mesh.normals = nrm;
            mesh.uv = uv;
            var colors = new Color32[vc];
            for (int i = 0; i < vc; i++) colors[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = colors;
            mesh.triangles = b.Indices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyMeshes(Model m)
        {
            foreach (Mesh mesh in m.Meshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
        }

        /// <summary>Drops entities, models and textures (link lost).</summary>
        public void Clear()
        {
            ClearEntities();
            foreach (Model m in _models.Values) DestroyMeshes(m);
            _models.Clear();
            foreach (Material m in _materials.Values)
                if (m != null) UnityEngine.Object.Destroy(m);
            _materials.Clear();
            foreach (Texture2D t in _textures.Values)
                if (t != null) UnityEngine.Object.Destroy(t);
            _textures.Clear();
            foreach (Texture2D t in _cutouts.Values)
                if (t != null) UnityEngine.Object.Destroy(t);
            _cutouts.Clear();
        }

        /// <summary>Same as Clear.</summary>
        public void Dispose()
        {
            Clear();
        }
    }
}
