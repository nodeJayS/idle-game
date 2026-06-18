import type { GameConfig, HeroInstance, Item, SaveState, StatBlock } from '../types';

/** base + growth*level + sum of equipped base stats & affixes. */
export function computeHeroStats(
  _hero: HeroInstance,
  _cfg: GameConfig,
  _equippedItems: Item[],
): StatBlock {
  // M2: implement. base + growthPerLevel*level, then add item baseStats + affixes.
  throw new Error('not implemented: computeHeroStats');
}

/** A single rolled-up "power" number for UI / future matchmaking. */
export function computePartyPower(_save: SaveState, _cfg: GameConfig): number {
  throw new Error('not implemented: computePartyPower');
}
