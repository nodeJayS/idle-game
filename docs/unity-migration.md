# Unity Migration — status & plan

Companion to [`unity-evaluation.md`](unity-evaluation.md). Tracks the actual move
from the web prototype to Unity.

## Decisions (locked)
- **Engine:** Unity (C#), committed at M0 to minimize the port (the sim was tiny).
- **Repo:** restructured in place (same GitHub repo). Web prototype archived to
  `web-prototype/`; `docs/` shared; new `gamecore/` + (incoming) `unity/`.
- **Look:** cozy low-poly 3D (URP), *Tunic*-ish. (Supersedes the earlier pixel-art
  direction.)
- **Gacha:** still deferred; `Party.AcquireHero` stays the single plug point.

## Architecture rule (unchanged)
`GameCore` is **pure C# with no `UnityEngine` references** — testable via
`dotnet test`, reusable on a .NET server for authority. Unity is a client that
reads simulation state; no game logic in MonoBehaviours.

## What's done
- ✅ Repo restructured; web prototype archived under `web-prototype/`.
- ✅ `.gitignore` (Unity + .NET + Node) and `.gitattributes` (LFS for art) added.
- ✅ **`GameCore` ported to C#** (Phase 1): `Models`, `Rng` (mulberry32 +
  weighted pick), `GameConfig.Default()` (heroes/items/affixes/monsters/rifts/
  balance), `Save.NewGame`/`Migrate`, `Party.SetPartySlot`, `Persistence`
  (System.Text.Json — the one host-specific adapter).
- ✅ xUnit tests mirroring the old vitest suite.
- ✅ **Combat + Stats systems** (engine-independent, ahead of the Unity scene):
  `Stats.ComputeHeroStats` (base + growth + gear) / `ComputePartyPower`; and
  `Combat.InitCombat`/`StepCombat`/`RunToEnd` — a deterministic auto-battle
  (walk-to-target + auto-attack, crits, win/lose, boss-defeated events). 21/21
  tests passing.
- ✅ **Unity project created** (Phase 3): `unity/` is a 3D (URP) project;
  `GameCore` sources copied to `unity/Assets/GameCore/` with a no-engine-refs
  `GameCore.asmdef`; `GameCore.Tests` still points at the canonical sources for
  `dotnet test`.
- ✅ **M0 scene** (Phase 3): `Bootstrap.cs` builds the iso environment in code on
  Play (camera, directional light, ground) — no manual editor wiring.
- ✅ **M1 auto-combat in the Unity client** (Phase 4): `CombatView` MonoBehaviour
  drives the deterministic battle at a fixed timestep, interpolates placeholder
  primitives toward sim positions, shows floating HP bars + a status line, and
  auto-restarts each run. Renderer only *reads* `CombatState` — combat rules stay
  in `GameCore`.

## Phase map
| Phase | Work | State |
|------|------|-------|
| 0 | Setup: SDK install, repo hygiene, structure | ✅ |
| 1 | Port `game-core` → C# library + tests | ✅ (foundation + Stats & Combat systems implemented) |
| 2 | Content as ScriptableObjects (or keep `GameConfig.Default()`) | later |
| 3 | **Unity project + M0 scene** (3D iso, camera, party lead) | ✅ |
| 4 | **M1 auto-combat** wired into the Unity client (`CombatView`) | ✅ |
| 5+ | M2 loot → M3 rifts → M4 idle → M5 persistence → M6 feel, in C# | **next** |

## What needs YOU (the editor, not scriptable by the agent)
- ✅ Unity Hub + Unity 6 LTS installed; 3D (URP) project created at `unity/`.
- ✅ `GameCore` wired in; M0 scene + M1 auto-combat implemented.
- **Now:** open `unity/` and press **Play** to verify M1 visually — party capsules
  advance on the pack + boss, HP bars drain, units vanish on death, and the run
  shows VICTORY/DEFEAT then auto-restarts. (Play-mode can't be driven headlessly
  by the agent; this is the one manual confirmation step.)

## Wiring GameCore into Unity (when the project exists)
- **Preferred:** copy `gamecore/GameCore/*.cs` into `unity/Assets/GameCore/` and add
  a `GameCore.asmdef` with **no engine references**. Keep `Persistence.cs` out (or
  swap to JsonUtility/Newtonsoft) if System.Text.Json isn't available in your Unity
  runtime. Keep `gamecore/GameCore.Tests` pointed at these sources for `dotnet test`.
- **Alt:** build `IdleGame.GameCore.dll` and drop it in `unity/Assets/Plugins/`.

## Global-release packages to add as needed (Phase 4+)
Addressables (content delivery / LiveOps), Localization, Unity IAP (+ server receipt
validation), Remote Config, Analytics — the "non-negotiables" from the game plan,
mostly first-party in Unity.

## Net cost of the pivot
- **Kept:** all design docs, formulas, data model, content, Supabase backend, the
  architecture rule, and the milestone plan.
- **Rewritten:** sim logic (TS → C#, small at M0), UI (React → Unity UI), renderer
  (Pixi → Unity), tests (vitest → xUnit).
- **Dropped:** frictionless web play; the TS toolchain.
