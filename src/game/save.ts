import type { GameConfig, SaveState } from './types';

/** Current save schema version. Bump when SaveState shape changes. */
export const SAVE_VERSION = 1;

/**
 * A fresh game: 1 basic Warrior in party slot 0, three empty slots.
 * Implement once the starting Warrior def exists in config.
 */
export function newGame(_seed: number, _cfg: GameConfig): SaveState {
  throw new Error('not implemented: newGame');
}

/** Bring an older/raw save up to the current version. Implement alongside changes. */
export function migrate(_raw: unknown): SaveState {
  throw new Error('not implemented: migrate');
}

export function serialize(save: SaveState): string {
  return JSON.stringify(save);
}

export function deserialize(json: string): SaveState {
  return migrate(JSON.parse(json));
}
