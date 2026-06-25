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

## Set up on a new computer

### 1. Clone
```bash
git clone https://github.com/nodeJayS/idle-game.git
cd idle-game
```

### 2. Develop & test the simulation (no Unity needed)
Install the **[.NET 8 SDK](https://dotnet.microsoft.com/download)**, then:
```bash
dotnet test gamecore/GameCore.Tests
```
This is the fast, scriptable inner loop — build and verify systems here first, then
wire them into Unity. If the tests pass, the sim half is set up correctly.

### 3. Run the Unity client
1. Install **[Unity 6 LTS](https://unity.com/releases/unity-6)** via Unity Hub (3D / URP).
2. In Unity Hub → **Add** → select the `unity/` folder, then open it. First open is
   slow (Unity imports assets + restores packages from `Packages/manifest.json`).
3. Press **Play** — `Bootstrap` builds the scene in code and `CombatView` drives the
   auto-battle. The sim lives in `unity/Assets/GameCore/` under a no-engine-refs
   `GameCore.asmdef`.

> Saves are per-machine, written to the OS app-data dir (e.g. on Windows
> `…/AppData/LocalLow/DefaultCompany/unity/save.json`) — they are **not** in the repo,
> so a new computer starts with a fresh game.

### 4. (Optional) Unity MCP — drive the Editor from Claude
The repo ships an MCP config so a fresh clone can drive the Unity Editor (compile
checks, play-mode screenshots) with no extra config. Travels with the repo:
- **`.mcp.json`** (repo root) — points Claude at the bridge: `http://127.0.0.1:8080/mcp`.
- **`unity/Packages/manifest.json` + `packages-lock.json`** — add & pin the bridge
  package (`com.coplaydev.unity-mcp`, lock-pinned to a commit hash → reproducible).

Per-machine prereqs (can't be committed):
1. Install **Python 3.12+** and **[`uv`](https://docs.astral.sh/uv/)**, then launch
   Unity *after* installing so it inherits them on PATH.
2. Open the project in Unity, then **MCP For Unity** window → **Connect** tab →
   **HTTP Local** → **Start Server** (listens on `127.0.0.1:8080`).
3. Open the clone in Claude — the `UnityMCP` server connects automatically. Localhost
   only, no secrets.

## Milestones
**Phase A (M0–M9) ✅** — auto-combat, loot, leveling, stage ladder + boss gates, idle, persistence, feel pass, ranged class, polish.
**Phase B (depth)** — M10 multi-character ✅, M11 skills ✅, Tunic art pivot ✅; roster/gacha/live-service ahead.
Full status and the live-service roadmap are in [`CLAUDE.md`](CLAUDE.md); the durable design is in [`docs/game-design.md`](docs/game-design.md).
