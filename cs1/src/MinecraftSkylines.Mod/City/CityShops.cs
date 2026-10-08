using System;
using ColossalFramework;
using ColossalFramework.Math;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Host;
using UnityEngine;

namespace MinecraftSkylines.Mod.City
{
    /// <summary>
    /// Protocol minor 20: the player trades with commercial and industrial buildings. Offers come from
    /// <see cref="ShopCatalog"/>; the city side goes through the building AI the way citizens and trucks do
    /// (docs/CS1-API-NOTES.md, "Shops").
    /// </summary>
    internal sealed class CityShops
    {
        private readonly HostLog _log;
        private readonly ShopLedger _ledger = new ShopLedger();

        public CityShops(HostLog log)
        {
            _log = log;
        }

        /// <summary>Forgets the day's trades (a new city).</summary>
        public void Reset()
        {
            Singleton<SimulationManager>.instance.AddAction(delegate { _ledger.Clear(); });
        }

        /// <summary>COLLISION_REGION extra flags of a building: the trader bit when its class trades. Main thread.</summary>
        public static ushort TraderFlags(ushort id)
        {
            return ClassOf(id, ref Singleton<BuildingManager>.instance.m_buildings.m_buffer[id]) == ShopClass.None ? (ushort)0 : ShopCatalog.TraderFlag;
        }

        /// <summary>Answers SHOP_OPEN on the simulation thread.</summary>
        public void Open(ShopOpen m, BridgeHost host)
        {
            Singleton<SimulationManager>.instance.AddAction(delegate
            {
                var reply = new ShopOffers { OpenSeq = m.OpenSeq, RequestId = m.RequestId, Status = ShopOffers.NotAShop };
                try
                {
                    Fill(m, reply);
                }
                catch (Exception ex)
                {
                    _log.Warn("shops: SHOP_OPEN failed: " + ex.Message);
                    reply = new ShopOffers { OpenSeq = m.OpenSeq, RequestId = m.RequestId, Status = ShopOffers.NotAShop };
                }
                if (host != null && host.State == BridgeState.Connected) host.Send(AppProtocol.ShopOffersType, reply.Encode());
            });
        }

        /// <summary>Applies SHOP_TRADE to the city on the simulation thread.</summary>
        public void Trade(ShopTrade m)
        {
            Singleton<SimulationManager>.instance.AddAction(delegate
            {
                try
                {
                    Apply(m);
                }
                catch (Exception ex)
                {
                    _log.Warn("shops: SHOP_TRADE for #" + m.Building + " failed: " + ex.Message);
                }
            });
        }

        private void Fill(ShopOpen m, ShopOffers reply)
        {
            var eye = new Vector3(m.EyeX, m.EyeY, -m.EyeZ);
            var hit = new Vector3(m.HitX, m.HitY, -m.HitZ);
            Vector3 dir = hit - eye;
            if (dir.sqrMagnitude < 1e-6f) return;
            BuildingManager bm = Singleton<BuildingManager>.instance;
            Vector3 at;
            ushort id;
            const Building.Flags ignore = Building.Flags.Deleted | Building.Flags.Hidden;
            if (!bm.RayCast(new Segment3(eye, hit + dir.normalized), ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Layer.Default, ignore, out at, out id) || id == 0)
            {
                return;
            }
            ref Building b = ref bm.m_buildings.m_buffer[id];
            ShopClass c = ClassOf(id, ref b);
            reply.Building = id;
            reply.Status = ShopCatalog.Gate(c, (b.m_flags & Building.Flags.Completed) != 0, (b.m_flags & Building.Flags.Abandoned) != 0,
                (b.m_flags & Building.Flags.Collapsed) != 0, b.m_fireIntensity != 0, (b.m_problems & Notification.Problem1.Electricity).IsNotNone);
            if (reply.Status != ShopOffers.Open) return;
            reply.Name = bm.GetBuildingName(id, InstanceID.Empty) ?? "";
            reply.Level = (byte)(b.m_level + 1);
            int amount, max;
            Buffer(id, ref b, c, out amount, out max);
            int day = Today();
            ShopOfferDef[] defs = ShopCatalog.OffersFor(c, reply.Level, id);
            reply.Offers = new ShopOffer[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                ShopOfferDef d = defs[i];
                reply.Offers[i] = d.ToOffer(ShopCatalog.Uses(d, _ledger.DailyLeft(id, d.Slot, d.DailyMax, day), amount, max));
            }
        }

        private void Apply(ShopTrade m)
        {
            ref Building b = ref Singleton<BuildingManager>.instance.m_buildings.m_buffer[m.Building];
            ShopClass c = ClassOf(m.Building, ref b);
            if (c == ShopClass.None) return;
            ShopOfferDef d = ShopCatalog.Find(c, b.m_level + 1, m.Building, m.Slot);
            if (d == null)
            {
                _log.Info("shops: #" + m.Building + " does not offer slot " + m.Slot + "; trade ignored");
                return;
            }
            _ledger.Record(m.Building, m.Slot, m.Times, Today());
            int units = d.Units * m.Times;
            BuildingAI ai = b.Info.m_buildingAI;
            if (d.Sells)
            {
                // A shopping citizen's path (ResidentAI): takes goods, fills the cash buffer, clamped to the stock.
                int delta = -units;
                ai.ModifyMaterialBuffer(m.Building, ref b, TransferManager.TransferReason.Shopping, ref delta);
                return;
            }
            if (ai is IndustrialExtractorAI)
            {
                // The extractor's own production path (ProduceGoods adds to m_customBuffer1 up to its storage).
                int amount, max;
                Buffer(m.Building, ref b, c, out amount, out max);
                b.m_customBuffer1 = (ushort)Math.Min(ushort.MaxValue, Math.Min(max, amount + units));
                return;
            }
            // A delivery truck's path: the processor's input buffer, clamped to its capacity.
            int add = units;
            ai.ModifyMaterialBuffer(m.Building, ref b, Incoming(m.Building, b.Info.m_class.m_subService), ref add);
        }

        // The building's material amount and capacity for its trade: goods on the shelf (sells), the input buffer
        // (processors) or the production storage (extractors, ProduceGoods' limit).
        private static void Buffer(ushort id, ref Building b, ShopClass c, out int amount, out int max)
        {
            BuildingAI ai = b.Info.m_buildingAI;
            if (ShopCatalog.Sells(c))
            {
                ai.GetMaterialAmount(id, ref b, TransferManager.TransferReason.Shopping, out amount, out max);
                return;
            }
            var extractor = ai as IndustrialExtractorAI;
            if (extractor != null)
            {
                int truck;
                ai.GetMaterialAmount(id, ref b, Incoming(id, b.Info.m_class.m_subService), out amount, out truck);
                int capacity = extractor.CalculateProductionCapacity((ItemClass.Level)b.m_level, new Randomizer((int)id), b.Width, b.Length);
                max = Math.Max(capacity * 500, truck * 2);
                return;
            }
            ai.GetMaterialAmount(id, ref b, Incoming(id, b.Info.m_class.m_subService), out amount, out max);
        }

        // IndustrialBuildingAI.GetIncomingTransferReason (private; IndustrialBuildingAI.cs:1106), which for the four
        // specialisations is also the extractor's outgoing material (IndustrialExtractorAI.cs:759).
        private static TransferManager.TransferReason Incoming(ushort id, ItemClass.SubService sub)
        {
            switch (sub)
            {
                case ItemClass.SubService.IndustrialForestry: return TransferManager.TransferReason.Logs;
                case ItemClass.SubService.IndustrialFarming: return TransferManager.TransferReason.Grain;
                case ItemClass.SubService.IndustrialOil: return TransferManager.TransferReason.Oil;
                case ItemClass.SubService.IndustrialOre: return TransferManager.TransferReason.Ore;
            }
            switch (new Randomizer((int)id).Int32(4u))
            {
                case 0: return TransferManager.TransferReason.Lumber;
                case 1: return TransferManager.TransferReason.Food;
                case 2: return TransferManager.TransferReason.Petrol;
                default: return TransferManager.TransferReason.Coal;
            }
        }

        private static ShopClass ClassOf(ushort id, ref Building b)
        {
            BuildingInfo info = b.Info;
            if (info == null || info.m_class == null) return ShopClass.None;
            BuildingAI ai = info.m_buildingAI;
            ItemClass.SubService sub = info.m_class.m_subService;
            if (ai is CommercialBuildingAI)
            {
                switch (sub)
                {
                    case ItemClass.SubService.CommercialLow: return ShopClass.CommercialLow;
                    case ItemClass.SubService.CommercialHigh: return ShopClass.CommercialHigh;
                    case ItemClass.SubService.CommercialLeisure: return ShopClass.CommercialLeisure;
                    case ItemClass.SubService.CommercialTourist: return ShopClass.CommercialTourist;
                    case ItemClass.SubService.CommercialEco: return ShopClass.CommercialEco;
                }
                return ShopClass.None;
            }
            if (!(ai is IndustrialBuildingAI) && !(ai is IndustrialExtractorAI)) return ShopClass.None;
            if (sub != ItemClass.SubService.IndustrialGeneric && sub != ItemClass.SubService.IndustrialForestry && sub != ItemClass.SubService.IndustrialFarming
                && sub != ItemClass.SubService.IndustrialOil && sub != ItemClass.SubService.IndustrialOre)
            {
                return ShopClass.None;
            }
            switch (Incoming(id, sub))
            {
                case TransferManager.TransferReason.Logs: return ShopClass.BuysLogs;
                case TransferManager.TransferReason.Grain: return ShopClass.BuysGrain;
                case TransferManager.TransferReason.Ore: return ShopClass.BuysOre;
                case TransferManager.TransferReason.Oil: return ShopClass.BuysOil;
                case TransferManager.TransferReason.Lumber: return ShopClass.BuysLumber;
                case TransferManager.TransferReason.Food: return ShopClass.BuysFood;
                default: return ShopClass.BuysFuel;
            }
        }

        private static int Today()
        {
            return ShopCatalog.Day(Singleton<SimulationManager>.instance.m_currentGameTime.Ticks);
        }
    }
}
