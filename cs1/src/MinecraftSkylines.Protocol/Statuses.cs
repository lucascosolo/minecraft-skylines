using System;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0100 HOST_STATUS (host to guest).</summary>
    public sealed class HostStatus
    {
        /// <summary>Bits from <see cref="HostStatusFlags"/>.</summary>
        public uint Flags;
        /// <summary>The loaded city's name; empty outside a city.</summary>
        public string CityName = "";
        /// <summary>The city's pairing id; <see cref="Guid.Empty"/> when none.</summary>
        public Guid SaveId;
        /// <summary>The game version, e.g. 1.21.1-f5.</summary>
        public string GameVersion = "";

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Flags).String(CityName).Uuid(SaveId).String(GameVersion).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static HostStatus Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var s = new HostStatus();
            s.Flags = r.U32();
            s.CityName = r.String();
            s.SaveId = r.Uuid();
            s.GameVersion = r.String();
            return s;
        }
    }

    /// <summary>0x0101 GUEST_STATUS (guest to host).</summary>
    public sealed class GuestStatus
    {
        /// <summary>Bits from <see cref="GuestStatusFlags"/>.</summary>
        public uint Flags;
        /// <summary>The loaded world's name; empty when none.</summary>
        public string WorldName = "";
        /// <summary>The saveId the loaded world belongs to; <see cref="Guid.Empty"/> when none.</summary>
        public Guid PairedSaveId;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Flags).String(WorldName).Uuid(PairedSaveId).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static GuestStatus Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var s = new GuestStatus();
            s.Flags = r.U32();
            s.WorldName = r.String();
            s.PairedSaveId = r.Uuid();
            return s;
        }
    }
}
