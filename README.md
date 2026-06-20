# Idle ARPG (Unity)

A low-poly **3D idle ARPG** — Diablo/PoE-style loot & build depth. A 3-hero party (each hero independently equippable from one shared bag) auto-clears dungeons, monsters drop gear, you build out the party and push higher difficulty.
Progress accrues while you're away.

> **Status:** the simulation lives in a pure-C# `GameCore` library (tested with
> `dotnet test`, 216 passing) that Unity references as its client. **Phase A (core
> loop, M0–M9) is complete**; **Phase B (depth)** is underway — multi-character
> foundation, skills firing in combat, and a *Tunic*-style art pass (height-blend
> shader, faceted vertex-coloured world, dappled lighting; heroes are code-built
> chibi placeholders pending Blender models). Gacha and live-service are deferred;
> the architecture supports both later.

## Repo layout
```
unity/Assets/GameCore/   # THE sim — pure C#, no UnityEngine refs. Single source of truth.
unity/Assets/Game/       # MonoBehaviours, read-only client (Bootstrap, CombatView)
gamecore/GameCore.Tests/ # xUnit tests (compile the Assets/GameCore sources via a glob)
docs/                    # game-design.md — the durable design
```
See [`CLAUDE.md`](CLAUDE.md) for the working context (architecture, stack, milestone
status) and [`docs/game-design.md`](docs/game-design.md) for the full design.

## The one architecture rule
All combat / loot / idle / progression logic lives in **`GameCore` (pure C#, zero
`UnityEngine` references)**. Unity is the client and only *reads* simulation state;
a .NET server can reuse the exact same `GameCore` for authority later. Don't let
game logic leak into MonoBehaviours.

## Develop & test the simulation (no Unity needed)
```bash
dotnet test gamecore/GameCore.Tests
```
This is the fast, scriptable inner loop — build and verify systems here first, then
wire them into Unity.

## Run the Unity client
Open `unity/` in **Unity 6 LTS** (3D / URP) and press **Play** — `Bootstrap` builds
the scene in code and `CombatView` drives the auto-battle. The sim lives in
`unity/Assets/GameCore/` under a no-engine-refs `GameCore.asmdef`.

## Milestones
**Phase A (M0–M9) ✅** — auto-combat, loot, leveling, stage ladder + boss gates, idle, persistence, feel pass, ranged class, polish.
**Phase B (depth)** — M10 multi-character ✅, M11 skills ✅, Tunic art pivot ✅; roster/gacha/live-service ahead.
Full status and the live-service roadmap are in [`CLAUDE.md`](CLAUDE.md); the durable design is in [`docs/game-design.md`](docs/game-design.md).
