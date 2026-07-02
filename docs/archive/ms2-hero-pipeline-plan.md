# MS2-style hero pipeline — handover plan (written 2026-07-01)

> **STATUS UPDATE (later 2026-07-01): plan largely superseded — shipped in two
> commits (ef2c3d1, eb51f58).** The user overruled the IP rule (§3): the real
> extracted f_body mesh now ships directly. What exists now:
> - `art/skinned_body.py` — imports MS2_f_body from the extract, rigs it on the
>   19-bone skeleton measured from the NIF (`art/tools/nif_skeleton.py`),
>   auto-weights, rebuilds idle/run/attack actions from decoded .kf motion,
>   exports `Resources/Models/warrior_basic_skinned.fbx`.
> - `art/tools/kf_motion.py` — decodes .kf (B-spline compressed + raw-key) into
>   world-space per-bone deltas (`art/motion/*.json`).
> - Unity: `SkinnedHero.cs` (+ `IHeroAnim` seam, `HeroAnimator.controller`) —
>   SpawnView tries skinned first, then ModelHero, then chibi. Verified in Play.
> - Remaining: equipment/hair layering on the base body (trace `Item/` NIFs onto
>   the same skeleton), more clips (walk/bore/hit/death), the other heroes,
>   in-Blender eyes/face, and the pre-existing hero-float quirk
>   (CombatView.SyncViews writes v.Height into hero Y).

**Goal:** heroes that look and move like MapleStory 2 chibis — MS2 proportions, a
16–18-bone skinned skeleton, and animation clips authored from *measured* MS2 motion —
while monsters/world stay cheap rigid/faceted (Tunic-style mixed fidelity).
Heroes are the ONLY skinned things; everything else keeps the current pipeline.

**Read first:** `CLAUDE.md` (architecture rules, Unity MCP verify loop, save-backup
loop), `art/warrior.py` (the current model source + its header comments). Auto-memory
`blender-hero-pipeline` has the pipeline gotchas.

---

## 1. What already exists (all verified working this session)

### Reference assets (extracted from the real MS2 client — NOT in the repo, local only)
- `C:\Games\MapleStory2\` — full 13.1 GB MS2 client (via Mushroom Launcher → Steam CDN).
- `C:\Games\MapleStory2\Extracted\` — unpacked archives:
  - `Character/` — `female/f_body.nif` (2,034-vert base body), `male/m_body.nif`,
    ~3,858 `.kf` animation files (Gamebryo keyframes, named `<class>_<action>_a.kf`),
    `.kfm` manifests.
  - `Item/` — 6,500 equipment meshes (hair `0/02/*hair*`, tops `1/14/`, pants `1/15/`,
    gloves `1/16/`, shoes `1/17/`, caps/hoods `1/13/`, shields `1/41/`).
  - `Textures/` — 33k DDS.
  - `f_body.blend`, `ms2_knight.blend` — double-clickable reference scenes. The knight =
    body + basichair01 + empireknight set (cp/cl/pa/gl/sh) + goldaquilashield, i.e. the
    user's chosen look for the warrior.
- Extraction tools (rebuildable): `C:\Users\jwn13\Projects\MS2Extract` (C# console app
  on `Maple2.File` repo next to it; `dotnet run -- <archive.m2d> <outDir> [filter]`).

### Repo tooling (`art/tools/`)
- `nif_mesh.py` — Gamebryo 30.2.0.3 NIF → PLY triangle meshes. Handles interleaved
  NiDataStreams; picks position components by non-unit-length heuristic; pairs index
  streams with the following vertex stream. Usage:
  `python art/tools/nif_mesh.py <in1.nif> [in2.nif ...] <out.ply>`
  Worn items assemble correctly in bind space by just concatenating with the body;
  WEAPONS/SHIELDS are in hand-space (land at origin — place manually).
- `nif_probe.py` — dumps a NIF header (block types/sizes, string table). Start here
  when extending the parser (e.g. to `.kf`).
- `ply_render.py` — headless Blender render of a PLY (front/f34/side, auto-framed):
  `blender -b --python art/tools/ply_render.py -- <in.ply> <ABSOLUTE outdir> <basename>`
  (relative outdir silently vanishes — always absolute).

### Game pipeline today
- `art/warrior.py` — the shipped warrior model source (smooth "toy" style — smooth-shaded
  UV spheres, Bevel-modifier slabs, Solidify hair; metal crisp). Exports FLAT name-prefixed
  parts (`<joint>.<part>`) to `Resources/Models/warrior_basic.fbx`.
- `art/valkyrie.py` — shelved faceted design; keeps prism()/layered-anime-eye/hood
  techniques. The USER'S CHOSEN DIRECTION: this valkyrie/knight character, rebuilt
  MS2-like, ships as the warrior.
- `unity/Assets/Game/ModelHero.cs` — loads the FBX, rebuilds the 7-joint skeleton in
  code, reparents parts by name prefix (worldPositionStays). `ChibiAnimator.cs` drives
  those joints procedurally (walk/attack layering, rest pose = identity).
- Heroes without a model fall back to code-built `ChibiHero` chibis.

### Measured MS2 facts (from the real f_body mesh — use these, don't re-derive)
- Total height 133.5 cm (MS2 units = cm, Z-up in Blender space; our exports face -Y).
- **Head = 42% of total height, 58 cm wide — WIDER than body+arms (53 cm).**
- Torso is a slim hourglass; body is narrow. The silhouette mass comes from GEAR
  (hood wings, layered pauldrons, skirt flare, chunky boots), not the body.
- MS2 skeleton is a ~40-bone Bip01 biped, but ~half is fingers/tail/twist bones.
  16–18 bones reproduce the motion: pelvis-spine-chest-neck-head, clavicle-upperArm-
  forearm-hand ×2, thigh-calf-foot ×2.

---

## 2. The slices (in order; each ends verified + committed)

### Slice A — Warrior rebuild, MS2 proportions (geometry only, current 7-joint rig)
Rebuild the knight/valkyrie design in `art/warrior.py`'s smooth style with the measured
proportions: head ~42% of height and wider than the body; slim tapered torso; thin arms
tight to the body; silhouette from gear (hood + wings, layered pauldrons, skirt flare,
armored boots, big kite shield via `prism()` from valkyrie.py, layered anime eyes ×2
highlights, sculpted hair clumps not a bowl shell).
- Iterate renders side-by-side against `ms2_knight.blend` renders (use
  `art/tools/ply_render.py` output as the comparison target).
- Keep the shared BONES table/joint positions AS-IS (ModelHero constants must match).
- Ship over `Resources/Models/warrior_basic.fbx`, verify in Play, commit.
- This slice alone is safe to do without any skeleton work.

### Slice B — Skinned skeleton + Unity import proof
- In `warrior.py`: build the 16–18 bone armature (extend the existing
  `build_rig_and_skin()`), skin the parts — rigid weights per part, BLENDED loops at
  elbows/knees/waist so limbs bend. Author ONE scripted idle clip (breathing bob).
- Export skinned FBX (`--skinned` path; `bake_space_transform=False` there; expect the
  usual axis-conversion rest rotations — irrelevant once clips drive the bones).
- Unity: new loader path (e.g. `SkinnedHero.cs`) — instantiate FBX, play the idle via
  `Animation`/`Animator`. Keep `ModelHero` as fallback. Verify the idle breathes in Play.
- Do NOT touch GameCore. CombatView seam only.

### Slice C — Motion measurement from MS2 .kf
- Extend the NIF parser to `.kf` (`nif_probe.py` first): NiControllerSequence →
  per-bone interpolators. WARNING: possibly B-spline compressed
  (NiBSplineCompTransformInterpolator) — check one file before promising timelines.
  Target clips: female idle (`*_bore_idle_*`), a run, one attack swing.
- Output NUMBERS, not data: pelvis bob amplitude/frequency, spine lean, arm swing arcs
  (degrees), attack anticipation/strike/settle timing (frames). Store the measured
  table IN the plan doc or `art/tools/ms2_motion_notes.md`.
- IP rule: measurements only. Never convert/retarget/ship Nexon keyframe data.

### Slice D — Authored clips + Animator wiring
- Scripted keyframing in `warrior.py` (or shared `art/tools/clips.py`): idle/run/attack
  clips built from the Slice C numbers. Export with `bake_anim=True`.
- Unity: Animator states idle↔run (crossfade from `SetMoving`), attack trigger
  (`TriggerAttack` → play, movement cancels). Replaces `ChibiAnimator` for skinned
  heroes only; code-built chibis keep the procedural path.

### Slice E — Scale the roster
- Magician (fire), Thief, Ice Mage on the same skeleton: per-hero cost = palette +
  gear geometry + kit props. Reuse clips wholesale.
- Remove/park `ChibiHero` fallbacks as models land. Monsters stay primitives/rigid.

---

## 3. Rules & gotchas (violating these wasted real time this session)

- **IP**: extracted MS2 assets are Nexon's. Local reference ONLY — never into the repo,
  never into a build. Measurements/numbers are fine; bytes are not.
- **Colors**: author palettes in sRGB, convert with `srgb_to_linear()` before assigning
  to Principled Base Color (Blender is linear; skipping this = washed-out import).
  Blender previews need `view_transform = "Standard"` (AgX desaturates flat colors).
- **FBX**: flat name-prefixed parts for the rigid pipeline (transform hierarchies import
  with +90° rest rotations — breaks `ChibiAnimator`'s absolute writes). FBX flips X
  (Blender L-side parts land on Unity +X; ModelHero's joint signs already account).
- **Unity verify loop**: back up `save.json` BEFORE Play, restore after stop; if the
  editor window is occluded the player loop freezes (`Time.frameCount` stuck) — drive
  frames with `EditorApplication.Step()` + `Time.captureDeltaTime = 1/60`.
  `SetMoving(true)` cancels attacks — pose attacks by disabling the animator component
  and invoking `Update()` via reflection.
- **Blender headless**: absolute output paths ALWAYS. `.ply` must be File→IMPORTED
  (or build a .blend), not opened.
- Commit per slice, `Co-Authored-By: Claude <model> <noreply@anthropic.com>` per
  CLAUDE.md. GameCore untouched by this entire plan (client/art work only).
