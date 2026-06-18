import { describe, it, expect } from 'vitest';
import { newGame, serialize, deserialize, STARTER_HERO_DEF } from './save';
import { setPartySlot } from './systems/acquisition';
import { gameConfig } from './config';

describe('newGame', () => {
  it('starts with one Warrior in slot 0 and three empty slots', () => {
    const save = newGame(123, gameConfig, 1000);
    expect(save.heroes).toHaveLength(1);
    expect(save.heroes[0].defId).toBe(STARTER_HERO_DEF);
    expect(save.party).toEqual([save.heroes[0].id, null, null, null]);
    expect(save.currencies.gold).toBe(0);
    expect(save.lastClaimAt).toBe(1000);
  });

  it('round-trips through serialize/deserialize', () => {
    const save = newGame(7, gameConfig, 42);
    expect(deserialize(serialize(save))).toEqual(save);
  });
});

describe('setPartySlot', () => {
  it('places an owned hero into a slot (pure)', () => {
    const save = newGame(1, gameConfig, 0);
    const next = setPartySlot(save, 1, save.heroes[0].id);
    expect(next.party[1]).toBe(save.heroes[0].id);
    expect(save.party[1]).toBeNull(); // original untouched
  });

  it('clears a slot with null', () => {
    const save = newGame(1, gameConfig, 0);
    expect(setPartySlot(save, 0, null).party[0]).toBeNull();
  });

  it('rejects an out-of-range slot', () => {
    const save = newGame(1, gameConfig, 0);
    expect(() => setPartySlot(save, 9, save.heroes[0].id)).toThrow();
  });

  it('rejects an unowned hero', () => {
    const save = newGame(1, gameConfig, 0);
    expect(() => setPartySlot(save, 0, 'nope')).toThrow();
  });
});
