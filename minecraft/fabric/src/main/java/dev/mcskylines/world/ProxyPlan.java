package dev.mcskylines.world;

import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashSet;
import java.util.Set;

/** Which citizen proxies to spawn, keep and remove, by citizen id, against the citizens currently near the player. */
public final class ProxyPlan {
	private ProxyPlan() {
	}

	public record Plan(int[] spawn, int[] remove, int[] keep) {
	}

	/** {@code wanted}: nearest first; its first {@code max} distinct ids are the target set. */
	public static Plan reconcile(int[] existing, int[] wanted, int max) {
		Set<Integer> target = new LinkedHashSet<>();
		for (int id : wanted) {
			if (target.size() >= max) break;
			target.add(id);
		}
		Set<Integer> have = new HashSet<>();
		for (int id : existing) have.add(id);
		int[] spawn = target.stream().filter(id -> !have.contains(id)).mapToInt(Integer::intValue).toArray();
		int[] keep = target.stream().filter(have::contains).mapToInt(Integer::intValue).toArray();
		int[] remove = Arrays.stream(existing).filter(id -> !target.contains(id)).distinct().sorted().toArray();
		return new Plan(spawn, remove, keep);
	}
}
