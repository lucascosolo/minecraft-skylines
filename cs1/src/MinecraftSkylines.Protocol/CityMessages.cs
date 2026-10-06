using System;
using System.Collections.Generic;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0150 CITY_OPEN (host to guest, minor 5): a paired city is open; its edit set follows in BLOCK_EDITS.</summary>
    public sealed class CityOpen
    {
        /// <summary>Strictly increasing within a host process.</summary>
        public uint OpenSeq;
        /// <summary>The city's pairing id, never all zero.</summary>
        public Guid SaveId;
        /// <summary>For logs and the guest's UI.</summary>
        public string CityName = "";
        /// <summary>Edits the following BLOCK_EDITS batches carry in total.</summary>
        public uint EditCount;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(OpenSeq).Uuid(SaveId).String(CityName).U32(EditCount).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CityOpen Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new CityOpen { OpenSeq = r.U32(), SaveId = r.Uuid(), CityName = r.String(), EditCount = r.U32() };
        }
    }

    /// <summary>One edit of <see cref="BlockEdits"/>: a Minecraft block position and a palette index.</summary>
    public struct BlockEdit
    {
        /// <summary>Block coordinates.</summary>
        public int X, Y, Z;
        /// <summary>Index into <see cref="BlockEdits.Palette"/>.</summary>
        public ushort State;

        /// <summary>Creates an edit.</summary>
        public BlockEdit(int x, int y, int z, ushort state)
        {
            X = x; Y = y; Z = z; State = state;
        }
    }

    /// <summary>0x0151 BLOCK_EDITS (both directions, minor 5): block states by position for one open.</summary>
    public sealed class BlockEdits
    {
        /// <summary><see cref="Flags"/> bit: last batch of a CITY_OPEN snapshot.</summary>
        public const byte FlagLast = 1;
        /// <summary>Most edits in one batch.</summary>
        public const int MaxEdits = 65536;

        /// <summary>The open these edits belong to.</summary>
        public uint OpenSeq;
        /// <summary><see cref="FlagLast"/> or 0.</summary>
        public byte Flags;
        /// <summary>Block states used in this batch, no duplicates.</summary>
        public string[] Palette = new string[0];
        /// <summary>The edits, at most <see cref="MaxEdits"/>.</summary>
        public BlockEdit[] Edits = new BlockEdit[0];

        /// <summary>Encodes the payload; throws <see cref="InvalidOperationException"/> if the batch breaks the spec's limits.</summary>
        public byte[] Encode()
        {
            if (Palette.Length > ushort.MaxValue) throw new InvalidOperationException("more than 65535 palette entries");
            if (Edits.Length > MaxEdits) throw new InvalidOperationException("more than " + MaxEdits + " edits");
            var seen = new Dictionary<string, bool>();
            foreach (string s in Palette)
            {
                if (seen.ContainsKey(s)) throw new InvalidOperationException("duplicate palette entry '" + s + "'");
                seen[s] = true;
            }
            var w = new PayloadWriter().U32(OpenSeq).U8(Flags).U16((ushort)Palette.Length);
            foreach (string s in Palette) w.String(s);
            w.U32((uint)Edits.Length);
            foreach (BlockEdit e in Edits)
            {
                if (e.State >= Palette.Length) throw new InvalidOperationException("state index " + e.State + " out of range");
                w.I32(e.X).I32(e.Y).I32(e.Z).U16(e.State);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static BlockEdits Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new BlockEdits { OpenSeq = r.U32(), Flags = r.U8() };
            int n = r.U16();
            m.Palette = new string[n];
            var seen = new Dictionary<string, bool>();
            for (int i = 0; i < n; i++)
            {
                string s = r.String();
                if (seen.ContainsKey(s)) throw new ProtocolException("duplicate palette entry");
                seen[s] = true;
                m.Palette[i] = s;
            }
            uint count = r.U32();
            if (count > MaxEdits) throw new ProtocolException("editCount " + count + " > " + MaxEdits);
            if (count > (uint)(r.Remaining / 14)) throw new ProtocolException("payload truncated");
            m.Edits = new BlockEdit[count];
            for (int i = 0; i < m.Edits.Length; i++)
            {
                var e = new BlockEdit(r.I32(), r.I32(), r.I32(), r.U16());
                if (e.State >= n) throw new ProtocolException("state index " + e.State + " >= paletteCount " + n);
                m.Edits[i] = e;
            }
            return m;
        }
    }

    /// <summary>0x0152 CITY_CLOSE (host to guest, minor 5).</summary>
    public sealed class CityClose
    {
        /// <summary>The open being closed.</summary>
        public uint OpenSeq;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().U32(OpenSeq).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CityClose Decode(byte[] payload) { return new CityClose { OpenSeq = new PayloadReader(payload).U32() }; }
    }

    /// <summary>0x0153 EDIT_SYNC (host to guest) and 0x0154 EDIT_SYNC_ACK (guest to host), minor 5: the save barrier.</summary>
    public sealed class EditSync
    {
        /// <summary>The open the barrier is for.</summary>
        public uint OpenSeq;
        /// <summary>Echoed in the ack.</summary>
        public uint Token;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().U32(OpenSeq).U32(Token).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static EditSync Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new EditSync { OpenSeq = r.U32(), Token = r.U32() };
        }
    }

    /// <summary>0x0155 CITY_STATE (guest to host, minor 5): how far the guest is with an open.</summary>
    public sealed class CityStateUpdate
    {
        /// <summary><see cref="State"/>: applying the snapshot.</summary>
        public const byte Applying = 0;
        /// <summary><see cref="State"/>: applied, recording.</summary>
        public const byte Ready = 1;
        /// <summary><see cref="State"/>: closed.</summary>
        public const byte Closed = 2;

        /// <summary>The open this is about.</summary>
        public uint OpenSeq;
        /// <summary>One of the constants above.</summary>
        public byte State;
        /// <summary>Edits applied so far for this open.</summary>
        public uint AppliedCount;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().U32(OpenSeq).U8(State).U32(AppliedCount).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CityStateUpdate Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new CityStateUpdate { OpenSeq = r.U32(), State = r.U8(), AppliedCount = r.U32() };
        }
    }
}
