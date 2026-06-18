import type { GameConfig } from '../types';
import { balance } from './balance';
import { heroes } from './heroes';
import { itemBases } from './itemBases';
import { affixPool } from './affixes';
import { monsters } from './monsters';
import { rifts } from './rifts';
import { skills } from './skills';

/**
 * The assembled static content set. Pass this `cfg` into game-core functions.
 * Injecting it (rather than importing globally) keeps logic swappable and lets
 * a server share the exact same config.
 */
export const gameConfig: GameConfig = {
  heroes,
  itemBases,
  affixPool,
  monsters,
  rifts,
  skills,
  balance,
};
