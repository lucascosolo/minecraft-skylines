using System;
using System.Collections.Generic;
using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>One lit light of <see cref="LightSources"/>: the Minecraft block containing it and its light level.</summary>
    public struct LightSource
    {
        /// <summary>Minecraft block coordinates.</summary>
        public int X, Y, Z;
        /// <summary>Minecraft light level, 1 to <see cref="LightSources.MaxLevel"/>.</summary>
        public byte Level;
    }

    /// <summary>0x0180 LIGHT_SOURCES (host to guest, minor 8): every lit city light near the player; replaces the previous set.</summary>
    public sealed class LightSources
    {
        /// <summary>Highest Minecraft light level.</summary>
        public const byte MaxLevel = 15;

        /// <summary>The lights, each position at most once; at most 65535.</summary>
        public LightSource[] Lights = new LightSource[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U16((ushort)Lights.Length);
            foreach (LightSource s in Lights) w.I32(s.X).I32(s.Y).I32(s.Z).U8(s.Level);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or a level is outside 1..15.</summary>
        public static LightSources Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var list = new LightSource[r.U16()];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = new LightSource { X = r.I32(), Y = r.I32(), Z = r.I32(), Level = r.U8() };
                if (list[i].Level < 1 || list[i].Level > MaxLevel) throw new ProtocolException("light level " + list[i].Level + " outside 1..15");
            }
            return new LightSources { Lights = list };
        }
    }

    /// <summary>Maps the host's lights to Minecraft light levels.</summary>
    public static class LightLevels
    {
        /// <summary>
        /// clamp(ceil(range * min(intensity, 1)), 0, 15): Minecraft light falls by one level per block (taxicab), so a level
        /// equal to the range in metres reaches at most as far as the host's light. 0 for a NaN or non-positive input.
        /// </summary>
        public static int FromRange(float range, float intensity)
        {
            if (!(range > 0f) || !(intensity > 0f)) return 0;
            return Math.Min(LightSources.MaxLevel, (int)Math.Ceiling(range * Math.Min(intensity, 1f)));
        }

        /// <summary>One entry per position with the highest level, level-0 entries dropped, sorted by X, then Z, then Y.</summary>
        public static LightSource[] Merge(IEnumerable<LightSource> candidates)
        {
            var list = new List<LightSource>();
            foreach (LightSource s in candidates) if (s.Level != 0) list.Add(s);
            list.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Y.CompareTo(b.Y));
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                LightSource s = list[i];
                if (n > 0 && list[n - 1].X == s.X && list[n - 1].Y == s.Y && list[n - 1].Z == s.Z)
                {
                    if (s.Level > list[n - 1].Level) list[n - 1] = s;
                }
                else list[n++] = s;
            }
            list.RemoveRange(n, list.Count - n);
            return list.ToArray();
        }
    }
}
