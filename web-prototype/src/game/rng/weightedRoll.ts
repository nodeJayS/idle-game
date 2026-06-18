/**
 * The ONE randomness engine. Used by loot now; gacha (pity/rates) will sit on
 * top of this exact module later. Deterministic: same (seed, cursor) => same
 * sequence, so the server can re-validate the client and loot is unit-testable.
 */

export interface Rng {
  /** Returns a float in [0, 1) and advances the cursor. */
  next(): number;
  /** How many values have been consumed from the seed so far. */
  readonly cursor: number;
}

/**
 * mulberry32 — small, fast, good-enough PRNG for game loot.
 * `cursor` lets you resume a stream deterministically from a SaveState.
 */
export function makeRng(seed: number, cursor = 0): Rng {
  let state = (seed >>> 0) + Math.imul(cursor, 0x6d2b79f5);
  let count = cursor;

  return {
    next() {
      count++;
      state |= 0;
      state = (state + 0x6d2b79f5) | 0;
      let t = Math.imul(state ^ (state >>> 15), 1 | state);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    },
    get cursor() {
      return count;
    },
  };
}

/** Weighted pick from a list of { item, weight }. weight must be > 0. */
export function weightedPick<T>(rng: Rng, entries: ReadonlyArray<{ item: T; weight: number }>): T {
  if (entries.length === 0) throw new Error('weightedPick: empty entries');
  let total = 0;
  for (const e of entries) total += e.weight;
  let roll = rng.next() * total;
  for (const e of entries) {
    roll -= e.weight;
    if (roll < 0) return e.item;
  }
  return entries[entries.length - 1].item; // float safety net
}

/** Integer in [min, max] inclusive. */
export function randInt(rng: Rng, min: number, max: number): number {
  return min + Math.floor(rng.next() * (max - min + 1));
}

/** Float in [min, max). */
export function randRange(rng: Rng, min: number, max: number): number {
  return min + rng.next() * (max - min);
}
