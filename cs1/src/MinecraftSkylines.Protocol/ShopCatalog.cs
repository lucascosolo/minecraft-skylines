using System;
using System.Collections.Generic;

namespace MinecraftSkylines.Protocol
{
    /// <summary>What a building trades, from its class (the host maps the city's building classes onto these).</summary>
    public enum ShopClass : byte
    {
        /// <summary>Does not trade.</summary>
        None,
        /// <summary>Low-density commercial: food, tools, everyday goods.</summary>
        CommercialLow,
        /// <summary>High-density commercial: better food, tools and armour.</summary>
        CommercialHigh,
        /// <summary>Leisure: luxury and nightlife.</summary>
        CommercialLeisure,
        /// <summary>Tourism: souvenirs and luxuries.</summary>
        CommercialTourist,
        /// <summary>Organic and local produce.</summary>
        CommercialEco,
        /// <summary>Forestry: buys logs.</summary>
        BuysLogs,
        /// <summary>Farming: buys crops and animal products.</summary>
        BuysGrain,
        /// <summary>Ore: buys raw ores, coal and stone.</summary>
        BuysOre,
        /// <summary>Oil: buys fossil fuel.</summary>
        BuysOil,
        /// <summary>Generic industry using lumber: buys planks.</summary>
        BuysLumber,
        /// <summary>Generic industry using food: buys crops.</summary>
        BuysFood,
        /// <summary>Generic industry using petrol or coal: buys fuel.</summary>
        BuysFuel,
    }

    /// <summary>One entry of a class's offer pool.</summary>
    public sealed class ShopOfferDef
    {
        /// <summary>Index in the class's pool; the offer's stable id.</summary>
        public readonly byte Slot;
        /// <summary>True: the shop sells <see cref="Item"/> for emeralds; false: it buys <see cref="Item"/> for emeralds.</summary>
        public readonly bool Sells;
        /// <summary>Namespaced Minecraft item id.</summary>
        public readonly string Item;
        /// <summary>How many of <see cref="Item"/> per trade.</summary>
        public readonly byte ItemCount;
        /// <summary>Emeralds per trade.</summary>
        public readonly byte Emeralds;
        /// <summary>Trades per city day.</summary>
        public readonly int DailyMax;
        /// <summary>City material units per trade: goods drawn from a shop, or raw material delivered to industry.</summary>
        public readonly int Units;

        internal ShopOfferDef(byte slot, bool sells, string item, byte count, byte emeralds)
        {
            Slot = slot;
            Sells = sells;
            Item = "minecraft:" + item;
            ItemCount = count;
            Emeralds = emeralds;
            DailyMax = sells ? (emeralds >= 5 ? 3 : 12) : 16;
            Units = ShopCatalog.UnitsPerEmerald * emeralds;
        }

        /// <summary>The offer as sent in SHOP_OFFERS.</summary>
        public ShopOffer ToOffer(ushort uses)
        {
            return Sells
                ? new ShopOffer { Slot = Slot, CostItem = ShopCatalog.Emerald, CostCount = Emeralds, ResultItem = Item, ResultCount = ItemCount, Uses = uses }
                : new ShopOffer { Slot = Slot, CostItem = Item, CostCount = ItemCount, ResultItem = ShopCatalog.Emerald, ResultCount = Emeralds, Uses = uses };
        }
    }

    /// <summary>Offer tables by class, the per-building choice, the trading gate and the stock maths (protocol minor 20).</summary>
    public static class ShopCatalog
    {
        /// <summary>The currency.</summary>
        public const string Emerald = "minecraft:emerald";
        /// <summary>COLLISION_REGION flag bit 10: the building trades.</summary>
        public const ushort TraderFlag = 1 << 10;
        /// <summary>City material units an emerald's worth of trade moves (a shopping citizen's visit draws 100).</summary>
        public const int UnitsPerEmerald = 100;

        private static readonly Dictionary<ShopClass, ShopOfferDef[]> Pools = new Dictionary<ShopClass, ShopOfferDef[]>
        {
            { ShopClass.None, new ShopOfferDef[0] },
            { ShopClass.CommercialLow, Sell("bread", 6, 1, "apple", 4, 1, "baked_potato", 8, 1, "cooked_chicken", 4, 1, "cooked_cod", 6, 1,
                "torch", 16, 1, "iron_shovel", 1, 2, "iron_axe", 1, 3, "iron_pickaxe", 1, 4, "shears", 1, 2, "bucket", 1, 3, "white_bed", 1, 3) },
            { ShopClass.CommercialHigh, Sell("cooked_beef", 5, 1, "cooked_porkchop", 5, 1, "pumpkin_pie", 4, 1, "cake", 1, 2, "lantern", 2, 1,
                "glass", 8, 1, "iron_sword", 1, 4, "shield", 1, 4, "iron_helmet", 1, 5, "iron_chestplate", 1, 8, "iron_leggings", 1, 7,
                "iron_boots", 1, 4, "clock", 1, 4, "compass", 1, 4) },
            { ShopClass.CommercialLeisure, Sell("cake", 1, 2, "cookie", 12, 1, "golden_carrot", 3, 2, "honey_bottle", 3, 1,
                "firework_rocket", 4, 1, "jukebox", 1, 6, "music_disc_cat", 1, 10, "name_tag", 1, 8, "golden_apple", 1, 8, "glow_berries", 8, 1,
                "painting", 2, 1) },
            { ShopClass.CommercialTourist, Sell("spyglass", 1, 5, "compass", 1, 3, "map", 1, 3, "painting", 2, 1, "flower_pot", 3, 1,
                "saddle", 1, 8, "lead", 2, 1, "amethyst_shard", 4, 1, "glow_item_frame", 2, 1, "firework_rocket", 4, 1, "golden_apple", 1, 8,
                "writable_book", 1, 2) },
            { ShopClass.CommercialEco, Sell("sweet_berries", 16, 1, "honey_bottle", 3, 1, "mushroom_stew", 1, 1, "beetroot_soup", 1, 1,
                "pumpkin_pie", 4, 1, "bone_meal", 12, 1, "oak_sapling", 4, 1, "wheat_seeds", 16, 1, "composter", 1, 1) },
            { ShopClass.BuysLogs, Buy("oak_log", 16, 1, "spruce_log", 16, 1, "birch_log", 16, 1, "jungle_log", 16, 1, "acacia_log", 16, 1,
                "dark_oak_log", 16, 1, "cherry_log", 16, 1, "mangrove_log", 16, 1, "stick", 32, 1) },
            { ShopClass.BuysGrain, Buy("wheat", 20, 1, "potato", 26, 1, "carrot", 22, 1, "beetroot", 15, 1, "pumpkin", 6, 1, "melon", 4, 1,
                "sugar_cane", 24, 1, "leather", 6, 1, "white_wool", 18, 1, "egg", 16, 1) },
            { ShopClass.BuysOre, Buy("raw_iron", 4, 1, "raw_copper", 12, 1, "raw_gold", 3, 1, "coal", 15, 1, "redstone", 24, 1,
                "lapis_lazuli", 16, 1, "cobblestone", 64, 1, "diamond", 1, 4) },
            { ShopClass.BuysOil, Buy("coal", 15, 1, "charcoal", 15, 1, "coal_block", 2, 1, "ink_sac", 12, 1, "dried_kelp_block", 6, 1,
                "blaze_rod", 2, 1, "lava_bucket", 1, 1) },
            { ShopClass.BuysLumber, Buy("oak_planks", 32, 1, "spruce_planks", 32, 1, "birch_planks", 32, 1, "jungle_planks", 32, 1,
                "acacia_planks", 32, 1, "dark_oak_planks", 32, 1, "stick", 32, 1) },
            { ShopClass.BuysFood, Buy("wheat", 20, 1, "potato", 26, 1, "carrot", 22, 1, "beetroot", 15, 1, "sugar_cane", 24, 1,
                "pumpkin", 6, 1, "melon", 4, 1, "egg", 16, 1) },
            { ShopClass.BuysFuel, Buy("coal", 15, 1, "charcoal", 15, 1, "coal_block", 2, 1, "dried_kelp_block", 6, 1, "blaze_rod", 2, 1,
                "lava_bucket", 1, 1, "bamboo", 32, 1) },
        };

        /// <summary>True for the classes that sell to the player (commercial); false for those that buy (industry).</summary>
        public static bool Sells(ShopClass c)
        {
            return c >= ShopClass.CommercialLow && c <= ShopClass.CommercialEco;
        }

        /// <summary>Every offer the class can make; entry i has slot i.</summary>
        public static IList<ShopOfferDef> Pool(ShopClass c)
        {
            ShopOfferDef[] pool;
            return Array.AsReadOnly(Pools.TryGetValue(c, out pool) ? pool : Pools[ShopClass.None]);
        }

        /// <summary>
        /// The offers one building makes: 2 + level (1-5) of its class's pool, picked by a shuffle seeded by the building id and
        /// class, so a shop keeps its offers and a level-up only adds to them. Ascending slot.
        /// </summary>
        public static ShopOfferDef[] OffersFor(ShopClass c, int level, ushort building)
        {
            IList<ShopOfferDef> pool = Pool(c);
            int n = Math.Min(pool.Count, 2 + Math.Max(1, Math.Min(5, level)));
            var order = new int[pool.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            uint s = (uint)building * 2654435761u ^ (uint)c * 0x9E3779B9u ^ 0x5BD1E995u;
            for (int i = order.Length - 1; i > 0; i--)
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                int j = (int)(s % (uint)(i + 1));
                int t = order[i];
                order[i] = order[j];
                order[j] = t;
            }
            var picked = new int[n];
            Array.Copy(order, picked, n);
            Array.Sort(picked);
            var result = new ShopOfferDef[n];
            for (int i = 0; i < n; i++) result[i] = pool[picked[i]];
            return result;
        }

        /// <summary>The building's offer with that slot, or null when it does not make it.</summary>
        public static ShopOfferDef Find(ShopClass c, int level, ushort building, byte slot)
        {
            foreach (ShopOfferDef d in OffersFor(c, level, building))
            {
                if (d.Slot == slot) return d;
            }
            return null;
        }

        /// <summary>The SHOP_OFFERS status of a building of class <paramref name="c"/> in that state.</summary>
        public static byte Gate(ShopClass c, bool completed, bool abandoned, bool collapsed, bool burning, bool unpowered)
        {
            if (c == ShopClass.None) return ShopOffers.NotAShop;
            return !completed || abandoned || collapsed || burning || unpowered ? ShopOffers.Closed : ShopOffers.Open;
        }

        /// <summary>
        /// Trades left now: the daily allowance left, capped by what the building can supply (a sale needs the goods in
        /// <paramref name="amount"/>; a purchase needs room below <paramref name="max"/>).
        /// </summary>
        public static ushort Uses(ShopOfferDef o, int dailyLeft, int amount, int max)
        {
            int supply = o.Sells ? amount / o.Units : Math.Max(0, max - amount) / o.Units;
            return (ushort)Math.Max(0, Math.Min(ushort.MaxValue, Math.Min(dailyLeft, supply)));
        }

        /// <summary>The city day a game time falls in; offers restock when it changes.</summary>
        public static int Day(long gameTimeTicks)
        {
            return (int)(gameTimeTicks / TimeSpan.TicksPerDay);
        }

        private static ShopOfferDef[] Sell(params object[] t)
        {
            return Defs(true, t);
        }

        private static ShopOfferDef[] Buy(params object[] t)
        {
            return Defs(false, t);
        }

        private static ShopOfferDef[] Defs(bool sells, object[] t)
        {
            var defs = new ShopOfferDef[t.Length / 3];
            for (int i = 0; i < defs.Length; i++)
            {
                defs[i] = new ShopOfferDef((byte)i, sells, (string)t[3 * i], (byte)(int)t[3 * i + 1], (byte)(int)t[3 * i + 2]);
            }
            return defs;
        }
    }

    /// <summary>Trades made per building and offer in the current city day. Not saved: offers restock daily anyway.</summary>
    public sealed class ShopLedger
    {
        private readonly Dictionary<int, int[]> _used = new Dictionary<int, int[]>();

        /// <summary>Trades of the offer left today.</summary>
        public int DailyLeft(ushort building, byte slot, int dailyMax, int day)
        {
            int[] e;
            if (!_used.TryGetValue(Key(building, slot), out e) || e[0] != day) return dailyMax;
            return Math.Max(0, dailyMax - e[1]);
        }

        /// <summary>Counts trades against today's allowance.</summary>
        public void Record(ushort building, byte slot, int times, int day)
        {
            int key = Key(building, slot);
            int[] e;
            if (!_used.TryGetValue(key, out e) || e[0] != day) _used[key] = e = new[] { day, 0 };
            e[1] += times;
        }

        /// <summary>Forgets every trade (a new city).</summary>
        public void Clear()
        {
            _used.Clear();
        }

        private static int Key(ushort building, byte slot)
        {
            return building << 8 | slot;
        }
    }
}
