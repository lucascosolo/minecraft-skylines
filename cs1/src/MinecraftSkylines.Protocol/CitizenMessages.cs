using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>One thing that happened to a citizen's villager proxy in Minecraft.</summary>
    public struct CitizenEvent
    {
        /// <summary><see cref="CitizenEvents.Panic"/>, <see cref="CitizenEvents.Killed"/> or <see cref="CitizenEvents.Converted"/>.</summary>
        public byte Kind;
        /// <summary>The citizen's id as sent in the obstacle set (CS1: index into <c>CitizenManager.m_instances</c>).</summary>
        public uint Id;
        /// <summary>The proxy's feet when it happened, Minecraft frame.</summary>
        public float X, Y, Z;
    }

    /// <summary>0x01D2 CITIZEN_EVENTS (guest to host, minor 19): mobs panicked, killed or converted citizens' proxies.</summary>
    public sealed class CitizenEvents
    {
        /// <summary>A mob targeted or hit the proxy.</summary>
        public const byte Panic = 1;
        /// <summary>A mob killed the proxy.</summary>
        public const byte Killed = 2;
        /// <summary>A zombie killed the proxy and it became a zombie villager.</summary>
        public const byte Converted = 3;
        /// <summary>Most events in one message.</summary>
        public const int MaxEvents = 1024;

        /// <summary>The open the guest saw.</summary>
        public uint OpenSeq;
        /// <summary>The events, in the order they happened.</summary>
        public CitizenEvent[] Events = new CitizenEvent[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U32(OpenSeq).U16((ushort)Events.Length);
            foreach (CitizenEvent e in Events) w.U8(e.Kind).U32(e.Id).F32(e.X).F32(e.Y).F32(e.Z);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static CitizenEvents Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new CitizenEvents { OpenSeq = r.U32() };
            int n = r.U16();
            if (n > MaxEvents) throw new ProtocolException("citizen event count " + n + " is above " + MaxEvents);
            m.Events = new CitizenEvent[n];
            for (int i = 0; i < n; i++)
            {
                var e = new CitizenEvent { Kind = r.U8(), Id = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32() };
                if (e.Kind < Panic || e.Kind > Converted) throw new ProtocolException("citizen event kind " + e.Kind + " is outside 1..3");
                m.Events[i] = e;
            }
            if (r.Remaining != 0) throw new ProtocolException("citizen events payload has " + r.Remaining + " bytes beyond its count");
            return m;
        }
    }
}
