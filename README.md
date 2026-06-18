# Idle ARPG (Unity)

A cozy low-poly **3D idle ARPG** — Diablo/PoE-style loot & build depth, *Tunic*-ish
visual surface. A 4-hero party (each hero independently equippable) auto-clears
dungeons, monsters drop gear, you build out the party and push higher difficulty.
Progress accrues while you're away.

> **Status:** mid-pivot from a web (Vite/React/Pixi) prototype to **Unity (C#)**.
> The web prototype is archived under `web-prototype/`. The simulation is being
> ported to a pure-C# `GameCore` library (tested with `dotnet test`) that Unity
> references as its client. Gacha is deferred; the architecture supports it later.

## Why Unity
The target look (stylized 3D) + native global mobile are a good fit for Unity's
pipeline (URP, lighting, animation, Addressables, IAP, Localization). See
[`docs/unity-evaluation.md`](docs/unity-evaluation.md) for the full trade-off
analysis and [`docs/unity-migration.md`](docs/unity-migration.md) for the plan.

## Repo layout
```
gamecore/         # PURE C# simulation — no UnityEngine refs. The real game logic.
  GameCore/       #   library (Models, Rng, GameConfig, Save, Party, Persistence)
  GameCore.Tests/ #   xUnit tests (mirrors the old vitest suite)
unity/            # Unity project (created via Unity Hub; references GameCore)
docs/             # design + plan + Unity evaluation/migration
web-prototype/    # ARCHIVED Vite/React/Pixi prototype (M0 worked here)
```

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

## Set up the Unity client
1. Install **Unity Hub** + a **Unity 6 LTS** editor with the **3D (URP)** template.
2. Create the project at `unity/` (or open it if already created).
3. Add `GameCore` to Unity: place the `gamecore/GameCore/*.cs` sources under
   `unity/Assets/GameCore/` with a `GameCore.asmdef` (no engine refs), **or**
   reference a built `IdleGame.GameCore.dll`. See the migration doc.
4. First milestone in Unity: the 3D iso scene + the party lead (M0-equivalent).

## Milestones (engine-independent; same order as the web plan)
M0 scene · M1 auto-combat · M2 loot · M3 rifts · M4 idle · M5 persistence · M6 feel.

See [`docs/idle-gacha-game-plan.md`](docs/idle-gacha-game-plan.md) and
[`docs/game-core-design.md`](docs/game-core-design.md) for the full design.
