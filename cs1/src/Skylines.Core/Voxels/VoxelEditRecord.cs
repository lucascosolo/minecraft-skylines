using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skylines.Core.Voxels
{
    /// <summary>
    /// The edit set as stored in a save: u8 version (1), u16 paletteCount, palette (u16 byte length + UTF-8 each),
    /// u32 editCount, edits sorted by (x, z, y) as i32 x, i32 y, i32 z, u16 paletteIndex, then u32 CRC-32 (IEEE)
    /// of every preceding byte. All little endian. See docs/plans/m4.md, "Edit-set record".
    /// </summary>
    public static class VoxelEditRecord
    {
        /// <summary>The only record version.</summary>
        public const byte Version = 1;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly uint[] Table = MakeTable();

        /// <summary>Encodes a set; the palette lists states in order of first use.</summary>
        public static byte[] Encode(VoxelEditSet set)
        {
            List<VoxelEdit> edits = set.Sorted();
            var palette = new List<string>();
            var index = new Dictionary<string, ushort>();
            var states = new ushort[edits.Count];
            for (int k = 0; k < edits.Count; k++)
            {
                ushort i;
                if (!index.TryGetValue(edits[k].State, out i))
                {
                    if (palette.Count == ushort.MaxValue) throw new InvalidOperationException("more than 65535 distinct states");
                    i = (ushort)palette.Count;
                    index[edits[k].State] = i;
                    palette.Add(edits[k].State);
                }
                states[k] = i;
            }
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Version);
            w.Write((ushort)palette.Count);
            foreach (string s in palette)
            {
                byte[] b = StrictUtf8.GetBytes(s);
                if (b.Length > ushort.MaxValue) throw new InvalidOperationException("state longer than 65535 bytes");
                w.Write((ushort)b.Length);
                w.Write(b);
            }
            w.Write((uint)edits.Count);
            for (int k = 0; k < edits.Count; k++)
            {
                w.Write(edits[k].Pos.X);
                w.Write(edits[k].Pos.Y);
                w.Write(edits[k].Pos.Z);
                w.Write(states[k]);
            }
            w.Flush();
            w.Write(Crc32(ms.GetBuffer(), 0, (int)ms.Length));
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Decodes a record; throws <see cref="FormatException"/> if it is not a valid version-1 record.</summary>
        public static VoxelEditSet Decode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (data.Length < 1 + 2 + 4 + 4) throw new FormatException("record truncated (" + data.Length + " bytes)");
            if (data[0] != Version) throw new FormatException("unknown record version " + data[0]);
            int body = data.Length - 4;
            uint stored = (uint)(data[body] | data[body + 1] << 8 | data[body + 2] << 16 | data[body + 3] << 24);
            if (Crc32(data, 0, body) != stored) throw new FormatException("CRC mismatch");

            int pos = 1;
            int paletteCount = U16(data, ref pos, body);
            var palette = new string[paletteCount];
            var seen = new Dictionary<string, bool>();
            for (int i = 0; i < paletteCount; i++)
            {
                int n = U16(data, ref pos, body);
                Need(pos, n, body);
                string s;
                try { s = StrictUtf8.GetString(data, pos, n); }
                catch (ArgumentException) { throw new FormatException("palette entry " + i + " is not UTF-8"); }
                pos += n;
                if (s == VoxelEditSet.Air) throw new FormatException("palette contains " + VoxelEditSet.Air);
                if (seen.ContainsKey(s)) throw new FormatException("duplicate palette entry '" + s + "'");
                seen[s] = true;
                palette[i] = s;
            }
            Need(pos, 4, body);
            uint count = (uint)(data[pos] | data[pos + 1] << 8 | data[pos + 2] << 16 | data[pos + 3] << 24);
            pos += 4;
            if (count > (uint)((body - pos) / 14)) throw new FormatException("record truncated (" + count + " edits claimed)");
            if (pos + count * 14 != body) throw new FormatException("trailing bytes after the edits");

            var set = new VoxelEditSet();
            bool first = true;
            VoxelPos prev = default(VoxelPos);
            for (uint k = 0; k < count; k++)
            {
                var p = new VoxelPos(I32(data, pos), I32(data, pos + 4), I32(data, pos + 8));
                int state = data[pos + 12] | data[pos + 13] << 8;
                pos += 14;
                if (state >= paletteCount) throw new FormatException("palette index " + state + " out of range");
                if (!first && prev.CompareTo(p) >= 0) throw new FormatException("edits not strictly sorted at " + p);
                first = false;
                prev = p;
                set.Set(p.X, p.Y, p.Z, palette[state]);
            }
            return set;
        }

        /// <summary>CRC-32 (IEEE 802.3, reflected, as zlib's crc32) of a byte range.</summary>
        public static uint Crc32(byte[] data, int offset, int count)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++) c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        private static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static void Need(int pos, int n, int end)
        {
            if (n > end - pos) throw new FormatException("record truncated");
        }

        private static int U16(byte[] d, ref int pos, int end)
        {
            Need(pos, 2, end);
            int v = d[pos] | d[pos + 1] << 8;
            pos += 2;
            return v;
        }

        private static int I32(byte[] d, int o)
        {
            return d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24;
        }
    }
}
