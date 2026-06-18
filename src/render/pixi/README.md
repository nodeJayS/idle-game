# `render/pixi` — the renderer (M0+)

The **only** place PixiJS is allowed. This layer:

- mounts a Pixi `Application` into a DOM canvas,
- reads `CombatState` from the app store each frame and draws the isometric
  scene (tiles, hero/monster sprites, loot beams, damage numbers),
- **interpolates** entity positions between fixed sim steps for smooth motion,
- plays `CombatEvent`s as juice (hit flashes, crit pops, loot beams, sfx).

It **never** decides game rules — no damage, no drops, no progression here.
All of that comes from `game-core` via the store.

## Pixel-art settings (cute/cozy + PoE depth)
- `PIXI.TextureSource.defaultOptions.scaleMode = 'nearest'` (crisp pixels)
- `roundPixels: true` on the app
- small low-res sprite atlases, small palette
- integer-scale the iso camera where possible

M0 deliverable: mount Pixi, draw a static iso tile floor + the Warrior sprite +
a couple of dummy monster sprites. No combat yet.
