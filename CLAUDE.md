# Idle ARPG — working context

A cozy low-poly **3D idle ARPG** (Diablo/PoE-style loot & build depth, *Tunic*-ish
look). A 4-hero party auto-clears dungeons, monsters drop gear, you build the party
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
unity/Assets/Game/       MonoBehaviours, read-only client (Bootstrap, CombatView, Party/
                         Equipment/Inventory UI, ChatPanel, TopBar, UiKit, StatDisplay)
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

## Status (203 tests passing)

**Phase A — core loop — M0–M9 ✅.** Deterministic auto-combat; loot (rarity + affixes,
equip → stat recompute); per-hero leveling; 50-stage ladder as farm zones with 60s timed
mini/major boss gates + tiered rates; idle accrual off highest cleared stage + claim modal;
save/load + menu; feel pass; warrior + magician (ranged/AoE); group/solo movement; chat/feed panel.

**Phase B — depth — in progress:**
- **M10 multi-character foundation ✅** — mana resource; **9 equip slots** (Weapon, Offhand,
  Helm, Chest, Gloves, Boots, Cape, Ring, Amulet) per-hero, drawn from **one shared account
  bag**; inventory cap (100 *loose* items) + opt-in **auto-salvage → `scrap`**; scarce drops
  (~1 per few min) with **Unique/Legendary boss-only** (guaranteed bundles: major 5–7, mini
  1–2), trash/idle capped at Rare; **Party HUD** (HP/mana bars; click a hero → its Equipment
  doll); rarity-bordered item tiles + grid bag; canonical stat display (`StatDisplay`).
- **M11 skills ✅** — sim: skills fire in combat (single/AoE damage, heal most-hurt ally,
  self stat-buff), cost mana, on cooldowns (scaled by `AtkSpd`); heroes **and bosses** cast;
  `Spd` split into `MoveSpd` (movement) + new `AtkSpd` (action rate; warrior slower than mage).
  Client: skill **FX** (`SkillCast`→meteor/cleave-ring/quake/heal-sparkle/war-cry-aura; `Heal`→
  green numbers) + an `AtkSpd`-scaled attack/cast **lunge tell** + a Party-HUD skill-ready cue.
- **Salvage UI ✅** — manual salvage (Unique/Legendary need a confirm) + an auto-salvage
  threshold toggle (Off→Normal→Magic→Rare), wired to `Inventory.SalvageItem`/`Settings.AutoSalvageMax`.
- **Roster screen ✅** — field/bench the party and gear ANY owned hero incl. benched ones;
  party swaps apply **live during farming** (no restart) via `Combat.ReconcileParty`, and are
  disabled in boss/other modes (`Party.FieldHero` keeps the party duplicate-free).
- **Next (depth, gameplay-first):** acquire heroes 2–4 via progression (more classes);
  crafting/sets/enhancement/loot-filter; alt modes (endless); prestige/retention.
- **Deferred to its own milestone *after* the depth gameplay:** a dedicated **UI/UX polish**
  pass (the screens above are functional placeholders — IMGUI HUD + code-built uGUI; known
  rough spots: control-bar crowding, hand-placed anchors, glyph/font checks). Gacha/live-service
  still deferred too.

Full roadmap is in [`docs/game-design.md`](docs/game-design.md) §8.

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
