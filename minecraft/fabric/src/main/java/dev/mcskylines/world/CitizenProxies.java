package dev.mcskylines.world;

import dev.mcskylines.collision.DynamicObstacleStore;
import dev.mcskylines.collision.ObstacleBox;
import dev.mcskylines.protocol.CitizenEvents;
import dev.mcskylines.protocol.DynamicObstacles;
import it.unimi.dsi.fastutil.ints.Int2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.ints.IntOpenHashSet;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.entity.npc.villager.Villager;
import net.minecraft.world.scores.PlayerTeam;
import net.minecraft.world.scores.Team;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.ArrayList;
import java.util.List;

/**
 * Protocol 1.19: every citizen near the player gets a villager proxy that follows the citizen's CS1 position each tick
 * (no AI of its own), so hostile mobs hunt citizens with vanilla AI. Proxies are on a team that never collides, are not
 * drawn by the host, cannot be picked or hurt by the player, and are never kept across a reload. Server thread only.
 */
public final class CitizenProxies {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";
	public static final String TEAM = "mcskylines_citizens";
	private static final int MAX_PROXIES = 128;
	private static final long PANIC_PERIOD_TICKS = 100;

	private static CityEdits city;
	private static final Int2ObjectOpenHashMap<Villager> proxies = new Int2ObjectOpenHashMap<>();
	private static final IntOpenHashSet converted = new IntOpenHashSet();
	private static final PanicThrottle panic = new PanicThrottle(PANIC_PERIOD_TICKS);
	private static final List<CitizenEvents.Event> events = new ArrayList<>();
	private static long ticks;

	private CitizenProxies() {
	}

	public static void register(CityEdits c) {
		city = c;
	}

	/** Both sides: a citizen's proxy (a villager on the proxies' team; teams reach the client). */
	public static boolean isProxy(Entity e) {
		PlayerTeam t = e instanceof Villager ? e.getTeam() : null;
		return t != null && TEAM.equals(t.getName());
	}

	/** ALLOW_DAMAGE: a proxy is hurt only by a hostile mob (or what one shot); that also panics the citizen. */
	public static boolean allowDamage(LivingEntity target, DamageSource source) {
		if (!isProxy(target)) {
			return true;
		}
		Entity by = source.getEntity();
		if (!(by instanceof Enemy)) {
			return false;
		}
		panicked(target);
		return true;
	}

	/** Mob.setTarget: a mob picked a proxy. */
	public static void targeted(LivingEntity target) {
		if (target != null && isProxy(target)) {
			panicked(target);
		}
	}

	/** Zombie.killedEntity: whether this zombie's kill of {@code victim} converts it (vanilla's rule plus the city's switch). */
	public static boolean convertsProxy(Villager victim, int difficultyId, boolean coin) {
		boolean yes = ZombieConversion.converts(difficultyId, city != null && city.rules().conversion(), coin);
		int id = idOf(victim);
		if (yes && id >= 0) {
			converted.add(id);
			// The zombie villager keeps the villager's team, AI switch and silence; it is a free mob, not a proxy.
			victim.level().getScoreboard().removePlayerFromTeam(victim.getScoreboardName());
			victim.setNoAi(false);
			victim.setSilent(false);
			victim.setNoGravity(false);
		}
		return yes;
	}

	/** ENTITY_LOAD: a proxy left in a saved chunk is stale (proxies are never kept). */
	public static void loaded(Entity e) {
		if (isProxy(e) && !proxies.containsValue(e)) {
			e.discard();
		}
	}

	public static void tick(MinecraftServer server) {
		ticks++;
		CityEdits c = city;
		long seq = c == null ? -1 : c.citizenOpenSeq();
		ServerLevel level = c == null ? null : c.cityLevel();
		List<ObstacleBox> near = new ArrayList<>();
		if (seq >= 0 && level != null) {
			for (ObstacleBox o : DynamicObstacleStore.INSTANCE.current(System.nanoTime())) {
				if (o.kind == DynamicObstacles.CITIZEN) {
					near.add(o);
				}
			}
		}
		reportDeaths();
		Int2ObjectOpenHashMap<ObstacleBox> byId = new Int2ObjectOpenHashMap<>();
		int[] wanted = new int[near.size()];
		for (int i = 0; i < wanted.length; i++) {
			wanted[i] = near.get(i).id;
			byId.put(wanted[i], near.get(i));
		}
		ProxyPlan.Plan plan = ProxyPlan.reconcile(proxies.keySet().toIntArray(), wanted, MAX_PROXIES);
		for (int id : plan.remove()) {
			Villager v = proxies.remove(id);
			panic.forget(id);
			if (v != null && !v.isRemoved()) {
				v.discard();
			}
		}
		for (int id : plan.keep()) {
			follow(proxies.get(id), byId.get(id));
		}
		for (int id : plan.spawn()) {
			Villager v = spawn(level, byId.get(id));
			if (v != null) {
				proxies.put(id, v);
			}
		}
		if (!events.isEmpty()) {
			if (seq >= 0) {
				c.sendCitizenEvents(new CitizenEvents((int) seq, List.copyOf(events)));
			}
			events.clear();
		}
	}

	private static void reportDeaths() {
		var it = proxies.int2ObjectEntrySet().fastIterator();
		while (it.hasNext()) {
			var e = it.next();
			Villager v = e.getValue();
			int id = e.getIntKey();
			boolean conv = converted.remove(id);
			if (conv || v.isDeadOrDying()) {
				LOG.info(PREFIX + "citizen #{} {} at {}", id, conv ? "turned into a zombie villager" : "killed by a mob", v.blockPosition());
				add(conv ? CitizenEvents.CONVERTED : CitizenEvents.KILLED, id, v);
				if (!v.isRemoved()) {
					v.discard();
				}
				it.remove();
				panic.forget(id);
			} else if (v.isRemoved()) {
				it.remove(); // unloaded, or struck by lightning (a witch now): no event
			}
		}
	}

	private static Villager spawn(ServerLevel level, ObstacleBox o) {
		Villager v = EntityTypes.VILLAGER.create(level, EntitySpawnReason.EVENT);
		if (v == null) {
			return null;
		}
		v.setNoAi(true);
		v.setSilent(true);
		v.setNoGravity(true);
		level.getScoreboard().addPlayerToTeam(v.getScoreboardName(), team(level));
		follow(v, o);
		level.addFreshEntity(v);
		return v;
	}

	private static void follow(Villager v, ObstacleBox o) {
		float yaw = (float) o.yaw;
		v.snapTo(o.x, o.y - o.halfHeight, o.z, yaw, 0f);
		v.setYHeadRot(yaw);
		v.setYBodyRot(yaw);
		v.setDeltaMovement(0, 0, 0);
	}

	private static PlayerTeam team(ServerLevel level) {
		PlayerTeam t = level.getScoreboard().getPlayerTeam(TEAM);
		if (t == null) {
			t = level.getScoreboard().addPlayerTeam(TEAM);
			t.setCollisionRule(Team.CollisionRule.NEVER);
		}
		return t;
	}

	private static void panicked(LivingEntity proxy) {
		int id = idOf(proxy);
		if (id >= 0 && panic.allow(id, ticks)) {
			add(CitizenEvents.PANIC, id, proxy);
		}
	}

	private static void add(int kind, int id, Entity at) {
		if (events.size() < CitizenEvents.MAX_EVENTS) {
			events.add(new CitizenEvents.Event(kind, id, (float) at.getX(), (float) at.getY(), (float) at.getZ()));
		}
	}

	private static int idOf(Entity e) {
		for (var en : proxies.int2ObjectEntrySet()) {
			if (en.getValue() == e) {
				return en.getIntKey();
			}
		}
		return -1;
	}
}
