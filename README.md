# Idle ARPG

A cute-pixel-art **idle ARPG** — Diablo 3 / Path of Exile–style loot & build depth,
with a Minecraft-friendly visual surface. A 4-hero party auto-clears isometric
dungeons, monsters drop gear, you build out your party and push higher difficulty.
All progress accrues while you're away.

> Status: **scaffold + game-core stubs.** Gameplay systems are stubbed and built
> milestone by milestone. Gacha is intentionally deferred (the architecture
> supports adding it later with no rewrite).

## Tech stack
- **Vite + React + TypeScript** — app shell
- **PixiJS** — 2.5D isometric pixel renderer (nearest-neighbor scaling)
- **Zustand** — app-layer state store
- **Supabase** — cloud saves (M5)
- **Vitest** — tests

## Architecture: the one rule
All combat / loot / idle / progression logic lives in **`src/game`** (`game-core`):
**pure TypeScript with zero imports of React, Pixi, the DOM, or Supabase.**
The renderer and UI only *read* state. This boundary is what keeps three things
additive instead of rewrites:
- swapping the Pixi renderer for 3D (react-three-fiber) later,
- porting to mobile (React Native + Expo) later,
- adding a gacha layer later (it plugs into `acquisition.ts`).

```
src/
  game/            # game-core — PURE, framework-free
    config/        # static content (heroes, items, affixes, monsters, rifts, skills, balance)
    systems/       # combat, loot, stats, idle, progression, inventory, acquisition
    rng/           # the one seeded weighted-roll engine (loot now, gacha later)
    sim/           # fixed-timestep driver for the combat sim
    types.ts       # all shared types
    save.ts        # versioned save + migrations
    index.ts       # public surface
  state/           # app-layer Zustand store (React binding)
  render/pixi/     # the ONLY place Pixi is allowed
  components/      # React UI (HUD, inventory, gear-compare, rift select)
  lib/             # supabase client
docs/              # game plan + game-core design
```

## Getting started
```bash
npm install
npm run dev        # start the dev server
npm run typecheck  # tsc --noEmit
npm test           # run unit tests
```

Cloud saves are optional locally — copy `.env.example` to `.env` and fill in
Supabase keys when you reach M5.

## Milestones
| | Milestone | Done = |
|---|---|---|
| M0 | Skeleton | Pixi renders a static iso scene + Warrior sprite |
| M1 | Auto-combat | deterministic `stepCombat` clears a pack + boss |
| M2 | Loot | drops w/ rarity + affixes, inventory, equip → stats recompute |
| M3 | Rifts | difficulty tiers scale monsters + loot |
| M4 | Idle | offline yield as math + claim modal |
| M5 | Persistence | Supabase save/load + RLS |
| M6 | Feel pass | pixel juice, gear-compare arrows, number formatting |

See [`docs/idle-gacha-game-plan.md`](docs/idle-gacha-game-plan.md) and
[`docs/game-core-design.md`](docs/game-core-design.md) for the full design.
