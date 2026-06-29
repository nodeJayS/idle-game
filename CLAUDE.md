# Idle ARPG — working context

A cozy low-poly **3D idle ARPG** (Diablo/PoE-style loot & build depth, *Tunic*-ish
look). A 3-hero party auto-clears dungeons, monsters drop gear, you build the party
and push higher difficulty; progress accrues while away. Built in **Unity (C#)**.
Gacha and global/live-service are deferred but the architecture keeps the door open.

For the full design (loops, economy, data model, gacha/live-service vision) see
[`docs/game-design.md`](docs/game-design.md). This file is the orientation Claude
loads every session — keep it short and current.

## The one architecture rule
All combat / loot / idle / progression logic lives in **`GameCore` — pure C#, zero
`UnityEngine` references.** Unity is a client that only *reads* simulation state; a
.NET server can later reuse the exact same `GameCore` for authority. **Never let game
logic leak into MonoBehaviours** — they spawn/animate/poll, they don't decide rules.

## Tech stack
- **Sim:** `GameCore` — pure C# library, `net8.0`, tested with `dotnet test` (xUnit).
- **Client:** Unity 6 LTS, 3D **URP**. The sim lives in `unity/Assets/GameCore/`
  under a no-engine-refs `GameCore.asmdef`; game-side MonoBehaviours live in
  `unity/Assets/Game/` (`IdleGame.Game`).
- **Persistence (today):** local JSON via `System.Text.Json` (the one host adapter).
- **Later:** ASP.NET Core server referencing `GameCore` + Postgres + Redis; Supabase
  was the web-era plan. See live-service roadmap below.

## Repo layout
```
unity/Assets/GameCore/   THE sim — pure C#, no engine refs. SINGLE SOURCE OF TRUTH.
unity/Assets/Game/       MonoBehaviours, read-only client (Bootstrap, CombatView, CameraRig,
                         EquipmentView=Heroes hub, InventoryView, ChatPanel, TopBar, UiKit,
                         StatDisplay, CombatJuice, DeathFx/TransientFx/Projectile FX)
gamecore/GameCore.Tests/ xUnit tests — compile the SAME Assets/GameCore sources via a
                         csproj glob (no copy). + gamecore/Adapters/Persistence.cs.
docs/game-design.md      the durable what/why
```
**Edit sim code in `unity/Assets/GameCore/`.** The test project compiles those exact
files (`..\unity\Assets\GameCore\**\*.cs`), so there's no copy and nothing to sync.

## Design principles (what keeps it scalable)
1. **Pure & deterministic.** Same state + seed ⇒ same result. Enables unit tests and
   future server re-validation.
2. **Three kinds of state, never mixed:**
   - **`SaveState`** — persisted: heroes, gear, currencies, progress, `lastClaimAt`,
     rng seed + cursor.
   - **`GameConfig`** — static content (injected, not imported): hero defs, item
     bases, affixes, monsters, stages, balance. `GameConfig.Default()` today.
   - **`CombatState`** — transient sim, never saved: live entities, hp, cooldowns.
3. **Config is injected** as a `cfg` param — swap balance/content without touching logic.
4. **Save reducers are pure** (return new state); the combat sim mutates `CombatState`
   in place per fixed step for performance, but stays deterministic.
5. **Seeded RNG (`Rng`, mulberry32), advanced via a cursor — never reseeded.**
6. **Versioned saves + `Migrate`** so the schema can evolve.
7. **Renderer reads, never writes game rules.**

## Build & test
- Sim (fast inner loop, no Unity): `dotnet test gamecore/GameCore.Tests`
- Client: open `unity/` in Unity 6 LTS and press **Play** — `Bootstrap` builds the
  scene in code (camera/light/ground) and `CombatView` drives the auto-battle.
  Play-mode can't be driven headlessly; visual checks are manual.

## Status (331 tests passing)

**Phase A — core loop (M0–M9) ✅** — deterministic auto-combat; loot (rarity + affixes, equip →
stat recompute); per-hero leveling; farm-zone stage ladder with 60s mini/major boss gates + tiered
rates; idle accrual + claim modal; save/load + menu; feel pass; warrior + (ranged/AoE) magician;
chat/feed panel.

**Phase B — depth (in progress):**
- **M10 multi-character ✅** — mana; 9 per-hero equip slots from one shared bag; inventory cap +
  auto-salvage → `scrap`; boss-only Unique/Legendary; Party HUD; rarity tiles; `StatDisplay`.
- **M11 skills ✅** — sim damage/AoE/heal/buff on mana + `AtkSpd` cooldowns (heroes + bosses);
  `Spd`→`MoveSpd`/`AtkSpd`. Client: skill FX, heal numbers, lunge tell, hit-recoil/death FX.
- **Heroes hub ✅** — one `EquipmentView` screen: hero rail + Equipment/Skills(read-only)/Stats
  tabs + Field/Bench + body doll + Salvage UI (`RosterView` retired).
- **Roster/party ✅** — start solo Warrior; `GameConfig.HeroUnlocks` grant heroes on stage clear
  (3→Magician) via `Party.AcquireHero`; `Combat.ReconcileParty` live-swaps; party moves as a group.
- **World/combat feel ✅** — geometric difficulty; 100-stage ladder; big open field (`MapHalf
  200/140`); party-relative pack spawning; follow `CameraRig` (wheel zoom + shake); top-centre
  stage nav + boss-clear popup.
- **Art direction — Tunic pivot ✅** — `TunicSurface` shader (normal-driven grass-top/dirt-side
  blend + inked facet edges + crisp light); faceted vertex-coloured `Ground` + props (`Scenery`);
  clean lighting + procedural dappled light cookie (`Bootstrap`/`LightCookieScroll`). Heroes are
  **code-built chibi placeholders** (`ChibiHero`/`ChibiAnimator`); Blender skinned models are the
  eventual goal, plugging into the `CombatView` spawn/animator seam. Mixamo fully removed.

- **Pack variety (Lever 1) ✅** — `MonsterRank` Elite/Rare rolled per-mob at farm spawn
  (`Combat.RollRank`/`ApplyRank`): tougher (HP/Atk mults), chunkier body, highlighted (blue/gold
  + glow), and a boosted Rare-capped loot bundle (`Loot.RollRankDrops`) + reward mult. Tunables in
  `BalanceConstants` (Elite/RareChance etc.).
- **Monster modifiers (Lever 1) ✅ — now STAGE-DRIVEN (core-loop rework).** Player-controlled
  risk/reward knob (PoE map-mods). **Acquisition + upgrade are driven by farm DEPTH** (highest stage),
  not bosses: unlock the next modifier in `GameConfig.ModifierUnlockOrder` every
  `Balance.ModifierNewEveryStages` (10) stages, +1 strength to ALL owned every
  `ModifierUpgradeEveryStages` (5) — all derived from stage (`Modifiers.SyncToStage`, called in
  `Progression.OnStageCleared` + on load in Bootstrap; idempotent, can't desync). Boss-banking
  (`AcquireFromStage`) **retired**. Catalog front-loads "boring" income mods (Prosperous/Studious/
  Bountiful = small monster-HP bump for +gold/+xp/+drop) before the behavioral Armored/Swift/Vampiric/
  Thorns. Player slots up to `Balance.MaxActiveModifiers` (3) as an **account-wide loadout**
  (`Modifiers.SetActive(...,cfg)` enforces the cap; `ModifierPanel` shows N/3 + locks "FULL" rows).
  Applied to farm trash in combat: stat mults + per-hit behaviors (lifesteal/reflect in `ApplyHit`) +
  reward (gold/XP/drop in `HandleDeath`). Bosses still *exhibit* a modifier (behavior-only) for flavor
  via `ModifierCycle`. Visual tells: aura tint + boss-HUD name.
  - **Loot-imprint mechanical modifiers (the headline hook) ◑ — slice 1 of 3 (GameCore) done.**
    A `Mechanical` modifier fights nastier via a REAL sim mechanic AND can stamp that signature onto
    its drops — a build-defining affix the normal pool never rolls, so the gear is obtainable ONLY by
    farming the mod. First one: **Volatile** — mobs' attacks SPLASH the whole party
    (`ModifierBehavior.Splash` grants an additive `SplashRadius` in `Combat.ApplyModifier`, uncapped);
    a kill imprints `+SplashRadius` onto the drop (`Loot.ImprintDrop`, called per farm-trash drop in
    `HandleDeath`, sourced from the dead mob's own `ModTypes` × `s.ActiveModifiers` strength). The affix
    folds into `Stats.ComputeHeroStats` like any other ⇒ flows into combat + DPS/Eff-Life power compare
    for free; `SplashRadius` is in no base's `AllowedAffixes`, so imprinted gear is exclusive.
    `ModifierDef` gained `Mechanical`/`ImprintStat`/`ImprintPerStrength`/`ImprintChance`. **Acquisition
    is TOWER-GATED, not farm depth** (`ModifierDef.TowerUnlockFloor`): Volatile unlocks on clearing
    **Tower floor 5** (early, reachable chase — tune up later), NOT via `ModifierUnlockOrder`. `Modifiers.SyncToStage` grants tower-gated mods from `TowerState.HighestFloor`
    at the same uniform strength; the Tower-win path resyncs + fires a "New modifier unlocked!" feed;
    `ModifierPanel` appends tower mods (not in the order) by unlock floor. SplashRadius + AttackRange are
    hidden from the hero stat sheet/compare (`StatDisplay.Order`) — under-the-hood mechanics, less
    clutter. 331 tests (`LootImprintTests.cs` + tower-gate tests in `ModifierTests.cs`). **Slice 2 =
    client tells** (panel flag + imprinted-item badge/line + loot feed); **slice 3 = more mechanical
    mods** (e.g. `+AttackRange`). Then item **gambling/crafting** as a separate feature.

- **Loot & power chase (Lever 2) ✅** — drops legible at a glance. `Upgrades.cs` collapses a
  candidate item into one honest power scalar (`PowerScore` = geometric mean of DPS and
  Effective-Life, reusing `DerivedStats`) and a banded verdict (`EvaluateForHero` →
  Upgrade/Sidegrade/Downgrade by `Balance.UpgradeBandPct`); `BestForItem` finds who gains most;
  `AutoEquipIfBetter` equips on a true upgrade only. Client tells (`UpgradeTell` presentation):
  green ▲ badges on bag tiles, a "▲ +N% power for <hero>" line in the bag detail + a verdict
  headline in the Heroes compare, an upgrade tag on the loot-rain feed line, and an opt-in
  **Auto-equip** toggle (`Settings.AutoEquipUpgrades`) that auto-equips fielded heroes on banking.

- **Build depth (Lever 3) ✅ — all 3 slices done.** Skills now *grow*: each hero earns 1 skill
  point per level (`Skills.PointsEarned`, derived from level — not persisted), spent to **rank up**
  skills (`Skills.InvestSkill`, capped at `SkillDef.MaxRank`; free `RespecHero`). A skill's primary
  magnitude scales `× (1 + EffectPerRank·rank)` in the sim (`Combat.TryCastSkill`) — **rank 0 = base
  = today's behavior**, so existing seeded fights are byte-identical. `HeroInstance.SkillRanks`
  persisted (threaded through the 5 hero-copy sites + Migrate). UI: Heroes→Skills shows "Skill
  Points: N", per-skill "Rk r/max", a **＋** invest button, and **Respec**; invests apply live via
  `RefreshPartyStats`. (The 4-of-6 active picker already shipped.) **Slice 2 ✅ — passives:** each
  class gained 2 always-on passive nodes (Warrior Toughness/Vitality, Magician Pyromancy/Attunement,
  Thief Deadly Precision/Killer Instinct) added to its known pool. Passives are investable but never
  slotted (`Skills.IsPassive`/`KnownActive`/`KnownPassive`; `ToggleSkill`/`SetLoadout`/`DefaultLoadout`
  exclude them); each rank folds into `Stats.ComputeHeroStats` (`SkillDef.Passive`/`PassiveStat`/
  `StatPerRank`), so it flows into the stat sheet + DPS/Eff-Life + the Lever 2 power compare for free.
  Rank 0 = +0 ⇒ existing fights byte-identical. UI: Heroes→Skills split into ACTIVE + PASSIVE
  sections — verified live via Unity MCP (invest ticks the gold rank + "now +N", folds into the
  Stats sheet). **Slice 3 ✅ — skill tree:** active skills now form a gated tree
  (`SkillDef.Prereq` + `UnlockLevel`; `Skills.IsUnlocked` + `CanInvest` enforce "prereq has ≥1 rank"
  AND "hero.Level ≥ UnlockLevel"). Per class: 2 root actives + a branching chain (e.g. Warrior
  cleave/bash → warcry/whirlwind → bulwark/frenzy); passives stay open roots. Gating restricts point
  INVESTMENT only — slotting/casting at rank 0 is unchanged, so seeded fights stay byte-identical.
  UI dims locked rows + shows "needs &lt;Prereq&gt; + Lv N"; ＋ greys until unlocked. Verified live
  (investing a root unlocks its child in real time). Lever 3 done.

- **Tower of Ascension (alt mode) ◑ — slices 1+2 of 3 done; PLAYABLE.** A one-clear-per-floor track
  distinct from the farmable ladder: **steeper** curve on both axes + rotating per-floor modifiers
  (reuses the Lever-1 catalog), **no idle income**, and a **permanent account-wide buff every 10
  floors**. `TowerState{HighestFloor}` nests under `ProgressState` (rides `Progress` threading — only
  the 2 `new ProgressState{}` reducers + Migrate carry it; dodges the ~18-site SaveState footgun).
  `Tower.cs`: NextFloor/MaxFloor/IsComplete/CanAttempt (sequential, forward-only), `RecordClear`,
  derived `MilestonesCleared`/`AccountBuffPct`/`ApplyAccountBuffs` (×Hp/Atk/Def), per-floor
  `FloorHpMult`/`FloorDmgMult`/`FloorModifier`. Tunables in `BalanceConstants` (Tower* — Floors=30,
  Hp 1.50, Dmg 1.20, MilestoneEvery 10, MilestonePct 0.05 — brutal/steep on purpose). **Slice 2:**
  `EncounterKind.Tower` + `CombatState.TowerFloor`; `Combat.EnterTower` (bounded fight in-place like
  EnterBossChallenge — floor-scaled pack + floor modifier as a full buff + guardian boss on milestone
  floors; win=all dead, lose=wipe — **heroes do NOT respawn in the tower**, do-or-die like the boss
  challenge); account buffs fold into `RefreshPartyStats` (gear seam); `HandleDeath`
  grants no income for Tower. Client: `TowerView` entry screen (control-bar "Tower (Fn)" button) →
  `CombatView.EnterTowerFloor`; `ResolveOutcome`/`DrawOutcome` handle the win (RecordClear + milestone
  buff feed + "Floor N cleared!" popup) and loss ("FLOOR N FAILED"). Verified live via Unity MCP
  (floor 1 clear, floor 10 guardian + "+5% account power" buff). **Slice 3 = per-floor reward bundles
  (TBD) + juice.** (Tuning since done: no-respawn do-or-die; steep curve Hp ×1.50 / Dmg ×1.20.)

The four "feels empty/boring" loop levers — all long-run goals — are in `docs/game-design.md` §7.1:
**1 combat variety ✅ (ranks + modifiers)**, **2 loot & power chase ✅ (legible upgrades + auto-equip)**,
**3 build depth ✅ (ranks + passives + gated tree)**, 4 progression hooks.

**Next (gameplay-first):** Lever 4 (progression hooks); a **Tower of Ascension** alt mode (100-floor
exponential tower, SW-ToA style — user-requested, must differ from the farm ladder: separate track,
one-time floor clears, milestone rewards; rewards TBD); more hero unlocks/classes;
crafting/sets/loot-filter; prestige.
**Deferred:** real Blender hero models; UI/UX layout-group refactor; console balance-sim.
Gacha/live-service still deferred. (Auto-advance push is built but shelved behind an off flag.)

Full roadmap: [`docs/game-design.md`](docs/game-design.md) §8. **Unity play-mode can't be tested
headlessly — visuals are verified by screenshot; client UI is hand-placed uGUI/IMGUI coords.**

## Conventions
- **GameCore-first:** build + `dotnet test` each piece, then wire into Unity (play-mode can't
  be tested headlessly — the user verifies visually). Milestones split into sequential
  subtasks; implement one, test, stop for review/commit.
- The user commits manually and works directly on `main`; end commit messages with
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. New content (skills, slots,
  heroes) is set at New Game, so the user starts a fresh game to see it.
- LF→CRLF git warnings on Windows are normal.

## Live-service / global roadmap (the long arc)
Goal: server-authoritative live-service — global chat, multiple servers, leveling
on the server so a modded client can't cheat. The design supports it; it's additive,
not a rewrite. When we get there:
- **Server:** ASP.NET Core service referencing `GameCore`. Client sends *intents*
  ("push tier N", "equip X", "claim idle"); server runs the sim, owns the save,
  returns results. First proof-of-concept: one authoritative "resolve a stage run"
  endpoint.
- **Authority model:** server result is truth; client sim is cosmetic prediction.
  (Avoids relying on bit-exact cross-platform float determinism — `GameCore` uses
  `double`; only the server needs to be self-consistent.)
- **Scale-out:** stateless app servers behind a load balancer; Postgres (durable) +
  Redis (hot state, leaderboards, chat fan-out).
- **Global chat:** its own real-time service (WebSocket gateway + pub/sub). Never
  touches `GameCore` — keep it separate.
- **Server time authority** for idle accrual (don't trust the device clock).
