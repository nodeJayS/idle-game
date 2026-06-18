import type { GameConfig, IdleYield, SaveState } from '../types';

/**
 * Offline = MATH, never a real timer. Implement in M4.
 *   elapsed = min(now - save.lastClaimAt, cap)
 *   yield   = rate(highestRiftTier) * elapsed   (loot rolled via rng module)
 */

/** Compute (but do not apply) what the player earned while away. */
export function computeIdleYield(_save: SaveState, _now: number, _cfg: GameConfig): IdleYield {
  throw new Error('not implemented: computeIdleYield');
}

/** Apply a yield to the save (pure: returns a new SaveState, stamps lastClaimAt). */
export function applyIdleYield(_save: SaveState, _yield: IdleYield): SaveState {
  throw new Error('not implemented: applyIdleYield');
}
