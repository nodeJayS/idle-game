# MS2 full-port pipeline — THE GOAL (set 2026-07-02)

**User's goal statement:** male + female base meshes generated from the MapleStory 2
client files, equippable with MS2 gear to compose unique heroes; MS2 animations on
the rig; MS2 sounds; MS2 skills. "Basically port the entire thing over to our game."
(IP rule overruled by user 2026-07-01 — real Nexon assets ship in this repo.)

**Ground truth about the source (verified in the client at C:\Games\MapleStory2\):**
- `Data\Resource\Model\Character.m2d` (125 MB) — bodies + ~3,858 .kf clips + .kfm manifests. EXTRACTED already.
- `Data\Resource\Model\Item.m2d` (520 MB) — 6,500 gear meshes. EXTRACTED already.
- `Data\Resource\Model\Textures.m2d` (1.8 GB) — 33k DDS. EXTRACTED already.
- `Data\Xml.m2d` (182 MB) — ALL game tables: items, skills, sounds mapping. NOT yet extracted. `Maple2.File` (repo at C:\Users\jwn13\Projects\Maple2.File) parses these.
- `Data\Sound\*.fsb/.fev` — FMOD banks (BGM, effects, attack voices, mob sounds). Need fsb extraction tooling (python-fsb5 / vgmstream).
- `Data\Resource\Model\Effect.m2d` (672 MB) — Gamebryo particle VFX (hard to port; see Phase 5).
- Extraction tool: `C:\Users\jwn13\Projects\MS2Extract` (`dotnet run -- <archive.m2d> <outDir> [filter]`).

**What already works (2026-07-01, commits ef2c3d1/eb51f58):** NIF skeleton extraction
(`art/tools/nif_skeleton.py`), .kf decoding both compressed + raw (`art/tools/kf_motion.py`),
f_body skinned on the measured 19-bone rig with decoded idle/run/attack in-game
(`art/skinned_body.py` → `warrior_basic_skinned.fbx`, `SkinnedHero.cs`, `HeroAnimator.controller`).

---

## Phase 0 — Full-fidelity NIF importer — ✅ DONE 2026-07-02
`art/tools/nif_import.py`: NiMesh semantics (INDEX/POSITION(_BP)/NORMAL(_BP)/
TEXCOORD/BLENDINDICES/BLENDWEIGHT/BONE_PALETTE), real skin weights via
NiSkinningMeshModifier bone lists, texture chain (byte-granular scan),
OverrideColor0 tint (MS2 customization: grayscale skin tex x color; pure-primary
values = face-mask placeholders, skip). f_body = 10 parts (HR scalp, CL_Skin,
FA face w/ alpha `00300003_f_cuteface.dds`, FA_Skin, FA_EA ears, GL hands,
PA_Skin, SH, PA_Panty, CL_Bra). DDS ship as-is next to the FBX (Unity reads
DXT natively; Blender can't re-encode); SkinnedHero.SetupMaterial wires
textures by material name at runtime + URP alpha-clip for the face + skin tint.
Verified in Play. Original phase text follows:
Extend the NIF parser to pull **UVs, normals, vertex colors, real skin weights**
(NiSkinningMeshModifier) and **material/texture references**, importing straight
NIF→Blender (retire the lossy PLY hop). DDS → Unity materials with alpha-clip
(face shell, lashes, hair all need alpha). Fixes the lumpy face as a side effect
(the face plate/lash shells are alpha-textured overlays).
*Output:* `art/tools/nif_import.py` usable by every later phase. ~1 session.

## Phase 1 — Base bodies — ✅ DONE 2026-07-02
Pipeline is gender-parameterized: `art/skinned_body.py --gender male|female`
(joint table now DERIVED from each body NIF via nif_skeleton.load_world_positions
— no hardcoded skeletons), `kf_motion.py --nif <body.nif>` decodes against the
matching rest skeleton, clips live in `art/motion/<gender>/`. Male body verified
in Blender (BraveFace `00300001_M_BraveFace.dds`, `M_PA_D.DDS` trunks, own idle
timing 2.20s); female re-exported + Unity-verified. Male FBX deliberately NOT
shipped to Unity yet — Phase 2 bakes body+gear per hero def, so the naked male
body has no consumer until then. Gotcha fixed: never mutate Blender images for
texture conversion (breaks rendering); DDS are copied as-is at export, lowercase.
Original phase text follows:
Run f_body AND m_body through the Phase-0 importer onto their measured skeletons
(re-run nif_skeleton on m_body — layout is the same Bip01, proportions differ).
One parameterized body script replaces `skinned_body.py`; per-gender skinned FBX
with real face/skin textures and NIF-authored weights (replaces heat-map guess).
*Output:* `body_female.fbx`, `body_male.fbx` + textures. Small once Phase 0 lands.

## Phase 2 — Equipment → unique heroes — ✅ CORE DONE 2026-07-02
Hero manifests work: `art/heroes/<defId>.json` (gender + item NIF paths +
hand-space attach list) → `skinned_body.py --hero <json>` bakes body+gear into
one FBX. Hiding rule: gear parts replace SAME-NAMED body parts (glove item
carries its own GL/GL_Skin) + worn CL/PA hide bra/panty. Weapons/shields
transform by the Weapon_*_Point world transform and rigid-bind to the hand
bones. Per-material MS2 tints (skin/hair OverrideColor0) export as
<fbx>_tints.txt; SkinnedHero applies them at runtime. warrior_basic =
empireknight set + basichair01 + goldaquilashield + goldenlongsword, verified
in Play. REMAINING in this phase: extract Xml.m2d + Maple2.File item table so
manifests can reference item IDS instead of raw NIF paths; male gear variants.
Original phase text follows:
1. Extract `Xml.m2d`; use Maple2.File to dump the **item table** (item id → mesh
   path, slot, gender variants, icon).
2. Importer binds worn-item NIFs to the body skeleton (they're authored in body
   bind space; weapons/shields go to the `Weapon_*_Point` bones already in the rig).
3. **Hero def = gender + item ids (+ optional palette tint)** in a manifest the
   build script reads. Start with BAKED composition: body+gear merged into one
   FBX per hero def (current SkinnedHero loader works unchanged). Runtime modular
   dress-up (shared-bone SkinnedMeshRenderers in Unity) is the later upgrade when
   the roster or a wardrobe feature demands it.
*Output:* the empireknight warrior (finally with her gear), then any hero is a
manifest entry. 1–2 sessions.

## Phase 3 — Animation library — ✅ DONE 2026-07-02
7-clip set decoded for the warrior (female): idle, sword_run, crossswing +
guardianswing (TriggerAttack picks randomly), knight_bore fidget (fires after
7–14s uninterrupted idle), knock_back hit flinch, dead_a collapse. Every JSON
in art/motion/<gender>/ becomes an FBX take automatically (clip_roles()).
HeroAnimator: 7 states; Dead bool (AnyState→Death, Death→Idle on revive), Hit
from Idle/Run/Bore, movement cancels everything. Seams: ApplyHitReaction →
TriggerHit, party Death → SetDowned(true), Respawn → SetDowned(false).
Verified in Play (Idle/Run/Attack/Hit seen live; Death/revive driven directly).
.kfm manifest parsing deferred — hero manifests list clips explicitly instead.
Original phase text follows:
Parse the `.kfm` manifests (class/weapon → anim-set mapping); batch `kf_motion.py`
over the needed sets (idle/walk/run/bore/hit/dead + per-weapon attacks + skill
anims). Clip sets keyed by weapon type; shared AnimatorController + per-hero
overrides (AnimatorOverrideController). Decoder exists — this is batching + wiring.
~1 session.

## Phase 4 — Sound — ✅ DONE 2026-07-02
`art/tools/fsb_extract.py`: pure-python FSB5 extractor — MS2 banks are all
MPEG codec, so samples are raw MP3 (no transcoding, no external tools).
Shipped set (Resources/Sound/): Swing_Sword x3, Hit_SwordDefault x3, wizard
fireball launch, CH_Levelup, BadWood_Dead x2. SoundFx helper (variant sets by
_NN suffix, per-set rate limit, 2D one-shot channel). Hooks: TriggerLunge->
swing, PlayImpact->hit, enemy Death, level-up, fireball launch.
NO BGM by user decision (2026-07-02): MS2 soundtracks are too recognizable —
sound EFFECTS only. Don't re-add MS2 music.
NOTE: audio is silent under EditorApplication.Step (editor pause-step) —
verify sound with real Play, not stepped frames.
Original phase text follows:
Extract FMOD banks (`python-fsb5` or vgmstream) → OGG into Unity; use the Xml
sound tables to map events → sound ids. Wire into CombatView seams (attack, hit,
death, level-up, UI) + BGM. ~1 session incl. tooling.

## Phase 5 — ✅ DONE 2026-07-02 (REVISED per user: no MS2 names/numbers)
User decision: GameCore skill DATA stays ours (2+2 template, own balance);
MS2 supplies only presentation (clips + sounds). Shipped:
- GameCore: SkillEffectKind.Dash (gap closer: leap to nearest in Range, strike
  on arrival, skipped in melee contact). Warrior actives reworked: cycloneslash
  (aoe spin, 5s cd) + shieldcharge (dash, 8s cd, unlock L10); cleave/warcry
  stay as library rows; Save.Migrate transfers invested ranks. 392 tests pass.
- Presentation bindings: manifest "skills" section (our skill id -> slot +
  MS2 sound set) exports as <fbx>_skills.txt; SkinnedHeroAnim.TriggerSkill
  routes to Skill1/Skill2 states (knight_cycloneshield / knight_shieldrush
  clips) + plays the bound sound. Skill2 plays THROUGH the dash (exit-time
  only); Skill1 cancels on movement. Chibi heroes fall back to TriggerAttack.
- VFX: rebuilt "charge" ground rings on CombatJuice (takeoff + arrival).
Verified live: Skill1+Skill2 fired in combat, dash = 3.2-unit frame jump.
Original phase text follows:
- **Data:** parse skill tables from Xml.m2d (Maple2.File schemas); translate into
  GameCore `GameConfig` entries. GameCore stays pure C# — this is data mapping
  only. Keep the 2+2 archetype template as the frame; MS2 supplies names, numbers,
  elements, anim/sound ids (per user decision — see Decisions).
- **VFX:** MS2 effects are Gamebryo particle NIFs (Effect.m2d) — a true port is a
  research project. V1 = "inspired-by" rebuilds on CombatJuice, keyed to the same
  skill ids. Revisit a real effect importer later if v1 feels flat.
1–2 sessions for the first skill batch.

## Cross-cutting rules
- Raw extracts stay OUTSIDE the repo (C:\Games\MapleStory2\Extracted); the repo
  gets generator scripts + per-hero baked FBX/PNG only. Watch repo size.
- Commit per phase; each phase ends verified in Play (save-backup loop per CLAUDE.md).
- GameCore never references Unity or asset files — ids and numbers only.

## Decisions locked
- 2026-07-01: IP rule overruled — real MS2 assets ship.
- 2026-07-02: goal set (this doc).
- 2026-07-02 (user): (a) gear = BAKED body+gear FBX per hero; (b) VFX =
  rebuilt "inspired-by" effects; (c) skills = MS2 names/numbers/anims poured
  into the existing 2+2 archetype template (game-design.md balance preserved).
