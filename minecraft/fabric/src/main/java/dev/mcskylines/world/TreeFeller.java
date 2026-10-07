package dev.mcskylines.world;

import com.mojang.brigadier.exceptions.CommandSyntaxException;
import dev.mcskylines.protocol.Trees;
import dev.mcskylines.shadow.TreeLayout;
import java.util.List;
import net.minecraft.commands.arguments.blocks.BlockStateParser;
import net.minecraft.core.registries.Registries;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.tags.BlockTags;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;

/**
 * Tree feller for the city's trees (owner, 2026-10-06: "make the CS1 trees break like normal Minecraft trees with tree
 * feller ... even if broken with hands, the whole tree falls"). Breaking any log of a generated tree brings down the
 * rest of it: every log still standing is broken with its normal loot, the CS1 canopy yields its leaves' loot, drops go to the player, and the last log makes CityEdits send TREE_FELLED so CS1 removes its tree.
 * Player-placed logs and logs of trees the player built are untouched (they are not shadow cells).
 */
public final class TreeFeller {
	private static int pendingTree = -1;
	private static BlockPos pendingPos;

	private TreeFeller() {
	}

	public static void register(CityEdits city) {
		PlayerBlockBreakEvents.BEFORE.register((level, player, pos, state, blockEntity) -> {
			pendingTree = state.is(BlockTags.LOGS) ? city.treeOfLog(level, pos) : -1;
			pendingPos = pendingTree < 0 ? null : pos.immutable();
			return true;
		});
		PlayerBlockBreakEvents.AFTER.register((level, player, pos, state, blockEntity) -> {
			int tree = pendingTree;
			BlockPos at = pendingPos;
			pendingTree = -1;
			pendingPos = null;
			if (tree < 0 || !pos.equals(at) || !(level instanceof ServerLevel server) || !(player instanceof ServerPlayer p)) {
				return;
			}
			fell(city, server, p, tree);
		});
	}

	private static void fell(CityEdits city, ServerLevel level, ServerPlayer player, int tree) {
		// The canopy CS1 draws has no blocks; it yields the loot its leaves would (saplings, sticks, apples).
		Trees.Tree t = city.tree(tree);
		BlockState leaves = null;
		if (t != null) {
			try {
				leaves = BlockStateParser.parseForBlock(level.holderLookup(Registries.BLOCK), TreeLayout.leaves(t.kind(), t.height()), false)
					.blockState();
			} catch (CommandSyntaxException e) {
				leaves = null;
			}
		}
		if (leaves != null) {
			BlockPos base = BlockPos.containing(t.x(), t.y() + 1, t.z());
			for (int i = TreeLayout.leafCount(t.kind(), t.height(), t.radius()); i > 0; i--) {
				for (ItemStack d : Block.getDrops(leaves, level, base, null, player, player.getMainHandItem())) {
					BuriedDrops.give(player, d);
				}
			}
		}
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		for (long key : city.treeCells(tree)) {
			pos.set(BlockKey.x(key), BlockKey.y(key), BlockKey.z(key));
			BlockState s = level.getBlockState(pos);
			if (s.isAir()) {
				continue;
			}
			List<ItemStack> drops = Block.getDrops(s, level, pos, null, player, player.getMainHandItem());
			for (ItemStack d : drops) {
				BuriedDrops.give(player, d);
			}
			level.setBlock(pos, Blocks.AIR.defaultBlockState(), Block.UPDATE_ALL);
		}
	}
}
