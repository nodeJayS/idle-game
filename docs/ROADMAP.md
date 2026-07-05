# Roadmap — the ONE "what's next" doc (updated 2026-07-03)

Living priority list; update in the same commit that ships an item. Durable
design → [`game-design.md`](game-design.md); session orientation →
[`../CLAUDE.md`](../CLAUDE.md). Shipped details live in git history — entries
here get ONE receipt line when done, then get pruned next pass.

## Where the game stands

Core loop, build depth, zones (10 themed, 30 faceted monsters), Tower,
achievements, daily gems, gacha (gem sink): all ✅. MS2 hero pipeline proven
(manifest + 9 clips + 1 bake per hero). Roster: Knight / Fire Mage / Assassin
/ Priest on the archetype backbone (Warrior/Rogue/Magician templates; class =
overrides); Ice Mage is banner-gated behind the live "Winter's Return" gacha
banner. Diorama look shipped (ortho 45°, split-tone grade). 455 GameCore
tests green. No mana — skills are cooldown-only.

## Priorities, in order

### 1. ✅ Combat-feel + QoL batch — shipped 2026-07-03 (6 slices, see git)

### 2. ✅ Monster procedural animation — shipped 2026-07-03
Receipt: client-only MonsterAnimator (body-pivot trick — CombatView owns the
root), 4 gait families over all 28 monster ids, telegraph/hit-flash/death
topple-or-poof, monsters now face movement/target; verified in Play across
3 zones.

### 3. ✅ Gem sink → hero gacha MVP — shipped 2026-07-03/04 (3 slices)
Receipt: GameCore `Gacha.Roll` (persisted-cursor RNG, per-banner pity, dupe
→ XP/scrap) + GachaPanel reveal beat + LIVE "Winter's Return" Ice Mage
banner (10 gems/roll, pity 20, featured ≈7.7%, dupe 2M XP + 500 scrap;
skinned icemage bake + icebolt/frostbolt/blizzard frost VFX + 11 ice SFX).
Play-verified end to end via injected free banner. Frost VFX read whiter
than ice-blue under bloom (glow ×2.5–3 clamps) — retune candidate for
item 5's presentation pass.

### ✅ Regroup hustle — shipped 2026-07-04 (unplanned sim QoL)
Receipt: Solo-tactic followers stranded past FormationBreakRadius regroup at
max(own, leader) MoveSpd × RegroupHustleMult (1.4), so a geared leader can't
outrun an ungeared follower between packs (the fresh gacha Ice Mage exposed
it). Sim-only, 459 tests. Play eyeball (+ possible run-clip foot-slide at
1.4×) rides the next editor window; ranged fire-in-transit idea parked.

### ✅ Party & movement feel batch — shipped 2026-07-04 (4 slices; jumped Tower rewards, user call)
Receipt: role-aware formation (melee flank 0.6 / ranged park 4.6) + soft
melee-leader default + ranged fire-in-transit + panic kite (1.8) + tank
aggro bias (2.0) + melee surround ring (FNV-1a rim angles) + regroup
hustle (1.4) · party reducers (last-hero guard, one-move SwapHero) ·
client UI (leader badge "★ Leader"/"(auto)" + faint gold ground disc,
left-rail "Swap in for:" stack, greyed "Last hero"). 485 tests;
Play-verified in one window (standoff/hustle/fire-in-transit live;
swap + guard exercised through the real UI). Knob feel (4.6/1.8/2.0)
= user's call after normal play; all one-line Balance tweaks.
Post-batch fix (2026-07-04, found in live play): panic kite's unbounded
away-vector ran casters off screen — retreat now runs TO the leader and
holds at PanicHoldDist (2.0), and melee heroes PEEL (prefer enemies
attacking a ranged ally, same acquisition radii). 491 tests.

### 4. ⭐ SDF blend-shell monsters — the faceted variant (user call 2026-07-04, jumps Tower rewards)
Port of the Three.js "blend-shell" idea: primitive capsules merged to one
draw call, vertex shader snaps every vertex onto the combined smooth-min
SDF (seams cease to exist), colors blend by SDF proximity. OUR twist keeps
the hard art rule: normals come from screen-space derivatives (flat facets)
NOT the SDF gradient — a faceted Tunic skin over a seamlessly blended body.
Client-only (zero GameCore impact). Slices:
1. ✅ Shader prototype (2026-07-04) — PROVISIONAL PASS: SdfBlendShell
   shader (16-prim uniform arrays via MPB, 3-step smin snap, ddx/ddy
   facet normals, proximity color blend) + SdfBlobRig + Tools > SDF
   Blob Test. Verified in-editor: compiles clean, seams GONE at
   overlaps, skin reads faceted, colors cross-fade at joins, antenna
   survives, fusion holds through pose changes (screenshots in git
   history's session). STILL OPEN: the kill criterion (temporal
   vertex-slide shimmer) needs a live eyeball — run the menu item,
   watch the wiggle ~10s. Tuning notes: proximity colors over-mix
   (pastel wash — sharpen exp weights) and small limbs get swallowed
   (authoring sizes). Fallback Lit supplies shadows from UNSNAPPED
   source geometry (mismatch acceptable so far).
2. Animation wiring: drive primitive transforms from a MonsterAnimator-
   style gait (fused-while-flopping test); optional jiggle-rope tail.
3. Content: one bounded family where seamless reads as material truth —
   slime/ooze/spirit zone family (JSON-ish defs, rank tint/glow +
   hit-flash per instance through the merged draw). NOT a roster re-skin;
   the 30 faceted monsters stay.

### 5. Tower slice 3 — per-floor reward bundles
The one unfinished system slice; floors pay only via milestones today.
GameCore-first, sim-testable, ~1 session.

### 6. Combat presentation debt
- Per-hero impact sounds (`PlayImpact` hardcodes `Hit_SwordDefault`; same
  pattern as the shipped per-hero attack sounds).
- Sanctify heal visual + Holy Smite cast flourish (`_skillFx` add-on points).
- Check hero-float quirk (SyncViews writes v.Height into hero Y for capsules).

### 7. Terraced terrain + water (the big remaining Tunic-look gap — sim-gated)
Tunic reads as PLACES: terraces, cliff strata, stairs, water — ours is an
infinite plane, and painted ground-wear fails without structure (tried
2026-07-02, reverted). Slices: (1) GameCore per-stage arena layout (walkable
region + height tiers as data, movement clamps); (2) client terraces/cliffs
(TunicSurface side colour = free strata), water + shore ink, stairs;
(3) per-zone water/lava/void flavor + camera composition pass.

### 8. Content & tuning pass
More stages/mods/kits; balance sim in console; XP curve at roster size.
Caster pacing lever if ever needed: per-skill CooldownMs (mana removal
changed nothing observable).

### 9. Later / parked
Prestige/rebirth · manual achievement-claim UX · real-money gems (after
gacha proves fun) · server authority (design §9) · Xml.m2d item-table
extraction (when wardrobe grows) · zone drop-table hint in stage picker.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
