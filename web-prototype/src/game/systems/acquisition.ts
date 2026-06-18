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

/** Place a hero into one of the 4 party slots (or null to clear). Pure. */
export function setPartySlot(save: SaveState, slot: number, heroId: HeroId | null): SaveState {
  if (slot < 0 || slot >= save.party.length) {
    throw new Error(`setPartySlot: slot ${slot} out of range (0..${save.party.length - 1})`);
  }
  if (heroId !== null && !save.heroes.some((h) => h.id === heroId)) {
    throw new Error(`setPartySlot: hero "${heroId}" not owned`);
  }
  const party = [...save.party];
  party[slot] = heroId;
  return { ...save, party };
}
