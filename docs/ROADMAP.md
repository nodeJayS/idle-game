# Roadmap — the ONE "what's next" doc (restructured 2026-07-07)

Living priority list; update in the same commit that ships an item. Durable
design → [`game-design.md`](game-design.md); session orientation →
[`../CLAUDE.md`](../CLAUDE.md). Shipped work gets ONE ledger line here —
full receipts live in the git commit messages. **ENFORCED:** `DocsTests`
in GameCore.Tests fails the suite if this file exceeds 250 lines or loses
its sections — prune, don't raise the budget.

## Where the game stands

Shipped and playable: the full core loop (farm ladder → loot → build →
push), 10 themed zones with terraced arenas, Tower (incl. per-floor
reward bundles), Crypt roguelite (own-depth curve, daily keys, dust
boons, SDF boss), Endless, quests/achievements/daily gems in the Goals
hub, gacha (live Ice Mage banner), combat juice, zone ambience + a UI
sound family, and a balance simulator over pure GameCore. Roster:
Knight / Fire Mage / Assassin / Priest (+ banner Ice Mage) on the MS2
skinned pipeline; monsters are faceted or SDF blend-shell.
**760 GameCore tests green.** The 100-stage ladder is FIXED (10.1):
stage 100 = the reachable endgame capstone, sim-verified. **NEW ARC
locked 2026-07-14: Phase M mobile launch (Backlog 10.13+).** Headline
gaps: music (10.9a, PAUSED), weak-hardware confirmation (10.12e,
laptop-gated), the user's ears pass on ambience/UI sounds.

## Your calls — decisions waiting on the USER

1-6, 8. RESOLVED 2026-07-09..12 (receipts in git + design doc): 10.1
   rebalance · caster root · crypt tuning + own-depth curve + 2× toggle ·
   key cadence/test keys stay · lighting/terrain fine · gaits both.
7. **Older feel list** (user: "later" — parked): arena sizes/roam,
   water colour, stair-tread chunkiness, terrace hop speed, caster
   MoveSpd vs follow floor (overworld), anim-end vs contact launch,
   corpse-linger, melee spacing, priest FX in real combat, formation
   knobs (standoff 4.6 / panic 1.8 / aggro 2.0), run-clip foot-slide.
9. **Mobile arc kickoff:** (a) RESOLVED 2026-07-14 — **LANDSCAPE**
   (user verdict; two-hand framing, keeps the diorama camera's current
   read); (b) which milestone opens the arc (rec: 10.13 → 10.14).
10. Still owed from earlier sessions: ears pass (ambience beds / UI
   sounds / juice feel), astral-bed music-or-not verdict, laptop
   `-benchmark` run (10.12e).

## Backlog — pre-sliced majors

**NEXT UP: the MOBILE ARC (Phase M, locked 2026-07-14 — durable scope
in design-doc §8 Phase M; this file owns priority + slicing). CUT by
the same verdict (final — never re-pitch): prestige/rebirth (this is
an idle ARPG, NOT an incremental) and offline-depth expansion (the 12h
idle claim stays as-is). Social-lite (friend codes / share cards /
platform leaderboards) = later, not cut.**

Also parked, deliberately not goals: real-money gems · server authority
(§9) · zone drop-table hints · manual achievement-claim UX · BFS
build-reveal anim · tilt-shift band-blur · SDF jiggle-rope tail · crypt
mid-run merchant/boon-draft (never dilute dust's permanent role).

**10.13 Touch-first UI reflow (MM1) — (a)-(d) SHIPPED 2026-07-14
(ledger).** Remaining coda (e): the last INTERACTIVE IMGUI → uGUI —
DrawTopControls (top-centre stage nav / Challenge / Exit / 2x) and
DrawModesPanel (the mode-select menu); non-interactive IMGUI (party
chips, currency HUD, world health bars) can stay for now (touch
doesn't care; migrate when touched). NEW KIT FOOT-GUN (Play-caught):
a PanelKit.Window's `body` is a bare Flex — call `PanelKit.Stack(body)`
(or add a layout group) BEFORE adding rows, or every child collapses
to a centered zero-size blob.

**10.14 The 30-second session (MM2).** Design the three session shapes
(30s claim / 5min push / 30min binge) and optimize the first
ruthlessly: cold boot → "while you were away" → ONE Claim-All sweep
(idle + goals + daily), plus a "Manage" super-verb (claim, salvage-all,
auto-equip upgrades in one confirm). Acceptance: time-to-first-dopamine
< 5s from cold boot. `Goals.Claimables` is the seed.

**10.15 Codex / collection (MM3).** Monster kill-tier codex, gear-set
gallery, zone completion ledgers; each tier pays a small permanent
account drip (Tower-buff/boon sibling). The retention layer BETWEEN
power walls — there is never zero visible next-goals. GameCore-first:
derive from lifetime counters where possible (achievements pattern).

**10.16 Live-ops events + season track (MM4).** Time-windowed
`GameConfig` drops (weekend zone boosts, mutated crypt, Tower rush) +
a season track (free track first, premium seam later). The real build
is the SCHEDULING seam: windowed config + one track reducer; event
countdowns ceil (rounding rule). Server-optional now, §9-ready later.

**10.17 Endgame sinks (MM5).** Enhancement scrolls (§6.1: enhanceLevel
gamble, rising risk/cost, seeded rng + persisted cursor) + hero dupe
ascension (star-ups beyond the current XP/scrap). Acceptance:
BalanceSim shows ~6 months of daily play with a meaningful weekly
spend target — mobile idle dies when currencies cap out.

**10.18 Cloud save & identity (MM6).** Platform auth (Play Games /
Game Center + guest), save sync, conflict UX that shows BOTH summaries
("Phone: stage 34, 2h ago / Tablet: stage 31, yesterday") and lets the
player pick. Versioned saves + `Migrate` are the base; de-risks §9.

**10.19 Telemetry & remote config (MM7).** Privacy-light funnel events
(FTUE beat completion, session length, wall positions, gacha
conversion) — every retention decision above is a guess until
measured. Remote `GameConfig` seam so balance patches skip store
review. Prerequisite for any monetization decision.

**10.20 Accessibility & l10n foundation (MM8).** Rarity gains a
shape/border language (colour-only today — a colorblind trap), text
scale setting, reduced-motion mode (hit-stop/shake gates exist),
haptics toggle. STRING-TABLE EXTRACTION EARLY — every hardcoded feed
line written after this ships is future localization debt.

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

**10.9 Audio identity (remaining).** (a) original music beds: PAUSED
BY THE USER 2026-07-14 ("skip music direction for now") — do NOT
re-pitch; (b) AudioDirector waits on (a) (the Ambience crossfade host
+ 10.6f duckExempt are the scaffolds); (c) ambience beds + (d) UI
sound family SHIPPED 2026-07-13 (ledger) — audibility ears pass still
owed by the user (the editor audio wedge looks healed); (e) mixer done
except the music slider, which lands with (a).

**10.12 Performance & mobile-readiness — (a-d) SHIPPED (ledger); (e)
the laptop `-benchmark` run is the final confirmation (user-gated).**
Traps that BITE: vSyncCount>0 makes Unity IGNORE targetFrameRate (keep
vSync off); Shader.Find-only shaders get STRIPPED from builds — custom
shaders MUST live under Assets/Game/Resources/Shaders; shadow gates
read MESH bounds × scale (renderer.bounds is zero pre-first-render);
profile in short bursts (a Step-storm once took down the editor).

**10.3 UI kit lessons (shipped 2026-07-11 — kept because 10.13 rides
them):** force-expand HLG clamps CHILD flex to ≥1 — a fixed-height
cell in a Row needs a non-expanding VStack slot (explicit
flexibleHeight=0 does NOT work); windows/modals scale match-0.5
(match-width starves height at 21:9); `KeepOnCanvas` heals are
DISPLAY-ONLY (never persist a clamp — canvas sizes are transient
mid-resolution-switch; it bit once); UIFont.ttf deliberately
untracked, importer fallbacks machine-local (re-apply on checkout).

**10.5 loot lessons (shipped 2026-07-11):** the SWEEP CONTRACT (bulk
verbs skip stale/guarded entries silently, single verbs throw — state
which in the doc comment); flat bonuses on multiplier-ish stats
explode late-tier (the §6.2 ≤8% gate test is the tuner, not eyes);
additive fields on hand-copied models must thread EVERY copy site or
a reducer silently strips them — grep `new Item` / `new HeroInstance`
/ `new ProgressState` when adding one.

## Shipped ledger (newest first — full receipts in `git log`)

- 2026-07-14 10.13(a-d) touch-first UI reflow: SafeArea plumbing +
  pinch zoom (a) · uGUI thumb-reach NavBar replaced the IMGUI control
  bar, all bar contracts carried (b) · 44pt floors + safe-inset panels
  over full-bleed dims, audit-driven (c) · Settings/TowerView/
  ModifierPanel/MainMenu onto PanelKit + SliderRow primitive — ZERO
  hand-placed windows remain (d). All Play-verified at 2340×1080.
- 2026-07-14 Tower per-floor reward bundles: first clear banks gold +
  a boss-loot bundle in `Tower.RecordClear` (exploit-proof gate; kills
  still pay nothing), anchored to `Tower.StageEquivalent` (floor 30 ≈
  stage 100); milestone floors pay MAJOR bundles; persisted-cursor
  roll; auto-salvage applies. +7 tests (760 green).
- 2026-07-13 10.6 combat juice COMPLETE: hit-stop (income provably
  untaxed — sim accumulator on REAL time × mode speed) · per-element
  ImpactBursts · frost de-whited · element trail ribbons · kill-streak
  beats · SFX duck bus with a duckExempt hook for 10.9 stingers.
- 2026-07-13 10.9(c)+(d): 16 zone ambience beds (AMB bank, crossfade
  host = the AudioDirector scaffold, volume slider) + one UI sound
  family at BOTH button factories (uGUI UiKit.TextButton + the IMGUI
  control-bar helpers), tile ticks, popup pair, claim chime, deny/
  spend, enchant pair.
- 2026-07-12/13 10.10 SDF monster expansion COMPLETE: 8 blob critters
  across the crypt tiers · Slither+Pulse gaits · Ossuary Wyrm crypt
  boss (rider binding, per-def Subdivisions) · perf gate passed ·
  bog_horror/chaos_spawn rebodied. Art lessons in SdfBlobDefs comments;
  faceted Tunic stays the rule — SDF only for genuinely amorphous.
- 2026-07-12 10.8 Endless COMPLETE: `StageFor` generates rows past the
  table, zones cycle, EndlessBest (save v3), "Push beyond…" nav +
  Endless-N label (`MaxSelectableStage` = the ONE selection rule), gems
  every 5th new depth, BalanceSim `endless` mode (caught a long-gold
  overflow → EndlessRateGrowth 1.02 taper), account-chip record line.
- 2026-07-12 crypt own-depth curve (user verdict): floors anchor to
  `Crypt.StageEquivalent` — nothing to sandbag; loot/gold/XP ride the
  depth, key-bounded; HP ×0.6 + atk +2%/floor (walls = wipes, never
  timer slogs) · 2× speed toggle (Crypt+Tower only; timeScale pinned
  off the mode kind every frame) · caster root (CastRootMs 700).
- 2026-07-11 crypt tuning (call #3): HpGrowth 1.045 / DmgGrowth 1.05 +
  CryptRewardGrowth 1.06 (dust/hour rises with depth) · boon base
  20→50 (finite-height economy).
- 2026-07-11 10.5 Loot QoL 2.0 COMPLETE: per-slot loot filter + imprint
  guard · CompareCard compare-anywhere · SalvageMany + Select mode ·
  §6.2 set bonuses (30 gen'd, gate-tuned ≤8%) + tells · loadout
  snapshots. 699 → 738 tests, all Play-verified.
- 2026-07-11 10.12(a-d) perf: FrameCap 60/10 · StepCombat scratch
  buffers (calm farming ~31→11 KB/frame) · scenery material cache +
  static combine (~1,514→~486 batches) · shader Prewarm behind the
  main menu · quality tiers in Settings (render scale / shadows /
  post FX → GraphicsQuality.Apply).
- 2026-07-10 10.4 Goals hub COMPLETE (§7.5): Claimables/ClaimAll read
  model + PreviewNext; Goals window on PanelKit; control-bar pip;
  Achievements panel retired. Play-verified claim loop end-to-end.
- 2026-07-10/11 10.3 UI foundation COMPLETE: Theme tokens + PanelKit
  layout-group kit (+Modal); Heroes/Inventory/three modals migrated —
  zero positional literals; HUD anchoring; font audit (single
  UiKit.Font, 24/24 glyphs); verified at 16:9/16:10/21:9.
- 2026-07-10 10.2 FTUE COMPLETE (§7.4): staged-reveal gating, five
  guided intro beats, staged button reveal, reveal toasts, intro
  strip, celebration beats, breadcrumb. Play-verified S0→S12.
- 2026-07-09 10.1 The Great Rebalance COMPLETE: thorns capped mirror,
  gear/level ~50/50, per-tier HP+damage taper, major-boss taper (soft
  wall ~80-90; stage 100 = the ~L100 mythic capstone), BalanceSim
  account stacks + `pace` mode.
- 2026-07-09 10.7 crypt overhaul COMPLETE: room roles/keys, wave
  phases, chests/mimics/reward vault, client tells, mid-run
  persistence+resume+summary, BalanceSim `crypt` chart (10.7a–g).
- 2026-07-07 10.11 hero look & FX COMPLETE: wardrobe browser, hero
  relooks (user-picked), FxKit procedural FX for every projectile.
- 2026-07-07 Projectile release-frame launch · anim feel fixes ·
  balance sim (walls|sweep|farm) + wall findings · project audit.
- 2026-07-06 Crypt meta (keys/runs/chests/boons) · dungeon mode ×5
  passes → packed maze + room aggro.
- 2026-07-05 Terrain slices 1+2 · presentation debt · ranged feel ·
  Tower gems/quest rework/modifier UX.
- 2026-07-04/05 SDF blend-shell (shader → gaits → swamp trio) · party
  feel batch: formation, kite/peel, leader UI.
- 2026-07-03/04 Gacha MVP + Ice Mage banner · monster anim · QoL batch.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
