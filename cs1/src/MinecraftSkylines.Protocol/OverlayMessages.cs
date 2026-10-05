using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0140 VIEWPORT (host to guest, minor 3).</summary>
    public sealed class Viewport
    {
        /// <summary>The host's screen size in pixels.</summary>
        public uint Width, Height;
        /// <summary>0 lets the guest choose its GUI scale; otherwise a requested scale.</summary>
        public float UiScale;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Width).U32(Height).F32(UiScale).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static Viewport Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new Viewport { Width = r.U32(), Height = r.U32(), UiScale = r.F32() };
        }
    }

    /// <summary>0x0141 OVERLAY_OFFER (guest to host, minor 3).</summary>
    public sealed class OverlayOffer
    {
        /// <summary>Absolute path of the shared-memory file.</summary>
        public string Path = "";
        /// <summary>Largest frame a slot holds.</summary>
        public uint MaxWidth, MaxHeight;
        /// <summary>3.</summary>
        public uint SlotCount;
        /// <summary>Changes whenever the guest recreates the file.</summary>
        public ulong Generation;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().String(Path).U32(MaxWidth).U32(MaxHeight).U32(SlotCount).U64(Generation).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static OverlayOffer Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new OverlayOffer { Path = r.String(), MaxWidth = r.U32(), MaxHeight = r.U32(), SlotCount = r.U32(), Generation = r.U64() };
        }
    }
}
