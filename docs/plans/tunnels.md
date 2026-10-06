# Realistic road tunnels in Minecraft mode

Owner (2026-10-06): walk underground with the cars; the black wall at tunnel mouths and the blue-grey x-ray look
should go; the roof over the entrance should be solid. Order: after M4 (done 2026-10-06).

## What CS1 gives us (owner's Ctrl+Shift+D dumps, 2026-10-06; docs/MILESTONES.md "Finding")

- `Basic Road Tunnel` segments and tunnel nodes draw only `Custom/Net/Metro` on layer 14 MetroTunnels (the x-ray look).
- `Basic Road Slope` = segment[0] `small-tunnel-segment` (layer 14, x-ray) + segment[1] `small-tunnel-slope`
  (layer 9 Road, `Custom/Net/RoadBridge`, the visible ramp and portal; the black wall at the mouth is part of it).
- Underground vehicles switch to `VehicleInfo.m_undergroundMaterial` (x-ray style).
- Collision already has the tunnel shape (NetGeometry.Tube: floor, walls, ceiling along the drawn edges).
- Built-in meshes, including net segment meshes, are now in the local mesh cache (tools/extract-cs1-meshes.sh).

## Plan

Phase A (first agent):
1. Tunnel interior: for road tunnel and slope segments near the player, draw a road surface, walls and ceiling along the
   same edges the collision uses. Road surface: the matching ground road's own segment mesh and material, drawn with the
   tunnel segment's bezier parameters the way NetSegment.RenderInstance feeds the net shader, so it looks like the real
   road; walls and ceiling: generated meshes with a concrete texture taken from CS1's own materials. Only while in
   Minecraft mode; the x-ray tunnel layer is not used then.
2. Underground vehicles in road tunnels drawn with their normal surface material and mesh.
3. Darkness: tunnels get CS1's normal lighting; anything better (lamps along the walls) is a follow-up.

Phase B (second agent):
4. Portal: while in Minecraft mode, swap the slope segment's mesh for a copy without the black end wall (from the mesh
   cache; NetInfo.Segment.m_segmentMesh is a public field), restored exactly on exit.
5. Portal roof collision: collision triangles from the slope mesh itself, bent along the segment the way CS1's net
   shaders bend segment meshes, so the roof over the entrance is solid.

Underground mode setting (Ctrl+Shift+N) stays for comparison until the owner signs off.
