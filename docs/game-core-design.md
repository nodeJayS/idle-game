# `game-core` Design Sketch (v1)

> The engine-agnostic heart of the idle ARPG. Pure TypeScript. **Zero** imports of React, Pixi, DOM, or anything renderer/UI/network-specific.
> Everything the web client, a future mobile client, and a future authoritative server need lives here and is called the same way by all of them.
>
> This is a **design sketch** — types + function signatures + contracts, not implementations.

---

## 0. Art direction → tech implications

**Style: cute/cozy pixel art + PoE-depth systems (Minecraft-friendly surface, PoE grind underneath).** Proven combo (Loop Hero, Moonlighter, Core Keeper).

What it buys the renderer (Pixi side, *not* `game-core`):
- **Tiny low-res sprite atlases** + nearest-neighbor scaling (`roundPixels`, `PIXI.SCALE_MODES.NEAREST`) → razor-sharp at any zoom, trivial to draw, easy to add content.
- **Small palette + small textures** → almost no GPU/memory pressure → runs on bad phones.
- **Isometric pixel tiles** for the dungeon floor; pixel character + monster sprites with a few animation frames.
- Readability: cute, high-contrast sprites make loot beams, rarity colors, and damage numbers pop.

`game-core` is unaffected by art — it only ever stores a `sprite: string` *hint* that the renderer interprets. That's the whole point of the boundary.

---

## 1. Design principles (the rules that keep everything open)

1. **Pure & deterministic.** Given the same state + seed, every function produces the same result. Lets the server re-validate the client later, and lets you unit-test loot/combat.
2. **Three kinds of state, never mixed** (§2): *Save state* (persisted), *Config* (static content), *Runtime sim state* (transient, never saved).
3. **Config is injected, not imported.** Functions take `cfg: GameConfig` as a param. Swap balance/content without touching logic; server and client share one config.
4. **Pure reducers.** State-changing functions return a *new* state object; they never mutate in place. (Plays nicely with Zustand + undo + server replay.)
5. **Seeded RNG, advanced not reseeded.** One master seed + a cursor; every roll advances the cursor. Reproducible and replayable.
6. **Versioned saves + migrations.** `SaveState.version` so you can evolve the schema without bricking saves.
7. **Renderer reads, never writes game rules.** Pixi consumes `CombatState` + events to draw; it never decides damage or drops.

---

## 2. The three kinds of state

| Kind | Lives where | Saved? | Examples |
|------|-------------|--------|----------|
| **SaveState** | Zustand store → Supabase | ✅ | heroes owned, gear, currencies, rift progress, `lastClaimAt`, rng cursor |
| **GameConfig** | static TS/JSON (or Supabase content tables) | ❌ (it's content) | hero defs, item bases, affix pool, monsters, rift tiers, balance |
| **CombatState** | the sim loop, frame to frame | ❌ (transient) | live entity positions, hp, cooldowns, pending loot |

---

## 3. Type sketches

### 3.1 Save state (serialized)
```ts
type HeroId = string;
type ItemId = string;
type CurrencyId = 'gold'; // later: | 'gems' | 'tickets' | 'shards'

interface SaveState {
  version: number;                       // schema version, for migrations
  rngSeed: number;                       // master deterministic seed
  rngCursor: number;                     // # rolls consumed (advance, don't reseed)
  heroes: HeroInstance[];                // owned heroes (party + bench)
  party: (HeroId | null)[];              // fixed length 4; null = empty slot
  inventory: Item[];                     // unequipped items
  currencies: Record<CurrencyId, number>;
  progress: {
    highestRiftTier: number;
    currentRiftTier: number;
    accountLevel: number;
  };
  lastClaimAt: number;                   // epoch ms (server-validated later)
}
```

### 3.2 Heroes & items
```ts
type Rarity   = 'normal' | 'magic' | 'rare' | 'unique';
type EquipSlot = 'weapon' | 'helm' | 'chest' | 'boots' | 'ring' | 'amulet';
type StatKey  = 'hp' | 'atk' | 'def' | 'spd' | 'critChance' | 'critDmg';
type StatBlock = Record<StatKey, number>;

interface HeroInstance {
  id: HeroId;
  defId: string;                         // -> GameConfig.heroes
  level: number;
  xp: number;
  equipped: Partial<Record<EquipSlot, ItemId>>;
  skillLoadout: string[];                // chosen skillDef ids
}

interface Affix { stat: StatKey; value: number; }

interface Item {
  id: ItemId;
  baseId: string;                        // -> GameConfig.itemBases
  rarity: Rarity;
  itemLevel: number;                     // scales affix magnitude
  affixes: Affix[];
}
```

### 3.3 Config (static content)
```ts
interface GameConfig {
  heroes:    Record<string, HeroDef>;
  itemBases: Record<string, ItemBaseDef>;
  affixPool: AffixDef[];
  monsters:  Record<string, MonsterDef>;
  rifts:     RiftTierDef[];
  skills:    Record<string, SkillDef>;
  balance:   BalanceConstants;
}

interface HeroDef {
  defId: string; name: string;
  class: string; role: 'melee' | 'ranged' | 'support';
  baseStats: StatBlock; growthPerLevel: StatBlock;
  skills: string[];
  sprite: string;                        // renderer hint only
}

interface ItemBaseDef {
  baseId: string; slot: EquipSlot;
  baseStats: Partial<StatBlock>;
  allowedAffixes: StatKey[];
  sprite: string;
}

interface AffixDef {
  stat: StatKey; weight: number;         // for weighted pick
  valuePerItemLevel: { min: number; max: number };
  rarityFloor: Rarity;                   // min rarity this affix can appear on
}

interface MonsterDef {
  id: string; name: string;
  baseStats: StatBlock;
  lootTableId: string; xpReward: number; goldReward: number;
  sprite: string;
}

interface RiftTierDef {
  tier: number; monsterLevel: number;
  packCount: number; bossId: string;
  dropRateMult: number; affixItemLevel: number;
}

interface SkillDef {
  id: string; name: string;
  cooldownMs: number; range: number;
  targeting: 'nearest' | 'lowestHp' | 'self' | 'aoe';
  effect: unknown;                       // damage/heal/buff descriptor (design later)
  sprite?: string;
}

interface BalanceConstants {
  idleCapHours: number; offlineRate: number;     // e.g. 0.8
  xpCurve: (level: number) => number;
  goldPerSec: (tier: number) => number;
  xpPerSec:   (tier: number) => number;
  lootRollsPerHour: (tier: number) => number;
  // ...all tunable numbers live here
}
```

### 3.4 Runtime / combat sim state (transient)
```ts
interface Vec2 { x: number; y: number; }   // position on the iso plane

interface CombatEntity {
  id: string;
  team: 'party' | 'enemy';
  pos: Vec2;
  stats: StatBlock; hp: number; maxHp: number;
  targetId?: string;
  cooldowns: Record<string, number>;       // skillId -> ms remaining
  ref: { kind: 'hero'; heroId: HeroId } | { kind: 'monster'; defId: string };
}

interface CombatState {
  timeMs: number;
  tier: number;
  entities: CombatEntity[];
  status: 'running' | 'won' | 'lost';
  pendingLoot: Item[];                      // accumulated drops this run
}

// What the renderer listens to (drives juice: numbers, beams, sfx)
type CombatEvent =
  | { type: 'hit'; sourceId: string; targetId: string; amount: number; crit: boolean }
  | { type: 'death'; entityId: string }
  | { type: 'lootDrop'; item: Item; at: Vec2 }
  | { type: 'levelUp'; heroId: HeroId; newLevel: number }
  | { type: 'waveCleared'; tier: number }
  | { type: 'bossDefeated'; tier: number };
```

---

## 4. Public API (by module)

### `rng/` — the one randomness engine (loot now, gacha later)
```ts
interface Rng { next(): number; cursor: number; }      // next() in [0,1)
function makeRng(seed: number, cursor: number): Rng;
function weightedPick<T>(rng: Rng, entries: { item: T; weight: number }[]): T;
```

### `stats.ts` — derive effective stats
```ts
// base + growth*level + sum of equipped base stats & affixes
function computeHeroStats(hero: HeroInstance, cfg: GameConfig, items: Item[]): StatBlock;
function computePartyPower(save: SaveState, cfg: GameConfig): number;  // single "power" number for UI/matchmaking
```

### `loot.ts` — the dopamine engine
```ts
function rollDrop(rng: Rng, monster: MonsterDef, tier: RiftTierDef, cfg: GameConfig): Item | null;
function rollItem(rng: Rng, baseId: string, itemLevel: number, rarity: Rarity, cfg: GameConfig): Item;
function rollAffixes(rng: Rng, base: ItemBaseDef, count: number, itemLevel: number, cfg: GameConfig): Affix[];
```

### `combat.ts` — deterministic auto-battle
```ts
function initCombat(party: HeroInstance[], tier: number, cfg: GameConfig, rng: Rng): CombatState;

// fixed-timestep reducer: pure, deterministic. Renderer calls it repeatedly.
function stepCombat(
  state: CombatState, dtMs: number, cfg: GameConfig, rng: Rng
): { state: CombatState; events: CombatEvent[] };
```
Combat model (v1, intentionally light): entities have an iso position, walk toward their target, auto-attack/auto-skill when in range & off cooldown. Spatial but simple — enough to *look* like an ARPG while staying deterministic and cheap. Renderer interpolates positions between fixed steps for smooth motion.

### `idle.ts` — offline = math, never a real timer
```ts
interface IdleYield { gold: number; xp: number; loot: Item[]; elapsedSec: number; wasCapped: boolean; }
function computeIdleYield(save: SaveState, now: number, cfg: GameConfig): IdleYield;  // rolls real loot via rng
function applyIdleYield(save: SaveState, y: IdleYield): SaveState;                    // pure; also stamps lastClaimAt
```

### `progression.ts`
```ts
function grantXp(hero: HeroInstance, amount: number, cfg: GameConfig): HeroInstance;
function setRiftTier(save: SaveState, tier: number): SaveState;
function onRiftCleared(save: SaveState, tier: number): SaveState;   // bump highest, advance current
```

### `inventory.ts` — equip / compare (the ARPG moment-to-moment)
```ts
function addItems(save: SaveState, items: Item[]): SaveState;
function equipItem(save: SaveState, heroId: HeroId, itemId: ItemId): SaveState;
function unequipItem(save: SaveState, heroId: HeroId, slot: EquipSlot): SaveState;

type StatDelta = Partial<Record<StatKey, number>>;
function compareForHero(hero: HeroInstance, item: Item, cfg: GameConfig): StatDelta;  // drives green▲/red▼ UI
```

### `acquisition.ts` — the gacha plug point
```ts
// v1: progression rewards call this directly with a chosen defId.
// Later: gacha rolls a defId via weightedPick(), then calls the SAME function.
// => adding gacha changes nothing downstream of acquisition.
function acquireHero(save: SaveState, defId: string, cfg: GameConfig): SaveState;
function setPartySlot(save: SaveState, slot: number, heroId: HeroId | null): SaveState;
```

### `save.ts` — versioning
```ts
function newGame(seed: number, cfg: GameConfig): SaveState;          // starts with 1 Warrior in slot 0
function migrate(raw: unknown): SaveState;                           // bring old saves up to current version
function serialize(save: SaveState): string;
function deserialize(json: string): SaveState;
```

---

## 5. How it all wires together (outside `game-core`)

```
Supabase  ──load/save──  Zustand store (holds SaveState)
                               │  selectors (computeHeroStats, party power, …)
                               ▼
        React UI  ◄── reads ──┤  (inventory, gear-compare, rift select, HUD)
                               │
        sim/tick.ts  ──────────┘  fixed-timestep loop:
           every dt: { state, events } = stepCombat(state, dt, cfg, rng)
             • push events → renderer (juice) & loot → save on win
             • on 'won': onRiftCleared() + applyIdleYield() etc.
                               │
        Pixi renderer  ◄── reads CombatState each frame, interpolates positions,
                           plays events (damage numbers, loot beams, sfx)
```

Key boundaries:
- **`game-core` knows nothing about Pixi, React, or Supabase.**
- **Renderer only reads** `CombatState` + `CombatEvent[]`.
- **Server later** imports the exact same `combat.ts` / `loot.ts` / `idle.ts` to re-validate or fully own the sim — no rewrite.

---

## 6. Why this scales to everything we deferred

- **Gacha** → just a `weightedPick` over hero defIds feeding the existing `acquireHero`. Pity/rates sit on top of `rng/`. Currencies map already supports `gems`/`tickets`.
- **3D renderer** → swap Pixi for react-three-fiber; `game-core` and `CombatState` are unchanged.
- **Mobile (RN/Expo)** → import the same `game-core`; only the renderer + shell differ.
- **Server-authoritative** → run `stepCombat`/`computeIdleYield` on the server with the client's seed; compare results. Deterministic design makes this cheap.

---

## 7. Open questions to resolve next
- **Combat fidelity**: keep the light "walk-to-target + auto-attack" model, or add skill shapes/AoE positioning in v1? (Recommend: light now.)
- **Hero acquisition pre-gacha**: milestone unlocks vs. crafting heroes from drops?
- **First content set**: 1 starting Warrior + ~3 monster types + ~5 item bases + ~8 affixes is enough for M0–M2.
- Then: draft `balance.ts` numbers and the Supabase `saves` table + RLS.
