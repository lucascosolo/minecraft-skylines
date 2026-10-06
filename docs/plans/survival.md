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
   save. Planting a sapling may later grow a CS1 tree.
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
4b. **Citizens are villagers to mobs** (owner, 2026-10-06: "CS1 npcs will be villagers to the Minecraft mobs and they
   will be triggered to panic when targeted by mobs, and they will be capable of actually being killed by them too").
   CS1 citizens near the simulated area get invisible villager proxies in Minecraft that follow them (positions as
   already sent for collision); hostile mobs target them with vanilla AI; being targeted or hit sets CS1's own panic
   (CitizenInstance.Flags.Panicking) so the citizen flees CS1-style; the proxy's death kills the citizen through CS1's
   own death path (hearses, mourning, population). Optional toggle: zombie kills convert citizens into zombie
   villagers. Behind its own setting, only in Minecraft-enabled (backed-up) cities; deaths are permanent city changes.
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
