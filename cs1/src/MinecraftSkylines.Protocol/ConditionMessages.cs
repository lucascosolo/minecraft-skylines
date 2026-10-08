using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>One CS1 natural resource cell (33.75 m) as CITY_CONDITIONS carries it.</summary>
    public struct ConditionCell
    {
        /// <summary>Resource grid cell, each 0..511.</summary>
        public ushort Cx, Cz;
        /// <summary>CS1's <c>NaturalResourceManager.ResourceCell</c> bytes.</summary>
        public byte Ore, Oil, Fertility, Forest, Pollution;
        /// <summary>Bit 0 <see cref="CityConditions.Worked"/>.</summary>
        public byte Flags;
        /// <summary>Crime rate (0..100) of the district at the cell's centre.</summary>
        public byte Crime;
        /// <summary>Buildings in the cell whose dead wait for a hearse, clamped to 255.</summary>
        public byte Dead;
    }

    /// <summary>A burning building as CITY_CONDITIONS carries it.</summary>
    public struct FireSpot
    {
        /// <summary>Building position, Minecraft frame.</summary>
        public float X, Y, Z;
        /// <summary>Half the footprint diagonal, m.</summary>
        public float Radius;
        /// <summary>Fire intensity, 1..255.</summary>
        public byte Intensity;
    }

    /// <summary>0x0210 CITY_CONDITIONS (host to guest, minor 21): resource cells and fires around the simulated area.</summary>
    public sealed class CityConditions
    {
        /// <summary>Most cells in one message.</summary>
        public const int MaxCells = 256;
        /// <summary>Most fires in one message.</summary>
        public const int MaxFires = 256;
        /// <summary>Cell flag: the city has extracted or changed resources there.</summary>
        public const byte Worked = 1;

        /// <summary>The open this belongs to.</summary>
        public uint OpenSeq;
        /// <summary>The cells.</summary>
        public ConditionCell[] Cells = new ConditionCell[0];
        /// <summary>The burning buildings.</summary>
        public FireSpot[] Fires = new FireSpot[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U32(OpenSeq).U16((ushort)Cells.Length);
            foreach (ConditionCell c in Cells)
                w.U16(c.Cx).U16(c.Cz).U8(c.Ore).U8(c.Oil).U8(c.Fertility).U8(c.Forest).U8(c.Pollution).U8(c.Flags).U8(c.Crime).U8(c.Dead);
            w.U16((ushort)Fires.Length);
            foreach (FireSpot f in Fires) w.F32(f.X).F32(f.Y).F32(f.Z).F32(f.Radius).U8(f.Intensity);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CityConditions Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new CityConditions { OpenSeq = r.U32() };
            int n = r.U16();
            if (n > MaxCells) throw new ProtocolException("condition cell count " + n + " is above " + MaxCells);
            m.Cells = new ConditionCell[n];
            for (int i = 0; i < n; i++)
            {
                var c = new ConditionCell
                {
                    Cx = r.U16(), Cz = r.U16(), Ore = r.U8(), Oil = r.U8(), Fertility = r.U8(), Forest = r.U8(),
                    Pollution = r.U8(), Flags = r.U8(), Crime = r.U8(), Dead = r.U8(),
                };
                if (c.Cx > 511 || c.Cz > 511) throw new ProtocolException("condition cell " + c.Cx + "," + c.Cz + " is outside 0..511");
                m.Cells[i] = c;
            }
            int k = r.U16();
            if (k > MaxFires) throw new ProtocolException("fire count " + k + " is above " + MaxFires);
            m.Fires = new FireSpot[k];
            for (int i = 0; i < k; i++)
            {
                var f = new FireSpot { X = r.F32(), Y = r.F32(), Z = r.F32(), Radius = r.F32(), Intensity = r.U8() };
                if (!Finite(f.X) || !Finite(f.Y) || !Finite(f.Z) || !Finite(f.Radius) || f.Radius < 0)
                    throw new ProtocolException("fire position or radius not finite, or radius below 0");
                if (f.Intensity == 0) throw new ProtocolException("fire intensity 0");
                m.Fires[i] = f;
            }
            if (r.Remaining != 0) throw new ProtocolException("city conditions payload has " + r.Remaining + " bytes beyond its counts");
            return m;
        }

        private static bool Finite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }

    /// <summary>Ore blocks of one resource the player broke in one resource cell.</summary>
    public struct OreCell
    {
        /// <summary><see cref="OreMined.Ore"/> or <see cref="OreMined.Oil"/>.</summary>
        public byte Resource;
        /// <summary>Resource grid cell, each 0..511.</summary>
        public ushort Cx, Cz;
        /// <summary>Blocks broken, at least 1.</summary>
        public ushort Blocks;
    }

    /// <summary>0x0211 ORE_MINED (guest to host, minor 21): shadow ore the player broke, per resource cell.</summary>
    public sealed class OreMined
    {
        /// <summary>Metal ores: the cell's ore.</summary>
        public const byte Ore = 1;
        /// <summary>Coal: the cell's oil.</summary>
        public const byte Oil = 2;
        /// <summary>Most entries in one message.</summary>
        public const int MaxEntries = 256;
        /// <summary>Resource units the host takes per block.</summary>
        public const int UnitsPerBlock = 4;

        /// <summary>The open the guest saw.</summary>
        public uint OpenSeq;
        /// <summary>The entries.</summary>
        public OreCell[] Entries = new OreCell[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U32(OpenSeq).U16((ushort)Entries.Length);
            foreach (OreCell e in Entries) w.U8(e.Resource).U16(e.Cx).U16(e.Cz).U16(e.Blocks);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static OreMined Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new OreMined { OpenSeq = r.U32() };
            int n = r.U16();
            if (n > MaxEntries) throw new ProtocolException("ore entry count " + n + " is above " + MaxEntries);
            m.Entries = new OreCell[n];
            for (int i = 0; i < n; i++)
            {
                var e = new OreCell { Resource = r.U8(), Cx = r.U16(), Cz = r.U16(), Blocks = r.U16() };
                if (e.Resource != Ore && e.Resource != Oil) throw new ProtocolException("ore resource " + e.Resource + " is outside 1..2");
                if (e.Cx > 511 || e.Cz > 511) throw new ProtocolException("ore cell " + e.Cx + "," + e.Cz + " is outside 0..511");
                if (e.Blocks == 0) throw new ProtocolException("ore entry with 0 blocks");
                m.Entries[i] = e;
            }
            if (r.Remaining != 0) throw new ProtocolException("ore mined payload has " + r.Remaining + " bytes beyond its count");
            return m;
        }
    }
}
