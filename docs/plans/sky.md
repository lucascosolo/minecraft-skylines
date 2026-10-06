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

## Research (2026-10-06, decompile and Minecraft 26.3 classes; not yet tried in game)

CS1 (Assembly-CSharp):
- The sky is Unity's skybox: `DayNightProperties` (singleton `instance`, DayNightProperties.cs:382) builds a private
  material from shader `Hidden/DayNight/Skybox` (:433-442) and assigns it to `RenderSettings.skybox` in
  `UpdateSkyboxMaterial` (:797-820, sun and moon matrices, moon texture, outer-space cubemap). Each frame it also forces
  `Camera.main.clearFlags = Skybox` (:727-737). Stars are a procedural mesh (`Starfield`, `starsMesh` :446) with a
  `Hidden/DayNight/Stars` material; whether they are drawn in a separate pass is not traced yet.
- Sun and moon light: public `m_SunLight`, `m_MoonLight`; their transforms give the light directions to draw the sun
  and moon at.
- Fog: `DayNightFogEffect` (DayNightFogEffect.cs:5), an `[ImageEffectOpaque]` blit with `Hidden/DayNight/Fog`
  (depth-reconstructed), colours from `DayNightProperties` gradients and `FogProperties` (FogProperties.cs:173 sets
  global colours). Shader sources are not available, so the fog tint must be found by experiment.
- Least invasive hide: per frame on the main camera, in `Camera.onPreCull` save `clearFlags`/`backgroundColor` and set
  `SolidColor` (or draw our sky first and clear depth only), restore in `onPostRender`. Disabling `DayNightProperties`
  is rejected (stops sun, ambient and fog updates).

Minecraft 26.3:
- `net.minecraft.client.renderer.SkyRenderer`: `extractRenderState(ClientLevel, float, Camera, SkyRenderState)`, then
  `render(...)` (sky disc, sunrise/sunset band, sun, moon, stars, dark disc). Clouds: `CloudRenderer`.
- Values: `Camera.attributeProbe().getValue(EnvironmentAttributes.X, partialTick)` for SKY_COLOR, FOG_COLOR,
  SUNRISE_SUNSET_COLOR, CLOUD_COLOR, CLOUD_HEIGHT, SUN_ANGLE, MOON_ANGLE, STAR_ANGLE, MOON_PHASE, STAR_BRIGHTNESS,
  SKY_LIGHT_COLOR, SKY_LIGHT_FACTOR, fog distances; rain from `ClientLevel.getRainLevel(float)`. Cloud scroll offset
  has no public getter (computed in `CloudRenderer.prepare` from game time).
- Textures: `textures/environment/celestial/sun.png`, `textures/environment/celestial/moon/<phase>.png` (8 phases),
  `textures/environment/clouds.png`; stars are procedural (`SkyRenderer.buildStars`).
