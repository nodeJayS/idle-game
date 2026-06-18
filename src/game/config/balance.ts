import type { BalanceConstants } from '../types';

/**
 * ALL tunable numbers live here. This is the file you'll edit constantly to
 * balance the economy — keep gameplay logic out of it and tuning out of logic.
 * Values below are placeholders to be tuned with a balance sim (see docs).
 */
export const balance: BalanceConstants = {
  idleCapHours: 12,
  offlineRate: 0.8,
  xpCurve: (level) => Math.floor(100 * Math.pow(1.15, level - 1)),
  goldPerSec: (tier) => Math.floor(5 * Math.pow(1.18, tier)),
  xpPerSec: (tier) => Math.floor(3 * Math.pow(1.15, tier)),
  lootRollsPerHour: (tier) => 20 + tier * 5,
};
