package dev.mcskylines.world;

import dev.mcskylines.protocol.ShopOffers;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.trading.ItemCost;
import net.minecraft.world.item.trading.Merchant;
import net.minecraft.world.item.trading.MerchantOffer;
import net.minecraft.world.item.trading.MerchantOffers;
import net.minecraft.world.phys.Vec3;

/** Protocol 1.20: a city building's shop, shown with vanilla's merchant screen. Server thread. */
public final class CityShop implements Merchant {
	private static final double REACH_SQ = 8.0 * 8.0;

	private final CityEdits city;
	private final int openSeq;
	private final int building;
	private final Vec3 hit;
	private final MerchantOffers offers = new MerchantOffers();
	private final Map<MerchantOffer, Integer> slots = new IdentityHashMap<>();
	private Player trading;

	public CityShop(CityEdits city, int openSeq, int building, Vec3 hit, List<ShopOffers.Offer> wanted) {
		this.city = city;
		this.openSeq = openSeq;
		this.building = building;
		this.hit = hit;
		for (ShopOffers.Offer o : wanted) {
			Optional<Item> cost = item(o.costItem());
			Optional<Item> result = item(o.resultItem());
			if (cost.isEmpty() || result.isEmpty()) {
				continue;
			}
			MerchantOffer offer = new MerchantOffer(new ItemCost(cost.get(), o.costCount()), Optional.empty(),
				new ItemStack(result.get(), o.resultCount()), 0, o.uses(), 0, 0f);
			slots.put(offer, o.slot());
			offers.add(offer);
		}
	}

	private static Optional<Item> item(String id) {
		Identifier key = Identifier.tryParse(id);
		return key == null ? Optional.empty() : BuiltInRegistries.ITEM.getOptional(key).filter(i -> i != Items.AIR);
	}

	public boolean hasOffers() {
		return !offers.isEmpty();
	}

	@Override
	public void setTradingPlayer(Player player) {
		trading = player;
	}

	@Override
	public Player getTradingPlayer() {
		return trading;
	}

	@Override
	public MerchantOffers getOffers() {
		return offers;
	}

	@Override
	public void overrideOffers(MerchantOffers replacement) {
	}

	@Override
	public void notifyTrade(MerchantOffer offer) {
		Integer slot = slots.get(offer);
		if (slot != null) {
			city.sendShopTrade(openSeq, building, slot);
		}
	}

	@Override
	public void notifyTradeUpdated(ItemStack stack) {
	}

	@Override
	public int getVillagerXp() {
		return 0;
	}

	@Override
	public void overrideXp(int xp) {
	}

	@Override
	public boolean showProgressBar() {
		return false;
	}

	@Override
	public SoundEvent getNotifyTradeSound() {
		return SoundEvents.VILLAGER_YES;
	}

	@Override
	public boolean canRestock() {
		return false;
	}

	@Override
	public boolean isClientSide() {
		return false;
	}

	@Override
	public boolean stillValid(Player player) {
		return city.shopOpen(openSeq) && player.distanceToSqr(hit) <= REACH_SQ;
	}

	public void open(Player player, String name, int level) {
		openTradingScreen(player, Component.literal(name), Math.max(1, Math.min(5, level)));
	}
}
