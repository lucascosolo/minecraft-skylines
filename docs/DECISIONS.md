# Decisions

Newest last. Each entry says what was chosen, what was rejected, and why.

## 2026-10-05: play happens entirely inside Cities: Skylines

The owner confirmed the SkyCraft model: CS1 is the only window the player uses and it draws
everything; Minecraft Java runs hidden in the background and supplies the player's physics,
inventory, crafting and block logic. Rejected: playing in Minecraft with the city imported as
blocks (loses CS1's simulation and visuals, which is the point).

## 2026-10-05: target the original Cities: Skylines and Minecraft Java 26.3 + Fabric

Minecraft 26.3 is the newest stable release on meta.fabricmc.net (checked 2026-10-05) and is what
SkyCraft targets, so its MIT-licensed Fabric code (collision, digging, mesh export, input replay)
can be reused with the fewest changes. 26.x ships unobfuscated, so Mixins use Mojang names. The
CS1 version is pinned once the owner's install is inspected.

## 2026-10-05: SKBR over TCP loopback instead of SkyCraft's shared memory

Rejected for the control plane: SkyCraft's Win32 named shared memory. It is Windows-only, CS1's
.NET 3.5 profile has no `MemoryMappedFiles`, and detecting a dead peer needs heartbeats on both
sides anyway. Chosen: a framed TCP protocol (`protocol/bridge-v1.md`) with a handshake, heartbeats
and GOODBYE; I/O on background threads so neither game's main thread blocks. Shared memory stays
the plan for the GUI overlay's pixels (milestone 3), negotiated over the bridge.

## 2026-10-05: CS1 hosts the listening socket

CS1 is the game the player starts and the one that owns the city; Minecraft is started beside it
and connects, retrying. Mirrors SkyCraft (Skyrim creates the mapping, Minecraft opens it).

## 2026-10-05: reusable layers (the owner's requirement)

`Skylines.Bridge` (transport) and the Java `bridge` module know nothing about either game;
`Skylines.Host` knows CS1 but not Minecraft; only `MinecraftSkylines.*` and the Fabric mod know
Minecraft. A future "CS1 + another game" project lifts the first three. See `ARCHITECTURE.md`.

## 2026-10-05: one block = one metre, z flipped

CS1 units are metres; a Minecraft block is a metre (the player is 1.8 blocks, a CS1 citizen is
about 1.8 m). No scale factor, so collision and placement need no rounding policy. Unity is
left-handed, Minecraft right-handed: `mc.z = -cs.z`, `yaw = unityY + 180`. Verified against the
two games' forward-vector formulas in `gen_vectors.py`.

## 2026-10-05: protocol tested by an independent reference

The Python reference (`protocol/reference/`) generates golden vectors and runs a black-box
conformance suite against each implementation; the suite itself was validated against a Python
reference peer (27/27). This substitutes for separately authored unit tests on the codecs: the
expectations come from a third implementation, not from the code under test.

## 2026-10-05: quarantine instead of deletion; no clean tasks

Owner's rule: no `rm`, no deletion through other means. Unwanted files go to `_quarantine/`.
MSBuild's `IncrementalClean` (which deletes stale outputs) is overridden to do nothing.
