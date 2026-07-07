# Idle ARPG (Unity)

A low-poly **3D idle ARPG** — Diablo/PoE-style loot & build depth. A 3-hero party
(each hero independently equippable from one shared bag) auto-clears dungeons,
monsters drop gear, you build out the party and push higher difficulty. Progress
accrues while you're away. Heroes are real MapleStory-2 models on a scripted
Blender→FBX pipeline; the world is faceted low-poly.

**The four docs** (each has one job — keep it that way):
- this README — what the project is + full setup on a new machine
- [`CLAUDE.md`](CLAUDE.md) — the working context a Claude session loads (architecture, stack, current systems, conventions)
- [`docs/game-design.md`](docs/game-design.md) — the durable design (loops, economy, data model, live-service arc)
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — the ordered "what's next" list (update it in the commit that ships an item)

## The one architecture rule
All combat / loot / idle / progression logic lives in **`GameCore` (pure C#, zero
`UnityEngine` references)**. Unity is the client and only *reads* simulation state;
a .NET server can reuse the exact same `GameCore` for authority later. Don't let
game logic leak into MonoBehaviours.

## Repo layout
```
unity/Assets/GameCore/   # THE sim — pure C#, no UnityEngine refs. Single source of truth.
unity/Assets/Game/       # MonoBehaviours, read-only client (Bootstrap, CombatView, views)
gamecore/GameCore.Tests/ # xUnit tests (compile the Assets/GameCore sources via a glob)
art/                     # hero manifests + decoded motion + the Blender bake scripts
docs/                    # game-design.md + ROADMAP.md
```

## Set up on a new machine

### 1. Clone (side by side, in one Projects dir)
| Repo | Source | Purpose |
|---|---|---|
| `idle-game` | github.com/nodeJayS/idle-game (private) | the game |
| `MS2Extract` | github.com/nodeJayS/MS2Extract (private) | .m2d archive extractor (ours; only for art authoring) |
| `Maple2.File` | github.com/kOchirasu/Maple2.File (public) | MS2 file-format library MS2Extract builds on |

### 2. Develop & test the simulation (no Unity needed)
Install the **[.NET 8 SDK](https://dotnet.microsoft.com/download)**, then:
```bash
dotnet test gamecore/GameCore.Tests
```
This is the fast, scriptable inner loop — build and verify systems here first,
then wire them into Unity. If the tests pass, the sim half is set up correctly.

### 3. Run the Unity client
1. Install **Unity 6 LTS** via Unity Hub (3D / URP).
2. Unity Hub → **Add** → select the `unity/` folder, open it (first import is slow).
3. Press **Play** — `Bootstrap` builds the scene in code; `CombatView` drives the battle.

Saves are per-machine at `%USERPROFILE%\AppData\LocalLow\DefaultCompany\unity\save.json`
(not in the repo). **Back it up before Play-mode testing** (CLAUDE.md verify loop).

### 4. (Optional) Unity MCP — drive the Editor from Claude
Committed with the repo: `.mcp.json` (points at `http://127.0.0.1:8080/mcp`) and the
bridge package pinned in `unity/Packages/manifest.json`. Per-machine: install
**Python 3.12+** and **`uv`**, launch Unity after installing (PATH), then
**MCP For Unity** window → Connect → HTTP Local → **Start Server**. Localhost only.

### 5. (Optional) The MS2 art pipeline — only needed to AUTHOR heroes
All baked assets are committed; skip this unless making new heroes/clips.
- **Blender 5.1** at `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`
  (scripts run headless: `blender -b --python art/skinned_body.py -- ...`).
- **MapleStory 2 client** (~13 GB, Mushroom Launcher → Steam CDN) at
  `C:\Games\MapleStory2`; extract `Character/Item/Textures/Xml.m2d` with MS2Extract
  into `C:\Games\MapleStory2\Extracted\` (paths are constants atop
  `art/skinned_body.py` / `art/tools/kf_motion.py`).
- New hero recipe: `art/heroes/<defId>.json` manifest → decode 9 clips into
  `art/motion/<defId>/` → bake with `--renders` (EYEBALL them — item transforms
  and dyes lie) → `--export` → Unity menu `Tools > Build Hero Animators`.
  The four shipped heroes are worked examples.
- Shop for outfit items with `python art/tools/wardrobe.py <keywords>`
  (`--slot CP --gender f`, `--json`) — joins the extracted item NIFs with the
  Xml.m2d name/slot tables and prints manifest-ready paths. Index caches at
  `Extracted\wardrobe_index.json` (outside the repo); `--rebuild` after
  re-extracting.
