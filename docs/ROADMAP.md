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
**751 GameCore tests green.** The 100-stage ladder is FIXED (10.1):
stage 100 = a reachable prestige gate, sim-verified. FTUE (10.2) and
UI foundation (10.3) COMPLETE. Headline problem now: performance on
weak/mobile hardware — see 10.12.

## Your calls — decisions waiting on the USER

Nothing here blocks autonomous work elsewhere, but these need your verdict
before anyone acts on them:

1. SHIPPED 2026-07-09: 10.1 rebalance (ledger below; sim-proven curve).
2. RESOLVED+SHIPPED 2026-07-12: casters ROOT during casts (CastRootMs
   700 — MoveToward no-ops; kiting cost accepted by the user).
3-6, 8. RESOLVED 2026-07-11/12 (user verdicts): crypt tuning + OWN
   depth curve + 2× alt-mode toggle all shipped · key cadence + ~996
   test keys STAY · crypt lighting fine · terrain look approved
   (slice 3 backlog, unscheduled) · gaits = BOTH, shipped.
7. **Older feel list** (user: "later" — parked): arena sizes/roam,
   water colour, stair-tread chunkiness, terrace hop speed, caster
   MoveSpd vs follow floor (overworld), anim-end vs contact launch,
   corpse-linger, melee spacing, priest FX in real combat, formation
   knobs (standoff 4.6 / panic 1.8 / aggro 2.0), run-clip foot-slide.

## Backlog — pre-sliced majors (brainstormed 2026-07-07)

**NEXT UP: 10.6 juice / 10.9 audio / 10.10(f) overworld backfill.
Perf SELF-VERIFIED 2026-07-12 (`-benchmark` mode; desktop\@720p
1.7-2.0ms avg, ~0 GC) — run the exe on the laptop for benchmark.json.
BUILD BUG fixed: Shader.Find-only shaders get stripped from builds —
custom shaders MUST live under Resources/.**

**Crypt own-depth curve — SHIPPED 2026-07-12** (user verdict): floors
anchor to `Crypt.StageEquivalent` (stage 8 at depth 1 → ~100 at 60,
linear on the campaign's tapered curves) — the player's stage is
irrelevant (nothing to sandbag); loot ilvl + kill gold/XP ride the
depth (key-bounded). Feel shapers: HP ×0.6 (rooms stay brisk) + atk
+2%/floor (deep failure = WIPE, never a timeout). Chart: rare/L50
walls at depth 31 by wipe; legendary/mid clears 60 at 200-400s sweeps.

**2× speed toggle — SHIPPED 2026-07-12** (user verdict): Crypt + Tower
get a 2x button beside Exit (campaign/endless stay 1×); Time.timeScale
applied EVERY frame off the mode kind (no transition can leak a scaled
clock), pref client-side. Play-verified: campaign pins 1, dungeon 2.

Self-contained briefs; slice order = shipping order, one verified slice
per commit. Standing rules apply (GameCore-first; MS2 = heroes only; no
MS2 music; content seeds at New Game). Parked, deliberately not goals:
prestige/rebirth · real-money gems · server authority (§9) · zone
drop-table hints · manual achievement-claim UX · BFS build-reveal anim ·
tilt-shift band-blur · SDF jiggle-rope tail · crypt mid-run merchant/
boon-draft (user cut 2026-07-07; never dilute dust's permanent role).

**10.3 UI/UX foundation — COMPLETE 2026-07-11.** Kit lessons that BITE:
rows pin flexibleHeight=0 + minHeight, BUT force-expand clamps CHILD
flex to ≥1 (a fixed-height cell in a row needs a non-expanding VStack
slot); windows/modals scale match-0.5 (match-width starves height at
21:9); `KeepOnCanvas` heals are DISPLAY-ONLY (persisting a clamp taken
against a transient mid-switch canvas size clobbers layout — happened).
UIFont.ttf deliberately untracked; importer fallbacks machine-local —
re-apply on fresh checkout. Hand-placed still: Settings, TowerView,
ModifierPanel, MainMenu (migrate when touched).

**10.5 Loot QoL 2.0 — COMPLETE 2026-07-11.** Durable lessons: the SWEEP
CONTRACT (bulk verbs skip stale/guarded entries silently, single verbs
throw — state which in the doc comment); flat set bonuses on
multiplier-ish stats explode late-tier (the §6.2 ≤8% gate test is the
tuner, not eyes); additive fields on hand-copied models MUST thread
through every copy site or a reducer silently strips them — grep
`new Item`/`new HeroInstance`/`new ProgressState` when adding one.

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
SdfBlendShell, DungeonLit) compiled via real hidden-RT draws.
(d) SHIPPED 2026-07-12 (quality tiers in Settings — see NEXT UP);
(e) the user's laptop run of `-benchmark` = final confirmation.

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

**10.8 Endless mode ("deepest stage") — COMPLETE 2026-07-12**
(a) SHIPPED 2026-07-11: `GameConfig.StageFor` generates rows past the
table (boot formulas, memoized; scaling continues at the gentle
last-tier rates), zones cycle, EndlessBest in ProgressState (v3),
SetStage opens endless after stage 100. (b) SHIPPED 2026-07-12: "Push
beyond…" nav entry + "Endless N" label; `MaxSelectableStage` = the ONE
selection rule (nav + reducer share it). (c) SHIPPED 2026-07-12: gems
every 5th NEW depth (gold/XP/loot formulas already extend past the
table). (d) SHIPPED 2026-07-12: BalanceSim `endless` mode; rate taper
EndlessRateGrowth 1.02 past the table (raw ×2.2/tier OVERFLOWED long
gold by depth ~200 — sim-caught); endgame entry power walls ~depth 91,
economy ×2.3 over the push, idle-farmable to ~186. (e) SHIPPED
2026-07-12: depth record on the account chip (change-only poll) +
record/milestone feed beats — Play-verified through a real depth-5
clear (record line, +10 gems, chip label).

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
(a) SHIPPED 2026-07-12: grave ooze / bone amalgam / crypt wraith join
the crypt roster. Palette lesson: dungeon light DESATURATES — a hue
must dominate its channels outright or it reads as rock;
(b) SHIPPED 2026-07-12: magma blob (obsidian + ember contrast pair) +
frost drifter (dark-on-light); every crypt tier mixes shells;
(c) SHIPPED 2026-07-12 (verdict #6 = BOTH): Slither (head→tail wave
over seg0..segN, tail-weighted, never rests; first user = crypt Bone
Serpent, the (d) boss shape rehearsal) + Pulse (|sin|³ heartbeat, ±7%
swell, quickens with travel; magma_blob on its named gait). Feed
shape unchanged; both mechanics verified numerically in live combat;
(d) SHIPPED 2026-07-13: Ossuary Wyrm, the crypt tier's OWN boss
(nightmare_maw stays the campaign's; stats identical). 13/16 prims:
z-lying capsule chain (sphere chains scallop at boss radii) + FAT
far-jutting details riding the wave via the new nearest-seg rider
binding; dark body + bleached skull (pale floors — contrast pairs);
per-def Subdivisions (boss 12) shrinks seam membranes;
(e) PASSED 2026-07-12 (no knobs turned): 512-640 tris + ≤2.1µs MPB
push + ZERO GC per blob; 120 live ≈ 0.26ms/frame; shadows match
faceted mobs. Blobs cleared to go wide — (f) unblocked;
(f) overworld backfill LAST (bog_horror, chaos_spawn) — faceted Tunic
stays the art rule; SDF is a body style, not a restyle.

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

- 2026-07-11 crypt tuning (call #3): ramp split (HpGrowth 1.045 /
  DmgGrowth 1.05 — deep failure = wipe, never a run-timer slog) +
  CryptRewardGrowth 1.06 on chest dust/room gold (dust/hour now RISES
  with depth; was ~9× decay) · boon base 20→50 (finite-height economy).
- 2026-07-11 10.5 Loot QoL 2.0 COMPLETE: per-slot loot filter + imprint
  guard · CompareCard compare-anywhere · SalvageMany + Select mode ·
  §6.2 set bonuses (30 gen'd, gate-tuned ≤8%) + tells · loadout
  snapshots. 699 → 738 tests, all Play-verified.
- 2026-07-11 10.12(a-c2) perf: FrameCap 60/10 · StepCombat scratch ·
  scenery combine (~1,514→~486) · Prewarm; (d)+(e) laptop-gated.
- 2026-07-10 10.4 Goals hub COMPLETE (§7.5): Claimables/ClaimAll read
  model + PreviewNext; Goals window on PanelKit; control-bar pip;
  Achievements panel retired. Play-verified claim loop end-to-end.
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
- 2026-07-07 Balance sim (walls|sweep|farm) + wall findings · project
  audit (`4635483`, `ed3d347`)
- 2026-07-06 Crypt meta (keys/runs/chests/boons) · dungeon mode ×5
  passes → packed maze + room aggro (`7462d84`..`a10135a`)
- 2026-07-05 Terrain slices 1+2 · presentation debt · ranged feel ·
  Tower gems/quest rework/modifier UX (`24cf528`..`92f8588`)
- 2026-07-04/05 SDF blend-shell (shader → gaits → swamp trio) · party
  feel batch: formation, kite/peel, leader UI (`5f17ce1`..`8364850`)
- 2026-07-03/04 Gacha MVP + Ice Mage banner · monster anim · QoL batch

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
