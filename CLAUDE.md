# Idle ARPG — working context

Cozy low-poly **3D idle ARPG** (Diablo-style loot/build depth, *Tunic* look):
a 3-hero party auto-clears stages, gear drops, you build the party and push;
progress accrues while away. Unity (C#). Gacha/live-service deferred but the
architecture keeps the door open. Durable design: [`docs/game-design.md`](docs/game-design.md).
What's next: [`docs/ROADMAP.md`](docs/ROADMAP.md) — THE priority list; update
it in the commit that ships an item. Doc set is exactly four files (README /
CLAUDE.md / game-design / ROADMAP), one job each. Keep this file short.

## The one architecture rule
All game logic lives in **`GameCore` — pure C#, zero UnityEngine refs**
(`unity/Assets/GameCore/`, no-engine-refs asmdef). Unity is a client that only
READS sim state; MonoBehaviours (`unity/Assets/Game/`, `IdleGame.Game`) spawn/
animate/poll, never decide rules. A future .NET server reuses GameCore as-is.

## Build & test
- Sim: `dotnet test gamecore/GameCore.Tests` (xunit; globs the SAME
  Assets/GameCore sources — edit sim code there). **455 tests green.**
- Client: open `unity/` in Unity 6 (URP); Play — `Bootstrap` builds the scene
  in code. Play mode can't run headless; visuals verify by screenshot via
  Unity MCP; UI is hand-placed uGUI/IMGUI.

## Design principles
1. Pure & deterministic: same state + seed ⇒ same result.
2. Three state types, never mixed: `SaveState` (persisted) / `GameConfig`
   (static content, injected `cfg`) / `CombatState` (transient).
3. Save reducers pure; combat step mutates CombatState per fixed step,
   deterministically.
4. Seeded RNG (`Rng`, mulberry32) advanced via a cursor in the save — never
   reseeded; gamble reducers persist the cursor so rolls can't re-roll.
5. Versioned saves + `Migrate` (v2 now). **`StatKey` persists numerically —
   enum members keep EXPLICIT pinned values; never renumber.**
6. Display rounding = correctness: balances floor, costs ceil, countdowns
   ceil, count-ups floor (`Num.CompactFloor`/`CompactCeil`; `Num.Compact`
   only for neutral stats). A rounded display must never contradict the data.

## Current systems (one line each; details in git/design doc)
- **Combat**: deterministic auto-battle; melee/range, crit, splash, chain,
  lifesteal, thorns; cooldown-gated skills (NO mana — removed 2026-07-03).
- **Farm ladder**: 100 stages, endless per-stage farm, boss gates, pack ranks;
  **10 themed zones** (one per 10-stage tier: `ZoneDef` roster/boss/palette/
  props/favored drop slot; Tower floors travel the same zones).
- **Loot**: 5 rarities (Normal→Mythic), affixes, 5 slots, shared bag,
  auto-salvage threshold, `SalvageAll` (arm/confirm) + `Item.Locked` (all
  salvage paths refuse locked), `PowerScore` one-verdict (▲ badges, opt-in
  auto-equip: damage-first across fielded heroes, EHP fallback).
- **Heroes**: archetype backbone (Warrior/Rogue/Magician template + per-key
  overrides = class); live: Knight/Fire Mage/Assassin/Priest; unlocks synced
  retroactively by `Progression.SyncHeroUnlocks` (removing a def shelves it).
  **2+2 kit** (2 actives + 2 passives, revealed L1/5/10/15, points =
  Level/5, MaxRank = rank+2 mastery ⇒ Lv100 exactly maxes the kit).
- **Modifiers** (core risk/reward loop): normal stat mods (farm-depth
  unlocks, 3-slot loadout) + rare Tower-gated loot-imprint mods in
  prefix/suffix pairs (mechanical threat AND exclusive gear stat; ≥2 active
  to apply, ≤1 prefix + ≤1 suffix per item). Shop/Reforge = one gamble verb
  (gold+scrap, ±5% floored, escalating cost).
- **Tower**: one-clear-per-floor alt mode, account buffs per 10 floors, gates
  the rare mod pairs. Per-floor reward bundles still TBD (roadmap 4).
- **Idle** (12h cap) · **quests** (rolling board) · **achievements**
  (lifetime ladder, auto-pay per tier) · **daily login → gems** (the ONLY gem
  source) · **gacha** (the gem sink: banner defs in config, pity, dupe →
  XP/scrap; "Winter's Return" gates the Ice Mage) · chat/feed panel (social
  tabs stubbed).
- **Art**: faceted vertex-coloured world + TunicSurface height-blend shader,
  **orthographic diorama camera** (45° pitch, zoom drives orthographicSize,
  factor in CameraRig; dead-zone follow) with split-tone grade (warm sun /
  purple-blue shadows). Heroes = MS2 skinned pipeline (manifest json + 9
  decoded clips + `art/skinned_body.py` bake + Tools > Build Hero Animators;
  ALWAYS eyeball --renders before --export). Monsters = `art/monsters.py`
  faceted FBX, `MonsterModel` + primitive fallback; rank/mod tells = gentle
  tint + faint glow, emission scaled by material luma.

## Conventions & gotchas
- **GameCore-first**: build + test a slice, wire into Unity, verify, STOP for
  review/commit. One verified slice per commit; end commit messages with
  `Co-Authored-By: <the model running the session> <noreply@anthropic.com>`.
  The user pushes. Work on `main`. New content seeds at New Game.
- **Delegation model (user)**: Fable designs/specs/reviews/commits; Opus 4.8
  subagents implement and run dotnet tests. **Agents never drive the Unity
  editor without a user-approved window** — user shares ONE editor + ONE live
  save; batch Play-verification into announced windows; user playing =
  pipeline paused.
- **Unity MCP verify loop**: refresh_unity + read_console for compile; stop
  Play BEFORE compiling (domain reload wipes Play state). In Play: dismiss
  MainMenu via reflected `CloseAnd(OnContinue)`, IdleClaimModal via first
  button, inject via reflection on CombatView (`_save`/`_cfg`,
  `GoToStage`), pump `EditorApplication.Step()` when unfocused. **Back up
  save.json (%USERPROFILE%\AppData\LocalLow\DefaultCompany\unity\save.json)
  BEFORE Play and compare before restoring** — live may hold newer legit
  progress. Bridge attaches at session start only.
- **Hard art rules (user)**: MS2 pipeline is for HEROES ONLY — monsters/
  world/props stay faceted Tunic style. NO MS2 music ever (SFX fine). No MS2
  skill names/numbers. Raw extracts stay outside the repo.
- LF→CRLF git warnings on Windows are normal. PowerShell 5.1: no `&&`,
  quote-mangling on `git commit -m` — write the message to a file, `-F` it.
