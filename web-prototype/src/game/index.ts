/**
 * game-core public surface. The renderer, React UI, and (later) the server
 * import from here. Nothing under src/game imports React, Pixi, the DOM, or
 * Supabase — that boundary is what keeps the renderer swappable and the
 * mobile/server/gacha paths additive.
 */
export * from './types';
export * from './rng/weightedRoll';

export * from './systems/stats';
export * from './systems/loot';
export * from './systems/combat';
export * from './systems/idle';
export * from './systems/progression';
export * from './systems/inventory';
export * from './systems/acquisition';

export * from './save';
export { gameConfig } from './config';
