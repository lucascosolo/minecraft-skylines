using System;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class EntityMessagesTests
    {
        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        private static byte[] Payload(string list, string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", "frames.json");
                if (File.Exists(p))
                {
                    using (JsonDocument d = JsonDocument.Parse(File.ReadAllText(p)))
                        foreach (JsonElement v in d.RootElement.GetProperty(list).EnumerateArray())
                            if (v.GetProperty("name").GetString() == name)
                                return FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                    throw new InvalidOperationException(name);
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/frames.json");
        }

        private static EntityDraw Draw(int parts)
        {
            return new EntityDraw { Parts = new EntityPartPose[parts] };
        }

        private static EntityStates OneEntity(EntityDraw[] draws)
        {
            return new EntityStates { Entities = new[] { new EntityState { Draws = draws } } };
        }

        [Fact]
        public void ConstantsMatchSpec()
        {
            Assert.Equal(0x01E0, AppProtocol.EntityModelType);
            Assert.Equal(0x01E1, AppProtocol.EntityTextureType);
            Assert.Equal(0x01E2, AppProtocol.EntityStatesType);
            Assert.Equal(21, AppProtocol.Minor);
            Assert.Equal(1024, EntityModel.MaxParts);
            Assert.Equal(4096, EntityModel.MaxQuads);
            Assert.Equal(23, EntityModel.FloatsPerQuad);
            Assert.Equal((ushort)0xFFFF, EntityModel.NoParent);
            Assert.Equal((byte)1, EntityTexture.FormatPng);
            Assert.Equal(4194304, EntityTexture.MaxLength);
            Assert.Equal(2048, EntityStates.MaxEntities);
            Assert.Equal(16, EntityStates.MaxDraws);
            Assert.Equal(1024, EntityStates.MaxParts);
            Assert.Equal((byte)1, EntityPartPose.FlagHidden);
            Assert.Equal((byte)2, EntityPartPose.FlagSkip);
        }

        [Theory]
        [InlineData("entity_model_too_many_parts")]
        [InlineData("entity_model_parent_not_before")]
        [InlineData("entity_model_too_many_quads")]
        public void InvalidModelVectorsThrow(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => EntityModel.Decode(p));
        }

        [Fact]
        public void InvalidTextureVectorThrows()
        {
            byte[] p = Payload("invalid", "entity_texture_too_long");
            Assert.Throws<ProtocolException>(() => EntityTexture.Decode(p));
        }

        [Theory]
        [InlineData("entity_states_too_many")]
        [InlineData("entity_states_too_many_draws")]
        [InlineData("entity_states_too_many_parts")]
        [InlineData("entity_states_truncated")]
        public void InvalidStatesVectorsThrow(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => EntityStates.Decode(p));
        }

        [Fact]
        public void EncodeRejectsOverLimitCounts()
        {
            var tooManyParts = new EntityModelPart[EntityModel.MaxParts + 1];
            for (int i = 0; i < tooManyParts.Length; i++) tooManyParts[i] = new EntityModelPart { Parent = EntityModel.NoParent };
            Assert.Throws<ArgumentException>(() => new EntityModel { Parts = tooManyParts }.Encode());

            var tooManyQuads = new EntityModelPart { Quads = new float[(EntityModel.MaxQuads + 1) * EntityModel.FloatsPerQuad] };
            Assert.Throws<ArgumentException>(() => new EntityModel { Parts = new[] { tooManyQuads } }.Encode());

            Assert.Throws<ArgumentException>(() => new EntityTexture { Data = new byte[EntityTexture.MaxLength + 1] }.Encode());

            var ents = new EntityState[EntityStates.MaxEntities + 1];
            for (int i = 0; i < ents.Length; i++) ents[i] = new EntityState();
            Assert.Throws<ArgumentException>(() => new EntityStates { Entities = ents }.Encode());

            var draws = new EntityDraw[EntityStates.MaxDraws + 1];
            for (int i = 0; i < draws.Length; i++) draws[i] = Draw(0);
            Assert.Throws<ArgumentException>(() => OneEntity(draws).Encode());

            Assert.Throws<ArgumentException>(() => OneEntity(new[] { Draw(EntityStates.MaxParts + 1) }).Encode());
        }

        [Fact]
        public void EncodeRejectsBadMatrixAndQuadLengths()
        {
            var bad = new EntityDraw { Matrix = new float[11] };
            Assert.Throws<ArgumentException>(() => OneEntity(new[] { bad }).Encode());
            bad = new EntityDraw { Matrix = new float[13] };
            Assert.Throws<ArgumentException>(() => OneEntity(new[] { bad }).Encode());
            var part = new EntityModelPart { Quads = new float[22] };
            Assert.Throws<ArgumentException>(() => new EntityModel { Parts = new[] { part } }.Encode());
        }

        [Fact]
        public void StatesRoundTripKeepsUnsignedValuesAboveInt32()
        {
            var matrix = new float[12];
            for (int i = 0; i < 12; i++) matrix[i] = i * 0.5f;
            var src = new EntityStates
            {
                Seq = 4000000000u,
                Entities = new[]
                {
                    new EntityState
                    {
                        EntityId = 4294967295u, X = 1.5f, Y = -2.25f, Z = 3f, BodyYaw = 90f, HeadYaw = -45f, Pitch = 10f,
                        Draws = new[]
                        {
                            new EntityDraw
                            {
                                ModelId = 3000000000u, TextureId = 2147483648u, Color = 0xFF8080FFu, Matrix = matrix,
                                Parts = new[] { new EntityPartPose { Px = 1f, Py = 2f, Pz = 3f, XRot = 0.5f, YRot = 0.25f, ZRot = -0.5f, XScale = 1f, YScale = 2f, ZScale = 1f, Flags = 3 } },
                            },
                        },
                    },
                },
            };
            EntityStates got = EntityStates.Decode(src.Encode());
            Assert.Equal(4000000000u, got.Seq);
            EntityState e = Assert.Single(got.Entities);
            Assert.Equal(4294967295u, e.EntityId);
            Assert.Equal(-45f, e.HeadYaw);
            EntityDraw d = Assert.Single(e.Draws);
            Assert.Equal(3000000000u, d.ModelId);
            Assert.Equal(2147483648u, d.TextureId);
            Assert.Equal(0xFF8080FFu, d.Color);
            Assert.Equal(matrix, d.Matrix);
            EntityPartPose p = Assert.Single(d.Parts);
            Assert.Equal(-0.5f, p.ZRot);
            Assert.Equal((byte)3, p.Flags);
            Assert.Equal(src.Encode(), got.Encode());
        }
    }
}
