# Idle ARPG — working context

A cozy low-poly **3D idle ARPG** (Diablo/PoE-style loot & build depth, *Tunic*-ish
look). A 3-hero party auto-clears dungeons, monsters drop gear, you build the party
and push higher difficulty; progress accrues while away. Built in **Unity (C#)**.
Gacha and global/live-service are deferred but the architecture keeps the door open.

This file is the orientation Claude loads every session — **keep it short and current.**
The durable design (loops, economy, data model, live-service vision) is in
[`docs/game-design.md`](docs/game-design.md).

## The one architecture rule
All combat / loot / idle / progression logic lives in **`GameCore` — pure C#, zero
`UnityEngine` references.** Unity is a client that only *reads* simulation state; a
.NET server can later reuse the exact same `GameCore` for authority. **Never let game
logic leak into MonoBehaviours** — they spawn/animate/poll, they don't decide rules.

## Tech stack
- **Sim:** `GameCore` — pure C# library, `net8.0`, tested with `dotnet test` (xUnit).
  Lives in `unity/Assets/GameCore/` under a no-engine-refs `GameCore.asmdef`.
- **Client:** Unity 6 LTS, 3D URP. MonoBehaviours in `unity/Assets/Game/` (`IdleGame.Game`).
- **Persistence:** local JSON via `System.Text.Json` (one host adapter). Server (ASP.NET
  Core + Postgres/Redis) is the long-arc plan — see game-design.md §9.

## Repo layout
```
unity/Assets/GameCore/   THE sim — pure C#, no engine refs. SINGLE SOURCE OF TRUTH.
unity/Assets/Game/       MonoBehaviours, read-only client (Bootstrap, CombatView, views…)
gamecore/GameCore.Tests/ xUnit — compiles the SAME Assets/GameCore sources via a glob (no copy).
docs/game-design.md      the durable what/why
```
**Edit sim code in `unity/Assets/GameCore/`** — the test project globs those exact files.

## Design principles
1. **Pure & deterministic.** Same state + seed ⇒ same result (unit-testable, server-verifiable).
2. **Three state types, never mixed:** `SaveState` (persisted), `GameConfig` (static content,
   injected as a `cfg` param), `CombatState` (transient sim).
3. **Save reducers are pure** (return new state); the combat step mutates `CombatState` in place
   per fixed step but stays deterministic.
4. **Seeded RNG** (`Rng`, mulberry32), advanced via a cursor stored in the save — never reseeded.
   Gamble reducers (shop/reforge) advance + persist that cursor so rolls can't be re-rolled.
5. **Versioned saves + `Migrate`.** Renderer reads, never writes rules.

## Build & test
- Sim (fast inner loop): `dotnet test gamecore/GameCore.Tests`
- Client: open `unity/` in Unity 6 LTS, press **Play** (`Bootstrap` builds the scene in code).
  Play-mode can't run headlessly — visuals are verified by screenshot (Unity MCP), UI is
  hand-placed uGUI/IMGUI coords.

## Current systems (426 tests passing)

Phase A (core loop) and most of Phase B (depth) are done. What exists today:

- **Combat** — deterministic auto-battle; melee/range, crit, mana skills (damage/AoE/heal/
  buff incl. party-wide heal-over-time via `HpRegenPct` buffs) on `AtkSpd` cooldowns, splash,
  chain, lifesteal, thorns. Farm = downed heroes respawn; boss & Tower = do-or-die (no respawn).
- **Farm ladder** — 100 stages, geometric difficulty, endless per-stage farming, 60s mini/major
  boss gates (major every 10), tiered rates, elite/rare **pack ranks** (tougher, better loot).
  **10 themed ZONES** (one per 10-stage tier, `ZoneDef` + `ZoneForStage`): per-zone trash
  roster + boss + engine-free palette/prop hints; Tower floors travel the same zones. Zone
  monsters stay in the slime/goblin stat band — flavor axis, the stage curve owns difficulty.
  (LOW-POLY faceted art only — MS2 pipeline is heroes-only.)
- **Loot & power (Lever 2)** — 5 rarities (**Normal/Rare/Unique/Legendary/Mythic**=red; Unique+
  boss-only, Mythic the extreme chase) + random affixes, 9 equip slots from one shared bag,
  inventory cap + auto-salvage→`scrap` (threshold up to Unique) + one-click mass salvage
  (`Inventory.SalvageAllUpTo`). Every drop reads as one `Upgrades.PowerScore` verdict →
  ▲ badges, loot-feed tags, opt-in auto-equip.
- **Heroes & build depth (Lever 3)** — **archetype → class backbone**: three families
  (`ArchetypeDef` Warrior/Rogue/Magician = stat template + shared passive pool) and a
  class = per-key overrides via `GameConfig.FromArchetype`; `Role` (melee/ranged/support)
  stays the sim's separate mechanical axis. Live classes: Knight (Warrior) / Fire Mage
  (Magician) / Assassin (Rogue) / Priest (Magician, party-HoT healer, first male body).
  Unlocks at stage 3/5/10, granted RETROACTIVELY on load by `Progression.SyncHeroUnlocks`
  (a def removed from `HeroUnlocks` is stripped from saves — that's how the Ice Mage is
  shelved for a comeback). Display names are cfg-only; def ids stay `warrior_basic`/
  `thief_basic`/etc. **Per-hero leveling** (level = the power axis). **2+2 hero template (design §7.2):** every hero
  = exactly 2 actives + 2 passives from a shared archetype library, revealed at Lv 1/5/10/15 and
  always on (no loadouts, no prereq trees — heroes are data rows so a solo dev can scale the
  roster for the hero-gacha arc). Points = `Level/5` derived; rank cap 5; **MaxRank = mastery**
  (counts as rank+2 via `Skills.EffectiveRank`) ⇒ 20 points at Lv 100 exactly maxes the kit,
  the build choice is ordering. XP curve `600 × 1.19^(lvl-1)` ⇒ ~95B to level 100.
  `AccountLevel` exists but is inert — the hook for a future cosmetic account icon.
- **Monster modifiers (Lever 1 — the core risk/reward loop)** — two pools:
  - **Normal** stat mods (Prosperous/Studious/Bountiful/Armored/Swift/Vampiric/Thorns): unlocked
    by **farm depth** (`ModifierUnlockOrder`, one per 10 stages), 3-slot account loadout.
  - **Rare** mechanical **loot-imprint** mods (Volatile→splash, Chaining→arc, of Leeching, of
    Thorns): **Tower-gated, unlock in prefix/suffix PAIRS**; separate loadout (2 prefix + 2 suffix).
    They fight nastier via a real sim mechanic AND stamp that trait onto drops as **exclusive gear**
    (a stat no normal affix rolls). Anti-target-farming: a slot only applies with ≥2 active, an item
    holds ≤1 prefix + ≤1 suffix imprint, and the imprint is a random pick among hits.
  - **Modifier shop + Reforge (gamble economy)** — spend **gold + scrap** to roll a ±5% delta,
    floored at base, escalating cost: on a mod's **tuning** (scales its danger + reward; hybrid
    reward splits supported) or on an **item's** affix values (`Inventory.Reforge`; imprints
    untouched). One gamble verb, two surfaces. No full crafting system (deliberate — anti-bloat).
- **Tower of Ascension** — alt one-clear-per-floor mode: steeper curve, no idle income, permanent
  account-wide buff every 10 floors, and the **gate for the rare modifier pairs**. Per-floor reward
  bundles still TBD (the one unfinished slice).
- **Idle** (offline gold/XP/loot, 12h cap), **quests** (rolling goal board), **chat/feed** panel
  (system feed live; social tabs stubbed pending the server).
- **Achievements (Lever 4 — slice 1)** — the permanent milestone ladder, distinct from the rolling
  goal board. Lifetime `AchievementState` (nested under `ProgressState`): ADD counters (kills/bosses/
  salvages/gold/rares) + MAX peaks (deepest stage/floor, hero level). `Achievements.Record` (fed the
  same events as quests) auto-pays a one-time gold+scrap+XP reward per tier crossed, announced in the
  feed; a read-only Achievements panel (control bar) shows the ladder. 8 achievements, geometric tiers.
- **Daily login + premium currency (Lever 4 — slice 2)** — **gems**, a THIRD currency
  (`Currencies["gems"]`) earnable *only* by the daily login streak (the seed of the future gacha/
  microtransaction economy — no other source yet). `DailyLoginState` (nested under `ProgressState`):
  once per UTC day `DailyLogin.Claim` grants gems, consecutive days build a streak (missed day resets),
  with a milestone bonus every 7th day. A launch `DailyLoginModal` (Collect → `CombatView.ClaimDailyLogin`)
  is the beat; gems show in the HUD. Still open: manual achievement-claim UX, the gem SINK (gacha/shop),
  real-money purchase, prestige/rebirth.
- **Art** — *Tunic* height-blend shader + faceted vertex-coloured world + dappled light.
  **Heroes: the MS2 skinned pipeline is SHIPPED and standard** (all 4 roster heroes on it):
  a hero = `art/heroes/<defId>.json` manifest (gender, gear NIFs, weapon attaches, dye
  `tints`, skill/sound bindings) + 9 decoded clips in `art/motion/<defId>/` + one headless
  `art/skinned_body.py` bake → `Resources/Models/<defId>_skinned.fbx`; then Unity menu
  `Tools > Build Hero Animators` (importer clips + per-hero override controller). ALWAYS
  eyeball `--renders` before `--export` — item transforms/dyes lie. Port history/phase
  notes live in git. Fallback chain stays SkinnedHero→ModelHero→ChibiHero.
  **Monsters: `art/monsters.py`** (faceted flat-shaded, HEROES-ONLY rule inverse) — one
  rigid FBX per monster id → `Resources/Models/monsters/`, loaded by `MonsterModel` with
  primitive fallback; same eyeball-renders-then-export loop. Model rank/mod tells are a
  gentle tint + faint glow (flat repaint/strong emission buries the palette).

**The four loop levers** (game-design.md §7.1): 1 combat variety ✅ · 2 loot/power chase ✅ ·
3 build depth ✅ · **4 progression hooks — in progress** (achievement ladder + daily-login premium
currency shipped; gem sink/gacha + prestige next).

## What's next / open
**One source of truth: [`docs/ROADMAP.md`](docs/ROADMAP.md)** — the ordered priority
list (gacha gem sink ⭐, Tower slice 3, combat presentation debt, MS2 monsters, then
content/tuning; parked items at the bottom). Update it in the same commit that ships
a roadmap item. Finished plans live in git history — don't keep them in the tree.
The doc set is exactly four files (README / CLAUDE.md / game-design / ROADMAP) with
one job each; new-machine setup incl. the MS2 art toolchain is in the README.

## Conventions
- **GameCore-first:** build + `dotnet test` a piece, then wire into Unity and verify by screenshot.
  Split features into sequential slices; implement one, test, stop for review/commit.
- **Display rounding = correctness (game-design.md §7):** balances/owned amounts round DOWN (`floor`),
  costs round UP (`ceil`), countdown timers `ceil`, count-up timers `floor`. Never let a rounded
  display contradict the real data (e.g. "says I can afford it but can't"). Use `Num.CompactFloor` /
  `Num.CompactCeil` for the directional cases, `Num.Compact` (round) only for neutral stats.
- The user commits (and pushes when asked); work on `main`. End commit messages with
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` (name whichever model is actually
  running the session). New content (skills, heroes, mods) is seeded at New Game, so start a
  fresh game to see it.
- LF→CRLF git warnings on Windows are normal.
- **Unity MCP** verify loop: refresh_unity + read_console for compile errors; enter Play, click
  Continue, inject state via `execute_code` reflection on `CombatView` (`_save`/`_cfg`), screenshot,
  then **restore the save to its real values before stopping** (autosave persists) and delete the
  screenshots. The MCP bridge only attaches at session start — a mid-session restart needs a fresh
  Claude session.
