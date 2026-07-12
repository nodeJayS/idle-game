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
**715 GameCore tests green.** The 100-stage ladder is FIXED (10.1,
2026-07-09): stage 100 is a reachable prestige gate demanding
near-mythic gear + maxed account stacks, sim-verified. FTUE shipped
2026-07-10 (two-button open, five guided beats, staged HUD reveal).
UI foundation (10.3) COMPLETE 2026-07-11. Headline problem now:
performance on weak/mobile hardware — see 10.12.

## Your calls — decisions waiting on the USER

Nothing here blocks autonomous work elsewhere, but these need your verdict
before anyone acts on them:

1. SHIPPED 2026-07-09: 10.1 rebalance (ledger below; sim-proven curve).
2. **Root casters during casts?** The last cast-cancel source: the sim
   moves a caster mid-cast and the clip travel-cancels. Rooting them is a
   sim/balance change (kiting implications).
3. **Crypt tuning:** key cadence (1/UTC day too stingy?) and depth-ramp
   steepness — both single Balance constants. New input, `crypt` sim
   chart 2026-07-09: a stage-25/L50/rare party walls at depth 50, and
   dust/hour DECAYS ~9× from depth 1→49 (floors lengthen ×12 while
   floor rewards barely grow) — deeper runs pay strictly worse, so the
   reward-growth constants want a look alongside the ramp.
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
9. RESOLVED 2026-07-07: skill FX procedural; hero relooks shipped.

## Backlog — pre-sliced majors (brainstormed 2026-07-07)

**NEXT UP: 10.12(e) laptop verify → (d) tiers → 10.5c-e / user pick.**

Self-contained briefs; slice order = shipping order, one verified slice
per commit. Standing rules apply (GameCore-first; MS2 = heroes only; no
MS2 music; content seeds at New Game). Parked, deliberately not goals:
prestige/rebirth · real-money gems · server authority (§9) · zone
drop-table hints · manual achievement-claim UX · BFS build-reveal anim ·
tilt-shift band-blur · SDF jiggle-rope tail · crypt mid-run merchant/
boon-draft (user cut 2026-07-07; never dilute dust's permanent role).

**10.3 UI/UX foundation refactor — COMPLETE 2026-07-11** (ledger below;
receipts in git). Kit lessons that BITE, kept here for future screens:
rows pin flexibleHeight=0 + minHeight (force-expand HLGs report phantom
flex to their parent) BUT force-expand also clamps CHILD flex to ≥1, so
a fixed-height cell inside a row needs a non-expanding VStack slot;
windows/modals scale match-0.5 (match-width leaves ~540 canvas units at
21:9 — vanishing labels = height starvation); `KeepOnCanvas` heals are
DISPLAY-ONLY (persisting a clamp taken against a transient mid-switch
canvas size clobbers the user's layout — happened, Play-caught).
Font: UIFont.ttf (+meta) DELIBERATELY untracked; importer fallbacks
(HYWenHei → Segoe UI Symbol → Segoe UI → Arial) are machine-local —
re-apply on fresh checkout. Glyph audit: 24/24 covered. Still
hand-placed (deferred): Settings panel, TowerView, ModifierPanel,
MainMenu — migrate opportunistically when next touched.

**10.5 Loot QoL 2.0 (legibility at scale)**
Late-game bags are noise. (a) SHIPPED 2026-07-11 — loot filter:
`LootFilterState` on ProgressState (per-slot floors + imprint guard,
guard defaults ON; `Inventory.WouldAutoSalvage` = THE predicate; all
salvage paths refuse guarded imprints like Locked; legacy pref
carried over once at boot then cleared; 715 tests). (b) SHIPPED
2026-07-11 — CompareCard (best-FIELDED-hero delta: all-verdict headline,
DPS/EHP, raw rows, benched fallback) in the bag pane; Heroes screen
gains the cross-hero "▲ for X instead" line. (c) SHIPPED 2026-07-11 —
`SalvageMany` (sweep contract: skips guarded/stale ids silently vs
SalvageItem's throw; 720 tests) + bag Select mode (tap-toggle, sweep
only SELECTS, guarded tiles get no relay). (d) design locked §6.2;
CORE SHIPPED 2026-07-11 (30 generated sets, Rare+ roll on the drop
rng, 2pc/4pc flat adds in ComputeHeroStats; the ≤8% gate test FAILED
1×/2× affix budgets — shipped 0.35×/0.7×; 733 tests); UI tells
("«Set» n/4" + bonus lines) still to come. (e) per-hero gear
loadout snapshots (save/apply, bag-integrity checked).

**10.12 Performance & mobile-readiness (user report 2026-07-11: weak
laptop OVERHEATS, textures pop in late; goal = playable on mobile)**
Measure first (Unity Profiler via MCP; dev build on the laptop if
possible), then slice by evidence. Static-recon suspects, in order:
(a) SHIPPED 2026-07-11 — FrameCap: 60 focused / 10 unfocused,
vSync OFF deliberately (vSyncCount>0 makes Unity ignore
targetFrameRate — a 144Hz panel would still run hot). (b) SHIPPED
2026-07-11 — steady-state GC: StepCombat de-LINQed onto CombatState
scratch buffers (total-order comparers keep byte-identical ordering;
200-step lockstep test guards it) + client per-frame caches; calm
farming ~31→11 KB/frame (editor-measured). Remaining churn is per-KILL
bursty (events/strings/upgrade evals) — pool later only if device
profiling says so. (c) SHIPPED 2026-07-11 — scenery collapse (~1,514 → ~486 batches full
pack): root cause ONE Material PER PROP (leaked per rebuild) → cached
per look; static-combine per zone rebuild (wind mask baked to vertex
ALPHA — post-combine posOS.y un-plants bases); shadow gate from MESH
bounds × scale (renderer.bounds is zero pre-render); cascades 4→2,
dist 60 (Bootstrap consts). (c2) SHIPPED 2026-07-11 — Prewarm behind
the MAIN MENU (campaign crossings have NO LoadingScreen): GroundDetail
styles baked at boot; cold variants (FxAdditive, Lit+emission,
SdfBlendShell, DungeonLit) compiled via real hidden-RT draws. NEXT:
(d) Settings tiers (render scale, post, SDF fallback) — tune AFTER
(e): the user plays the weak laptop (the real acceptance test).
Acceptance: steady frame time at cap on the weak laptop, no first-use
pop-in, thermals sane after 10 min idle-farming.

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

- 2026-07-10 10.4 Goals hub COMPLETE (design §7.5): `Goals.Claimables`/
  `ClaimAll` read model + `DailyLogin.PreviewNext` (10 tests → 699);
  Goals window (Today/Achievements/Login + claim-all + tomorrow
  preview) on PanelKit; control-bar pip; Achievements button + panel
  retired. Play-verified claim loop end-to-end.
- 2026-07-10/11 10.3 COMPLETE: Theme tokens + PanelKit layout-group kit
  (+Modal); Heroes, Inventory, and the three modals (outcome / idle
  claim / gacha reveal) migrated — zero positional literals; HUD
  anchoring (KeepOnCanvas display-only heal, corner regions, HUD
  tokens); font audit (single UiKit.Font everywhere, 24/24 glyphs,
  local importer fallbacks); all verified at 16:9/16:10/21:9
- 2026-07-10 10.2 FTUE COMPLETE (design game-design §7.4): staged-reveal
  gating (`FeatureUnlocked` + New-Game arming), five guided intro beats
  (`IntroQuests`, now paying strictly in beat order), staged button
  reveal, pre-S3 popup gating, reveal toasts, intro strip, celebration
  beats, breadcrumb (clip-fixed). Play-verified New Game walkthrough
  S0→S12: every reveal/beat/toast fired on cue (`2dd332c`..`d30211d`..)
- 2026-07-09 10.1 The Great Rebalance COMPLETE: thorns capped mirror
  (ThornsReflectHpCap), gear/level ~50/50, per-tier HP+damage taper
  (Monster{Hp,Dmg}GrowthByTier), major-boss taper (MajorBossMultByTier:
  mid majors ease so on-curve legendary+mid clears 50-80 at L76-85 and
  soft-walls ~80-90; tier 9 keeps ×2 so stage 100 stays the ~L100
  mythic+max-stacks capstone), BalanceSim account stacks + `pace` mode.
- 2026-07-09 10.7 crypt overhaul COMPLETE: room roles/keys, wave phases,
  chests/mimics/reward vault, client tells, mid-run persistence+resume+summary,
  BalanceSim `crypt` depth difficulty/reward chart (10.7a–g)
- 2026-07-07 10.11 complete: wardrobe browser, hero relooks
  (user-picked), FxKit procedural FX for all projectiles (`638d356`..)
- 2026-07-07 Projectile release-frame launch (`b91359d`) · anim feel: wing flap + swing/cast stutter fix (`0c5e1d4`)
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
