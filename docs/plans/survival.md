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
4. **Creatures and dropped items drawn in CS1** (needed before hostile mobs).
5. **City-survival links** (trade with shops, city problems as dangers, resource depletion), then the Ender Dragon.

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
