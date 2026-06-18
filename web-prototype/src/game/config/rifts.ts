import type { RiftTierDef } from '../types';

/**
 * Rift tiers (static). The progression spine: higher tier = tougher monsters +
 * better loot. Generated as a curve; tune the formulas (or hand-author) later.
 */
export const rifts: RiftTierDef[] = Array.from({ length: 50 }, (_, i): RiftTierDef => {
  const tier = i + 1;
  return {
    tier,
    monsterLevel: tier,
    packCount: 3 + Math.floor(tier / 5),
    bossId: 'goblin_king',
    dropRateMult: 1 + tier * 0.05,
    affixItemLevel: tier,
  };
});
