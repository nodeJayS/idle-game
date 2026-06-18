# Idle ARPG (Unity)

A low-poly **3D idle ARPG** — Diablo/PoE-style loot & build depth. A 4-hero party (each hero independently equippable) auto-clears dungeons, monsters drop gear, you build out the party and push higher difficulty.
Progress accrues while you're away.

> **Status:** the simulation lives in a pure-C# `GameCore` library (tested with
> `dotnet test`) that Unity references as its client. M0 (scene) and M1 (auto-combat)
> are done; M2 (loot) is complete. Gacha and live-service are deferred; the
> architecture supports both later.

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
M0 scene ✅ · M1 auto-combat ✅ · M2 loot ✅ · M3 stages · M4 idle · M5 persistence · M6 feel.
Full status and the live-service roadmap are in [`CLAUDE.md`](CLAUDE.md).
