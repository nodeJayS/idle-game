# Reproducing this setup on a new machine

Everything needed to continue the idle game + MS2 port pipeline elsewhere.
(Auto-memory does NOT transfer between machines — the repo docs are the source
of truth: `CLAUDE.md`, `docs/ms2-port-plan.md`, this file.)

## 1. Install
- **Unity 6 LTS** (open `idle-game/unity/`).
- **Blender 5.1** — expected at `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`
  (art scripts are invoked headless: `blender -b --python art/<script>.py -- ...`).
- **.NET 8 SDK** (GameCore tests + MS2Extract).
- **Python 3** on PATH (NIF/.kf parser tools, no third-party deps).
- **MapleStory 2 client** (~13 GB) via Mushroom Launcher → Steam CDN, installed to
  `C:\Games\MapleStory2` (art tool paths are hardcoded to this; adjust the
  constants at the top of `art/skinned_body.py` and `art/tools/kf_motion.py` if
  it lands elsewhere).

## 2. Clone (side by side, in one Projects dir)
| Repo | Source | Purpose |
|---|---|---|
| `idle-game` | github.com/nodeJayS/idle-game (private) | the game |
| `MS2Extract` | github.com/nodeJayS/MS2Extract (private) | .m2d archive extractor (ours) |
| `Maple2.File` | github.com/kOchirasu/Maple2.File (public) | MS2 file-format library MS2Extract builds on |
| `Maple2Tools` | github.com/Wunkolo/Maple2Tools (public) | reference tooling (optional) |

## 3. Rebuild the local asset extract (NOT in any repo, ~a few GB)
From `MS2Extract/` (see its README for the exact commands):
extract `Character.m2d`, `Item.m2d`, `Textures.m2d`, and `Xml.m2d` into
`C:\Games\MapleStory2\Extracted\{Character,Item,Textures,Xml}`.

The reference scenes `f_body.blend` / `ms2_knight.blend` in `Extracted\` were
hand-assembled; regenerate meshes with `art/tools/nif_mesh.py` if needed
(`f_body.ply` etc. — see the plan doc). Note: the appended `MS2_f_body` object
in `f_body.blend` carries a 0.01 object scale; `art/skinned_body.py` handles it.

## 4. Regenerate game assets (all committed, so only needed when iterating)
```
blender -b --python art/skinned_body.py -- --export unity/Assets/Resources/Models/warrior_basic_skinned.fbx
python art/tools/kf_motion.py <anim.kf> art/motion/<clip>.json   # decode more clips
```
`HeroAnimator.controller` is committed; if clips are added, rebuild it in-editor
(states/transitions are documented in `unity/Assets/Game/SkinnedHero.cs`).

## 5. Verify
- `dotnet test gamecore/GameCore.Tests` (sim, no Unity needed).
- Open `unity/` in Unity 6, press Play — Bootstrap builds the scene in code; the
  warrior should spawn as the skinned MS2 body running the decoded clips.
- Save file lives at `%USERPROFILE%\AppData\LocalLow\DefaultCompany\unity\save.json`
  — back it up before Play-mode testing (CLAUDE.md verify loop).

## Current state / next work
See `docs/ms2-port-plan.md` — the phased goal (bodies → gear → anims → sound →
skills). Phase 0 (full-fidelity NIF importer: UVs/normals/weights/textures) is
the next unstarted step.
