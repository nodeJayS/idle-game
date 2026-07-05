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

### ⭐ Party & movement feel batch (IN PROGRESS — user-approved jump ahead of Tower rewards, 2026-07-04)
Jank list from play: casters face-tank, nobody reacts to a caster being
aggro'd, leader feels arbitrary/undiscoverable, melee gets stuck/orbits,
party edit UX (last-hero foot-gun, two-step swaps). Slices:
1. ✅ Party reducers (2026-07-04): last-fielded-hero guard + one-move
   Party.SwapHero (exact slot, leader reverts to auto when benched).
2. ✅ Movement core (2026-07-04): role-aware formation (melee flank at
   FormationMeleeBack 0.6, ranged park at FormationRangedBack 4.6),
   soft melee-leader default (explicit pick always honored),
   ranged fire-in-transit (shoot in-reach, never chase), panic
   micro-kite (PanicRadius 1.8, fires while backpedaling). 473 tests.
3. ✅ Sim (2026-07-04): tank aggro bias (TankAggroBias 2.0 — monsters
   count melee heroes closer, gated monster-side so it can't leak) +
   melee surround ring (stable FNV-1a contact angles on the target rim,
   both teams — the stuck/orbit fix). 480 tests.
4. Client UI: leader crown + in-world ring, one-click swap flow,
   last-hero warning; Play-verify the whole batch + regroup hustle +
   knob feel (4.6 / 1.8 / bias 2.0) in ONE editor window.

### 4. Tower slice 3 — per-floor reward bundles
The one unfinished system slice; floors pay only via milestones today.
GameCore-first, sim-testable, ~1 session.

### 5. Combat presentation debt
- Per-hero impact sounds (`PlayImpact` hardcodes `Hit_SwordDefault`; same
  pattern as the shipped per-hero attack sounds).
- Sanctify heal visual + Holy Smite cast flourish (`_skillFx` add-on points).
- Check hero-float quirk (SyncViews writes v.Height into hero Y for capsules).

### 6. Terraced terrain + water (the big remaining Tunic-look gap — sim-gated)
Tunic reads as PLACES: terraces, cliff strata, stairs, water — ours is an
infinite plane, and painted ground-wear fails without structure (tried
2026-07-02, reverted). Slices: (1) GameCore per-stage arena layout (walkable
region + height tiers as data, movement clamps); (2) client terraces/cliffs
(TunicSurface side colour = free strata), water + shore ink, stairs;
(3) per-zone water/lava/void flavor + camera composition pass.

### 7. Content & tuning pass
More stages/mods/kits; balance sim in console; XP curve at roster size.
Caster pacing lever if ever needed: per-skill CooldownMs (mana removal
changed nothing observable).

### 8. Later / parked
Prestige/rebirth · manual achievement-claim UX · real-money gems (after
gacha proves fun) · server authority (design §9) · Xml.m2d item-table
extraction (when wardrobe grows) · zone drop-table hint in stage picker.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
