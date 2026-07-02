# Handoff: magician + thief on the MS2 skinned pipeline (written 2026-07-02)

**Read first:** `CLAUDE.md`, then `docs/ms2-port-plan.md` (every phase ✅ done,
with per-phase status notes = the format knowledge). The warrior is the fully
worked example — trace her files before building anything.

## State at handoff (all pushed, main @ ab834aa)
The full MS2 port shipped 2026-07-02: NIF importer with UVs/normals/real
weights/textures/tints (`art/tools/nif_import.py`), gender-parameterized body
builder + hero-manifest gear baking (`art/skinned_body.py`,
`art/heroes/warrior_basic.json`), .kf clip decoding (`art/tools/kf_motion.py`),
FSB5 sound extraction (`art/tools/fsb_extract.py`), and the Unity side
(`SkinnedHero.cs` loader/animator + shared `HeroAnimator.controller` with 9
states: Idle/Run/Attack/Attack2/Skill1/Skill2/Bore/Hit/Death). The warrior is
the MS2 empire knight with 9 decoded clips, skill bindings (cycloneslash →
cycloneshield clip, shieldcharge → shieldrush clip), sounds, grounded
speed-matched movement. SpawnView tries SkinnedHero → ModelHero → ChibiHero,
so a new `<defId>_skinned.fbx` in Resources/Models ships itself.

## The task: magician_basic and thief_basic as skinned MS2 heroes

### 0. One refactor FIRST: per-hero motion dirs
`art/motion/<gender>/` is currently shared — `clip_roles()` bakes EVERY json in
the dir into EVERY same-gender FBX. Three heroes need three clip sets. Move to
`art/motion/<heroId>/` (manifest knows its id) with the 9 standard roles per
hero; re-export the warrior to prove nothing broke. Keep the role names exactly
(idle/run/attack/attack2/bore/hit/death/skill1/skill2) — the shared Unity
controller binds states by role.

### 1. Kits (GameCore — DO NOT change data; presentation only)
- magician_basic actives: `fireball` (L1), `scorch` (L10) — check GameConfig.
- thief_basic actives: `shadowstab` (L1), `vitalstrike` (L10) — check GameConfig.
- Magician is the FIRE wizard (auto-memory: ice/other mages are future heroes).

### 2. Source material (all under C:\Games\MapleStory2\Extracted)
- Clips `Character\female\` (or male): wizard_*.kf (fire kit: wizard_fireball,
  flamewave, etc.), assassin_*.kf (thief: star/dagger kit). Locomotion: idle_a
  shared; runs are per-weapon (`wand_run_a` / `staff_run_a` for magician,
  `dagger_run_a` for thief — check what exists). hit = knock_back_a,
  death = dead_a, bore = wizard_bore_a / assassin_bore_a if present.
- Gear `Item\`: robes/hats for a fire wizard, leathers/hoods for the thief —
  browse by name (`Get-ChildItem -Recurse | Where Name -match ...`). Weapons:
  staffs/wands 1\31 (verify), daggers 1\34 (verify) — attach via
  `Weapon_Hand_R_Point` (+L for offhand dagger). Hair: 0\02.
- Sounds `Data\Sound\MS2Sound_effect.fsb`: `--list wizard`, `--list assassin`
  (Skill_Wizard_FireBall_* already shipped for the projectile).

### 3. Recipe per hero (warrior = worked example)
1. Write `art/heroes/<defId>.json`: gender, items, attach (weapons), skills
   bindings (OUR skill ids → slot + sound set).
2. Decode 9 clips into `art/motion/<defId>/`:
   `python art/tools/kf_motion.py <file.kf> art/motion/<defId>/<role>.json [--nif <body.nif>]`
   (use the male NIF for a male hero — male clips live in Character\male).
3. Extract sounds: `python art/tools/fsb_extract.py <bank> unity/Assets/Resources/Sound <filter>`.
4. Bake: `blender -b --python art/skinned_body.py -- --hero art/heroes/<defId>.json
   --renders <scratch> --export unity/Assets/Resources/Models/<defId>_skinned.fbx`
   — eyeball the renders BEFORE shipping (gear fit, tints, weapon grip).
5. Unity: set clip loops (idle/run only) on the new FBX via editor script
   (see plan doc Phase 3 note); the shared HeroAnimator needs NO rebuild, but
   clips are per-FBX — Animator on each hero uses its own FBX takes only if the
   controller motions point at THAT fbx's clips. ⚠ The controller currently
   references the WARRIOR's clips directly → other heroes will play warrior
   clips. Fix: use an AnimatorOverrideController per hero (build once in
   editor script: base = HeroAnimator, override each state's clip with the
   hero's takes, save as Resources/Models/<defId>Animator.overrideController;
   SkinnedHeroAnim loads its own if present, else the base).
6. Verify in Play (backup save.json first — see CLAUDE.md loop), commit per hero.

### 4. Magician specifics
- Ranged: basic attack fires the fireball projectile (CombatView `_projectileFx`);
  her Attack/Attack2 states should be CAST anims (wizard cast), not sword swings.
  TriggerLunge plays "Swing_Sword" for any skinned hero's basic attack — make
  the sound per-hero (bind a basic-attack sound in the manifest, or key off
  ranged role → wizard cast sound) instead of hardcoded.
- Fire palette; robes tint via OverrideColor0 if the item is dyeable.

### 5. Rules (violations wasted time before)
- NO MS2 music ever (user ban — SFX only). NO MS2 skill names/numbers (2+2
  template is sacred). GameCore = pure C#, `dotnet test gamecore/GameCore.Tests`
  (392 green at handoff).
- Audio is silent under `EditorApplication.Step` (editor pause-step) — that's
  not a bug.
- Never mutate loaded Blender images (breaks all rendering downstream).
- DDS ship as-is, lowercase, next to the FBX; SkinnedHero wires textures by
  material name at runtime; tints/skills ride sidecar txt files.
- Blender 5.1 at `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`,
  headless, absolute output paths only.
- Commit per verified slice; end commit messages with the Co-Authored-By line
  per CLAUDE.md. Push after each hero.
