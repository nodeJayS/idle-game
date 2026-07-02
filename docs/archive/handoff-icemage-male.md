# Handoff: icemage_basic on the MALE body (written 2026-07-02)

**Goal (user):** ship the Ice Mage as the first MALE skinned hero — proves the
male body path AND adds hero #4 in one slice. Read `CLAUDE.md` +
`docs/handoff-next-heroes.md` (STATUS block = pipeline learnings); the
magician (`art/heroes/magician_basic.json`, commit 5b29408) is the closest
worked example — same recipe, different gender.

## Ground truth (verified 2026-07-02)
- GameConfig kit (DO NOT change data): icemage_basic actives = **frostbolt
  (L1, slot 1)** + **blizzard (L10, slot 2)**; permafrost/frostflow are the
  passives. Unlocks at stage 8. Role=ranged, AttackRange 6.
- Pipeline is already gender-parameterized: manifest `"gender": "male"` →
  m_body.nif, joints + Bip01 Head socket derived from that NIF automatically.
  Male body was Blender-verified in Phase 1 but has NEVER been baked with
  gear or shipped to Unity — expect the render-eyeball step to earn its keep.
- Male clips (all confirmed in `Character\male\`): idle_a, staff_run_a,
  wizard_magicclaw_a, wizard_icestrike_a, wizard_frostnova_a,
  wizard_icebomb_01_a, wizard_icesphere_a, wizard_bore_a, knock_back_a, dead_a.
- Blue gear candidates (eyeball renders decide, magician taught us hats lie):
  - Sets with `_m_` variants: **shiningwizard** (11400917/11500821/11600041/
    11700061 + unisex 11300073 starcap), **mageclassset** (11400424/11500345/
    11600433/11700460 + 11300490 cap).
  - Ice-specific: hat 11300181_m_cpsnowbell_d, 11300185_c_cpsnowgianthat_e,
    11300740_c_cpwinterhat_c; pants 11500091_m_pacoolice.
  - Staffs (1/52): 15200037_snowgiantstaff, 15200208_snowqueenstaff,
    15200014_glittergemstaff, 15200144_luminousangelic_staff.
  - Male hair (0/02): 00200001_m_wolfhug_a, 00200002_m_soltmohican_a, browse more.
  - If nothing reads "ice blue", remember OverrideColor0 tints ride free — a
    neutral robe + blue tint may beat a themed set that renders wrong.
- Sounds (MS2Sound_effect.fsb): Skill_Wizard_FrostNova_Cast_01,
  Skill_Wizard_IceStrike_Splash_01..04, Skill_Wizard_MasterOfIce_Cast_01,
  Skill_Magician_IceBreath_Cast_01. attack_sound: MagicClaw again or IceBreath.

## Recipe (identical to magician; one new wrinkle each step)
1. `art/heroes/icemage_basic.json`: gender **male**, blue set + hat + hair,
   staff on Weapon_Hand_R_Point, `attack_sound`, skills = frostbolt slot 1
   (suggest wizard_icestrike_a clip) + blizzard slot 2 (wizard_frostnova_a).
   Optional `run_speed` if the male staff run cycle differs from 0.6s.
2. Decode 9 roles into `art/motion/icemage_basic/` — **must pass
   `--nif C:\Games\MapleStory2\Extracted\Character\male\m_body.nif`** to
   kf_motion.py (male clips against the male rest skeleton).
   attack/attack2 = casts (magicclaw + icebomb/icesphere), not staff swings.
3. Extract sounds to `unity/Assets/Resources/Sound`.
4. Bake with `--renders` FIRST and eyeball (male gear fit is unproven;
   watch hat placement — socket conventions vary, swap items rather than
   debug, per handoff-next-heroes learnings). Then `--export
   .../Models/icemage_basic_skinned.fbx`.
5. Unity: refresh → `Tools > Build Hero Animators` (handles clip loops +
   `icemage_basicAnimator.overrideController` automatically) → console check.
6. Play verify (BACK UP save.json first, restore after — CLAUDE.md loop).
   The test save's party is warrior/magician/thief; icemage may be benched —
   verify via `SkinnedHero.Build("icemage_basic")` from execute_code or swap
   the party. Confirm: own override controller, bindings (frostbolt/blizzard/
   _attack), textures wired, grounded scale next to the others.
7. Commit the slice, push.

## Known gap to decide (small GameConfig touch, flag to user if unsure)
`icemage_basic` has **no `AttackFx`** in GameConfig → her ranged basic attack
shows NO projectile (magician has `AttackFx = "fireball"`). AttackFx is
explicitly a *renderer hint* field, not balance data, so setting
`AttackFx = "frostbolt"` + registering a blue projectile in
`CombatView.BuildProjectileEffects` (the documented ADD-ON POINT, copy the
fireball entry with an ice palette + an ice sound) completes the hero. No
numbers/names change. If in doubt, ship the model slice first and the
projectile as a second commit.

## Rules (standing)
- NO MS2 music (SFX only). GameCore data untouched (hint field above is the
  one sanctioned exception). Never mutate loaded Blender images. Absolute
  output paths for headless Blender. Commit per verified slice; push after.
  Co-Authored-By line per CLAUDE.md.
