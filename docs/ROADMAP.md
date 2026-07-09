# Roadmap — the ONE "what's next" doc (restructured 2026-07-07)

Living priority list; update in the same commit that ships an item. Durable
design → [`game-design.md`](game-design.md); session orientation →
[`../CLAUDE.md`](../CLAUDE.md). Shipped work gets ONE ledger line here —
full receipts live in the git commit messages. **ENFORCED:** `DocsTests`
in GameCore.Tests fails the suite if this file exceeds 250 lines or loses
its sections — prune, don't raise the budget.

## Where the game stands

Shipped and playable: the full core loop (farm ladder → loot → build →
push), 10 themed zones with terraced arenas, Tower, Crypt roguelite
(packed-maze floors, daily keys, dust boons), quests/achievements/daily
gems, gacha (live Ice Mage banner), and a balance simulator over pure
GameCore. Roster: Knight / Fire Mage / Assassin / Priest (+ banner Ice
Mage) on the MS2 skinned pipeline; monsters are faceted or SDF blend-shell.
**625 GameCore tests green.** Headline problem (sim-proven): the 100-stage
ladder mathematically ends near stage 53 — see backlog 10.1.

## Your calls — decisions waiting on the USER

Nothing here blocks autonomous work elsewhere, but these need your verdict
before anyone acts on them:

1. **Rebalance intents (gates backlog 10.1):** how thorns should scale
   (reflect off victim EHP / cap vs bosses / exempt boss self-mod?), how
   deep the ladder curve flattens (55–100 reachable on-curve), and how much
   gear should dominate level.
2. **Root casters during casts?** The last cast-cancel source: the sim
   moves a caster mid-cast and the clip travel-cancels. Rooting them is a
   sim/balance change (kiting implications).
3. **Crypt tuning:** key cadence (1/UTC day too stingy?) and depth-ramp
   steepness — both single Balance constants; play a few real runs first.
4. **Crypt mood/brightness polish:** play & eyeball, then direct.
5. **Terrain slice 3** (zone water/lava/void flavor + camera composition):
   wants your island-look eyeball before building.
6. **Backlog 10.10c gait pick:** Slither (segment sine-chain) vs Pulse
   (radial breathing) as the next SDF gait family.
7. **Older feel list** (park or direct): arena sizes/roam, water colour,
   stair-tread chunkiness, terrace hop speed, caster MoveSpd vs follow
   floor (overworld), anim-end vs contact launch, corpse-linger, melee
   spacing, priest FX in real combat, formation knobs (standoff 4.6 /
   panic 1.8 / aggro 2.0), run-clip foot-slide at regroup-hustle 1.4×.
8. **Housekeeping:** the live save still holds ~996 test crypt keys
   (2026-07-07 testing hack) — say when to restore the normal key economy.
9. RESOLVED 2026-07-07: skill FX go PROCEDURAL (user pick after the
   10.11e spike); hero relooks all picked and shipped the same day.

## Backlog — pre-sliced majors (brainstormed 2026-07-07)

**NEXT UP: 10.1 The Great Rebalance — awaiting the user's rebalance intents
(Your calls #1) before any tuning constant moves.**

Self-contained briefs for future sessions (any model); slice order =
shipping order, each one-verified-slice-per-commit sized. Standing rules
apply (GameCore-first; MS2 = heroes only; no MS2 music; content seeds at
New Game). Parked, deliberately not goals: prestige/rebirth · real-money
gems · server authority (design §9) · zone drop-table hints · manual
achievement-claim UX · dungeon BFS build-reveal anim · tilt-shift
band-blur · SDF jiggle-rope tail · crypt mid-run merchant/boon-draft
(user cut 2026-07-07; must never dilute dust's permanent-boon role).

**10.1 The Great Rebalance (sim-driven — unblocks everything else)**
The balance sim's first run (2026-07-07) found the walls; these findings
are THE inputs, and the fix intents are user-gated (Your calls #1):
- thorns boss gates anti-scale: every 4th stage's boss wears Thorns
  (ModifierCycle[(stage-1)%4]) and reflect scales with attacker damage —
  stages 32/36/44/48/52 are unwinnable even at L100 full-mythic while
  their neighbors fall at L1; 20/40 stack thorns × MajorBossMult.
- the ladder's math ends near stage 53: no level/gear combo clears 55+
  inside the 30s boss timer (MonsterHpGrowth 1.18^stage vs ilvl-linear
  gear). Stages 55–100 are currently decorative.
- gear rarity dominates level: a full rare set clears stages 1–27 at
  hero level 1; bare heroes need L37 by 25 and L75 by 28.
(Sim excludes tower buffs/boons/enhance/imprints — the real frontier sits
a bit deeper, but not 45 stages deeper. Caster pacing lever if ever
needed: per-skill CooldownMs.) Slices: (a) fix thorns anti-scaling;
(b) flatten the curve so 55–100 is reachable (BalanceSim walls chart
green to 100 = the acceptance test); (c) close the gear≫level gap;
(d) extend BalanceSim: model tower buffs/crypt boons/enhance, add a
`pace` mode (full simulated playthrough, charts hours-to-stage);
(e) re-run walls + farm charts, commit tuning + updated findings together.

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
AttackFx/skill sprite; (c) frost VFX retune (reads white under bloom —
drop the ×2.5–3 glow clamps); (d) projectile trails (TrailRenderer,
pooled); (e) kill-streak feedback: N kills in 2s → small screen pulse +
feed line; (f) sound mix bus: SFX ducking under big moments, volume
sliders in Settings (foundation for 10.9).

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
2026-07-07)**
The proven pipeline (SdfBlendShell shader + SdfBlobRig + SdfBlobAnimator
with Hop/Walk/Float gaits, seamless smin bodies under faceted normals)
reaches only 3 of 30+ monsters — and the crypt, where packs are densest
and monsters are seen closest, uses none of it. Slices:
(a) author a CRYPT-NATIVE blob family in SdfBlobDefs: grave ooze
(Hop), bone amalgam (Walk — legs swing on the existing hip logic),
crypt wraith (Float) — dark palettes with GENTLE glows; each needs a
GameCore MonsterDef (stats/XP/gold, content-as-data) + CryptTierDef
roster entry; new content seeds at New Game;
(b) molten + frost tier blobs (magma pulse-blob, frost drifter) so
every crypt tier mixes at least one SDF critter into its roster;
(c) ONE new gait family in SdfBlobAnimator (Your calls #6: Slither or
Pulse); keep the IMonsterAnim feed shape unchanged;
(d) an SDF ELITE/BOSS: one big multi-prim creature for a deep crypt
tier — check the 16-prim uniform budget first (raise it or compose two
rigs if a boss silhouette needs more);
(e) profile at packed-maze density (78+ mobs live today): per-blob
mesh + MPB cost, subdivision level, cull distance — the acceptance
gate before blobs go wide in dungeon rosters;
(f) overworld backfill LAST: swap 2-3 faceted zone monsters whose
silhouettes suit blending (bog_horror, chaos_spawn) — faceted Tunic
look stays the art rule; SDF is a body style, not a restyle.

**10.11 Hero look & skill-FX identity (user-assigned 2026-07-07 — NEXT)**
The shipped heroes wear arbitrary MS2 outfits ("ugly hats") instead of
reading as their class stereotype, and projectiles/skill FX are glowing
primitive spheres. All hero work rides the existing MS2 pipeline
(manifest json → bake `--renders` → USER EYEBALLS → `--export` →
Tools > Build Hero Animators; raw extracts stay outside the repo). Slices:
(a) DONE 2026-07-07: Xml.m2d extracted; `art/tools/wardrobe.py` browses
the wardrobe (keyword → id/name/slot/gender + manifest-ready nif path);
(b-d) DONE 2026-07-07 (user-picked renders): Ice Mage = magician hat +
Winter Fairy Tale Snowflake set; Assassin = red pirate-bandana wrap over
red/black-tinted Shadowy Spiked set; Fire Mage = hot-tinted magician hat
+ warm Magician's Robe. Knight/Priest audited fine as-is;
(e) DONE 2026-07-07: spike ran, user picked PROCEDURAL — `FxKit`
(code-built crystal meshes + IdleGame/FxAdditive halo shader) is the
FX language, spike folder deleted;
(f) DONE 2026-07-07: FxKit covers every projectile in the registry —
ice (bolt/tumbling frostbolt/blizzard crystal ring), fire (molten
chunk + meteor), holy (light shard); physical heroes are melee (their
impact language belongs to 10.6b). 10.11 COMPLETE.

## Shipped ledger (newest first — full receipts in `git log`)

- 2026-07-09 10.7 crypt overhaul COMPLETE: room roles/keys, wave phases,
  chests/mimics/reward vault, client tells, mid-run persistence+resume+summary,
  BalanceSim `crypt` depth difficulty/reward chart (10.7a–g)
- 2026-07-07 10.11 complete: wardrobe browser, hero relooks
  (user-picked), FxKit procedural FX for all projectiles (`638d356`..)
- 2026-07-07 Projectile release-frame launch (`b91359d`)
- 2026-07-07 Anim feel: wing flap + hero swing/cast stutter fix (`0c5e1d4`)
- 2026-07-07 Balance sim (walls|sweep|farm) + wall findings (`4635483`)
- 2026-07-07 Project audit: dead files, stale docs, clean sweep (`ed3d347`)
- 2026-07-06 Crypt meta: keys, 3-floor runs, chest, boons (`7462d84`, `14ca7a5`)
- 2026-07-06 Dungeon mode ×5 passes → packed maze + room aggro (`ba378d7`..`a10135a`)
- 2026-07-05 Terrain slices 1+2: arenas + terraces/water (`24cf528`, `839a0bc`)
- 2026-07-05 Presentation debt: impact sounds, priest FX (`845c2b3`)
- 2026-07-05 Ranged feel: speed, launch chain, anti-stutter (`1d67c7d`..`3b18ead`)
- 2026-07-05 Tower floor gems + quest rework + modifier UX (`92f8588`)
- 2026-07-04/05 SDF blend-shell: shader → gaits → swamp trio (`5f17ce1`..`ccfd9a9`)
- 2026-07-04 Party feel batch: formation, kite/peel, leader UI (`208b435`..`8364850`)
- 2026-07-03/04 Gacha MVP + Ice Mage banner · monster procedural anim ·
  combat-feel/QoL batch

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
