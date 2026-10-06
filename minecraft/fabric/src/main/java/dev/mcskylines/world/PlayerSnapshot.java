package dev.mcskylines.world;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.NbtUtils;
import net.minecraft.nbt.Tag;
import net.minecraft.network.protocol.game.ClientboundSetHeldSlotPacket;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.ProblemReporter;
import net.minecraft.util.datafix.DataFixTypes;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.level.storage.TagValueOutput;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Protocol 1.11 PLAYER_DATA on the Fabric guest: the player's own state exactly as Minecraft saves it (gzip NBT of
 * {@code saveWithoutId}, which carries DataVersion), and applying it to the live player. Server thread only.
 */
final class PlayerSnapshot {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	// Identity is the profile's and placement is the host's (ENTER_PLAYER_MODE): these keep the live player's values.
	private static final String[] KEPT = {"UUID", "Pos", "Motion", "Rotation", "Dimension"};
	private static final String GAME_TYPE = "playerGameType";
	private static final String PREVIOUS_GAME_TYPE = "previousPlayerGameType";
	private static final String GROWTH = "mcskylines:growth";

	private PlayerSnapshot() {
	}

	/** {@code growth}: the city's growth clocks (chunkKey, tick pairs), kept in the blob so the city's save holds them. */
	static byte[] capture(ServerPlayer p, long[] growth) throws IOException {
		ByteArrayOutputStream bytes = new ByteArrayOutputStream();
		CompoundTag tag = tagOf(p);
		tag.putLongArray(GROWTH, growth);
		NbtIo.writeCompressed(tag, bytes);
		return bytes.toByteArray();
	}

	/**
	 * Replaces the player's state with {@code data}, or with a fresh survival player when it is empty, and syncs the
	 * client. Returns the growth clocks the data carried (empty when none).
	 */
	static long[] apply(ServerPlayer p, byte[] data) throws IOException {
		CompoundTag tag = null;
		long[] growth = new long[0];
		if (data.length > 0) {
			tag = NbtIo.readCompressed(new ByteArrayInputStream(data), NbtAccounter.unlimitedHeap());
			tag = DataFixTypes.PLAYER.updateToCurrentVersion(p.level().getServer().getFixerUpper(), tag, NbtUtils.getDataVersion(tag));
		}
		reset(p);
		if (tag != null) {
			growth = tag.getLongArray(GROWTH).orElse(growth);
			tag.remove(GROWTH);
			CompoundTag own = tagOf(p);
			for (String key : KEPT) {
				Tag v = own.get(key);
				if (v != null) {
					tag.put(key, v.copy());
				} else {
					tag.remove(key);
				}
			}
			GameType mode = GameType.byId(tag.getIntOr(GAME_TYPE, GameType.SURVIVAL.getId()));
			// Loaded without its game mode (the player stays survival, as the client knows it); setGameMode below sends it.
			tag.remove(GAME_TYPE);
			tag.remove(PREVIOUS_GAME_TYPE);
			try (ProblemReporter.ScopedCollector reporter = new ProblemReporter.ScopedCollector(p.problemPath(), LOG)) {
				p.load(TagValueInput.create(reporter, p.registryAccess(), tag));
			}
			p.setGameMode(mode);
		}
		p.resetSentInfo();
		p.inventoryMenu.sendAllDataToRemote();
		p.containerMenu.sendAllDataToRemote();
		p.connection.send(new ClientboundSetHeldSlotPacket(p.getInventory().getSelectedSlot()));
		p.onUpdateAbilities();
		p.level().getServer().getPlayerList().sendActivePlayerEffects(p);
		return growth;
	}

	/** Everything a city's player owns back to a new survival player's values, so nothing leaks from the previous city. */
	private static void reset(ServerPlayer p) {
		p.closeContainer();
		p.getInventory().clearContent();
		p.getEnderChestInventory().clearContent();
		p.getInventory().setSelectedSlot(0);
		p.removeAllEffects();
		p.setHealth(p.getMaxHealth());
		p.setAbsorptionAmount(0f);
		p.getFoodData().setFoodLevel(20);
		p.getFoodData().setSaturation(5f);
		p.experienceLevel = 0;
		p.experienceProgress = 0f;
		p.totalExperience = 0;
		p.setRespawnPosition(null, false);
		p.setGameMode(GameType.SURVIVAL);
		p.getAbilities().flying = false;
		p.resetFallDistance();
		p.setAirSupply(p.getMaxAirSupply());
		p.clearFire();
	}

	private static CompoundTag tagOf(ServerPlayer p) {
		try (ProblemReporter.ScopedCollector reporter = new ProblemReporter.ScopedCollector(p.problemPath(), LOG)) {
			TagValueOutput out = TagValueOutput.createWithContext(reporter, p.registryAccess());
			p.saveWithoutId(out);
			return out.buildResult();
		}
	}
}
