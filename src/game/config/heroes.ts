import type { HeroDef } from '../types';

/** Hero definitions (static). M0 content set: the starting Warrior. */
export const heroes: Record<string, HeroDef> = {
  warrior_basic: {
    defId: 'warrior_basic',
    name: 'Warrior',
    class: 'Warrior',
    role: 'melee',
    baseStats: { hp: 120, atk: 14, def: 8, spd: 1.0, critChance: 0.05, critDmg: 1.5 },
    growthPerLevel: { hp: 18, atk: 3, def: 1.5, spd: 0, critChance: 0, critDmg: 0 },
    skills: ['cleave'],
    sprite: 'warrior',
  },
};
