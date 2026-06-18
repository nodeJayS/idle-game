/**
 * game-core type definitions.
 *
 * THE RULE: this folder (src/game) is engine-agnostic. No imports of React,
 * Pixi, the DOM, or Supabase anywhere under src/game. Web client, a future
 * mobile client, and a future authoritative server all import these same types.
 *
 * Three kinds of state, never mixed:
 *   - SaveState   : persisted to Supabase
 *   - GameConfig  : static content, injected (not imported globally)
 *   - CombatState : transient sim state, never saved
 */

// ----------------------------------------------------------------------------
// Identifiers & primitives
// ----------------------------------------------------------------------------
export type HeroId = string;
export type ItemId = string;
export type CurrencyId = 'gold'; // later: | 'gems' | 'tickets' | 'shards'

export type Rarity = 'normal' | 'magic' | 'rare' | 'unique';
export type EquipSlot = 'weapon' | 'helm' | 'chest' | 'boots' | 'ring' | 'amulet';
export type StatKey = 'hp' | 'atk' | 'def' | 'spd' | 'critChance' | 'critDmg';
export type StatBlock = Record<StatKey, number>;

export interface Vec2 {
  x: number;
  y: number;
}

// ----------------------------------------------------------------------------
// Save state (serialized to Supabase)
// ----------------------------------------------------------------------------
export interface SaveState {
  version: number; // schema version, for migrations
  rngSeed: number; // master deterministic seed
  rngCursor: number; // # rolls consumed (advance, never reseed)
  heroes: HeroInstance[]; // owned heroes (party + bench)
  party: (HeroId | null)[]; // fixed length 4; null = empty slot
  inventory: Item[]; // unequipped items
  currencies: Record<CurrencyId, number>;
  progress: ProgressState;
  lastClaimAt: number; // epoch ms (server-validated later)
}

export interface ProgressState {
  highestRiftTier: number;
  currentRiftTier: number;
  accountLevel: number;
}

export interface HeroInstance {
  id: HeroId;
  defId: string; // -> GameConfig.heroes
  level: number;
  xp: number;
  equipped: Partial<Record<EquipSlot, ItemId>>;
  skillLoadout: string[]; // chosen skillDef ids
}

export interface Affix {
  stat: StatKey;
  value: number;
}

export interface Item {
  id: ItemId;
  baseId: string; // -> GameConfig.itemBases
  rarity: Rarity;
  itemLevel: number; // scales affix magnitude
  affixes: Affix[];
}

// ----------------------------------------------------------------------------
// Config (static content, injected as a param)
// ----------------------------------------------------------------------------
export interface GameConfig {
  heroes: Record<string, HeroDef>;
  itemBases: Record<string, ItemBaseDef>;
  affixPool: AffixDef[];
  monsters: Record<string, MonsterDef>;
  rifts: RiftTierDef[];
  skills: Record<string, SkillDef>;
  balance: BalanceConstants;
}

export interface HeroDef {
  defId: string;
  name: string;
  class: string;
  role: 'melee' | 'ranged' | 'support';
  baseStats: StatBlock;
  growthPerLevel: StatBlock;
  skills: string[];
  sprite: string; // renderer hint only
}

export interface ItemBaseDef {
  baseId: string;
  slot: EquipSlot;
  baseStats: Partial<StatBlock>;
  allowedAffixes: StatKey[];
  sprite: string;
}

export interface AffixDef {
  stat: StatKey;
  weight: number; // for weighted pick
  valuePerItemLevel: { min: number; max: number };
  rarityFloor: Rarity; // min rarity this affix can appear on
}

export interface MonsterDef {
  id: string;
  name: string;
  baseStats: StatBlock;
  lootTableId: string;
  xpReward: number;
  goldReward: number;
  sprite: string;
}

export interface RiftTierDef {
  tier: number;
  monsterLevel: number;
  packCount: number;
  bossId: string;
  dropRateMult: number;
  affixItemLevel: number;
}

export type SkillTargeting = 'nearest' | 'lowestHp' | 'self' | 'aoe';

export interface SkillDef {
  id: string;
  name: string;
  cooldownMs: number;
  range: number;
  targeting: SkillTargeting;
  effect: unknown; // damage/heal/buff descriptor — design later
  sprite?: string;
}

export interface BalanceConstants {
  idleCapHours: number;
  offlineRate: number; // e.g. 0.8
  xpCurve: (level: number) => number;
  goldPerSec: (tier: number) => number;
  xpPerSec: (tier: number) => number;
  lootRollsPerHour: (tier: number) => number;
}

// ----------------------------------------------------------------------------
// Runtime / combat sim state (transient, never saved)
// ----------------------------------------------------------------------------
export type Team = 'party' | 'enemy';

export type EntityRef =
  | { kind: 'hero'; heroId: HeroId }
  | { kind: 'monster'; defId: string };

export interface CombatEntity {
  id: string;
  team: Team;
  pos: Vec2;
  stats: StatBlock;
  hp: number;
  maxHp: number;
  targetId?: string;
  cooldowns: Record<string, number>; // skillId -> ms remaining
  ref: EntityRef;
}

export type CombatStatus = 'running' | 'won' | 'lost';

export interface CombatState {
  timeMs: number;
  tier: number;
  entities: CombatEntity[];
  status: CombatStatus;
  pendingLoot: Item[]; // accumulated drops this run
}

/** Events emitted by the sim for the renderer to react to (juice). */
export type CombatEvent =
  | { type: 'hit'; sourceId: string; targetId: string; amount: number; crit: boolean }
  | { type: 'death'; entityId: string }
  | { type: 'lootDrop'; item: Item; at: Vec2 }
  | { type: 'levelUp'; heroId: HeroId; newLevel: number }
  | { type: 'waveCleared'; tier: number }
  | { type: 'bossDefeated'; tier: number };

// ----------------------------------------------------------------------------
// Shared helpers
// ----------------------------------------------------------------------------
export type StatDelta = Partial<Record<StatKey, number>>;

export interface IdleYield {
  gold: number;
  xp: number;
  loot: Item[];
  elapsedSec: number;
  wasCapped: boolean;
}
