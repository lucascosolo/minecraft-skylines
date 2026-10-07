using System;
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

    /// <summary>0x0161 TIME_SET (guest to host, minor 16): a Minecraft time command moves the city's clock forward.</summary>
    public sealed class TimeSet
    {
        /// <summary>The time of day to move to, 0 &lt;= hour &lt; 24.</summary>
        public float Hour;
        /// <summary>Whole days to move on beyond the next <see cref="Hour"/>.</summary>
        public ushort Days;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode() { return new PayloadWriter().F32(Hour).U16(Days).ToArray(); }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or the hour is outside [0, 24).</summary>
        public static TimeSet Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new TimeSet { Hour = r.F32(), Days = r.U16() };
            if (!(m.Hour >= 0f && m.Hour < 24f)) throw new ProtocolException("TIME_SET hour " + m.Hour + " outside [0, 24)");
            return m;
        }

        /// <summary>
        /// Frames to add to SimulationManager.m_dayTimeOffsetFrames so the sun reaches the next <paramref name="hour"/>
        /// (no move when it is the current one), then <paramref name="days"/> whole days more.
        /// <paramref name="dayTimeFrame"/> is m_referenceFrameIndex + m_dayTimeOffsetFrames.
        /// </summary>
        public static uint OffsetFrames(uint dayTimeFrame, float hour, ushort days)
        {
            uint target = Math.Min(65535u, (uint)(hour * 65536f / 24f));
            return ((target - dayTimeFrame) & 65535u) + days * 65536u;
        }
    }
}
