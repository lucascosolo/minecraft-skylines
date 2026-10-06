# Blended survival

Owner (2026-10-06): "a blended survival type experience within your city where the state of your city affects your
minecraft survival experience and vice versa"; a late-game path to summoning the Ender Dragon into the city;
"mining terrain should give an appropriate material based on what I'm breaking, but there should be plenty of good
material available including some ores buried in the ground. we need trees to get wood too".

## Order

1. **Player state per city** (in progress): survival by default, inventory/health/hunger/XP/spawn saved in the city
   save, respawn at the bed or the last entry spot.
2. **Trees give wood**: breaking a CS1 tree's trunk drops logs (by tree size and type), plus leaves, saplings and
   sometimes apples, and the tree is removed from the city (TreeManager), so the city records the felling in its own
   save. Breaking any log of a city tree, even by hand, fells the whole tree (owner, 2026-10-06: tree feller, "even
   if broken with hands, the whole tree falls"; no extra axe wear). Queued (owner, 2026-10-06: "make all the trees
   spawned by the mod look like vanilla CS1 trees but behave like Minecraft trees"): a sapling that grows in Minecraft
   becomes a real CS1 tree (a guest-to-host "tree grown" message; the host creates it through TreeManager and sends it
   back in TREES), drawn by CS1 as its own tree of the matching kind and felled like any other city tree. The
   Minecraft-grown blocks are replaced by the shadow tree, so it is saved by the city, not as player edits.
3. **Mining terrain** (milestone 5, see the terrain notes in docs/MILESTONES.md): hidden "shadow blocks" under CS1's
   ground make breaking vanilla Minecraft; the hole is drawn in CS1.
4. **Creatures live in the city** (needed before hostile mobs). Owner (2026-10-06): "Minecraft mobs need to exist in
   the CS1 world even when the player leaves Minecraft mode, just like the blocks". So: (a) mobs and other entities
   are saved in the city save and restored on load (Minecraft's world stays a cache); (b) in city view the guest keeps
   chunks loaded and simulated around where CS1's camera looks (chunk tickets following the camera, the shadow world
   built there too), with despawn rules adjusted so mobs do not vanish because the player is far away; (c) entities are
   drawn in CS1 from any camera: each entity type's model (cuboids and texture) sent once like the block atlas, then
   per entity ~20 Hz position, orientation and part rotations, animated and drawn by CS1 in its own scene (no image
   streaming, no lag). Dropped items, arrows, minecarts the same way.
4a. **Farming and animals readily available** (owner, 2026-10-06: "farming and natural animal spawning needs to be
   readily available"). Farming is vanilla on the shadow ground (hoe → farmland, seeds from grass, CS1 water hydrates,
   crops/saplings/cane grow at Minecraft's pace); tilled and planted cells are player edits, so CS1 draws and saves
   them; arrives with the shadow world. Animals: vanilla rarely spawns passive mobs after generation, so the guest spawns
   them regularly near the player by city context — cows/pigs/sheep/chickens on open grass, parks and CS1 farm land;
   horses/llamas on open country; rabbits/foxes/wolves near forest; fish/squid in CS1 water — on shadow grass, standing
   on CS1's visible ground. Needs entities drawn in CS1 (step 4) first.
   Growing conditions (owner, 2026-10-06: "Saplings need to be fed the conditions necessary for them to grow which might
   not exist in the environment they're placed in currently", "Same with plants that can be farmed", "I would like to be
   able to hoe CS1 grass and turn it into farmland just like a normal grass block"). Vanilla (checked in the 26.3 jar):
   a sapling advances on a random tick with chance 1/7 when the light at the cell above is at least 9, in two stages,
   on a block in `supports_vegetation`; crops add farmland moisture and neighbour rules. What the city lacks:
   (a) random ticks happen only in chunks simulated near the Minecraft player, so a farm or sapling anywhere else, or
   in city view, never grows; (b) a CS1 tree grown from a sapling needs room against the whole city (buildings above the
   4-block barrier fill, props), not only shadow blocks; (c) hoes, shovels and bone meal aimed at CS1's ground hit the
   invisible grass plant or the empty cell above it, never the ground block. Plan: tool use on CS1 ground acts on the
   shadow ground block (a hoe clears the plant on it and tills, as on a bare grass block); every growing plant the
   player owns catches up when its chunk loads, by vanilla's probabilities applied to the city time that passed, with
   light from the synced sun and CS1's lamps and moisture from CS1 water; pausing CS1 pauses growth; a sapling that
   completes grows into a CS1 tree of the matching kind (TREES), if the city has room for it.
4b. **Citizens are villagers to mobs** (owner, 2026-10-06: "CS1 npcs will be villagers to the Minecraft mobs and they
   will be triggered to panic when targeted by mobs, and they will be capable of actually being killed by them too").
   CS1 citizens near the simulated area get invisible villager proxies in Minecraft that follow them (positions as
   already sent for collision); hostile mobs target them with vanilla AI; being targeted or hit sets CS1's own panic
   (CitizenInstance.Flags.Panicking) so the citizen flees CS1-style; the proxy's death kills the citizen through CS1's
   own death path (hearses, mourning, population). Zombie kills convert citizens into zombie villagers by Minecraft's
   own rule (Easy never, Normal half the time, Hard always), on by default so an outbreak can spread through the city
   (owner: "Zombie apocalypse would be a great side effect of this mod"); can be switched off. Difficulty becomes a
   per-city setting (the dev world is Peaceful today, which has no hostile mobs). Behind its own setting, only in Minecraft-enabled (backed-up) cities; deaths are permanent city changes.
5. **City-survival links** (trade with shops, city problems as dangers, resource depletion), then the Ender Dragon.

## The shadow world (foundation for trees, mining and mobs)

Owner (2026-10-06): mobs need physical Minecraft terrain to walk and pathfind on. Minecraft's world near the player
gets real blocks for the whole city, rebuilt from the streamed collision (whose triangles are already tagged terrain,
road, building, vegetation, prop): ground blocks under CS1's terrain (material rules below), a paved layer under roads
and pavements, invisible solid blocks filling buildings (so mobs path around them), logs and leaves where CS1 trees
stand. CS1 never draws these and they are never saved: only the player's changes (placed, dug, felled) go into the
city's edit set. The player keeps the smooth triangle collision; mobs, fluids, spawning and pathfinding use the blocks.
CS1's lit lamps (light blocks, protocol 1.8) keep lit streets free of hostile spawns. Built chunk by chunk around the
player on the guest. Approximation: 1 m steps against CS1's smooth ground (slabs/stairs later).
Order change: trees come as part of this (real logs/leaves), before mining's hole drawing.

## Material rules for mining

- What you break depends on where it is: the top block follows CS1's ground surface (grass on grass, sand on beaches
  and riverbeds, gravel or coarse dirt on CS1's dirt/ruined ground, stone where CS1 shows cliff rock), then a few
  blocks of dirt (sand under sand), then stone, with deepslate further down and bedrock at the bottom of CS1's terrain.
- Plenty of good material: ores are buried throughout, with Minecraft-like depth bands (coal and copper shallow,
  iron throughout, gold, redstone and lapis deeper, diamond near the bottom), plus clay near water and occasional
  gravel, andesite, diorite and granite pockets.
- CS1's own natural resource map shapes it: where the city map shows ore, ores are noticeably richer; where it shows
  oil, more coal; fertile land has deeper dirt. (A later link: mining ore could deplete CS1's ore resource.)
- The layout is fixed per city (seeded from the city's pairing id) and position, so the same spot always holds the
  same block, and only what the player changed is saved.
