import { describe, it, expect } from 'vitest';
import { makeRng, weightedPick, randInt } from './weightedRoll';

describe('rng', () => {
  it('is deterministic for the same seed', () => {
    const a = makeRng(12345);
    const b = makeRng(12345);
    const seqA = [a.next(), a.next(), a.next()];
    const seqB = [b.next(), b.next(), b.next()];
    expect(seqA).toEqual(seqB);
  });

  it('can resume a stream from a cursor', () => {
    const full = makeRng(999);
    full.next();
    full.next();
    const expected = full.next();

    const resumed = makeRng(999, 2); // skip the first two
    expect(resumed.next()).toEqual(expected);
  });

  it('weightedPick respects weights (statistically)', () => {
    const rng = makeRng(42);
    const entries = [
      { item: 'common', weight: 90 },
      { item: 'rare', weight: 10 },
    ];
    const counts: Record<string, number> = { common: 0, rare: 0 };
    for (let i = 0; i < 10000; i++) counts[weightedPick(rng, entries)]++;
    // ~9000 / ~1000 — generous bounds to stay non-flaky
    expect(counts.common).toBeGreaterThan(8500);
    expect(counts.rare).toBeGreaterThan(700);
    expect(counts.rare).toBeLessThan(1300);
  });

  it('randInt stays in range', () => {
    const rng = makeRng(7);
    for (let i = 0; i < 1000; i++) {
      const v = randInt(rng, 3, 6);
      expect(v).toBeGreaterThanOrEqual(3);
      expect(v).toBeLessThanOrEqual(6);
    }
  });
});
