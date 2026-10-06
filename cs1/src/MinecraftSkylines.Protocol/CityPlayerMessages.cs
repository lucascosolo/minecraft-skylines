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
}
