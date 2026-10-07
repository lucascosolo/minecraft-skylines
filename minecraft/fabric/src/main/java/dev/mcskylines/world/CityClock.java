package dev.mcskylines.world;

import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.TimeSet;
import dev.mcskylines.protocol.WorldTime;
import net.minecraft.client.Minecraft;
import net.minecraft.core.Holder;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.clock.WorldClock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Shows the city's time of day (WORLD_TIME, minor 6) on the overworld clock of the integrated server, so the sky and
 * light levels (the hand included) follow Cities: Skylines. The city world never advances time on its own
 * (DevWorld sets ADVANCE_TIME false). Owner, 2026-10-06: "sync the minecraft clock to the cities skylines days".
 * Anything else that moves that clock (/time set, /time add, sleeping) is undone and sent to the city as TIME_SET
 * (minor 16; owner, same day: "minecraft /time command should control the CS1 time"); the next WORLD_TIME shows it.
 */
public final class CityClock {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";

	private static volatile long lastTicks = Long.MIN_VALUE;
	private static volatile boolean dayNight;
	private static volatile CityClock current;
	private static boolean applying; // server thread

	private final BridgeGuest guest;
	private volatile int appMinor;
	private WorldTime latest;
	private long appliedTicks = Long.MIN_VALUE;
	private MinecraftServer appliedOn;
	private boolean logged;

	public CityClock(BridgeGuest guest) {
		this.guest = guest;
		current = this;
	}

	/** Server thread: the city tick last applied, {@link Long#MIN_VALUE} while unknown (growth catch-up pauses then). */
	public static long lastTicks() {
		return lastTicks;
	}

	/** Server thread: whether the city runs day and night (otherwise the sun is always up). */
	public static boolean dayNight() {
		return dayNight;
	}

	/** Server thread: whether a change of this clock belongs to the city (and goes there as TIME_SET). */
	public static boolean drives(MinecraftServer server, Holder<WorldClock> clock) {
		CityClock c = current;
		if (applying || c == null || c.appMinor < 16 || lastTicks == Long.MIN_VALUE) return false;
		ServerLevel level = server.overworld();
		return level != null && level.dimensionType().defaultClock().map(clock::equals).orElse(false);
	}

	/**
	 * Server thread, after something moved {@code clock} from {@code before} to {@code after}: true when the caller
	 * must put it back because the city was asked to move instead.
	 */
	public static boolean moved(MinecraftServer server, Holder<WorldClock> clock, long before, long after) {
		if (!drives(server, clock)) return false;
		TimeSet t = CityTime.toCity(before, after);
		if (t == null) return false;
		current.guest.send(AppProtocol.TIME_SET, t.encode());
		LOG.info(PREFIX + "clock: Minecraft moved the city clock {} -> {} ticks; asked the city for {} h plus {} days",
			before, after, String.format("%.2f", t.hour()), t.days());
		return true;
	}

	/** Client thread, link up: the negotiated app minor (TIME_SET from 16). */
	public void linkUp(int minor) {
		appMinor = minor;
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
			level.dimensionType().defaultClock().ifPresent(clock -> {
				applying = true;
				try {
					server.clockManager().setTotalTicks(clock, ticks);
				} finally {
					applying = false;
				}
			});
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
		appMinor = 0;
	}
}
