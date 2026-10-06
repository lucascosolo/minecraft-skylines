using System;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class SkyTests
    {
        private static SkyState Sample(byte moon)
        {
            return new SkyState
            {
                Flags = SkyState.FlagSky | SkyState.FlagClouds,
                SkyColor = new[] { 0.25f, 0.5f, 0.75f },
                FogColor = new[] { 0.1f, 0.2f, 0.3f },
                SunriseColor = new[] { 1f, 0.5f, 0.25f, 0.75f },
                StarBrightness = 0.5f,
                RainLevel = 0.125f,
                MoonPhase = moon,
                CloudColor = new[] { 0.9f, 0.8f, 0.7f, 0.6f },
                CloudHeight = 192.33f,
                CloudOffset = -12.5f,
                CloudSpeed = 0.03f,
            };
        }

        private static SkyTexture T(byte kind, byte phase, byte format, params byte[] data)
        {
            return new SkyTexture { Kind = kind, Phase = phase, Format = format, Data = data };
        }

        [Fact]
        public void SkyStateRoundTripAndEveryTruncationThrows()
        {
            byte[] full = Sample(5).Encode();
            SkyState m = SkyState.Decode(full);
            Assert.Equal(3, m.Flags);
            Assert.Equal(new[] { 0.25f, 0.5f, 0.75f }, m.SkyColor);
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, m.FogColor);
            Assert.Equal(new[] { 1f, 0.5f, 0.25f, 0.75f }, m.SunriseColor);
            Assert.Equal(0.5f, m.StarBrightness);
            Assert.Equal(0.125f, m.RainLevel);
            Assert.Equal(5, m.MoonPhase);
            Assert.Equal(new[] { 0.9f, 0.8f, 0.7f, 0.6f }, m.CloudColor);
            Assert.Equal(192.33f, m.CloudHeight);
            Assert.Equal(-12.5f, m.CloudOffset);
            Assert.Equal(0.03f, m.CloudSpeed);
            Assert.Equal(full, m.Encode());
            for (int len = 0; len < full.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => SkyState.Decode(cut));
            }
        }

        [Fact]
        public void SkyStatePayloadIs78Bytes()
        {
            Assert.Equal(78, Sample(0).Encode().Length);
            Assert.Equal(78, new SkyState().Encode().Length);
        }

        [Fact]
        public void SkyStateConstants()
        {
            Assert.Equal(1, SkyState.FlagSky);
            Assert.Equal(2, SkyState.FlagClouds);
            Assert.Equal(8, SkyState.MoonPhases);
        }

        [Theory]
        [InlineData(7, true)]
        [InlineData(8, false)]
        [InlineData(255, false)]
        public void SkyStateMoonPhaseAbove7Rejected(byte phase, bool ok)
        {
            byte[] p = Sample(phase).Encode();
            if (ok) Assert.Equal(phase, SkyState.Decode(p).MoonPhase);
            else Assert.Throws<ProtocolException>(() => SkyState.Decode(p));
        }

        [Fact]
        public void SkyTexturesRoundTripAndEveryTruncationThrows()
        {
            byte[] full = new SkyTextures
            {
                Textures = new[]
                {
                    T(SkyTextures.KindSun, 0, SkyTextures.FormatPng, 1, 2, 3),
                    T(SkyTextures.KindMoon, 7, SkyTextures.FormatPng, 9),
                    T(SkyTextures.KindClouds, 0, SkyTextures.FormatPng),
                },
            }.Encode();
            SkyTextures m = SkyTextures.Decode(full);
            Assert.Equal(3, m.Textures.Length);
            Assert.Equal(SkyTextures.KindSun, m.Textures[0].Kind);
            Assert.Equal(new byte[] { 1, 2, 3 }, m.Textures[0].Data);
            Assert.Equal(SkyTextures.KindMoon, m.Textures[1].Kind);
            Assert.Equal(7, m.Textures[1].Phase);
            Assert.Equal(SkyTextures.FormatPng, m.Textures[1].Format);
            Assert.Equal(new byte[] { 9 }, m.Textures[1].Data);
            Assert.Equal(SkyTextures.KindClouds, m.Textures[2].Kind);
            Assert.Empty(m.Textures[2].Data);
            Assert.Equal(full, m.Encode());
            for (int len = 0; len < full.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => SkyTextures.Decode(cut));
            }
            Assert.Empty(SkyTextures.Decode(new SkyTextures().Encode()).Textures);
        }

        [Fact]
        public void SkyTexturesMoonPhase8Rejected()
        {
            byte[] p = new SkyTextures { Textures = new[] { T(SkyTextures.KindMoon, 8, SkyTextures.FormatPng, 1) } }.Encode();
            Assert.Throws<ProtocolException>(() => SkyTextures.Decode(p));
        }

        [Fact]
        public void SkyTexturesPhaseAbove7IsFineForNonMoonKinds()
        {
            byte[] p = new SkyTextures { Textures = new[] { T(SkyTextures.KindSun, 200, SkyTextures.FormatPng, 1) } }.Encode();
            Assert.Equal(200, SkyTextures.Decode(p).Textures[0].Phase);
        }

        [Fact]
        public void SkyTexturesUnknownKindAndFormatAreAccepted()
        {
            byte[] p = new SkyTextures { Textures = new[] { T(77, 0, 99, 4, 5) } }.Encode();
            SkyTexture t = SkyTextures.Decode(p).Textures[0];
            Assert.Equal(77, t.Kind);
            Assert.Equal(99, t.Format);
            Assert.Equal(new byte[] { 4, 5 }, t.Data);
        }

        [Fact]
        public void SkyTexturesByteLengthBeyondPayloadThrows()
        {
            // count 1, kind 0, phase 0, format 1, byteLength 5 (little-endian assumed per spec), only 2 bytes follow.
            byte[] full = new SkyTextures { Textures = new[] { T(0, 0, 1, 1, 2, 3, 4, 5) } }.Encode();
            byte[] cut = new byte[full.Length - 3];
            Array.Copy(full, cut, cut.Length);
            Assert.Throws<ProtocolException>(() => SkyTextures.Decode(cut));
        }
    }
}
