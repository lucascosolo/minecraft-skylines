using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0160 WORLD_TIME (host to guest, minor 6): the city's time of day, which the guest shows exactly.</summary>
    public sealed class WorldTime
    {
        /// <summary>Flags bit 0: the city has a day/night cycle (when clear the guest shows midday).</summary>
        public const byte DayNight = 1;

        /// <summary>Time of day, 0 &lt;= hour &lt; 24.</summary>
        public float Hour;
        /// <summary>Whole days since a fixed epoch (moon phase).</summary>
        public uint Day;
        /// <summary>See <see cref="DayNight"/>.</summary>
        public byte Flags;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().F32(Hour).U32(Day).U8(Flags).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static WorldTime Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            return new WorldTime { Hour = r.F32(), Day = r.U32(), Flags = r.U8() };
        }

        /// <summary>Ticks into Minecraft's day (0 = 06:00, 6000 = noon) for a city hour.</summary>
        public static int MinecraftDayTicks(float hour)
        {
            double h = ((hour - 6.0) % 24.0 + 24.0) % 24.0;
            return (int)(h * 1000.0) % 24000;
        }
    }
}
