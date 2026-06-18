import type { AffixDef } from '../types';

/**
 * Affix pool (static). Weight = relative roll chance; valuePerItemLevel scales
 * magnitude with item level; rarityFloor gates an affix to higher rarities.
 */
export const affixPool: AffixDef[] = [
  { stat: 'hp', weight: 30, valuePerItemLevel: { min: 4, max: 8 }, rarityFloor: 'magic' },
  { stat: 'atk', weight: 25, valuePerItemLevel: { min: 1, max: 2 }, rarityFloor: 'magic' },
  { stat: 'def', weight: 20, valuePerItemLevel: { min: 1, max: 2 }, rarityFloor: 'magic' },
  { stat: 'spd', weight: 8, valuePerItemLevel: { min: 0.01, max: 0.03 }, rarityFloor: 'rare' },
  { stat: 'critChance', weight: 8, valuePerItemLevel: { min: 0.005, max: 0.015 }, rarityFloor: 'rare' },
  { stat: 'critDmg', weight: 9, valuePerItemLevel: { min: 0.03, max: 0.08 }, rarityFloor: 'rare' },
];
