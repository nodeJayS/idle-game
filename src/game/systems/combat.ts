import type { CombatEvent, CombatState, GameConfig, HeroInstance } from '../types';
import type { Rng } from '../rng/weightedRoll';

/**
 * Deterministic auto-battle. Implement in M1.
 *
 * v1 model (intentionally light): each entity has an iso position, walks toward
 * its target, and auto-attacks / auto-casts when in range and off cooldown.
 * Spatial enough to LOOK like an ARPG, simple enough to stay deterministic and
 * cheap. The renderer interpolates positions between fixed steps for smooth motion.
 */

/** Build the initial combat state for a rift tier from the current party. */
export function initCombat(
  _party: HeroInstance[],
  _tier: number,
  _cfg: GameConfig,
  _rng: Rng,
): CombatState {
  throw new Error('not implemented: initCombat');
}

/**
 * Advance the sim by a fixed dt. Pure & deterministic. Returns the next state
 * plus the events that happened this step (for the renderer's juice layer).
 * Call repeatedly from a fixed-timestep loop.
 */
export function stepCombat(
  _state: CombatState,
  _dtMs: number,
  _cfg: GameConfig,
  _rng: Rng,
): { state: CombatState; events: CombatEvent[] } {
  throw new Error('not implemented: stepCombat');
}
