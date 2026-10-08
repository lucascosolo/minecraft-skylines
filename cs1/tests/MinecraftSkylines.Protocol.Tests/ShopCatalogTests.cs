using System;
using System.Collections.Generic;
using System.Linq;
using MinecraftSkylines.Protocol;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class ShopCatalogTests
    {
        private static readonly ShopClass[] Shops =
        {
            ShopClass.CommercialLow, ShopClass.CommercialHigh, ShopClass.CommercialLeisure,
            ShopClass.CommercialTourist, ShopClass.CommercialEco, ShopClass.BuysLogs,
            ShopClass.BuysGrain, ShopClass.BuysOre, ShopClass.BuysOil, ShopClass.BuysLumber,
            ShopClass.BuysFood, ShopClass.BuysFuel,
        };

        private static readonly ShopClass[] Commercial =
        {
            ShopClass.CommercialLow, ShopClass.CommercialHigh, ShopClass.CommercialLeisure,
            ShopClass.CommercialTourist, ShopClass.CommercialEco,
        };

        public static IEnumerable<object[]> ShopClasses()
        {
            foreach (ShopClass c in Shops) yield return new object[] { c };
        }

        private static ShopOfferDef SellsDef() { return ShopCatalog.Pool(ShopClass.CommercialLow)[0]; }
        private static ShopOfferDef BuysDef() { return ShopCatalog.Pool(ShopClass.BuysLogs)[0]; }

        private static string Key(ShopOfferDef[] d)
        {
            return string.Join(",", d.Select(x => x.Slot.ToString()).ToArray());
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal("minecraft:emerald", ShopCatalog.Emerald);
            Assert.Equal((ushort)(1 << 10), ShopCatalog.TraderFlag);
        }

        [Fact]
        public void SellsIsTrueExactlyForCommercialClasses()
        {
            foreach (ShopClass c in Enum.GetValues(typeof(ShopClass)))
                Assert.Equal(Commercial.Contains(c), ShopCatalog.Sells(c));
        }

        [Fact]
        public void NoneHasNoPoolAndNoOffers()
        {
            Assert.Empty(ShopCatalog.Pool(ShopClass.None));
            Assert.Empty(ShopCatalog.OffersFor(ShopClass.None, 3, 10));
            Assert.Null(ShopCatalog.Find(ShopClass.None, 3, 10, 0));
        }

        [Theory]
        [MemberData(nameof(ShopClasses))]
        public void PoolEntriesAreWellFormed(ShopClass c)
        {
            IList<ShopOfferDef> pool = ShopCatalog.Pool(c);
            Assert.InRange(pool.Count, 7, 32);
            for (int i = 0; i < pool.Count; i++)
            {
                ShopOfferDef o = pool[i];
                Assert.Equal(i, o.Slot);
                Assert.Equal(ShopCatalog.Sells(c), o.Sells);
                Assert.StartsWith("minecraft:", o.Item);
                Assert.NotEqual(ShopCatalog.Emerald, o.Item);
                Assert.InRange((int)o.ItemCount, 1, 64);
                Assert.InRange((int)o.Emeralds, 1, 64);
                Assert.True(o.DailyMax >= 1);
                Assert.True(o.Units >= 1);
            }
        }

        [Theory]
        [MemberData(nameof(ShopClasses))]
        public void OffersForLengthFollowsLevel(ShopClass c)
        {
            int pool = ShopCatalog.Pool(c).Count;
            Assert.Equal(Math.Min(pool, 3), ShopCatalog.OffersFor(c, 1, 7).Length);
            Assert.Equal(Math.Min(pool, 7), ShopCatalog.OffersFor(c, 5, 7).Length);
            Assert.Equal(Math.Min(pool, 3), ShopCatalog.OffersFor(c, -4, 7).Length);
            Assert.Equal(Math.Min(pool, 7), ShopCatalog.OffersFor(c, 99, 7).Length);
        }

        [Theory]
        [MemberData(nameof(ShopClasses))]
        public void OffersAreDistinctAscendingPoolEntriesAndDeterministic(ShopClass c)
        {
            for (ushort b = 1; b <= 20; b++)
            {
                ShopOfferDef[] a = ShopCatalog.OffersFor(c, 3, b);
                Assert.Equal(Key(a), Key(ShopCatalog.OffersFor(c, 3, b)));
                Assert.Equal(a.Length, a.Select(x => (int)x.Slot).Distinct().Count());
                for (int i = 1; i < a.Length; i++) Assert.True(a[i].Slot > a[i - 1].Slot);
                foreach (ShopOfferDef o in a) Assert.Same(ShopCatalog.Pool(c)[o.Slot], o);
            }
        }

        [Theory]
        [MemberData(nameof(ShopClasses))]
        public void OffersAreNestedAcrossLevels(ShopClass c)
        {
            for (ushort b = 1; b <= 20; b++)
                for (int level = 1; level < 5; level++)
                {
                    var upper = new HashSet<byte>(ShopCatalog.OffersFor(c, level + 1, b).Select(x => x.Slot));
                    foreach (ShopOfferDef o in ShopCatalog.OffersFor(c, level, b))
                        Assert.Contains(o.Slot, upper);
                }
        }

        [Fact]
        public void OffersDependOnBuilding()
        {
            var sets = new HashSet<string>();
            for (ushort b = 1; b <= 50; b++) sets.Add(Key(ShopCatalog.OffersFor(ShopClass.CommercialLow, 1, b)));
            Assert.True(sets.Count >= 2);
        }

        [Fact]
        public void FindReturnsOnlyOfferedSlots()
        {
            ShopOfferDef[] offers = ShopCatalog.OffersFor(ShopClass.CommercialHigh, 2, 9);
            foreach (ShopOfferDef o in offers)
                Assert.Same(o, ShopCatalog.Find(ShopClass.CommercialHigh, 2, 9, o.Slot));
            var offered = new HashSet<byte>(offers.Select(x => x.Slot));
            for (int s = 0; s < 40; s++)
                if (!offered.Contains((byte)s))
                    Assert.Null(ShopCatalog.Find(ShopClass.CommercialHigh, 2, 9, (byte)s));
        }

        [Fact]
        public void ToOfferSellsPaysEmeraldsForItem()
        {
            ShopOfferDef d = SellsDef();
            ShopOffer o = d.ToOffer(9);
            Assert.Equal(ShopCatalog.Emerald, o.CostItem);
            Assert.Equal(d.Emeralds, o.CostCount);
            Assert.Equal(d.Item, o.ResultItem);
            Assert.Equal(d.ItemCount, o.ResultCount);
            Assert.Equal(d.Slot, o.Slot);
            Assert.Equal((ushort)9, o.Uses);
        }

        [Fact]
        public void ToOfferBuysPaysItemForEmeralds()
        {
            ShopOfferDef d = BuysDef();
            ShopOffer o = d.ToOffer(4);
            Assert.Equal(d.Item, o.CostItem);
            Assert.Equal(d.ItemCount, o.CostCount);
            Assert.Equal(ShopCatalog.Emerald, o.ResultItem);
            Assert.Equal(d.Emeralds, o.ResultCount);
            Assert.Equal(d.Slot, o.Slot);
            Assert.Equal((ushort)4, o.Uses);
        }

        [Fact]
        public void GateNoneIsAlwaysNotAShop()
        {
            Assert.Equal(ShopOffers.NotAShop, ShopCatalog.Gate(ShopClass.None, true, false, false, false, false));
            Assert.Equal(ShopOffers.NotAShop, ShopCatalog.Gate(ShopClass.None, false, true, true, true, true));
        }

        [Fact]
        public void GateOpenOnlyWhenCompletedAndNoHazard()
        {
            Assert.Equal(ShopOffers.Open, ShopCatalog.Gate(ShopClass.CommercialLow, true, false, false, false, false));
            Assert.Equal(ShopOffers.Closed, ShopCatalog.Gate(ShopClass.CommercialLow, false, false, false, false, false));
            Assert.Equal(ShopOffers.Closed, ShopCatalog.Gate(ShopClass.BuysOre, true, true, false, false, false));
            Assert.Equal(ShopOffers.Closed, ShopCatalog.Gate(ShopClass.BuysOre, true, false, true, false, false));
            Assert.Equal(ShopOffers.Closed, ShopCatalog.Gate(ShopClass.BuysOre, true, false, false, true, false));
            Assert.Equal(ShopOffers.Closed, ShopCatalog.Gate(ShopClass.BuysOre, true, false, false, false, true));
        }

        [Fact]
        public void UsesForSellsIsLimitedByDailyLeftAndStock()
        {
            ShopOfferDef d = SellsDef();
            int u = d.Units;
            Assert.Equal((ushort)4, ShopCatalog.Uses(d, 12, 4 * u + (u - 1), 0));
            Assert.Equal((ushort)12, ShopCatalog.Uses(d, 12, 1000 * u, 0));
            Assert.Equal((ushort)0, ShopCatalog.Uses(d, 12, u - 1, 0));
            Assert.Equal((ushort)0, ShopCatalog.Uses(d, 0, 1000 * u, 0));
            Assert.Equal((ushort)0, ShopCatalog.Uses(d, -3, 1000 * u, 0));
        }

        [Fact]
        public void UsesForBuysIsLimitedByDailyLeftAndRoom()
        {
            ShopOfferDef d = BuysDef();
            int u = d.Units;
            int max = 10 * u;
            Assert.Equal((ushort)2, ShopCatalog.Uses(d, 12, 8 * u, max));
            Assert.Equal((ushort)0, ShopCatalog.Uses(d, 12, max + 5, max));
            Assert.Equal((ushort)0, ShopCatalog.Uses(d, 0, 0, max));
            Assert.Equal((ushort)3, ShopCatalog.Uses(d, 3, 0, max));
            Assert.Equal((ushort)0, ShopCatalog.Uses(d, -1, 0, max));
        }

        [Fact]
        public void UsesIsCappedAtUshortMax()
        {
            Assert.Equal((ushort)65535, ShopCatalog.Uses(SellsDef(), 1000000, int.MaxValue, 0));
        }

        [Fact]
        public void DayIsWholeDaysOfGameTime()
        {
            Assert.Equal(0, ShopCatalog.Day(0));
            Assert.Equal(0, ShopCatalog.Day(TimeSpan.TicksPerDay - 1));
            Assert.Equal(3, ShopCatalog.Day(3 * TimeSpan.TicksPerDay + 1));
        }

        [Fact]
        public void LedgerFreshReturnsDailyMax()
        {
            Assert.Equal(12, new ShopLedger().DailyLeft(5, 2, 12, 2));
        }

        [Fact]
        public void LedgerRecordReducesAndRestocksNextDay()
        {
            var l = new ShopLedger();
            l.Record(5, 2, 5, 2);
            Assert.Equal(7, l.DailyLeft(5, 2, 12, 2));
            Assert.Equal(12, l.DailyLeft(5, 2, 12, 3));
        }

        [Fact]
        public void LedgerNeverGoesNegativeAndIsolatesKeys()
        {
            var l = new ShopLedger();
            l.Record(5, 2, 20, 2);
            Assert.Equal(0, l.DailyLeft(5, 2, 12, 2));
            Assert.Equal(12, l.DailyLeft(6, 2, 12, 2));
            Assert.Equal(12, l.DailyLeft(5, 3, 12, 2));
        }

        [Fact]
        public void LedgerCountsFromZeroOnANewDay()
        {
            var l = new ShopLedger();
            l.Record(5, 2, 5, 2);
            l.Record(5, 2, 4, 3);
            Assert.Equal(8, l.DailyLeft(5, 2, 12, 3));
        }

        [Fact]
        public void LedgerClearResetsEverything()
        {
            var l = new ShopLedger();
            l.Record(5, 2, 5, 2);
            l.Clear();
            Assert.Equal(12, l.DailyLeft(5, 2, 12, 2));
        }
    }
}
