import type { MonsterDef } from '../types';

/** Monster definitions (static). M0 set: a trash mob + a slightly tankier one + a boss. */
export const monsters: Record<string, MonsterDef> = {
  slime: {
    id: 'slime',
    name: 'Slime',
    baseStats: { hp: 30, atk: 5, def: 1, spd: 0.8, critChance: 0, critDmg: 1.5 },
    lootTableId: 'common',
    xpReward: 5,
    goldReward: 2,
    sprite: 'slime',
  },
  goblin: {
    id: 'goblin',
    name: 'Goblin',
    baseStats: { hp: 45, atk: 8, def: 2, spd: 1.1, critChance: 0.03, critDmg: 1.5 },
    lootTableId: 'common',
    xpReward: 9,
    goldReward: 4,
    sprite: 'goblin',
  },
  goblin_king: {
    id: 'goblin_king',
    name: 'Goblin King',
    baseStats: { hp: 320, atk: 16, def: 5, spd: 0.9, critChance: 0.05, critDmg: 1.6 },
    lootTableId: 'boss',
    xpReward: 60,
    goldReward: 40,
    sprite: 'goblin_king',
  },
};
