import type { Affix, GameConfig, Item, ItemBaseDef, MonsterDef, Rarity, RiftTierDef } from '../types';
import type { Rng } from '../rng/weightedRoll';

/**
 * The dopamine engine. Implement in M2.
 * NOTE: every roll here goes through the shared rng module so results are
 * deterministic and statistically testable (sim 10k+ drops to verify rates).
 */

/** Roll whether a monster drops anything, and if so, what. */
export function rollDrop(
  _rng: Rng,
  _monster: MonsterDef,
  _tier: RiftTierDef,
  _cfg: GameConfig,
): Item | null {
  throw new Error('not implemented: rollDrop');
}

/** Build a concrete item of a given base/rarity/itemLevel. */
export function rollItem(
  _rng: Rng,
  _baseId: string,
  _itemLevel: number,
  _rarity: Rarity,
  _cfg: GameConfig,
): Item {
  throw new Error('not implemented: rollItem');
}

/** Roll N affixes from the base's allowed pool, scaled by itemLevel. */
export function rollAffixes(
  _rng: Rng,
  _base: ItemBaseDef,
  _count: number,
  _itemLevel: number,
  _cfg: GameConfig,
): Affix[] {
  throw new Error('not implemented: rollAffixes');
}
