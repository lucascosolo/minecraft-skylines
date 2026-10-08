using System;
using System.IO;
using System.Text.Json;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class CityConditionsTests
    {
        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        private static JsonElement Vector(string list, string name, out JsonDocument doc)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", "frames.json");
                if (File.Exists(p))
                {
                    doc = JsonDocument.Parse(File.ReadAllText(p));
                    foreach (JsonElement v in doc.RootElement.GetProperty(list).EnumerateArray())
                        if (v.GetProperty("name").GetString() == name) return v;
                    throw new InvalidOperationException(name);
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/frames.json");
        }

        private static byte[] Payload(string list, string name)
        {
            JsonDocument d;
            JsonElement v = Vector(list, name, out d);
            using (d) return FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal(21, AppProtocol.Minor);
            Assert.Equal(0x0210, AppProtocol.CityConditionsType);
            Assert.Equal(0x0211, AppProtocol.OreMinedType);
            Assert.Equal(256, CityConditions.MaxCells);
            Assert.Equal(256, CityConditions.MaxFires);
            Assert.Equal(1, CityConditions.Worked);
            Assert.Equal(1, OreMined.Ore);
            Assert.Equal(2, OreMined.Oil);
            Assert.Equal(256, OreMined.MaxEntries);
            Assert.Equal(4, OreMined.UnitsPerBlock);
        }

        [Theory]
        [InlineData("city_conditions_empty")]
        [InlineData("city_conditions_full")]
        public void ConditionsVectorDecodesToFields(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                CityConditions m = CityConditions.Decode(payload);
                JsonElement f = v.GetProperty("fields");
                Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                JsonElement cells = f.GetProperty("cells");
                Assert.Equal(cells.GetArrayLength(), m.Cells.Length);
                int i = 0;
                foreach (JsonElement c in cells.EnumerateArray())
                {
                    ConditionCell o = m.Cells[i++];
                    Assert.Equal(c.GetProperty("cx").GetInt32(), (int)o.Cx);
                    Assert.Equal(c.GetProperty("cz").GetInt32(), (int)o.Cz);
                    Assert.Equal(c.GetProperty("ore").GetInt32(), (int)o.Ore);
                    Assert.Equal(c.GetProperty("oil").GetInt32(), (int)o.Oil);
                    Assert.Equal(c.GetProperty("fertility").GetInt32(), (int)o.Fertility);
                    Assert.Equal(c.GetProperty("forest").GetInt32(), (int)o.Forest);
                    Assert.Equal(c.GetProperty("pollution").GetInt32(), (int)o.Pollution);
                    Assert.Equal(c.GetProperty("flags").GetInt32(), (int)o.Flags);
                    Assert.Equal(c.GetProperty("crime").GetInt32(), (int)o.Crime);
                    Assert.Equal(c.GetProperty("dead").GetInt32(), (int)o.Dead);
                }
                JsonElement fires = f.GetProperty("fires");
                Assert.Equal(fires.GetArrayLength(), m.Fires.Length);
                i = 0;
                foreach (JsonElement e in fires.EnumerateArray())
                {
                    FireSpot o = m.Fires[i++];
                    Assert.Equal((float)e.GetProperty("x").GetDouble(), o.X);
                    Assert.Equal((float)e.GetProperty("y").GetDouble(), o.Y);
                    Assert.Equal((float)e.GetProperty("z").GetDouble(), o.Z);
                    Assert.Equal((float)e.GetProperty("radius").GetDouble(), o.Radius);
                    Assert.Equal(e.GetProperty("intensity").GetInt32(), (int)o.Intensity);
                }
            }
        }

        [Theory]
        [InlineData("city_conditions_empty")]
        [InlineData("city_conditions_full")]
        public void ConditionsEncodingFieldsEqualsVectorPayload(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                JsonElement cs = f.GetProperty("cells");
                var cells = new ConditionCell[cs.GetArrayLength()];
                int i = 0;
                foreach (JsonElement c in cs.EnumerateArray())
                {
                    cells[i++] = new ConditionCell
                    {
                        Cx = (ushort)c.GetProperty("cx").GetInt32(),
                        Cz = (ushort)c.GetProperty("cz").GetInt32(),
                        Ore = (byte)c.GetProperty("ore").GetInt32(),
                        Oil = (byte)c.GetProperty("oil").GetInt32(),
                        Fertility = (byte)c.GetProperty("fertility").GetInt32(),
                        Forest = (byte)c.GetProperty("forest").GetInt32(),
                        Pollution = (byte)c.GetProperty("pollution").GetInt32(),
                        Flags = (byte)c.GetProperty("flags").GetInt32(),
                        Crime = (byte)c.GetProperty("crime").GetInt32(),
                        Dead = (byte)c.GetProperty("dead").GetInt32(),
                    };
                }
                JsonElement fs = f.GetProperty("fires");
                var fires = new FireSpot[fs.GetArrayLength()];
                i = 0;
                foreach (JsonElement e in fs.EnumerateArray())
                {
                    fires[i++] = new FireSpot
                    {
                        X = (float)e.GetProperty("x").GetDouble(),
                        Y = (float)e.GetProperty("y").GetDouble(),
                        Z = (float)e.GetProperty("z").GetDouble(),
                        Radius = (float)e.GetProperty("radius").GetDouble(),
                        Intensity = (byte)e.GetProperty("intensity").GetInt32(),
                    };
                }
                var m = new CityConditions { OpenSeq = f.GetProperty("openSeq").GetUInt32(), Cells = cells, Fires = fires };
                Assert.Equal(payload, m.Encode());
            }
        }

        [Theory]
        [InlineData("ore_mined_empty")]
        [InlineData("ore_mined_two")]
        public void OreVectorDecodesToFields(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                OreMined m = OreMined.Decode(payload);
                JsonElement f = v.GetProperty("fields");
                Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                JsonElement list = f.GetProperty("entries");
                Assert.Equal(list.GetArrayLength(), m.Entries.Length);
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    OreCell o = m.Entries[i++];
                    Assert.Equal(e.GetProperty("resource").GetInt32(), (int)o.Resource);
                    Assert.Equal(e.GetProperty("cx").GetInt32(), (int)o.Cx);
                    Assert.Equal(e.GetProperty("cz").GetInt32(), (int)o.Cz);
                    Assert.Equal(e.GetProperty("blocks").GetInt32(), (int)o.Blocks);
                }
            }
        }

        [Theory]
        [InlineData("ore_mined_empty")]
        [InlineData("ore_mined_two")]
        public void OreEncodingFieldsEqualsVectorPayload(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                JsonElement list = f.GetProperty("entries");
                var entries = new OreCell[list.GetArrayLength()];
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    entries[i++] = new OreCell
                    {
                        Resource = (byte)e.GetProperty("resource").GetInt32(),
                        Cx = (ushort)e.GetProperty("cx").GetInt32(),
                        Cz = (ushort)e.GetProperty("cz").GetInt32(),
                        Blocks = (ushort)e.GetProperty("blocks").GetInt32(),
                    };
                }
                var m = new OreMined { OpenSeq = f.GetProperty("openSeq").GetUInt32(), Entries = entries };
                Assert.Equal(payload, m.Encode());
            }
        }

        [Theory]
        [InlineData("city_conditions_too_many_cells")]
        [InlineData("city_conditions_cell_outside_grid")]
        [InlineData("city_conditions_too_many_fires")]
        [InlineData("city_conditions_fire_intensity_zero")]
        [InlineData("city_conditions_fire_nan")]
        [InlineData("city_conditions_fire_negative_radius")]
        [InlineData("city_conditions_truncated")]
        [InlineData("city_conditions_trailing_bytes")]
        public void InvalidConditionsVectorThrows(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => CityConditions.Decode(p));
        }

        [Theory]
        [InlineData("ore_mined_too_many")]
        [InlineData("ore_mined_bad_resource")]
        [InlineData("ore_mined_cell_outside_grid")]
        [InlineData("ore_mined_zero_blocks")]
        [InlineData("ore_mined_trailing_bytes")]
        public void InvalidOreVectorThrows(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => OreMined.Decode(p));
        }
    }
}
