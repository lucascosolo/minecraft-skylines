# Minecraft sky in Minecraft mode

Owner (2026-10-06): "erase the Cities Skylines skybox and replace it with the Minecraft sky when in Minecraft mode",
"do the one where we redraw in CS1", and "the minecraft sun / moon should still be a light source like the cities
skylines sun / moon".

## Decisions

- CS1 draws the sky itself, from parameters Minecraft sends (sky and fog colours, sunrise band, moon phase, star
  brightness, rain, cloud colour and offset) plus Minecraft's sun, moon-phase and cloud textures once. Rejected:
  streaming Minecraft's rendered sky as an image (one to two frames of lag, so the sky and sun swim when turning).
- The sun and moon are drawn where CS1's own sun and moon light comes from (the directional light's direction), not
  at Minecraft's celestial angle: CS1's sun follows a tilted path for its latitude, Minecraft's goes straight overhead,
  and shadows and shading in the city come from CS1's light. So the Minecraft-textured sun is the light source you
  see, and the scene stays lit by it. The time of day is already shared (WORLD_TIME, protocol 1.6).
- CS1's own sky is hidden only while in Minecraft mode and restored exactly on exit; terrain, buildings and lighting
  are untouched. CS1's distance fog is tinted towards Minecraft's fog colour so the horizon has no seam (to be
  confirmed against how CS1 draws fog and scattering; research pending).

## Order

After the lamp-light work (protocol 1.8) lands, as the next protocol minor.
