using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0190 SKY_STATE (guest to host, minor 9): the values Minecraft draws its sky with; the newest replaces the previous.</summary>
    public sealed class SkyState
    {
        /// <summary>Flag: the dimension has an overworld-style sky (dome, glow, sun, moon, stars).</summary>
        public const byte FlagSky = 1;
        /// <summary>Flag: clouds are shown.</summary>
        public const byte FlagClouds = 2;
        /// <summary>Number of moon phases.</summary>
        public const int MoonPhases = 8;

        /// <summary><see cref="FlagSky"/>, <see cref="FlagClouds"/>.</summary>
        public byte Flags;
        /// <summary>Sky colour overhead (r, g, b), 0-1.</summary>
        public float[] SkyColor = new float[3];
        /// <summary>Fog colour, the sky's colour at the horizon (r, g, b).</summary>
        public float[] FogColor = new float[3];
        /// <summary>Sunrise/sunset glow (r, g, b, a).</summary>
        public float[] SunriseColor = new float[4];
        /// <summary>Star brightness 0-1 before rain.</summary>
        public float StarBrightness;
        /// <summary>Rain level 0-1.</summary>
        public float RainLevel;
        /// <summary>Moon phase 0-7 (0 full moon, 4 new moon).</summary>
        public byte MoonPhase;
        /// <summary>Cloud colour (r, g, b, a).</summary>
        public float[] CloudColor = new float[4];
        /// <summary>Minecraft Y of the cloud layer.</summary>
        public float CloudHeight;
        /// <summary>Blocks the cloud pattern has moved along +x.</summary>
        public float CloudOffset;
        /// <summary>Blocks per second <see cref="CloudOffset"/> grows by.</summary>
        public float CloudSpeed;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U8(Flags);
            F32s(w, SkyColor, 3);
            F32s(w, FogColor, 3);
            F32s(w, SunriseColor, 4);
            w.F32(StarBrightness).F32(RainLevel).U8(MoonPhase);
            F32s(w, CloudColor, 4);
            return w.F32(CloudHeight).F32(CloudOffset).F32(CloudSpeed).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or the moon phase is above 7.</summary>
        public static SkyState Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new SkyState { Flags = r.U8(), SkyColor = F32s(r, 3), FogColor = F32s(r, 3), SunriseColor = F32s(r, 4) };
            m.StarBrightness = r.F32();
            m.RainLevel = r.F32();
            m.MoonPhase = r.U8();
            if (m.MoonPhase >= MoonPhases) throw new ProtocolException("moon phase " + m.MoonPhase + " outside 0..7");
            m.CloudColor = F32s(r, 4);
            m.CloudHeight = r.F32();
            m.CloudOffset = r.F32();
            m.CloudSpeed = r.F32();
            return m;
        }

        private static void F32s(PayloadWriter w, float[] v, int n)
        {
            for (int i = 0; i < n; i++) w.F32(v[i]);
        }

        private static float[] F32s(PayloadReader r, int n)
        {
            var v = new float[n];
            for (int i = 0; i < n; i++) v[i] = r.F32();
            return v;
        }
    }

    /// <summary>One image of <see cref="SkyTextures"/>.</summary>
    public struct SkyTexture
    {
        /// <summary><see cref="SkyTextures.KindSun"/>, <see cref="SkyTextures.KindMoon"/>, <see cref="SkyTextures.KindClouds"/> or unknown.</summary>
        public byte Kind;
        /// <summary>Moon phase 0-7 for the moon, else 0.</summary>
        public byte Phase;
        /// <summary><see cref="SkyTextures.FormatPng"/> or unknown.</summary>
        public byte Format;
        /// <summary>The encoded image.</summary>
        public byte[] Data;
    }

    /// <summary>0x0191 SKY_TEXTURES (guest to host, minor 9): the sun, moon phase and cloud images; replaces the previous set.</summary>
    public sealed class SkyTextures
    {
        /// <summary>Kinds and the one format.</summary>
        public const byte KindSun = 0, KindMoon = 1, KindClouds = 2, FormatPng = 1;

        /// <summary>The images, at most 255.</summary>
        public SkyTexture[] Textures = new SkyTexture[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U8((byte)Textures.Length);
            foreach (SkyTexture t in Textures)
            {
                byte[] data = t.Data ?? new byte[0];
                w.U8(t.Kind).U8(t.Phase).U8(t.Format).U32((uint)data.Length).Bytes(data);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed or a moon phase is above 7.</summary>
        public static SkyTextures Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var list = new SkyTexture[r.U8()];
            for (int i = 0; i < list.Length; i++)
            {
                var t = new SkyTexture { Kind = r.U8(), Phase = r.U8(), Format = r.U8() };
                if (t.Kind == KindMoon && t.Phase >= SkyState.MoonPhases) throw new ProtocolException("moon phase " + t.Phase + " outside 0..7");
                uint n = r.U32();
                if (n > (uint)r.Remaining) throw new ProtocolException("payload truncated");
                t.Data = r.Bytes((int)n);
                list[i] = t;
            }
            return new SkyTextures { Textures = list };
        }
    }
}
