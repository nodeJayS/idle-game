import type { GameConfig, HeroInstance, SaveState } from '../types';

/** Grant XP to a hero, applying level-ups via cfg.balance.xpCurve. Implement M2. */
export function grantXp(_hero: HeroInstance, _amount: number, _cfg: GameConfig): HeroInstance {
  throw new Error('not implemented: grantXp');
}

/** Set the rift tier the player is currently running. Implement M3. */
export function setRiftTier(_save: SaveState, _tier: number): SaveState {
  throw new Error('not implemented: setRiftTier');
}

/** Called on a win: bump highestRiftTier and advance current. Implement M3. */
export function onRiftCleared(_save: SaveState, _tier: number): SaveState {
  throw new Error('not implemented: onRiftCleared');
}
