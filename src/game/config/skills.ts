import type { SkillDef } from '../types';

/** Skill definitions (static). M1+ — the `effect` descriptor shape is TBD. */
export const skills: Record<string, SkillDef> = {
  cleave: {
    id: 'cleave',
    name: 'Cleave',
    cooldownMs: 3000,
    range: 1.5,
    targeting: 'nearest',
    effect: { kind: 'damage', mult: 1.4 }, // shape to be finalized in M1
    sprite: 'cleave',
  },
};
