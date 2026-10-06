using System;

namespace Skylines.Core.Sky
{
    /// <summary>Colour, cloud and star maths for drawing a block-game sky (protocol 1.9). No engine types.</summary>
    public static class SkyMath
    {
        /// <summary>Blocks per cloud texel.</summary>
        public const float CloudCellBlocks = 12f;
        /// <summary>The cloud grid's z shift in blocks.</summary>
        public const float CloudZShift = 3.96f;
        /// <summary>Sky state older than this is not drawn.</summary>
        public const double StaleSeconds = 5.0;
        /// <summary>Texture slots: sun 0, moon phases 1-8, clouds 9.</summary>
        public const int Slots = 10;

        public static float Clamp01(float x)
        {
            if (float.IsNaN(x)) return 0f;
            return x < 0f ? 0f : x > 1f ? 1f : x;
        }

        /// <summary>0 at and below the horizon to 1 from 30 degrees up (smoothstep on the sine).</summary>
        public static float HorizonBlend(float sinElevation)
        {
            float t = Clamp01(sinElevation / 0.5f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Weight of the sunrise glow: strongest on the horizon towards the sun.</summary>
        public static float GlowWeight(float sinElevation, float cosToSunAzimuth)
        {
            float a = float.IsNaN(cosToSunAzimuth) ? 0f : Math.Max(0f, cosToSunAzimuth);
            float e = 1f - Clamp01(Math.Abs(sinElevation) / 0.6f);
            return a * a * e;
        }

        /// <summary>Dome colour: fog at the horizon to sky overhead, then the glow blended in by its alpha.</summary>
        public static void DomeColour(float sinElevation, float cosToSunAzimuth, float[] sky3, float[] fog3, float[] sunrise4, float[] out3)
        {
            float h = HorizonBlend(sinElevation);
            float w = Clamp01(sunrise4[3]) * GlowWeight(sinElevation, cosToSunAzimuth);
            for (int i = 0; i < 3; i++)
            {
                float b = fog3[i] + (sky3[i] - fog3[i]) * h;
                out3[i] = b + (sunrise4[i] - b) * w;
            }
        }

        /// <summary>Sun and moon opacity: 1 - rain.</summary>
        public static float CelestialAlpha(float rainLevel)
        {
            return float.IsNaN(rainLevel) ? 1f : Clamp01(1f - rainLevel);
        }

        public static float StarAlpha(float starBrightness, float rainLevel)
        {
            return Clamp01(starBrightness) * CelestialAlpha(rainLevel);
        }

        /// <summary>Slot of a texture message entry, or -1 to skip it.</summary>
        public static int TextureSlot(byte kind, byte phase)
        {
            switch (kind)
            {
                case 0: return 0;
                case 1: return phase < 8 ? 1 + phase : -1;
                case 2: return 9;
                default: return -1;
            }
        }

        public static int MoonSlot(int moonPhase)
        {
            return 1 + ((moonPhase % 8) + 8) % 8;
        }

        /// <summary>Cloud texel at a block position (row 0 = the image's top row).</summary>
        public static void CloudTexel(double mcX, double mcZ, double cloudOffset, int width, int height, out int column, out int row)
        {
            column = FloorMod((long)Math.Floor((mcX + cloudOffset) / CloudCellBlocks), width);
            row = FloorMod((long)Math.Floor((mcZ + CloudZShift) / CloudCellBlocks), height);
        }

        /// <summary>Texture coordinates (bottom-up v, repeat wrap) of a block position on the cloud layer.</summary>
        public static void CloudUv(double mcX, double mcZ, double cloudOffset, int width, int height, out float u, out float v)
        {
            u = (float)Frac((mcX + cloudOffset) / (CloudCellBlocks * width));
            if (u >= 1f) u = 0f;
            v = 1f - (float)Frac((mcZ + CloudZShift) / (CloudCellBlocks * height));
        }

        public static double CloudOffsetAt(double offset, double speed, double ageSeconds)
        {
            return offset + speed * Math.Max(0.0, Math.Min(ageSeconds, StaleSeconds));
        }

        public static bool IsFresh(double ageSeconds)
        {
            return ageSeconds >= 0 && ageSeconds < StaleSeconds;
        }

        /// <summary>Deterministic star directions (uniform on the sphere) and angular half-sizes.</summary>
        public static void Stars(int count, uint seed, float[] dirs, float[] sizes)
        {
            if (dirs == null || sizes == null || dirs.Length < 3 * count || sizes.Length < count) throw new ArgumentException("star buffers too small");
            uint s = seed == 0 ? 0x9E3779B9u : seed;
            for (int i = 0; i < count; i++)
            {
                double x, y, z, l2;
                do
                {
                    x = Next(ref s) * 2 - 1;
                    y = Next(ref s) * 2 - 1;
                    z = Next(ref s) * 2 - 1;
                    l2 = x * x + y * y + z * z;
                }
                while (l2 < 0.01 || l2 > 1);
                double l = Math.Sqrt(l2);
                dirs[3 * i] = (float)(x / l);
                dirs[3 * i + 1] = (float)(y / l);
                dirs[3 * i + 2] = (float)(z / l);
                sizes[i] = (float)(0.0015 + 0.001 * Next(ref s));
            }
        }

        private static double Next(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return s / 4294967296.0;
        }

        private static int FloorMod(long a, int n)
        {
            long m = a % n;
            return (int)(m < 0 ? m + n : m);
        }

        private static double Frac(double x)
        {
            return x - Math.Floor(x);
        }
    }
}
