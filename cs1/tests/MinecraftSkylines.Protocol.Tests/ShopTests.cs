using System;
using System.IO;
using System.Text.Json;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class ShopTests
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
            Assert.Equal(20, AppProtocol.Minor);
            Assert.Equal(0x0200, AppProtocol.ShopOpenType);
            Assert.Equal(0x0201, AppProtocol.ShopOffersType);
            Assert.Equal(0x0202, AppProtocol.ShopTradeType);
            Assert.Equal(0, ShopOffers.Open);
            Assert.Equal(1, ShopOffers.NotAShop);
            Assert.Equal(2, ShopOffers.Closed);
            Assert.Equal(32, ShopOffers.MaxOffers);
            Assert.Equal(64, ShopOffers.MaxItemCount);
        }

        [Fact]
        public void ShopOpenDecodesToFields()
        {
            JsonDocument d;
            JsonElement v = Vector("valid", "shop_open", out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                ShopOpen m = ShopOpen.Decode(payload);
                Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                Assert.Equal(f.GetProperty("requestId").GetUInt32(), m.RequestId);
                Assert.Equal((float)f.GetProperty("eye")[0].GetDouble(), m.EyeX);
                Assert.Equal((float)f.GetProperty("eye")[1].GetDouble(), m.EyeY);
                Assert.Equal((float)f.GetProperty("eye")[2].GetDouble(), m.EyeZ);
                Assert.Equal((float)f.GetProperty("hit")[0].GetDouble(), m.HitX);
                Assert.Equal((float)f.GetProperty("hit")[1].GetDouble(), m.HitY);
                Assert.Equal((float)f.GetProperty("hit")[2].GetDouble(), m.HitZ);
            }
        }

        [Fact]
        public void ShopOpenEncodingFieldsEqualsVectorPayload()
        {
            JsonDocument d;
            JsonElement v = Vector("valid", "shop_open", out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                var m = new ShopOpen
                {
                    OpenSeq = f.GetProperty("openSeq").GetUInt32(),
                    RequestId = f.GetProperty("requestId").GetUInt32(),
                    EyeX = (float)f.GetProperty("eye")[0].GetDouble(),
                    EyeY = (float)f.GetProperty("eye")[1].GetDouble(),
                    EyeZ = (float)f.GetProperty("eye")[2].GetDouble(),
                    HitX = (float)f.GetProperty("hit")[0].GetDouble(),
                    HitY = (float)f.GetProperty("hit")[1].GetDouble(),
                    HitZ = (float)f.GetProperty("hit")[2].GetDouble(),
                };
                Assert.Equal(payload, m.Encode());
            }
        }

        [Theory]
        [InlineData("shop_offers_open")]
        [InlineData("shop_offers_closed")]
        [InlineData("shop_offers_not_a_shop")]
        public void ShopOffersDecodesToFields(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                ShopOffers m = ShopOffers.Decode(payload);
                Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                Assert.Equal(f.GetProperty("requestId").GetUInt32(), m.RequestId);
                Assert.Equal(f.GetProperty("building").GetUInt16(), m.Building);
                Assert.Equal(f.GetProperty("status").GetByte(), m.Status);
                Assert.Equal(f.GetProperty("name").GetString(), m.Name);
                Assert.Equal(f.GetProperty("level").GetByte(), m.Level);
                JsonElement list = f.GetProperty("offers");
                Assert.Equal(list.GetArrayLength(), m.Offers.Length);
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    ShopOffer o = m.Offers[i++];
                    Assert.Equal(e.GetProperty("slot").GetByte(), o.Slot);
                    Assert.Equal(e.GetProperty("costItem").GetString(), o.CostItem);
                    Assert.Equal(e.GetProperty("costCount").GetByte(), o.CostCount);
                    Assert.Equal(e.GetProperty("resultItem").GetString(), o.ResultItem);
                    Assert.Equal(e.GetProperty("resultCount").GetByte(), o.ResultCount);
                    Assert.Equal(e.GetProperty("uses").GetUInt16(), o.Uses);
                }
            }
        }

        [Theory]
        [InlineData("shop_offers_open")]
        [InlineData("shop_offers_closed")]
        [InlineData("shop_offers_not_a_shop")]
        public void ShopOffersEncodingFieldsEqualsVectorPayload(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                JsonElement list = f.GetProperty("offers");
                var offers = new ShopOffer[list.GetArrayLength()];
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    offers[i++] = new ShopOffer
                    {
                        Slot = e.GetProperty("slot").GetByte(),
                        CostItem = e.GetProperty("costItem").GetString(),
                        CostCount = e.GetProperty("costCount").GetByte(),
                        ResultItem = e.GetProperty("resultItem").GetString(),
                        ResultCount = e.GetProperty("resultCount").GetByte(),
                        Uses = e.GetProperty("uses").GetUInt16(),
                    };
                }
                var m = new ShopOffers
                {
                    OpenSeq = f.GetProperty("openSeq").GetUInt32(),
                    RequestId = f.GetProperty("requestId").GetUInt32(),
                    Building = f.GetProperty("building").GetUInt16(),
                    Status = f.GetProperty("status").GetByte(),
                    Name = f.GetProperty("name").GetString(),
                    Level = f.GetProperty("level").GetByte(),
                    Offers = offers,
                };
                Assert.Equal(payload, m.Encode());
            }
        }

        [Fact]
        public void ShopTradeDecodesAndEncodes()
        {
            JsonDocument d;
            JsonElement v = Vector("valid", "shop_trade", out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                ShopTrade m = ShopTrade.Decode(payload);
                Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                Assert.Equal(f.GetProperty("building").GetUInt16(), m.Building);
                Assert.Equal(f.GetProperty("slot").GetByte(), m.Slot);
                Assert.Equal(f.GetProperty("times").GetUInt16(), m.Times);
                var built = new ShopTrade
                {
                    OpenSeq = f.GetProperty("openSeq").GetUInt32(),
                    Building = f.GetProperty("building").GetUInt16(),
                    Slot = f.GetProperty("slot").GetByte(),
                    Times = f.GetProperty("times").GetUInt16(),
                };
                Assert.Equal(payload, built.Encode());
            }
        }

        [Theory]
        [InlineData("shop_open_short")]
        [InlineData("shop_open_not_finite")]
        public void InvalidShopOpenThrows(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => ShopOpen.Decode(p));
        }

        [Theory]
        [InlineData("shop_offers_bad_status")]
        [InlineData("shop_offers_too_many")]
        [InlineData("shop_offers_closed_with_offers")]
        [InlineData("shop_offers_zero_count")]
        [InlineData("shop_offers_count_65")]
        [InlineData("shop_offers_truncated")]
        [InlineData("shop_offers_trailing_bytes")]
        public void InvalidShopOffersThrows(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => ShopOffers.Decode(p));
        }

        [Theory]
        [InlineData("shop_trade_short")]
        [InlineData("shop_trade_zero_times")]
        [InlineData("shop_trade_building_zero")]
        public void InvalidShopTradeThrows(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => ShopTrade.Decode(p));
        }
    }
}
