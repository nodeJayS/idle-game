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
1. ✅ Shader prototype (2026-07-04) — PASS (kill criterion closed
   2026-07-05: user eyeballed the live wiggle, no temporal vertex-slide
   shimmer): SdfBlendShell shader (16-prim uniform arrays via MPB,
   3-step smin snap, ddx/ddy facet normals, proximity color blend) +
   SdfBlobRig + Tools > SDF Blob Test. Verified in-editor: compiles
   clean, seams GONE at overlaps, skin reads faceted, colors cross-fade
   at joins, antenna survives, fusion holds through pose changes
   (screenshots in git history's session). Tuning debts → slice 2:
   proximity colors over-mix (pastel wash — sharpen exp weights), small
   limbs get swallowed (authoring sizes), bounds baked at BuildMesh can
   frustum-pop under big gait swings (recompute or fatten padding).
   Fallback Lit supplies shadows from UNSNAPPED source geometry
   (mismatch acceptable so far).
2. ✅ Animation wiring (2026-07-05) — SdfBlobAnimator (Walk + Hop
   families driving the prim:* nodes; hip-orbit leg swing, MonsterAnimator
   phase/seed/sticky-speed/stop-mid-air contracts, squash faked via
   center-Y compression + radius dip; never touches root — CombatView
   contract holds) + Tools > SDF Gait Test (walker + hopper, root-owning
   pacer, edit-mode player-loop pump for smooth no-Play preview).
   Slice-1 debts paid: shader _ColorSharp (pastel wash), rig
   boundsPadding (frustum-pop), chubby-limb authoring (smin swallow).
   User-verified in editor: hips stay fused through the swing, hopper
   fused through landing squash, no frustum-pop. Jiggle-rope tail parked.
3. ✅ Content (2026-07-05) — swamp blob trio JOINS Murkwater Swamp's
   roster (originals stay): mire_slime (tanky Hop, rises from the bog),
   bog_shambler (Walk), fen_spirit (fast fragile RANGED Float, icebolt;
   first ranged trash — authored inline, Trash() can't set AttackRange;
   test pins it). SdfBlobDefs registry (per-instance prim clones — the
   shared-def node-stomp bug), IMonsterAnim (both animator kinds feed
   through CombatView's five sites unchanged), rank/mod tells + hit-
   flash per instance via _BlobTint/_BlobEmit through the MPB, Float/
   TriggerAttack/TriggerHit/Die(poof) on SdfBlobAnimator. 492 tests.
   Play-verified at stage 25: mixed roster spawns, blobs fused in
   motion, elite tint+size tell, mod glow, flash + poof exercised.
   Two Play-caught fixes: defs ground at y=0 (body at y=0 buried the
   lower half — read as pale crescents) and .linear colour push (raw
   sRGB floats through the MPB render ~2x bright in linear space).

⭐ Item 4 DONE — the blend-shell is proven tech + shipped content.

### ✅ 5. Tower slice 3 — per-floor rewards — shipped 2026-07-05 (user-specified)
Every FIRST clear pays TowerGemsPerFloor (10) gems through RecordClear
(no-op path pays nothing); 10th-interval floors stay the milestone
floors (account buff + configured rare-pair unlock = the "modifier
reward" — unlock floors deliberately NOT moved: SyncToStage re-derives
ownership, moving them would revoke owned mods). TowerView previews the
next floor's reward line. Shipped in the same batch (2026-07-05):
quest board rework (ClearStages retired from the roll cycle — enum kept
for saves; no duplicate kinds, dedup vs SURVIVORS not the built prefix
— Fable-caught ordering bug + regression test; load-time sanitize
replaces old ClearStages/dupes; every kind passive under AFK), rare
mods untunable + free ResetTuning reducer, ModifierPanel UX rework
(per-number gold ▲ tuning deltas replace the "+x% tuned" jargon, help
line, Tune ▲ relabel, arm/confirm ↺ reset, inline imprint payload +
hover gear-preview card), Skill2→Run Moving exit in HeroAnimator (the
"assassin walks in idle pose" bug), Bootstrap sweep of editor test-tool
objects leaking into Play. 505 tests green.

### ✅ 6. Ranged-hero feel: base speed + projectile/anim/damage sync — shipped 2026-07-05
Receipt: magician-family base MoveSpd 3.15 (above Warrior 3.0, Rogue 3.4
stays fastest; ordering test pins the intent) + client launch-delay chain:
`IHeroAnim.AttackFinishSec`/`SkillFinishSec` (real playback length of the
take last triggered; 0 on a refused trigger, so fire-in-transit keeps its
instant launch) and `CombatView.ScheduleLaunch` defer ranged projectile
LAUNCH until the cast anim finishes; number/flash stay on impact. Scope
call: health bars/loot/XP feeds stay sim-time (bars poll sim HP directly —
routing them through the delay is a separate presentation-queue slice if
the mismatch ever reads badly). Play-probed frame-by-frame: impact==number
same frame, launch = hit + clip finish (~0.49s), FrostOrb rides the Skill2
clip; at high cadence anims queue behind hits (Animator trigger latching)
— reads as continuous casting, not broken. Live-save note: gear MoveSpd
(Knight +1.13 vs mage +0.30) still out-dashes the base bump on pack moves;
regroup hustle reattaches in ~1.5s — knob/deeper lever (follow-speed
floor) = user feel call after normal play. 506 tests.
Post-ship fix (2026-07-05, found in live play): heroes stuttered/paced —
three sim causes, all fixed: formation heading recomputed per step from
the leader's noisy ~1-unit nearest-enemy vector (now sticky
`CombatState.FormationHeading`, frozen while engaged, refreshed while
traveling), leader re-acquiring "nearest" flapped between equidistant
mobs (now sticky target; peel still overrides), and attackers
overshooting into the separation pass each tick (arrival cap: stop
`ArriveDepth` 0.05 inside reach — applies to monsters too, mosh-pit
shove gone). Play-probed: 3 isolated mage direction-reversals in 30s
(was continuous), heading constant through a 10s fight. 512 tests.
Ranged assist (2026-07-05, user call): ranged FOLLOWERS prefer enemies
the melee heroes are fighting or walking toward (`meleeFocusIds` =
living melee TargetIds incl. the leader's advance-branch pack; second
`preferIds` bucket in the acquisition helpers, mirror of peel — reorders
within the existing leash radii, never widens). Stops the caster waking
fresh mobs; aggro piles onto tanked ones. Ranged LEADER keeps own
acquisition (anchor). Play-probed 97% assist rate over 40s. 517 tests.

### ✅ 7. Combat presentation debt — shipped 2026-07-05
Receipt: per-source impact sounds (`PlayImpact(sound)` threaded through
Schedule/ImpactAfter + `IHeroAnim.ImpactSound` reading a new reserved
"_impact" sidecar binding — no distinct melee-impact clips extracted yet,
so heroes stay on the sword clang until clips land, one sidecar line each;
fireball/firebolt now land with the previously-unused Fireball_Destroy,
icebolt/frostbolt with IceStrike_Splash replacing the splash+clang
double-play) · Sanctify + Holy Smite own their looks via skillId-keyed
`_skillFx` entries that override the shared sprite key (icons/GameCore
untouched — they'd borrowed the warcry aura and the BOSS QUAKE red wave
+ shake): party-wide rising heal sparkles + caster ring, and warm-white
AoE rings + collapsing light pillar, no shake · hero-float quirk fixed
(primitives grounded at real half-height 0.9/0.45×scale; placement at
`height` floated them 0.1×scale). FX Play-verified via direct closure
invoke + screenshot (priest not fielded in the live party); sound sets
verified resolving. 517 tests.

### 8. Terraced terrain + water (the big remaining Tunic-look gap — sim-gated)
Tunic reads as PLACES: terraces, cliff strata, stairs, water — ours is an
infinite plane, and painted ground-wear fails without structure (tried
2026-07-02, reverted). Slices: (1) ✅ GameCore arena layout; (2) client
terraces/cliffs (TunicSurface side colour = free strata), water + shore ink,
stairs; (3) per-zone water/lava/void flavor + camera composition pass.
1. ✅ GameCore arena layout (2026-07-05) — ArenaLayout = union of convex
   rect/disc shapes + cosmetic height tiers (max-tier-wins; client Y =
   Tier × Balance.TerrainTierHeight, no sim rule reads height yet); pinned
   on CombatState.ArenaId at init/transition (null = open plane ⇒ the 517
   legacy tests bit-identical); collide-and-slide MoveToward + nearest-point
   clamps on spawns/homes/dash/collisions/wander (no pathfinding — authoring
   rules: connected star-ish unions, tier-0 covers the r≈32 spawn bubble,
   terraces overlap a lower tier = the ramp, water only as shallow perimeter
   bays; all test-pinned); 10 authored zone arenas in Default(). 543 tests.
   Play-probed live: stage-22 swamp bay + stage-5 glade, ~110k position
   samples, zero off-union (after a nanometre rim-inset fix the probe
   caught), kills/spawns flowing, no shore-jams.
2. ✅ Client terraces/cliffs/water (2026-07-05) — ArenaTerrain heightfield
   mesh from the layout (1-tile cells; shoreline corners pulled onto the
   exact union boundary so shores hug disc rims; tier walls read
   TunicSurface _SideColor strata for free; tier-diff-1 edges become
   two half-step terracotta treads = the stairs read, outset into the
   lower cell + corner-extended — both fixes Play-caught: inset-under
   left an open slot, corners left pinhole gaps); shore-ink vertex band;
   matte water plane at −0.45 (neutral colour — zone flavor is slice 3);
   rebuilt from ZoneDress.Sync per zone crossing; units/FX ride tiers
   via smoothed View.TerrainY (~0.25s step-up; gait uses horizontal-only
   delta), leader disc + skill ground-rings included; Scenery re-scatters
   per zone onto walkable ground at tier height. Play-verified by
   screenshot: swamp bay + hummock stairs + glade island, live zone-hop
   rebuild, mob on tier 1 at y=0.70 exact, console clean.

### 8.5 Roguelite dungeon mode (user call 2026-07-06 — aesthetic-first)
Port of the MIT "Dungeon Forge" Three.js procedural dungeon (repo cloned to
~/reference/threejs-procedural-dungeon; its preview.jpg = the art target).
Goal: the EXACT reference aesthetic on our stack, roguelite rules designed
later. Slices: (1) ✅ GameCore generator (2026-07-06) — DungeonGen 9-stage
pipeline (scatter/separate/kNN graph+MST+loops/semantics/carve/rasterize/
decorate/name), pure + deterministic (FNV-1a checksum), 29 acceptance tests,
~5ms @60 rooms (budget 50). (2) ✅ client renderer (2026-07-06) —
chunked baked meshes + DungeonLit shader (vertex colour; point lights come
from OUR global light table pushed by DungeonFlicker: URP's additional-light
plumbing proved path-dependent in edit mode — the Forward+ cluster macros
rendered black offscreen), 12-light budget (entrance/boss/shrine keys +
farthest-point torches, ×15 URP intensity scale ≈ the reference's gamma-
display lift), emissive flame pairs under bloom, exact theme palettes/AO
(0.11)/checkerboard/doorway/tint recipe with hex used RAW (three.js gamma
pipeline — .linear read 4× dark), Tools > Dungeon Preview window (seed/dice/
sliders/theme/overlays/stats, stages camera+fog+ambient+post itself, no Play
needed). Screenshot-matched to preview.jpg at seed 880239/molten/80.
NO tilt-shift: URP DoF is perspective-only (uniform blur under ortho) —
custom band-blur pass = later polish. (3) ✅ playable test floor (2026-07-06, two commits) — 3a sim:
IArenaSurface abstraction, grid-backed DungeonArena (0.35 wall inset,
ring-search Clamp), room-gated acquisition (GateTargets: same room or
corridor-proximity 6.5 — no wallhacks either direction), leader walks the
BossBfs flow field between fights (the BFS field IS the pathfinding),
room-bound wander, win = boss dead / lose = wipe or 600s, 587 tests.
3b client: "Crypt Run (dev)" farm button → DungeonMode world swap
(overworld roots hidden, DungeonRenderer world, crypt fog/ambient/sun
staging with a 0.35 gameplay sun-dim — the preview calibration washes out
under the game grade), Gloom Hollow cast (cave_bat/gloom_shade/
nightmare_maw), win/lose feed + popup, auto-return to farm. Play-verified
end-to-end: seeded name in feed, party traversed room-to-room (toBoss
77→52→26→1 with a 24s room-clear plateau, 31 trash killed en route),
35s boss grind (12,283 HP), loot flowing, auto-exit to farm, save intact.
Reworked same day after live user feedback (mode-leak + "one room" feel):
FULL MODE ISOLATION — Combat.InitDungeon builds a fresh CombatState
(EnterDungeon deleted; the old in-place path let ResumeFarm clamp dungeon
grid coords onto the campaign arena rim, ringing farm packs there);
returning rebuilds the campaign via StartFarm from scratch. FULL-CLEAR
WIN — the whole crypt is the level: win = every monster dead (timeout
900s), leader SWEEPS rooms shallowest-entrance-depth-first via cached
per-room BFS flow fields (plus: flow-field approach for out-of-reach
targets, immovable sweep leader vs body-shove — 12/12 seeds full-clear).
MODES MENU — control-bar "Modes" panel lists Campaign/Crypt with active
markers, enter/abandon actions (dev button removed); button glows violet
during a run. 591 tests; Play-verified both leak directions numerically
(spawns spread X[23..97] zero pre-aggro; return centroid (0.3,0.1) with
packs ringing 11-22) + a 146-kill full clear → auto-return.
Second same-day rework (user: transitions must be LOADS, not pans):
LoadingScreen (fade→black, swap+camera SNAP at full cover, dummy-load
hold, fade out; sim paused while covered so destination mobs take their
first step on-screen); Tower joined the isolation model (EnterTower
deleted → Combat.InitTower fresh state on the floor's zone arena; exits
rebuild camp via StartFarm through the load); CameraRig.SnapTo (the
cross-map glide read as one map sliding over); Modes menu now lists
Campaign/Tower/Crypt (Tower's bar button folded in; Choose Floor opens
TowerView); alt modes show a top-centre EXIT where Challenge lives;
Begin() folds gear+account buffs into every fresh state (fresh-init
parties were fielding naked base stats). Play-verified: sim frozen at
black (t=0 → first step after fade), camera off-axis 1.3 post-snap,
tower floor 8 in/out, crypt arrival calm at the portal.
Third rework (user, playing live): LINEAR layouts — DungeonParams.Linear
places rooms as a single self-avoiding chain (drifting heading + light
curl, 110-reach guard, deterministic angle fan; entrance/boss at the
ends, treasure/shrine = on-path breather rooms, elites 55-85%, all
corridors width-3). Branching maps force the auto-battled sweep to
BACKTRACK through cleared rooms to reach the next fork — fine when a
player steers, dumb-looking when nobody does; on a chain the party only
ever advances ("pseudo move to the next room", the true-ARPG push).
Crypt uses Linear; preview window has a toggle (branching showpiece
kept). 596 tests (5 new: chain shape/ends/roles/bounds + a sim-level
forward-only sweep proof); Play-verified objective depth 289→417
strictly forward. Sweep-tail note above is MOOT for linear runs.
Fourth pass (user, playing live — four calls): SMOOTH TRAVEL —
DungeonArena string-pulls the flow field (walk the downhill chain ≤8
cells, cut straight to the farthest point with a walkable line;
Play-measured travel ≥100% of stat speed vs visible wall-grinding
before). NO WALL-HACKS — corridor sight now requires SegmentWalkable
(parallel maze halls sit one wall apart) and a dash's FLIGHT LINE must
be walkable or the skill is skipped. MAZE-IN-A-SQUARE — the linear
chain became a randomized self-avoiding walk on a compact room lattice
(pitch 22, jitter ±1.5, DFS+backtracking, entrance on the rim):
up/down/left/right maze turns, dense square block, pocket voids instead
of empty oceans; still strictly single-path. CASTER KEEPS UP —
dungeon-only: followers home at ≥ the leader's speed and ranged
standoff compresses 4.6→DungeonRangedBack 2.6 (Play-measured: Ice Mage
avg 5.8 from the leader = inside cast range, vs a full room behind).
596 tests.
Fifth pass (user, reference screenshot): PACKED MAZE — rooms nearly
fill their lattice cell (pitch 22→16, odd dims {11,13,15}, jitter
dropped): 1–5 wall tiles between adjacent footprints — walls TOUCH,
doorways punched through, no void fields (test-pinned; spawn divisor
scaled so kill counts hold ~147; pillared halls rare + boss arena
clean). ROOM-SCOPED AGGRO — GateTargets is strictly same-area: same
room, or both in the hallway with LoS; room↔hallway is ALWAYS false
(even across an open doorway). Judged from the BODY everywhere: the
leader's sticky keep re-passes the gate, and a follower drops its
slot-acquired target until it steps into the room. MAZE REJOIN —
straight-line slot homing wedged far followers on wall corners
(Play-caught: two heroes stranded at the entrance while the Knight
soloed); a follower whose straight segment to slot is wall-blocked now
descends the flow field toward the leader. Play-verified live: gate
violations 0/177 sampled pairs, follower avg 3.8 tiles off the leader,
full 147-kill clear + auto-return. 599 tests.
ROGUELITE META SHIPPED (user-approved design 2026-07-06, two
commits): GameCore `Crypt.cs` + `CryptState` under ProgressState —
KEYS (1/UTC day, DailyLogin day-index, bank 2, StartRun consumes),
3-floor RUNS from DepthRecord+1 (RecordFloorClear mirrors
Tower.RecordClear: sequential first clears only, 5 gems each, cap 60),
end-of-run CHEST (grave dust 25+5×depth; wipe forfeits, drops keep),
BOON tracks (Vigor/Ferocity/Bulwark +2%/rank, ceil(20×1.5^r), max 10,
folded beside Tower.ApplyAccountBuffs), themed depth TIERS (10-floor
bands crypt→molten→frost cycling zone casts), floor difficulty
ramp (1.06 HP / 1.04 dmg per floor) into InitDungeon hp/dmg mults.
Client: key-gated entry (refusal feed), descend-beat loading screens
between floors, chest/gems feed lines, Modes panel Crypt row (keys +
next-key countdown + depth record) + Boons shop (live
RefreshPartyStats on buy). Play-verified end-to-end: key grant→spend,
3-floor run → record 3 / +15 gems / 40 dust chest → auto-return;
0-key refusal; wipe = no chest/record; boon buy 40→20 dust, Knight
MaxHp +2% live. 617 tests. Run state is transient by design (quit =
abandon). NEXT: crypt mood polish (user eyeball); balance pass on
key cadence/ramp once the user has played a few real runs.
ANIM FEEL PASS (2026-07-07, user-caught in crypt): winged monsters
now FLAP (MonsterAnimator rotates wing* parts about a geometry-derived
axis — the crypt's tier-1 roster is all Float-family, which read
dead); hero swings/casts no longer truncate (attack takes pace to the
hero's live cadence, a busy window refuses mid-clip re-triggers +
flinches, Moving debounces 0.12s so formation micro-shuffles can't
cancel, stale latched triggers cleared). Play-probed: all clip exits
are now completions or genuine travel cancels (cutOther=0). OPEN
VERDICT: casters still travel-cancel long casts when the sim moves
them mid-cast — rooting casters during casts is a sim/balance call.
(4) BFS build-reveal animation (parked).

### 8. Content & tuning pass
More stages/mods/kits; XP curve at roster size.
BALANCE SIM SHIPPED (2026-07-07): `dotnet run --project
gamecore/BalanceSim -- walls|sweep|farm` — min-clear-level per stage ×
gear tier, stage×level win grids, farm throughput (kills/min,
hits-per-kill ≈1 = one-shotting, XP/gold rates). Deterministic
(--seed); scenario saves built through the live reducers. First
findings, THE inputs for this pass (sim excludes tower buffs / crypt
boons / enhance / imprints, so the real frontier sits a bit deeper —
but not 45 stages deeper):
(a) THORNS BOSS GATES ANTI-SCALE — every 4th stage's boss wears Thorns
(ModifierCycle[(stage-1)%4]) and reflect scales with attacker damage,
so a stronger party self-kills faster: stages 32/36/44/48/52 are
unwinnable even at L100 full-mythic while their neighbors fall at L1;
stages 20/40 stack thorns × MajorBossMult (the bare/normal-gear wall at
20 jumps +21 levels).
(b) THE LADDER'S MATH ENDS NEAR STAGE 53 — no level/gear combo clears
55+ inside the 30s boss timer: MonsterHpGrowth 1.18^stage outruns
ilvl-linear gear. Stages 55-100 are currently decorative.
(c) GEAR RARITY DOMINATES LEVEL — a full rare set clears stages 1-27
at hero level 1; bare heroes need L37 by 25 and L75 by 28.
Caster pacing lever if ever needed: per-skill CooldownMs (mana removal
changed nothing observable).

### 9. Later / parked
Prestige/rebirth · manual achievement-claim UX · real-money gems (after
gacha proves fun) · server authority (design §9) · Xml.m2d item-table
extraction (when wardrobe grows) · zone drop-table hint in stage picker.

### 10. Milestone backlog — ten majors, pre-sliced (brainstormed 2026-07-07)
Self-contained briefs for future sessions (any model). Ordering within
each milestone = shipping order; every slice is one-verified-slice-per-
commit sized. Written from a UX-first lens: the game's systems are ahead
of its presentation, and the biggest wins now are legibility, feel, and
retention structure. Standing rules apply (GameCore-first; MS2 = heroes
only; no MS2 music; content seeds at New Game).

**10.1 The Great Rebalance (sim-driven — unblocks everything else)**
The §8 findings make the ladder end at ~53 and thorns gates unwinnable.
Needs user design verdicts on intent first, then:
(a) fix thorns anti-scaling (reflect off victim EHP, or cap vs bosses,
or exempt the boss's own modifier from reflect — pick with user);
(b) flatten MonsterHpGrowth / raise gear-affix scaling so 55–100 is
reachable on-curve (BalanceSim walls chart green to 100 = the acceptance
test); (c) close the gear≫level gap (level should matter);
(d) extend BalanceSim: model Tower buffs/crypt boons/enhance, add a
`pace` mode (full simulated playthrough: farm→XP/drops→boss→advance,
charts hours-to-stage); (e) re-run walls + farm charts, commit the
tuning + updated §8 receipt together.

**10.2 First-time experience (FTUE) & staged UI reveal**
A new player today sees ~10 buttons and 100 numbers in minute one.
(a) GameCore: `Progression.FeatureUnlocked(feature, save)` gating table
(stage/level driven — e.g. Modifiers@S10, Modes@S15, Gacha@S20, sim-side
so a future server agrees); (b) client: hide locked HUD buttons, reveal
with a one-line toast ("Modifiers unlocked — risk for reward");
(c) a 5-beat guided intro (kill pack → first drop → equip → first boss →
first unlock) driven by the existing quest board, seeded only at New
Game; (d) first-boss/first-hero celebration moments (existing juice,
bigger beat); (e) "what do I do next" breadcrumb: one contextual hint
line on the HUD fed by game state (idle claim ready / boss beatable /
skill point unspent).

**10.3 UI/UX foundation refactor (the deferred layout-group pass)**
Hand-placed coords rot on every screen change; do the real fix in order:
(a) a reusable uGUI panel kit (header/close/scroll/list-row prefab
builders, one theme-token file: colors/font sizes/spacing consts);
(b) migrate ONE screen (Heroes) to layout groups + the kit, verify at
16:9/16:10/ultrawide, THEN (c) Inventory, (d) modals (boss result,
idle claim, gacha reveal), (e) HUD anchoring pass (safe margins, corner
regions), (f) glyph/font audit last (single font asset, fallbacks).
Acceptance: no hand-placed pixel coords left in migrated screens.

**10.4 Goals hub (quests + achievements + daily login, one surface)**
Three separate reward systems = three places to forget to click.
(a) GameCore: unify claimable state behind one `Goals.Pending(save)`
read model (no schema change — a view over the three systems);
(b) one Goals panel with tabs (Today / Achievements / Login), claim-all
button (respect display-rounding rules on reward totals); (c) HUD
notification pip when anything is claimable (the single red-dot
pattern); (d) retire the separate quest/achievement buttons; (e) a
"tomorrow preview" line (next login reward + quest reroll time) for the
come-back-tomorrow pull.

**10.5 Loot QoL 2.0 (legibility at scale)**
Late-game bags are noise. (a) loot filter: per-rarity auto-salvage
floor PER SLOT + "never salvage imprinted" toggle (GameCore reducer +
tests, wire into the existing salvage paths); (b) compare-anywhere:
hover any item anywhere → delta vs equipped of the fielded hero with
best PowerScore gain; (c) bulk-select salvage UI (tap-drag multi-select,
locked items refuse as today); (d) set bonuses (design §6.1): 3 sets
per zone tier, 2pc/4pc bonuses as flat StatBlock adds in
ComputeHeroStats (content-as-data, seeds at New Game); (e) per-hero
gear loadout snapshots (save/apply, bag-integrity checked).

**10.6 Combat presentation pass (juice v2)**
The sim reads honest; make it FELT. (a) hit-stop: 30–50ms time-scale
dip on crits/kills (client-only, cap frequency so packs don't slideshow);
(b) per-element impact language: fire=ember burst, ice=shard ring +
brief tint, holy=flash column — one reusable burst API keyed off
AttackFx/skill sprite; (c) frost VFX retune (§3 receipt: reads white
under bloom — drop glow clamps); (d) projectile trails (TrailRenderer,
pooled); (e) kill-streak feedback: N kills in 2s → small screen pulse +
feed line; (f) sound mix bus: SFX ducking under big moments, volume
sliders in Settings (foundation for 10.9).

**10.7 Crypt 2.0 (roguelite depth — the mode has legs)**
The maze/aggro/meta shell shipped; add the decisions that make
roguelites sticky. (a) mid-run persistence (CryptState.ActiveRun:
floor/party HP/rng cursor — quit resumes instead of forfeiting the
key; the handoff's old candidate); (b) between-floor BOON DRAFT: pick
1 of 3 run-scoped modifiers (rides ModifierInstance machinery,
run-only, not persisted past the run); (c) elite/cursed rooms: one
room per floor rolls a rank-up pack guarding a bonus chest (dungeon
gen already has room metadata); (d) depth-tier BOSS modifiers (the
tier boss wears its tier's signature mod — telegraphed in the entry
toast); (e) run summary screen (floors, kills, dust, drops — the
share-able moment).

**10.8 Endless mode ("deepest stage")**
The post-100 chase once 10.1 makes 100 reachable. (a) GameCore:
endless stage generator past MaxStage (reuse zone cycling + geometric
scaling with a gentler exponent; EndlessBest in ProgressState, v3
migration); (b) entry via stage nav at 100 ("Push beyond…");
(c) reward curve: scrap/gold multiplier per depth, first-clear gems
every 5 (mirror Tower/crypt receipts); (d) BalanceSim endless mode
(walls chart to depth 200 = tuning acceptance); (e) depth record on
the account panel + feed line (leaderboard seam for Phase C).

**10.9 Audio identity (original, adaptive — NO MS2 music ever)**
The game is silent between SFX. (a) pick/produce 3 original loop beds
(overworld calm / combat sim-intensity layer / crypt dread) — royalty-
free or commissioned, NEVER MS2; (b) client AudioDirector: crossfade
beds on mode swap, duck under boss stingers; (c) zone ambience one-shots
(wind, embers, frost) keyed off ZoneDef; (d) UI sound language pass
(one family for click/claim/error/upgrade — replace defaults);
(e) mixer panel in Settings (music/SFX/ambience sliders, persisted
client-side).

**10.10 SDF blend-shell monster expansion — dungeons first (user call
2026-07-07; prestige/rebirth dropped from the goals to §9 parked)**
The §4 pipeline (SdfBlendShell shader + SdfBlobRig + SdfBlobAnimator
with Hop/Walk/Float gaits, seamless smin bodies under faceted normals)
is proven on the swamp trio but reaches only 3 of 30+ monsters — and
the crypt, where packs are densest and monsters are seen closest, uses
none of it. Its tier rosters (bat/shade, imp/hound, sprite/wolf) are
all rigid faceted models. Slices:
(a) author a CRYPT-NATIVE blob family in SdfBlobDefs: grave ooze
(Hop), bone amalgam (Walk — legs swing on the existing hip logic),
crypt wraith (Float) — dark palettes with GENTLE glows (§4's lesson);
each needs a GameCore MonsterDef (stats/XP/gold, content-as-data) +
CryptTierDef roster entry; new content seeds at New Game;
(b) molten + frost tier blobs (magma pulse-blob, frost drifter) so
every crypt tier mixes at least one SDF critter into its roster;
(c) ONE new gait family in SdfBlobAnimator (pick with user: Slither —
segment sine-chain, or Pulse — radial prim breathing for oozes); keep
the IMonsterAnim feed shape unchanged;
(d) an SDF ELITE/BOSS: one big multi-prim creature for a deep crypt
tier — check the 16-prim uniform budget first (raise it or compose two
rigs if a boss silhouette needs more);
(e) profile at packed-maze density (78+ mobs live today): per-blob
mesh + MPB cost, subdivision level, cull distance — the acceptance
gate before blobs go wide in dungeon rosters;
(f) overworld backfill LAST: swap 2-3 faceted zone monsters whose
silhouettes suit blending (bog_horror, chaos_spawn) — faceted Tunic
look stays the art rule; SDF is a body style, not a restyle.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
