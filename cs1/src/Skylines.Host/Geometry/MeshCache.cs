using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skylines.Host.Geometry
{
    /// <summary>
    /// Built-in CS1 mesh geometry extracted from the game's data files by <c>tools/extract-cs1-meshes.sh</c>, for meshes the
    /// game keeps GPU-only. Opening reads only the entry headers; geometry is read from the file on each successful
    /// <see cref="TryGet"/>. Format: <c>tools/cs1_meshes.py</c>. Unity-free.
    /// </summary>
    public sealed class MeshCache
    {
        /// <summary>How far each bounds component may differ from the cached one, in metres.</summary>
        public const float BoundsTolerance = 1e-3f;

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CS1MESH\0");
        private const uint Version1 = 1, Version2 = 2;

        /// <summary>Channel mask bits of a version 2 entry, stored after the indices in this order.</summary>
        public const int ChannelNormals = 1, ChannelTangents = 2, ChannelColors = 4, ChannelUv = 8, ChannelUv2 = 16, ChannelUv3 = 32, ChannelUv4 = 64;
        private const int KnownChannels = 127;

        private sealed class Entry
        {
            public string Name;
            public float Cx, Cy, Cz, Ex, Ey, Ez;
            public int IndexCount;
            public int Mask;
            public long Offset; // of the positions
        }

        private readonly string _path;
        private readonly Dictionary<int, List<Entry>> _byVertexCount = new Dictionary<int, List<Entry>>();

        /// <summary>True when the file was opened and indexed completely.</summary>
        public bool Available { get; private set; }
        /// <summary>Entry count, or why the cache is unavailable.</summary>
        public string Status { get; private set; }
        /// <summary>Entries indexed; 0 when unavailable.</summary>
        public int Count { get; private set; }

        private MeshCache(string path)
        {
            _path = path;
        }

        /// <summary>The cache file under a home folder, or null without one.</summary>
        public static string DefaultPath(string home)
        {
            return string.IsNullOrEmpty(home) ? null : home.TrimEnd('/') + "/.cache/minecraft-skylines/cs1-meshes/meshes.bin";
        }

        /// <summary>Indexes the file at <paramref name="path"/>. Never throws; check <see cref="Available"/>.</summary>
        public static MeshCache Open(string path)
        {
            var c = new MeshCache(path);
            try
            {
                if (string.IsNullOrEmpty(path)) c.Status = "no cache path (HOME unset)";
                else if (!File.Exists(path)) c.Status = "no cache at " + path + " (run tools/extract-cs1-meshes.sh)";
                else c.Status = c.Index();
            }
            catch (Exception e)
            {
                c.Status = path + ": " + e.GetType().Name + ": " + e.Message;
            }
            if (!c.Available)
            {
                c._byVertexCount.Clear();
                c.Count = 0;
            }
            return c;
        }

        private string Index()
        {
            using (var s = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var r = new BinaryReader(s))
            {
                long length = s.Length;
                if (length < 16) return _path + ": truncated header";
                byte[] magic = r.ReadBytes(8);
                for (int i = 0; i < 8; i++)
                    if (magic[i] != Magic[i]) return _path + ": not a mesh cache (bad magic)";
                uint version = r.ReadUInt32();
                if (version != Version1 && version != Version2) return _path + ": unsupported version " + version;
                uint count = r.ReadUInt32();
                for (uint n = 0; n < count; n++)
                {
                    if (s.Position + 2 > length) return _path + ": truncated at entry " + n;
                    int nameLength = r.ReadUInt16();
                    if (s.Position + nameLength + 32 > length) return _path + ": truncated at entry " + n;
                    var e = new Entry { Name = Encoding.UTF8.GetString(r.ReadBytes(nameLength)) };
                    uint vertexCount = r.ReadUInt32();
                    e.Cx = r.ReadSingle(); e.Cy = r.ReadSingle(); e.Cz = r.ReadSingle();
                    e.Ex = r.ReadSingle(); e.Ey = r.ReadSingle(); e.Ez = r.ReadSingle();
                    uint indexCount = r.ReadUInt32();
                    if (vertexCount > 65535 || indexCount > int.MaxValue / 2 || indexCount % 3 != 0) return _path + ": bad entry " + n;
                    e.IndexCount = (int)indexCount;
                    if (version == Version2)
                    {
                        if (s.Position + 4 > length) return _path + ": truncated at entry " + n;
                        e.Mask = (int)r.ReadUInt32();
                        if ((e.Mask & ~KnownChannels) != 0) return _path + ": bad channel mask at entry " + n;
                    }
                    e.Offset = s.Position;
                    long next = e.Offset + 12L * vertexCount + 2L * indexCount + ChannelBytes(e.Mask) * vertexCount;
                    if (next > length) return _path + ": truncated at entry " + n;
                    s.Position = next;
                    List<Entry> list;
                    if (!_byVertexCount.TryGetValue((int)vertexCount, out list)) _byVertexCount[(int)vertexCount] = list = new List<Entry>();
                    list.Add(e);
                    Count++;
                }
                Available = true;
                return Count + " meshes from " + _path;
            }
        }

        /// <summary>
        /// Geometry of the cached mesh with this vertex count and bounds (centre, extent): the one with this name, else the
        /// only one. <paramref name="positions"/> is flat xyz in mesh-local space. False when none or several match.
        /// </summary>
        public bool TryGet(string name, int vertexCount, float cx, float cy, float cz, float ex, float ey, float ez,
            out float[] positions, out int[] indices)
        {
            positions = null;
            indices = null;
            CachedMesh mesh;
            if (!TryGetMesh(name, vertexCount, cx, cy, cz, ex, ey, ez, out mesh)) return false;
            positions = mesh.Positions;
            indices = mesh.Indices;
            return true;
        }

        /// <summary>
        /// Like <see cref="TryGet"/>, with every vertex channel the cache holds for the mesh (version 2 caches; a version 1
        /// cache gives positions and indices only).
        /// </summary>
        public bool TryGetMesh(string name, int vertexCount, float cx, float cy, float cz, float ex, float ey, float ez, out CachedMesh mesh)
        {
            mesh = null;
            List<Entry> list;
            if (!Available || !_byVertexCount.TryGetValue(vertexCount, out list)) return false;
            Entry found = null;
            int matches = 0;
            foreach (Entry e in list)
            {
                if (!(Near(e.Cx, cx) && Near(e.Cy, cy) && Near(e.Cz, cz) && Near(e.Ex, ex) && Near(e.Ey, ey) && Near(e.Ez, ez))) continue;
                if (e.Name == name) { found = e; matches = 1; break; }
                matches++;
                found = e;
            }
            if (matches == 0)
            {
                // CS1 replaces the bounds of net segment meshes with a 128 m box when it loads them
                // (NetInfo.InitSegmentInfo, NetInfo.cs:1568), so those match by name and vertex count alone.
                foreach (Entry e in list)
                {
                    if (e.Name != name) continue;
                    matches++;
                    found = e;
                }
            }
            if (matches != 1) return false;
            try
            {
                mesh = Read(found, vertexCount);
                return true;
            }
            catch (Exception)
            {
                mesh = null;
                return false;
            }
        }

        private static bool Near(float a, float b)
        {
            return Math.Abs(a - b) <= BoundsTolerance;
        }

        private static long ChannelBytes(int mask)
        {
            long b = 0;
            if ((mask & ChannelNormals) != 0) b += 12;
            if ((mask & ChannelTangents) != 0) b += 16;
            if ((mask & ChannelColors) != 0) b += 4;
            for (int bit = ChannelUv; bit <= ChannelUv4; bit <<= 1)
                if ((mask & bit) != 0) b += 8;
            return b;
        }

        private CachedMesh Read(Entry e, int vertexCount)
        {
            var m = new CachedMesh { Indices = new int[e.IndexCount] };
            using (var s = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var r = new BinaryReader(s))
            {
                s.Position = e.Offset;
                m.Positions = Floats(r, 3 * vertexCount);
                for (int i = 0; i < m.Indices.Length; i++)
                {
                    m.Indices[i] = r.ReadUInt16();
                    if (m.Indices[i] >= vertexCount) throw new InvalidDataException("index out of range");
                }
                if ((e.Mask & ChannelNormals) != 0) m.Normals = Floats(r, 3 * vertexCount);
                if ((e.Mask & ChannelTangents) != 0) m.Tangents = Floats(r, 4 * vertexCount);
                if ((e.Mask & ChannelColors) != 0) m.Colors = r.ReadBytes(4 * vertexCount);
                if ((e.Mask & ChannelUv) != 0) m.Uv = Floats(r, 2 * vertexCount);
                if ((e.Mask & ChannelUv2) != 0) m.Uv2 = Floats(r, 2 * vertexCount);
                if ((e.Mask & ChannelUv3) != 0) m.Uv3 = Floats(r, 2 * vertexCount);
                if ((e.Mask & ChannelUv4) != 0) m.Uv4 = Floats(r, 2 * vertexCount);
            }
            return m;
        }

        private static float[] Floats(BinaryReader r, int count)
        {
            var a = new float[count];
            for (int i = 0; i < count; i++) a[i] = r.ReadSingle();
            return a;
        }
    }
}

namespace Skylines.Host.Geometry
{
    /// <summary>One cached mesh: flat per-vertex arrays (positions and normals 3, tangents 4, colors 4 bytes RGBA, uvs 2 per vertex); absent channels are null.</summary>
    public sealed class CachedMesh
    {
        /// <summary>Mesh-local xyz.</summary>
        public float[] Positions;
        /// <summary>Triangle list over all submeshes.</summary>
        public int[] Indices;
        /// <summary>xyz normals, or null.</summary>
        public float[] Normals;
        /// <summary>xyzw tangents, or null.</summary>
        public float[] Tangents;
        /// <summary>RGBA colours, or null.</summary>
        public byte[] Colors;
        /// <summary>Texture coordinate sets 0 to 3 (Unity uv, uv2, uv3, uv4), or null.</summary>
        public float[] Uv, Uv2, Uv3, Uv4;
    }
}
