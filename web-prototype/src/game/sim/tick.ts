import type { CombatEvent, CombatState, GameConfig } from '../types';
import type { Rng } from '../rng/weightedRoll';
import { stepCombat } from '../systems/combat';

/**
 * Fixed-timestep driver for the combat sim. Framework-free: it returns the new
 * state + accumulated events; the caller (app layer) decides what to render and
 * when to persist. Using a fixed dt keeps the sim deterministic regardless of
 * the display frame rate; the renderer interpolates between steps.
 */
export const FIXED_DT_MS = 1000 / 30; // 30 sim steps per second

/** Run as many fixed steps as `elapsedMs` allows; returns leftover + events. */
export function advanceSim(
  state: CombatState,
  elapsedMs: number,
  cfg: GameConfig,
  rng: Rng,
): { state: CombatState; events: CombatEvent[]; leftoverMs: number } {
  let acc = elapsedMs;
  let cur = state;
  const events: CombatEvent[] = [];
  while (acc >= FIXED_DT_MS && cur.status === 'running') {
    const res = stepCombat(cur, FIXED_DT_MS, cfg, rng);
    cur = res.state;
    events.push(...res.events);
    acc -= FIXED_DT_MS;
  }
  return { state: cur, events, leftoverMs: acc };
}
