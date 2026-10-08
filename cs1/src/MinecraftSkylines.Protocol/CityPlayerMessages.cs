using System;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x01B0 PLAYER_DATA (both directions, minor 11): the city's player as an opaque blob.</summary>
    public sealed class PlayerData
    {
        /// <summary>Largest <see cref="Data"/> a message may carry.</summary>
        public const int MaxLength = 4194304;

        /// <summary>The open the data belongs to.</summary>
        public uint OpenSeq;
        /// <summary>Opaque to the host. Host to guest: empty means a fresh player.</summary>
        public byte[] Data = new byte[0];

        /// <summary>Encodes the payload; throws <see cref="ArgumentException"/> if <see cref="Data"/> is longer than <see cref="MaxLength"/>.</summary>
        public byte[] Encode()
        {
            if (Data.Length > MaxLength) throw new ArgumentException("player data is " + Data.Length + " bytes, above " + MaxLength);
            return new PayloadWriter().U32(OpenSeq).U32((uint)Data.Length).Bytes(Data).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or the length is above <see cref="MaxLength"/>.</summary>
        public static PlayerData Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            uint openSeq = r.U32();
            uint length = r.U32();
            if (length > MaxLength) throw new ProtocolException("player data length " + length + " is above " + MaxLength);
            return new PlayerData { OpenSeq = openSeq, Data = r.Bytes((int)length) };
        }
    }

    /// <summary>0x01B1 RESPAWN_REQUEST (guest to host, minor 11): the player respawned away from a spawn point of its own.</summary>
    public sealed class RespawnRequest
    {
        /// <summary>The current open (informational).</summary>
        public uint OpenSeq;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().U32(OpenSeq).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static RespawnRequest Decode(byte[] payload)
        {
            return new RespawnRequest { OpenSeq = new PayloadReader(payload).U32() };
        }
    }

    /// <summary>0x01D0 CITY_ENTITIES (both directions, minor 18): the city's non-player entities as an opaque blob.</summary>
    public sealed class CityEntities
    {
        /// <summary>Largest <see cref="Data"/> a message may carry.</summary>
        public const int MaxLength = 4194304;

        /// <summary>The open the data belongs to.</summary>
        public uint OpenSeq;
        /// <summary>Opaque to the host. Empty means the city has no entities.</summary>
        public byte[] Data = new byte[0];

        /// <summary>Encodes the payload; throws <see cref="ArgumentException"/> if <see cref="Data"/> is longer than <see cref="MaxLength"/>.</summary>
        public byte[] Encode()
        {
            if (Data.Length > MaxLength) throw new ArgumentException("city entities are " + Data.Length + " bytes, above " + MaxLength);
            return new PayloadWriter().U32(OpenSeq).U32((uint)Data.Length).Bytes(Data).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or the length is above <see cref="MaxLength"/>.</summary>
        public static CityEntities Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            uint openSeq = r.U32();
            uint length = r.U32();
            if (length > MaxLength) throw new ProtocolException("city entities length " + length + " is above " + MaxLength);
            return new CityEntities { OpenSeq = openSeq, Data = r.Bytes((int)length) };
        }
    }

    /// <summary>0x01D1 CITY_FOCUS (host to guest, minor 18): where the city view looks, Minecraft frame.</summary>
    public sealed class CityFocus
    {
        /// <summary>Flag bit 0: the city view is in use.</summary>
        public const byte Active = 1;

        /// <summary>Minecraft X.</summary>
        public float X;
        /// <summary>Minecraft Z (-cs.z).</summary>
        public float Z;
        /// <summary>Flags; bit 0 is <see cref="Active"/>, others are ignored.</summary>
        public byte Flags;

        /// <summary>True when the city view is in use.</summary>
        public bool IsActive { get { return (Flags & Active) != 0; } }

        /// <summary>Encodes the 9-byte payload.</summary>
        public byte[] Encode() { return new PayloadWriter().F32(X).F32(Z).U8(Flags).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> unless it is 9 bytes and an active focus is finite.</summary>
        public static CityFocus Decode(byte[] payload)
        {
            if (payload.Length != 9) throw new ProtocolException("city focus payload is " + payload.Length + " bytes, not 9");
            var r = new PayloadReader(payload);
            var m = new CityFocus { X = r.F32(), Z = r.F32(), Flags = r.U8() };
            if (m.IsActive && (float.IsNaN(m.X) || float.IsInfinity(m.X) || float.IsNaN(m.Z) || float.IsInfinity(m.Z)))
                throw new ProtocolException("active city focus has a non-finite coordinate");
            return m;
        }
    }
}
