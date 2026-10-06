using System;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>One tree of <see cref="Trees"/>: trunk base in the Minecraft frame, size in metres.</summary>
    public struct TreeRecord
    {
        /// <summary>Kind of a bush (leaves only).</summary>
        public const byte KindBush = 6;

        /// <summary>The host's tree id (TreeManager index).</summary>
        public uint Id;
        /// <summary>Trunk base, Minecraft frame.</summary>
        public float X, Y, Z;
        /// <summary>Height of the drawn tree in metres.</summary>
        public float Height;
        /// <summary>Crown radius in metres.</summary>
        public float Radius;
        /// <summary>0 oak, 1 spruce, 2 birch, 3 jungle, 4 acacia, 5 dark oak, 6 bush.</summary>
        public byte Kind;

        /// <summary>
        /// Kind from a TreeInfo name (case-insensitive substring) and drawn height: pine/conifer/spruce/fir 1, birch 2,
        /// palm/jungle 3, acacia/savanna 4, dark/dead 5, bush/shrub/hedge or height under 2.5 m 6, else 0 (oak).
        /// </summary>
        public static byte KindOf(string name, float height)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (Has(n, "pine", "conifer", "spruce", "fir")) return 1;
            if (Has(n, "birch")) return 2;
            if (Has(n, "palm", "jungle")) return 3;
            if (Has(n, "acacia", "savanna")) return 4;
            if (Has(n, "dark", "dead")) return 5;
            if (Has(n, "bush", "shrub", "hedge") || height < 2.5f) return KindBush;
            return 0;
        }

        private static bool Has(string name, params string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (name.IndexOf(words[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    /// <summary>0x01C0 TREES (host to guest, minor 12): the trees the host draws in one collision region.</summary>
    public sealed class Trees
    {
        /// <summary>Largest tree count a message may carry.</summary>
        public const int MaxCount = 4096;

        /// <summary>Collision epoch of the region this list belongs to.</summary>
        public uint Epoch;
        /// <summary>Region column index.</summary>
        public int RegionX;
        /// <summary>Region row index.</summary>
        public int RegionZ;
        /// <summary>The trees (at most <see cref="MaxCount"/>).</summary>
        public TreeRecord[] Items = new TreeRecord[0];

        /// <summary>Encodes the payload; throws <see cref="ArgumentException"/> above <see cref="MaxCount"/> trees.</summary>
        public byte[] Encode()
        {
            if (Items.Length > MaxCount) throw new ArgumentException(Items.Length + " trees, above " + MaxCount);
            PayloadWriter w = new PayloadWriter().U32(Epoch).I32(RegionX).I32(RegionZ).U16((ushort)Items.Length);
            for (int i = 0; i < Items.Length; i++)
            {
                TreeRecord t = Items[i];
                w.U32(t.Id).F32(t.X).F32(t.Y).F32(t.Z).F32(t.Height).F32(t.Radius).U8(t.Kind);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed, the count is above
        /// <see cref="MaxCount"/> or the length does not match the count exactly.</summary>
        public static Trees Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new Trees { Epoch = r.U32(), RegionX = r.I32(), RegionZ = r.I32() };
            int n = r.U16();
            if (n > MaxCount) throw new ProtocolException("tree count " + n + " is above " + MaxCount);
            m.Items = new TreeRecord[n];
            for (int i = 0; i < n; i++)
                m.Items[i] = new TreeRecord { Id = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(), Height = r.F32(), Radius = r.F32(), Kind = r.U8() };
            if (r.Remaining != 0) throw new ProtocolException("trees payload has " + r.Remaining + " bytes beyond its count");
            return m;
        }
    }

    /// <summary>0x01C1 TREE_FELLED (guest to host, minor 12): the player broke every log of the tree placed for <see cref="TreeId"/>.</summary>
    public sealed class TreeFelled
    {
        /// <summary>The open the guest saw.</summary>
        public uint OpenSeq;
        /// <summary>The host's tree id.</summary>
        public uint TreeId;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().U32(OpenSeq).U32(TreeId).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static TreeFelled Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new TreeFelled { OpenSeq = r.U32(), TreeId = r.U32() };
        }
    }
}
