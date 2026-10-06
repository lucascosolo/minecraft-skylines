using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Skylines.Host.Rendering
{
    /// <summary>
    /// Owns Unity meshes keyed by id: builds queued geometry on the main thread within a per-frame budget (at
    /// least one per frame), splits anything above <see cref="MeshSplit.MaxVertices"/>, and draws everything
    /// with Graphics.DrawMesh (all cameras) on one layer with one material. Call <see cref="LateUpdate"/>
    /// every frame; DrawMesh queues for this frame's rendering only. Main thread only.
    /// </summary>
    public sealed class MeshStore : IDisposable
    {
        private sealed class Pending
        {
            public Vector3 Origin;
            public float[] Positions, Uvs;
            public byte[] Colors;
            public int[] Indices;
        }

        private sealed class Entry
        {
            public Vector3 Origin;
            public Mesh[] Meshes;
            public int Vertices;
            public Pending Source; // kept so a lighting change can rebuild (uploaded meshes are not readable)
        }

        private readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();
        private readonly Dictionary<long, Pending> _pending = new Dictionary<long, Pending>();
        private readonly Queue<long> _order = new Queue<long>();
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly int _layer;
        private readonly double _budgetMs;

        /// <summary>Material used for every draw; nothing is drawn while null. Not owned.</summary>
        public Material Material;

        private float _normalUpBend;

        /// <summary>
        /// 0..1: how far vertex normals are bent toward +Y (Skylines.Core.Geometry.NormalBend) to soften the sun's
        /// contrast between lit and shaded faces. Changing it re-queues every built mesh for a rebuild.
        /// </summary>
        public float NormalUpBend
        {
            get { return _normalUpBend; }
            set
            {
                if (Math.Abs(value - _normalUpBend) < 1e-4f) return;
                _normalUpBend = value;
                foreach (KeyValuePair<long, Entry> kv in _entries)
                {
                    if (kv.Value.Source == null || _pending.ContainsKey(kv.Key)) continue;
                    _pending[kv.Key] = kv.Value.Source;
                    _order.Enqueue(kv.Key);
                }
            }
        }

        /// <param name="layer">Unity layer for the draws.</param>
        /// <param name="frameBudgetMs">Mesh building time per frame (checked between meshes).</param>
        public MeshStore(int layer, double frameBudgetMs)
        {
            _layer = layer;
            _budgetMs = frameBudgetMs;
        }

        /// <summary>Built entries.</summary>
        public int Count { get { return _entries.Count; } }
        /// <summary>Entries waiting to be built.</summary>
        public int PendingCount { get { return _pending.Count; } }
        /// <summary>Vertices across built entries.</summary>
        public long VertexCount { get; private set; }
        /// <summary>Entries built so far.</summary>
        public long Built { get; private set; }
        /// <summary>Building time in the last frame that built anything, the worst such frame, and the total.</summary>
        public double LastFrameBuildMs { get; private set; }
        /// <summary>See <see cref="LastFrameBuildMs"/>.</summary>
        public double MaxFrameBuildMs { get; private set; }
        /// <summary>See <see cref="LastFrameBuildMs"/>.</summary>
        public double TotalBuildMs { get; private set; }

        /// <summary>True if <paramref name="id"/> is built or queued.</summary>
        public bool Contains(long id) { return _entries.ContainsKey(id) || _pending.ContainsKey(id); }

        /// <summary>Queues (or re-queues) geometry for <paramref name="id"/>: 3 floats per position, 2 per uv, 4 colour bytes per vertex, a triangle list. Arrays are kept, not copied.</summary>
        public void Put(long id, Vector3 origin, float[] positions, float[] uvs, byte[] colors, int[] indices)
        {
            int n = positions.Length / 3;
            if (positions.Length != 3 * n || uvs.Length != 2 * n || colors.Length != 4 * n) throw new ArgumentException("vertex arrays disagree on the vertex count");
            if (!_pending.ContainsKey(id)) _order.Enqueue(id);
            _pending[id] = new Pending { Origin = origin, Positions = positions, Uvs = uvs, Colors = colors, Indices = indices };
        }

        /// <summary>Drops <paramref name="id"/>, built or queued.</summary>
        public void Remove(long id)
        {
            _pending.Remove(id);
            Entry e;
            if (_entries.TryGetValue(id, out e))
            {
                Destroy(e);
                _entries.Remove(id);
            }
        }

        /// <summary>Drops everything.</summary>
        public void Clear()
        {
            foreach (Entry e in _entries.Values) Destroy(e);
            _entries.Clear();
            _pending.Clear();
            _order.Clear();
        }

        /// <summary>Builds within the budget, then draws if <paramref name="draw"/>.</summary>
        public void LateUpdate(bool draw)
        {
            BuildSome();
            if (!draw || Material == null) return;
            foreach (Entry e in _entries.Values)
                foreach (Mesh m in e.Meshes)
                    Graphics.DrawMesh(m, e.Origin, Quaternion.identity, Material, _layer);
        }

        /// <summary>Destroys every mesh.</summary>
        public void Dispose()
        {
            Clear();
        }

        private void BuildSome()
        {
            if (_order.Count == 0) return;
            _watch.Reset();
            _watch.Start();
            while (_order.Count > 0)
            {
                long id = _order.Dequeue();
                Pending p;
                if (!_pending.TryGetValue(id, out p)) continue;
                _pending.Remove(id);
                Remove(id);
                _entries[id] = Build(p);
                Built++;
                if (_watch.Elapsed.TotalMilliseconds >= _budgetMs) break;
            }
            _watch.Stop();
            LastFrameBuildMs = _watch.Elapsed.TotalMilliseconds;
            MaxFrameBuildMs = Math.Max(MaxFrameBuildMs, LastFrameBuildMs);
            TotalBuildMs += LastFrameBuildMs;
        }

        private Entry Build(Pending p)
        {
            int n = p.Positions.Length / 3;
            List<MeshPart> parts = MeshSplit.Split(p.Indices, n, MeshSplit.MaxVertices);
            var e = new Entry { Origin = p.Origin, Meshes = new Mesh[parts.Count], Vertices = n, Source = p };
            for (int k = 0; k < parts.Count; k++)
            {
                int[] map = parts[k].VertexMap;
                var v = new Vector3[map.Length];
                var uv = new Vector2[map.Length];
                var c = new Color32[map.Length];
                for (int j = 0; j < map.Length; j++)
                {
                    int s = map[j];
                    v[j] = new Vector3(p.Positions[3 * s], p.Positions[3 * s + 1], p.Positions[3 * s + 2]);
                    uv[j] = new Vector2(p.Uvs[2 * s], p.Uvs[2 * s + 1]);
                    c[j] = new Color32(p.Colors[4 * s], p.Colors[4 * s + 1], p.Colors[4 * s + 2], p.Colors[4 * s + 3]);
                }
                var mesh = new Mesh();
                mesh.vertices = v;
                mesh.uv = uv;
                mesh.colors32 = c;
                mesh.triangles = parts[k].Indices;
                mesh.RecalculateNormals();
                if (_normalUpBend > 0f)
                {
                    Vector3[] normals = mesh.normals;
                    for (int j = 0; j < normals.Length; j++)
                    {
                        float x, y, z;
                        Skylines.Core.Geometry.NormalBend.Apply(normals[j].x, normals[j].y, normals[j].z, _normalUpBend, out x, out y, out z);
                        normals[j] = new Vector3(x, y, z);
                    }
                    mesh.normals = normals;
                }
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                e.Meshes[k] = mesh;
            }
            VertexCount += n;
            return e;
        }

        private void Destroy(Entry e)
        {
            foreach (Mesh m in e.Meshes) UnityEngine.Object.Destroy(m);
            VertexCount -= e.Vertices;
        }
    }
}
