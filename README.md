# Idle ARPG (Unity)

A cozy low-poly **3D idle ARPG** — Diablo/PoE-style loot & build depth, *Tunic*-ish
visual surface. A 4-hero party (each hero independently equippable) auto-clears
dungeons, monsters drop gear, you build out the party and push higher difficulty.
Progress accrues while you're away.

> **Status:** pivoted from a web (Vite/React/Pixi) prototype to **Unity (C#)**.
> The web prototype is archived under `web-prototype/`. The simulation lives in a
> pure-C# `GameCore` library (tested with `dotnet test`) that Unity references as its
> client. M0 (scene) and M1 (auto-combat) are done. Gacha and live-service are
> deferred; the architecture supports both later.

## Why Unity
The target look (stylized low-poly 3D) + native global mobile fit Unity's pipeline
(URP, lighting, animation, Addressables, IAP, Localization).

## Repo layout
```
gamecore/         # PURE C# simulation — no UnityEngine refs. The real game logic.
  GameCore/       #   library (Models, Rng, GameConfig, Save, Party, Stats, Combat)
  GameCore.Tests/ #   xUnit tests
unity/            # Unity project; references GameCore (game code in Assets/Game)
docs/             # game-design.md — the durable design
web-prototype/    # ARCHIVED Vite/React/Pixi prototype (pre-pivot)
```
See [`CLAUDE.md`](CLAUDE.md) for the working context (architecture, stack, milestone
status) and [`docs/game-design.md`](docs/game-design.md) for the full design.

## The one architecture rule (unchanged by the pivot)
All combat / loot / idle / progression logic lives in **`GameCore` (pure C#, zero
`UnityEngine` references)**. Unity is the client and only *reads* simulation state;
a .NET server can reuse the exact same `GameCore` for authority later. Don't let
game logic leak into MonoBehaviours.

## Develop & test the simulation (no Unity needed)
```bash
dotnet test gamecore/GameCore.Tests
```
This is the fast, scriptable inner loop — port and verify systems here first, then
wire them into Unity.

## Run the Unity client
Open `unity/` in **Unity 6 LTS** (3D / URP) and press **Play** — `Bootstrap` builds
the scene in code and `CombatView` drives the auto-battle. `GameCore` sources are
mirrored into `unity/Assets/GameCore/` under a no-engine-refs `GameCore.asmdef`.

## Milestones
M0 scene ✅ · M1 auto-combat ✅ · M2 loot · M3 rifts · M4 idle · M5 persistence · M6 feel.
Full status and the live-service roadmap are in [`CLAUDE.md`](CLAUDE.md).
