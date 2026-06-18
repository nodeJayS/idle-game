import type { GameConfig, HeroInstance, SaveState } from './types';

/** Current save schema version. Bump when SaveState shape changes. */
export const SAVE_VERSION = 1;

/** The hero every new player starts with. */
export const STARTER_HERO_DEF = 'warrior_basic';

/**
 * A fresh game: 1 basic Warrior in party slot 0, three empty slots.
 * `now` is passed in (not read from the clock) to keep game-core pure/testable.
 */
export function newGame(seed: number, cfg: GameConfig, now: number): SaveState {
  const def = cfg.heroes[STARTER_HERO_DEF];
  if (!def) throw new Error(`newGame: missing starter hero def "${STARTER_HERO_DEF}"`);

  const warrior: HeroInstance = {
    id: 'h1',
    defId: def.defId,
    level: 1,
    xp: 0,
    equipped: {},
    skillLoadout: [...def.skills],
  };

  return {
    version: SAVE_VERSION,
    rngSeed: seed >>> 0,
    rngCursor: 0,
    heroes: [warrior],
    party: [warrior.id, null, null, null],
    inventory: [],
    currencies: { gold: 0 },
    progress: { highestRiftTier: 0, currentRiftTier: 1, accountLevel: 1 },
    lastClaimAt: now,
  };
}

/** Bring an older/raw save up to the current version. */
export function migrate(raw: unknown): SaveState {
  const s = raw as SaveState | null;
  if (!s || typeof s !== 'object') throw new Error('migrate: invalid save');
  if (s.version !== SAVE_VERSION) {
    throw new Error(`migrate: unsupported save version ${s.version} (expected ${SAVE_VERSION})`);
  }
  return s;
}

export function serialize(save: SaveState): string {
  return JSON.stringify(save);
}

export function deserialize(json: string): SaveState {
  return migrate(JSON.parse(json));
}
