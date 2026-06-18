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
unity/Assets/Game/       MonoBehaviours, read-only client (Bootstrap, CombatView)
gamecore/GameCore.Tests/ xUnit tests — compile the SAME Assets/GameCore sources via a
                         csproj glob (no copy). + gamecore/Adapters/Persistence.cs.
docs/game-design.md      the durable what/why
web-prototype/           ARCHIVED Vite/React/Pixi prototype (pre-pivot; do not extend)
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
     bases, affixes, monsters, rifts, balance. `GameConfig.Default()` today.
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

## Milestones & status
Engine-independent order, same as the original plan:

| | Milestone | State |
|--|--|--|
| M0 | Iso scene, camera, party lead (placeholder primitives) | ✅ |
| M1 | Deterministic auto-combat in the client (`CombatView`) | ✅ |
| M2 | Loot: drops + rarity + affixes, inventory, equip → stats recompute | **next** |
| M3 | Rift/difficulty tiers (progression spine) | later |
| M4 | Idle accrual (offline = math, claim modal) | later |
| M5 | Persistence (save/load) | later |
| M6 | Feel pass (number formatting, juice, item-compare UI) | later |

## Live-service / global roadmap (the long arc)
Goal: server-authoritative live-service — global chat, multiple servers, leveling
on the server so a modded client can't cheat. The design supports it; it's additive,
not a rewrite. When we get there:
- **Server:** ASP.NET Core service referencing `GameCore`. Client sends *intents*
  ("push tier N", "equip X", "claim idle"); server runs the sim, owns the save,
  returns results. First proof-of-concept: one authoritative "resolve a rift run"
  endpoint.
- **Authority model:** server result is truth; client sim is cosmetic prediction.
  (Avoids relying on bit-exact cross-platform float determinism — `GameCore` uses
  `double`; only the server needs to be self-consistent.)
- **Scale-out:** stateless app servers behind a load balancer; Postgres (durable) +
  Redis (hot state, leaderboards, chat fan-out).
- **Global chat:** its own real-time service (WebSocket gateway + pub/sub). Never
  touches `GameCore` — keep it separate.
- **Server time authority** for idle accrual (don't trust the device clock).
