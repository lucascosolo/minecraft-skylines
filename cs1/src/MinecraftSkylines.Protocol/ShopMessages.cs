using Skylines.Bridge;

namespace MinecraftSkylines.Protocol
{
    /// <summary>0x0200 SHOP_OPEN (guest to host, minor 20): the player used a trader building.</summary>
    public sealed class ShopOpen
    {
        /// <summary>Exact payload length.</summary>
        public const int PayloadLength = 32;

        /// <summary>The open the guest saw.</summary>
        public uint OpenSeq;
        /// <summary>Echoed in <see cref="ShopOffers"/>.</summary>
        public uint RequestId;
        /// <summary>The player's eye, Minecraft frame.</summary>
        public float EyeX, EyeY, EyeZ;
        /// <summary>The targeted point on the building, Minecraft frame.</summary>
        public float HitX, HitY, HitZ;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(OpenSeq).U32(RequestId).F32(EyeX).F32(EyeY).F32(EyeZ).F32(HitX).F32(HitY).F32(HitZ).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static ShopOpen Decode(byte[] payload)
        {
            if (payload.Length != PayloadLength) throw new ProtocolException("shop open payload is " + payload.Length + " bytes, not 32");
            var r = new PayloadReader(payload);
            var m = new ShopOpen { OpenSeq = r.U32(), RequestId = r.U32(), EyeX = r.F32(), EyeY = r.F32(), EyeZ = r.F32(), HitX = r.F32(), HitY = r.F32(), HitZ = r.F32() };
            if (!Finite(m.EyeX) || !Finite(m.EyeY) || !Finite(m.EyeZ) || !Finite(m.HitX) || !Finite(m.HitY) || !Finite(m.HitZ))
            {
                throw new ProtocolException("shop open position is not finite");
            }
            return m;
        }

        private static bool Finite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }

    /// <summary>One trade offered: give <see cref="CostCount"/> of <see cref="CostItem"/>, get <see cref="ResultCount"/> of <see cref="ResultItem"/>.</summary>
    public struct ShopOffer
    {
        /// <summary>The offer's stable id within its building.</summary>
        public byte Slot;
        /// <summary>Namespaced item id the player gives.</summary>
        public string CostItem;
        /// <summary>1-64.</summary>
        public byte CostCount;
        /// <summary>Namespaced item id the player gets.</summary>
        public string ResultItem;
        /// <summary>1-64.</summary>
        public byte ResultCount;
        /// <summary>Trades left now.</summary>
        public ushort Uses;
    }

    /// <summary>0x0201 SHOP_OFFERS (host to guest, minor 20): the answer to a <see cref="ShopOpen"/>.</summary>
    public sealed class ShopOffers
    {
        /// <summary>The building trades now.</summary>
        public const byte Open = 0;
        /// <summary>No building found, or one that does not trade.</summary>
        public const byte NotAShop = 1;
        /// <summary>A trading building that cannot trade now (abandoned, burning, collapsed, unfinished, without power).</summary>
        public const byte Closed = 2;
        /// <summary>Most offers in one message.</summary>
        public const int MaxOffers = 32;
        /// <summary>Largest item count of one side of an offer.</summary>
        public const int MaxItemCount = 64;

        /// <summary>As in the request.</summary>
        public uint OpenSeq;
        /// <summary>The request answered.</summary>
        public uint RequestId;
        /// <summary>The city's building id; 0 when none.</summary>
        public ushort Building;
        /// <summary><see cref="Open"/>, <see cref="NotAShop"/> or <see cref="Closed"/>.</summary>
        public byte Status;
        /// <summary>The building's name; empty unless open.</summary>
        public string Name = "";
        /// <summary>The building's level; 0 unless open.</summary>
        public byte Level;
        /// <summary>The offers; empty unless open.</summary>
        public ShopOffer[] Offers = new ShopOffer[0];

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            var w = new PayloadWriter().U32(OpenSeq).U32(RequestId).U16(Building).U8(Status).String(Name).U8(Level).U16((ushort)Offers.Length);
            foreach (ShopOffer o in Offers)
            {
                w.U8(o.Slot).String(o.CostItem).U8(o.CostCount).String(o.ResultItem).U8(o.ResultCount).U16(o.Uses);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static ShopOffers Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var m = new ShopOffers { OpenSeq = r.U32(), RequestId = r.U32(), Building = r.U16(), Status = r.U8(), Name = r.String(), Level = r.U8() };
            int n = r.U16();
            if (m.Status > Closed) throw new ProtocolException("shop status " + m.Status + " is above 2");
            if (n > MaxOffers) throw new ProtocolException("shop offer count " + n + " is above " + MaxOffers);
            if (n != 0 && m.Status != Open) throw new ProtocolException("offers on a shop that is not open");
            m.Offers = new ShopOffer[n];
            for (int i = 0; i < n; i++)
            {
                var o = new ShopOffer { Slot = r.U8(), CostItem = r.String(), CostCount = r.U8(), ResultItem = r.String(), ResultCount = r.U8(), Uses = r.U16() };
                if (o.CostCount < 1 || o.CostCount > MaxItemCount || o.ResultCount < 1 || o.ResultCount > MaxItemCount)
                {
                    throw new ProtocolException("shop offer item count outside 1..64");
                }
                m.Offers[i] = o;
            }
            if (r.Remaining != 0) throw new ProtocolException("shop offers payload has " + r.Remaining + " bytes beyond its count");
            return m;
        }
    }

    /// <summary>0x0202 SHOP_TRADE (guest to host, minor 20): the player completed trades.</summary>
    public sealed class ShopTrade
    {
        /// <summary>Exact payload length.</summary>
        public const int PayloadLength = 9;

        /// <summary>The current open.</summary>
        public uint OpenSeq;
        /// <summary>From <see cref="ShopOffers.Building"/>; never 0.</summary>
        public ushort Building;
        /// <summary>The offer traded.</summary>
        public byte Slot;
        /// <summary>Trades completed, at least 1.</summary>
        public ushort Times;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(OpenSeq).U16(Building).U8(Slot).U16(Times).ToArray();
        }

        /// <summary>Decodes a payload; throws <see cref="ProtocolException"/> if it is malformed.</summary>
        public static ShopTrade Decode(byte[] payload)
        {
            if (payload.Length != PayloadLength) throw new ProtocolException("shop trade payload is " + payload.Length + " bytes, not 9");
            var r = new PayloadReader(payload);
            var m = new ShopTrade { OpenSeq = r.U32(), Building = r.U16(), Slot = r.U8(), Times = r.U16() };
            if (m.Building == 0 || m.Times == 0) throw new ProtocolException("shop trade with building 0 or zero times");
            return m;
        }
    }
}
