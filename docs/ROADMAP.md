# Roadmap — the ONE "what's next" doc (restructured 2026-07-07)

Living priority list; update in the same commit that ships an item. Durable
design → [`game-design.md`](game-design.md); session orientation →
[`../CLAUDE.md`](../CLAUDE.md). Shipped work gets ONE ledger line here —
full receipts live in the git commit messages. **ENFORCED:** `DocsTests`
in GameCore.Tests fails the suite if this file exceeds 250 lines or loses
its sections — prune, don't raise the budget.

## Where the game stands

Shipped and playable: the full core loop (farm ladder → loot → build →
push), 10 themed zones, Tower (per-floor reward bundles), Crypt
roguelite, Endless, quests/achievements/daily gems in the Goals hub,
gacha (live Ice Mage banner), combat juice, zone ambience + a UI sound
family, and a balance simulator over pure GameCore. Roster: Knight /
Fire Mage / Assassin / Priest (+ banner Ice Mage) on the MS2 skinned
pipeline; monsters faceted or SDF blend-shell. **857 GameCore tests
green.** The 100-stage ladder is FIXED (10.1). **Phase M mobile arc:
MM1-MM5 + MM8 shipped; the UI polish arc (10.23) is COMPLETE — MM6
(cloud save) is next and BLOCKED on call 11.** Gaps: music (10.9a,
PAUSED), laptop perf (10.12e), the user's ears pass.

## Your calls — decisions waiting on the USER

1-6, 8, 9. RESOLVED 2026-07-09..14 (orientation = LANDSCAPE; the arc
   opened at 10.13).
7. **Older feel list** (user: "later" — parked): arena sizes/roam, water
   colour, stair treads, terrace hop, caster MoveSpd vs follow floor,
   launch timing, corpse-linger, melee spacing, priest FX, formation
   knobs (standoff 4.6 / panic 1.8 / aggro 2.0), run-clip foot-slide.
10. Still owed: ears pass (ambience / UI sounds / juice feel),
   astral-bed music-or-not, laptop `-benchmark` run (10.12e).
11. **10.18 cloud-save brief verdicts** — provider (rec: Unity Gaming
   Services, which also settles 10.19), account model, sync policy,
   desktop participation. 10.18 is BLOCKED until these land.

## Backlog — pre-sliced majors

**NEXT UP: the MOBILE ARC (Phase M, locked 2026-07-14 — durable scope
in design-doc §8; this file owns priority + slicing). CUT by the same
verdict (final — never re-pitch): prestige/rebirth (idle ARPG, NOT an
incremental) and offline-depth expansion. Social-lite = later, not cut.**

Also parked, not goals: real-money gems · server authority (§9) · zone
drop-table hints · manual achievement-claim UX · BFS build-reveal anim ·
tilt-shift band-blur · SDF jiggle tail · crypt mid-run merchant/boon-draft.

**10.23 UI polish arc — COMPLETE 2026-09-04 (user: "best UX and
impressiveness").** P1 reskin · P2 layering · P3 motion · icons · gacha
reveal · arrival card · HUD cards · feed slide-in. NOT worth it, don't
re-pitch (2026-08-19, measured): count-ups (feed already prints "+X"); NAV
icons (clusters use 1016 of 1280 ref width; 7 icons eat 210 of 264 slack).

**MM1-MM5 (10.13-10.17) COMPLETE 2026-07-15 — receipts in the ledger.
Durable lessons the next slices ride:** non-interactive IMGUI remains by
choice (party chips, currency HUD, world health bars) · KIT FOOT-GUN: a
PanelKit.Window's `body` is a bare Flex — `Stack(body)` BEFORE adding
rows or children collapse to a zero-size blob, while a Modal's body
ALREADY has the group so never Stack it · `Inventory.SalvageAll` is
NUCLEAR (every loose unlocked item, any rarity — NOT the loot filter),
so aggregates must equip-sweep FIRST or they destroy upgrades ·
cfg-aware retro-grants live beside SyncHeroUnlocks at LOAD, never in the
cfg-less Migrate, and loot-path reducers RE-WRAP nested Progress state
so threading sweeps assert VALUE survival, not ref identity · schedule
rules are PURE functions of nowMs, effects snapshot AT FIGHT INIT via an
optional `nowMs = 0` · endgame gold/scrap FLOOD material sinks; the
binding 6-month sink is gem-gated ascension (GemFraction 0.33).

**10.18 Cloud save & identity (MM6).** Platform auth (Play Games /
Game Center + guest), save sync, conflict UX that shows BOTH summaries
("Phone: stage 34, 2h ago / Tablet: stage 31, yesterday") and lets the
player pick. Versioned saves + `Migrate` are the base; de-risks §9.

**10.19 Telemetry & remote config (MM7).** Privacy-light funnel events
(FTUE beat completion, session length, wall positions, gacha
conversion) — every retention decision above is a guess until
measured. Remote `GameConfig` seam so balance patches skip store
review. Prerequisite for any monetization decision.

**10.20 Accessibility & l10n (MM8) — a11y + string-table foundation
SHIPPED 2026-07-16; PHASE-2 sweep IN PROGRESS.** Done: StatDisplay /
Compare / Tower + the `Loc.Content` seam (item/hero/zone/set/modifier
names by stable id — one helper per kind, wrong prefix won't compile).
REMAINS: Inventory / Equipment / Gacha / Goals-body / Modifier. Durable:
uGUI Text CLIPS to its rect, so every text-bearing rect AND chrome metric
must ride the text scale or labels behead; rarity marks avoid ▲/▼/✦.

**10.21 Monetization charter (MM9 — design-only, written BEFORE any
real-money work un-parks).** What we sell (roster breadth via gacha,
time-respect boosts, cosmetics) and NEVER sell (power past walls
BalanceSim shows are designed frustration); pity/rate disclosure
standards. A document, not code — it keeps monetization from warping
the game design later.

**10.22 Handheld feel pass (MM10 — ships LAST).** Haptic vocabulary
(crit tick, boss thump, gacha reveal), thermal-aware auto-tiering
(extends the 10.12 quality tiers with device detection), 30fps battery
mode, cold start < 5s.

**10.9 Audio identity (remaining).** (a) music beds PAUSED BY THE USER
2026-07-14 — do NOT re-pitch; (b) AudioDirector + (e) the music slider
wait on (a) (Ambience crossfade host + 10.6f duckExempt = scaffolds);
(c) ambience + (d) UI sounds SHIPPED (ledger) — the audibility ears
pass is still owed by the user (the editor audio wedge looks healed).

**10.12 Performance & mobile-readiness — (a-d) SHIPPED (ledger); (e)
the laptop `-benchmark` run is the final confirmation (user-gated).**
Rig SWEEPS levers per run (scale 1/.75/.6, shadows+post isolated,
boot_to_playable_ms, `-benchmarkTag`) — one trip says WHICH cut to
spend. Traps: vSync>0 ⇒ targetFrameRate IGNORED; Shader.Find-only
customs get STRIPPED (→ Resources/Shaders); gates read MESH bounds.

**10.3 UI kit lessons (shipped 2026-07-11 — 10.13 and the polish arc
ride them):** force-expand HLG clamps CHILD flex to ≥1 — a fixed-height
cell in a Row needs a non-expanding VStack slot (explicit
flexibleHeight=0 does NOT work); windows/modals scale match-0.5
(match-width starves height at 21:9); `KeepOnCanvas` heals are
DISPLAY-ONLY (never persist a clamp); UIFont.ttf untracked, importer
fallbacks machine-local (re-apply on checkout).

**10.5 loot lessons (shipped 2026-07-11):** the SWEEP CONTRACT (bulk
verbs skip stale/guarded entries silently, single verbs throw — say
which in the doc comment); flat bonuses on multiplier-ish stats explode
late-tier (the §6.2 ≤8% gate test is the tuner, not eyes); additive
fields on hand-copied models must thread EVERY copy site — grep
`new Item` / `new HeroInstance` first.

## Shipped ledger (newest first — full receipts in `git log`)

- 2026-09-04 feed slide-in (10.23) — the arc's LAST item, 10.23 COMPLETE: a
  new line rises in from below instead of popping. The feed is ONE rich-text
  label, so there is no per-row transform to animate; the handle that DOES
  exist is the scroll POSITION — start one line back, ease to the pinned
  bottom (150ms, unscaled, Reduced-Motion-settled). Only AddFeed slides: a
  rebuild would replay the arrival of a line already read.
- 2026-09-04 HUD cards (10.23): party chips were flat DrawRect slabs and the
  wallet bare text on the diorama — the last surfaces reading as debug overlay
  beside the rounded, shadowed uGUI panels. Both now sit on the kit's OWN baked
  sprites, 9-sliced by hand for IMGUI (`DrawSliced`/`DrawCard`, scratch arrays
  so OnGUI still allocates ~nothing): the same arcs as every window, not a
  lookalike that drifts when a radius changes. The wallet card is MEASURED off
  the widest line, so 130% text or a 999.9M jump grows it, never spills it.
- 2026-09-04 arrival card (10.23): the boot payoff stopped being a list of
  labelled lines and became three TILES (number loud, caption quiet) that
  land in sequence and count up — the arc's card language, on the offline
  return. Unscaled + Reduced-Motion-settled; every band pinned with
  PanelKit.Fixed and the PANEL SIZED FROM THE SAME NUMBERS, because sizing
  a card by guesswork clipped its own title at 100% (Play-caught, the 10.20
  trap). Verified at 100% and 130% text scale, zero clipped rects.
- 2026-08-19 UI polish P3 motion (10.23): windows ease IN (130ms) and OUT
  (90ms); controls squash 4% under the finger. Panels are destroy-and-rebuild,
  so a build ≠ an open and a destroy ≠ a close: `IsRebuild` gates the entrance
  (killing the chime-on-redraw wart), the exit gates structurally (public
  `Close()` animates, rebuild paths tear down instantly), a closing canvas is
  renamed + raycast-deaf, and the squash rides `ApplyButtonStates` so tint and
  movement are one contract. The gacha reveal gained a neutral WIND-UP whose
  length is the rarity tell (skippable), a card so the payoff isn't text over
  the banner, unscaled time (it ran at 2× in alt modes) and a ReducedMotion gate.
- 2026-08-19 icons (10.23): the 5 equip slots + 3 wallet currencies stopped
  being text. game-icons.net art (CC BY 3.0 — Lorc + Delapouite), baked
  white-on-transparent (`art/icons/build.py`) so ONE tint serves anything;
  `SlotIcon`/`IconTex` cache misses. Attribution in SETTINGS — CC BY binds it.
- 2026-08-14 movement freeze FIXED (user-reported, stage 24): two defects
  in `Combat.StepAlong`, PERMANENT because the geometry is stateless — a
  slide could collapse onto the unit's own position and read as success,
  and doubly-blocked units held in concave bays. Slides must now DISPLACE,
  strays project back on, a 30° ring sweep escapes pockets. 412 steps → 0.
- 2026-08-01..06 UI polish P1+P2 (10.23): the warm "Tunic" reskin at KIT
  level — Theme palette rotated warm (RED highest channel, data-carrying
  hues frozen), procedural rounded 9-slice + shadow sprites, ColorTint
  press states · P2 layering: Summon backdrop, Heroes scroll, IMGUI
  z-order, bottom-fade, the NavBar band, Tower packs ring. 848 tests.
- 2026-07-16 10.20(a-c) a11y + l10n foundation (MM8): Text Size
  100/115/130 via one `UiKit.Scaled` multiplier (IMGUI styles restamped
  per OnGUI), Reduced Motion (hit-stop, shake, pulse), Haptics toggle +
  stub seam; rarity glyph marks ● ■ ◆ ★ beside every colour; `Loc` table
  (225 keys) + LocTests key-existence contract; `EventInfo.ZoneIndex`
  ends GameCore's English banner leak. 846 tests.
- 2026-07-15 10.17 endgame sinks COMPLETE: ascension (dupes → per-hero
  shards; universal shards on endless milestones; 5★ × +4% hero-local;
  costs 10/20/30/50/80, hero wallet first; Stars + AscensionState
  sweep-threaded) + BalanceSim `sinks` (24.2wk horizon, ≥20wk gate).
  841 tests; Play-verified.
- 2026-07-15 10.16 live-ops COMPLETE (user: 10% magnitudes, monthly):
  weekend zone boost [Sat,Mon) rotating weekly + mutated crypt
  [Wed,Fri) (+1 modifier, +10% dust); monthly 30-tier free season
  (10 pts/quest, auto-pay, gems every 5th); Season tab + Today
  live-events + boot banners. 823 tests; verified on the live window.
- 2026-07-15 10.15 codex/collection COMPLETE: CodexState (lifetime kills
  via PendingKills + BankKills tiers; set discovery stamped at AddLoot
  BEFORE the salvage decision — seen is seen). Each tier pays +0.1%
  Hp/Atk/Def (≤25% ceiling-tested); Codex tab; retro-stamp. 794 tests.
- 2026-07-15 10.14 the 30-second session COMPLETE: `Session.Arrive` (idle +
  daily as ONE atomic boot payoff) + `Session.Preview/Apply` (the Manage
  super-verb: claim → equip sweep → nuclear salvage). Boot taps: 2.
- 2026-07-14/15 10.13 touch-first UI reflow COMPLETE: SafeArea + pinch zoom
  (a) · uGUI thumb-reach NavBar replaced the IMGUI control bar (b) · 44pt
  floors + safe-inset panels (c) · Settings/Tower/Modifier/MainMenu onto
  PanelKit — ZERO hand-placed windows (d) · TopControls + ModesWindow retire
  the last interactive IMGUI (e). Verified at 2340×1080.
- 2026-07-14 Tower per-floor reward bundles: first clear banks gold + a
  boss-loot bundle in `Tower.RecordClear` (exploit-proof gate; kills still
  pay nothing), anchored to `Tower.StageEquivalent`. 760 tests.
- 2026-07-13 10.6 combat juice COMPLETE: hit-stop (income provably untaxed
  — sim accumulator on REAL time × mode speed) · per-element ImpactBursts ·
  frost de-whited · trail ribbons · kill-streak beats · SFX duck bus.
- 2026-07-13 10.9(c)+(d): 16 zone ambience beds (AMB bank, crossfade host
  = the AudioDirector scaffold, volume slider) + one UI sound family at
  BOTH button factories, tile ticks, popups, claim, deny/spend, enchant.
- 2026-07-12/13 10.10 SDF monster expansion COMPLETE: 8 blob critters ·
  Slither+Pulse gaits · Ossuary Wyrm crypt boss · perf gate passed. Art
  lessons in SdfBlobDefs; faceted Tunic stays the rule.
- 2026-07-12 10.8 Endless COMPLETE: `StageFor` generates rows past the
  table, zones cycle, EndlessBest (save v3), "Push beyond…" nav
  (`MaxSelectableStage` = the ONE selection rule), gems every 5th depth.
- 2026-07-11/12 crypt own-depth curve + tuning (user verdicts): floors
  anchor to `Crypt.StageEquivalent` — nothing to sandbag; HP ×0.6 + atk
  +2%/floor (walls = wipes, never timer slogs); HpGrowth 1.045 / DmgGrowth
  1.05 / RewardGrowth 1.06, boon base 20→50 · 2× speed toggle (Crypt+Tower;
  timeScale pinned off the mode kind every frame) · CastRootMs 700.
- 2026-07-11 10.5 Loot QoL 2.0 COMPLETE: per-slot loot filter + imprint
  guard · CompareCard · SalvageMany + Select mode · §6.2 set bonuses
  (30 gen'd, gate-tuned ≤8%) + tells · loadout snapshots. 738 tests.
- 2026-07-11 10.12(a-d) perf: FrameCap 60/10 · StepCombat scratch buffers
  (~31→11 KB/frame) · scenery cache + static combine (~1,514→~486
  batches) · shader Prewarm · quality tiers → GraphicsQuality.Apply.
- 2026-07-10 10.4 Goals hub COMPLETE (§7.5): Claimables/ClaimAll +
  PreviewNext; Goals on PanelKit; pip; claim loop verified. 10.11 hero look
  & FX (2026-07-07): wardrobe browser, relooks, FxKit projectile FX.
- 2026-07-10/11 10.3 UI foundation COMPLETE: Theme tokens + PanelKit
  layout kit (+Modal); Heroes/Inventory/three modals migrated — zero
  positional literals; font audit; verified at 16:9/16:10/21:9.
- 2026-07-10 10.2 FTUE COMPLETE (§7.4): staged-reveal gating, five guided
  intro beats + toasts/intro strip/breadcrumb. Play-verified S0→S12.
- 2026-07-09 10.1 The Great Rebalance COMPLETE: thorns capped mirror,
  gear/level ~50/50, per-tier + major-boss tapers (soft wall ~80-90;
  stage 100 = the ~L100 capstone), BalanceSim stacks + `pace` mode.
- 2026-07-09 10.7 crypt overhaul COMPLETE: room roles/keys, wave phases,
  chests/mimics/vault, tells, mid-run resume+summary (10.7a–g).
- 2026-07-03..07 foundation week (receipts in git): gacha MVP + Ice
  Mage banner · SDF blend-shell + party feel · terrain slices + Tower
  gems/quest rework · crypt meta + packed maze · balance sim + audit ·
  projectile release-frame launch · anim feel fixes.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
