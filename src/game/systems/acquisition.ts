import type { GameConfig, HeroId, SaveState } from '../types';

/**
 * THE GACHA PLUG POINT.
 *
 * v1: progression rewards call acquireHero() directly with a chosen defId.
 * Later: a gacha system rolls a defId via weightedPick() (with pity/rates) and
 * then calls this SAME function. => adding gacha changes nothing downstream.
 */

/** Create & grant a new hero instance from a hero def (pure). Implement M3-ish. */
export function acquireHero(_save: SaveState, _defId: string, _cfg: GameConfig): SaveState {
  throw new Error('not implemented: acquireHero');
}

/** Place a hero into one of the 4 party slots (or null to clear). Implement M0/M3. */
export function setPartySlot(_save: SaveState, _slot: number, _heroId: HeroId | null): SaveState {
  throw new Error('not implemented: setPartySlot');
}
