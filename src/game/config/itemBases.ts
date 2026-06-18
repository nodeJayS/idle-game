import type { ItemBaseDef } from '../types';

/** Item bases (static). Small M2 starter set — one per slot is plenty to start. */
export const itemBases: Record<string, ItemBaseDef> = {
  rusty_sword: {
    baseId: 'rusty_sword',
    slot: 'weapon',
    baseStats: { atk: 6 },
    allowedAffixes: ['atk', 'critChance', 'critDmg', 'spd'],
    sprite: 'sword',
  },
  leather_cap: {
    baseId: 'leather_cap',
    slot: 'helm',
    baseStats: { def: 3, hp: 10 },
    allowedAffixes: ['hp', 'def'],
    sprite: 'helm',
  },
  leather_vest: {
    baseId: 'leather_vest',
    slot: 'chest',
    baseStats: { def: 5, hp: 20 },
    allowedAffixes: ['hp', 'def'],
    sprite: 'chest',
  },
  worn_boots: {
    baseId: 'worn_boots',
    slot: 'boots',
    baseStats: { def: 2, spd: 0.05 },
    allowedAffixes: ['spd', 'def', 'hp'],
    sprite: 'boots',
  },
  copper_ring: {
    baseId: 'copper_ring',
    slot: 'ring',
    baseStats: {},
    allowedAffixes: ['atk', 'critChance', 'hp'],
    sprite: 'ring',
  },
};
