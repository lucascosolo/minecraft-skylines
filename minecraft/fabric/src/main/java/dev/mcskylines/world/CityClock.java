package dev.mcskylines.world;

import dev.mcskylines.protocol.WorldTime;
import net.minecraft.client.Minecraft;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Shows the city's time of day (WORLD_TIME, minor 6) on the overworld clock of the integrated server, so the sky and
 * light levels (the hand included) follow Cities: Skylines. The city world never advances time on its own
 * (DevWorld sets ADVANCE_TIME false). Owner, 2026-10-06: "sync the minecraft clock to the cities skylines days".
 */
public final class CityClock {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";

	private static volatile long lastTicks = Long.MIN_VALUE;
	private static volatile boolean dayNight;

	private WorldTime latest;
	private long appliedTicks = Long.MIN_VALUE;
	private MinecraftServer appliedOn;
	private boolean logged;

	/** Server thread: the city tick last applied, {@link Long#MIN_VALUE} while unknown (growth catch-up pauses then). */
	public static long lastTicks() {
		return lastTicks;
	}

	/** Server thread: whether the city runs day and night (otherwise the sun is always up). */
	public static boolean dayNight() {
		return dayNight;
	}

	/** Client thread: the newest time from the host. */
	public void deliver(WorldTime t) {
		latest = t;
	}

	/** Client thread, every tick: hands a changed time to the server thread. */
	public void tick(Minecraft mc) {
		WorldTime t = latest;
		MinecraftServer server = mc.getSingleplayerServer();
		if (t == null || server == null) {
			return;
		}
		long ticks = t.totalTicks();
		if (ticks == appliedTicks && server == appliedOn) {
			return;
		}
		appliedTicks = ticks;
		appliedOn = server;
		server.execute(() -> {
			ServerLevel level = server.overworld();
			if (level == null) {
				return;
			}
			lastTicks = ticks;
			dayNight = (t.flags() & WorldTime.DAY_NIGHT) != 0;
			level.dimensionType().defaultClock().ifPresent(clock -> server.clockManager().setTotalTicks(clock, ticks));
			if (!logged) {
				logged = true;
				LOG.info(PREFIX + "clock: city time {} h (day/night {}) -> overworld clock {} ticks", String.format("%.2f", t.hour()),
					(t.flags() & WorldTime.DAY_NIGHT) != 0 ? "on" : "off", ticks);
			}
		});
	}

	/** Link loss: keep the last time shown, but apply the next one even if it is equal. */
	public void linkDown() {
		latest = null;
		appliedTicks = Long.MIN_VALUE;
		lastTicks = Long.MIN_VALUE;
	}
}
