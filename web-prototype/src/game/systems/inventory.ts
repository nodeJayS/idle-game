import type { EquipSlot, GameConfig, HeroId, HeroInstance, Item, ItemId, SaveState, StatDelta } from '../types';

/** Add dropped items to the inventory (pure). Implement M2. */
export function addItems(_save: SaveState, _items: Item[]): SaveState {
  throw new Error('not implemented: addItems');
}

/** Equip an inventory item onto a hero's slot (pure). Implement M2. */
export function equipItem(_save: SaveState, _heroId: HeroId, _itemId: ItemId): SaveState {
  throw new Error('not implemented: equipItem');
}

/** Move an equipped item back to inventory (pure). Implement M2. */
export function unequipItem(_save: SaveState, _heroId: HeroId, _slot: EquipSlot): SaveState {
  throw new Error('not implemented: unequipItem');
}

/**
 * Stat change if `hero` equipped `item` vs. what's in that slot now.
 * Drives the green ▲ / red ▼ gear-compare UI. Implement M2/M6.
 */
export function compareForHero(_hero: HeroInstance, _item: Item, _cfg: GameConfig): StatDelta {
  throw new Error('not implemented: compareForHero');
}
