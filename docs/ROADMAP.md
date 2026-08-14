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
pipeline; monsters faceted or SDF blend-shell. **848 GameCore tests
green.** The 100-stage ladder is FIXED (10.1). **Phase M mobile arc:
MM1-MM5 + MM8 shipped; the UI polish arc (10.23) is ACTIVE.** Gaps:
music (10.9a, PAUSED), laptop perf (10.12e), the user's ears pass.

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

**10.23 UI polish arc — ACTIVE (user: "best UX and impressiveness").**
P1 warm-Tunic kit reskin + P2 layering/scroll fixes SHIPPED (ledger).
REMAINING in order: **P3 motion** (120-150ms window open/close, press
squash, reward count-ups, feed slide-in — every path gates on
`Settings.ReducedMotion`) · **icons** (item tiles are still text
abbreviations "Wpn/Glov/Boot", the most placeholder thing left; plus
currency + nav — UI icons are NOT under the MS2 heroes-only rule) ·
**moment screens** (arrival card, gacha reveal, outcome) · HUD cards.

**10.13 Touch-first UI reflow (MM1) — COMPLETE 2026-07-15 (ledger).**
Non-interactive IMGUI deliberately remains (party chips, currency HUD,
world health bars — migrate when touched). KIT FOOT-GUNS that BITE: a
PanelKit.Window's `body` is a bare Flex — `PanelKit.Stack(body)` BEFORE
adding rows or every child collapses to a centered zero-size blob
(Play-caught); Modal's body ALREADY has the layout group — never Stack.

**10.14 The 30-second session (MM2) — COMPLETE 2026-07-15 (ledger).**
Durable lesson: `Inventory.SalvageAll` is NUCLEAR (every loose unlocked
non-guarded item, any rarity — NOT the loot filter), so any aggregate
touching it must equip-sweep FIRST (worn gear is salvage-immune) or it
destroys upgrades — `Session.Apply` locks that ordering with a test.

**10.15 Codex / collection (MM3) — COMPLETE 2026-07-15 (ledger).**
Durable: cfg-aware retro-grants (SyncFromInventory) live beside
SyncHeroUnlocks at LOAD, never in the cfg-less Save.Migrate; loot-path
reducers RE-WRAP nested Progress state, so threading sweeps assert
VALUE survival, not ref identity.

**10.16 Live-ops events + season track (MM4) — COMPLETE 2026-07-15
(ledger).** Durable: schedule rules are PURE functions of nowMs (the
§9 remote-config stand-in); event effects snapshot AT FIGHT INIT via
optional `nowMs = 0` params (0 = legacy all-off, no call-site churn);
the crypt mutation keys off StageEquivalent + the window's EndMs.

**10.17 Endgame sinks (MM5) — COMPLETE 2026-07-15 (ledger).** Durable:
+15 enhance had ALREADY shipped (`48a9f52`); endgame gold/scrap FLOOD
material sinks (enhance saturates <1wk) — the binding 6-month sink is
gem-gated ascension via GemFractionToAscension=0.33 (retune consciously).

**10.18 Cloud save & identity (MM6).** Platform auth (Play Games /
Game Center + guest), save sync, conflict UX that shows BOTH summaries
("Phone: stage 34, 2h ago / Tablet: stage 31, yesterday") and lets the
player pick. Versioned saves + `Migrate` are the base; de-risks §9.

**10.19 Telemetry & remote config (MM7).** Privacy-light funnel events
(FTUE beat completion, session length, wall positions, gacha
conversion) — every retention decision above is a guess until
measured. Remote `GameConfig` seam so balance patches skip store
review. Prerequisite for any monetization decision.

**10.20 Accessibility & l10n (MM8) — a11y + the string-table
foundation SHIPPED 2026-07-16 (ledger); the PHASE-2 sweep REMAINS**
(Inventory/Equipment/Gacha/Goals-body/Modifier/Tower/Compare, plus
GameCore content names via `Loc.Content`). Durable: uGUI Text CLIPS to
its rect, so every text-bearing rect AND chrome metric must ride the
text scale or labels behead (Play-caught at 130%); rarity marks avoid
▲/▼/✦ — the shipped upgrade/imprint vocabulary.

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
Traps that BITE: vSyncCount>0 makes Unity IGNORE targetFrameRate;
Shader.Find-only shaders get STRIPPED from builds — customs MUST live
under Assets/Game/Resources/Shaders; shadow gates read MESH bounds ×
scale (renderer.bounds is zero pre-first-render); profile in bursts.

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

- 2026-08-01..06 UI polish P1+P2 (10.23): the warm "Tunic" reskin at KIT
  level — Theme palette rotated warm (RED highest channel, BLUE lowest,
  data-carrying hues frozen), procedural rounded 9-slice + shadow
  sprites, ColorTint press states — so one slice reskinned every screen ·
  P2 layering pass: Summon backdrop, Heroes columns scroll, Settings/
  IMGUI z-order, scroll bottom-fade affordance, HUD panels reserving the
  NavBar band, Tower floor packs ringing the party. 848 tests.
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
  via PendingKills + BankKills tier crossings; set-slot discovery
  stamped at AddLoot BEFORE the salvage decision — seen is seen). Every
  completed tier pays +0.1% Hp/Atk/Def (~+16% full, ≤25% ceiling-tested);
  Goals hub Codex tab; retro-stamp at load. 794 tests.
- 2026-07-15 10.14 the 30-second session COMPLETE: `Session.Arrive`
  (idle + daily as ONE atomic boot payoff — two boot modals became one
  arrival card) + `Session.Preview/Apply` (the Manage super-verb: claim
  → equip sweep → nuclear salvage, one confirm card + nav pip).
  Taps-to-payoff at boot: 2. 770 tests; Play-verified to the gold.
- 2026-07-14/15 10.13 touch-first UI reflow COMPLETE: SafeArea + pinch
  zoom (a) · uGUI thumb-reach NavBar replaced the IMGUI control bar (b) ·
  44pt floors + safe-inset panels over full-bleed dims (c) · Settings/
  TowerView/ModifierPanel/MainMenu onto PanelKit — ZERO hand-placed
  windows (d) · TopControls + ModesWindow retire the last interactive
  IMGUI (e). Play-verified at 2340×1080 through real button clicks.
- 2026-07-14 Tower per-floor reward bundles: first clear banks gold + a
  boss-loot bundle in `Tower.RecordClear` (exploit-proof gate; kills
  still pay nothing), anchored to `Tower.StageEquivalent` (floor 30 ≈
  stage 100); milestone floors pay MAJOR bundles. 760 tests.
- 2026-07-13 10.6 combat juice COMPLETE: hit-stop (income provably
  untaxed — sim accumulator on REAL time × mode speed) · per-element
  ImpactBursts · frost de-whited · element trail ribbons · kill-streak
  beats · SFX duck bus with a duckExempt hook for 10.9 stingers.
- 2026-07-13 10.9(c)+(d): 16 zone ambience beds (AMB bank, crossfade
  host = the AudioDirector scaffold, volume slider) + one UI sound
  family at BOTH button factories (uGUI UiKit.TextButton + the IMGUI
  control-bar helpers), tile ticks, popups, claim, deny/spend, enchant.
- 2026-07-12/13 10.10 SDF monster expansion COMPLETE: 8 blob critters ·
  Slither+Pulse gaits · Ossuary Wyrm crypt boss · perf gate passed ·
  bog_horror/chaos_spawn rebodied. Art lessons in SdfBlobDefs comments;
  faceted Tunic stays the rule — SDF only for genuinely amorphous.
- 2026-07-12 10.8 Endless COMPLETE: `StageFor` generates rows past the
  table, zones cycle, EndlessBest (save v3), "Push beyond…" nav +
  Endless-N label (`MaxSelectableStage` = the ONE selection rule), gems
  every 5th new depth, BalanceSim `endless` (caught a long-gold overflow
  → EndlessRateGrowth 1.02 taper), account-chip record line.
- 2026-07-11/12 crypt own-depth curve + tuning (user verdicts): floors
  anchor to `Crypt.StageEquivalent` — nothing to sandbag; loot/gold/XP
  ride the depth, key-bounded; HP ×0.6 + atk +2%/floor (walls = wipes,
  never timer slogs); HpGrowth 1.045 / DmgGrowth 1.05 / RewardGrowth
  1.06, boon base 20→50 · 2× speed toggle (Crypt+Tower only; timeScale
  pinned off the mode kind every frame) · caster root (CastRootMs 700).
- 2026-07-11 10.5 Loot QoL 2.0 COMPLETE: per-slot loot filter + imprint
  guard · CompareCard compare-anywhere · SalvageMany + Select mode ·
  §6.2 set bonuses (30 gen'd, gate-tuned ≤8%) + tells · loadout
  snapshots. 699 → 738 tests, all Play-verified.
- 2026-07-11 10.12(a-d) perf: FrameCap 60/10 · StepCombat scratch buffers
  (calm farming ~31→11 KB/frame) · scenery material cache + static
  combine (~1,514→~486 batches) · shader Prewarm behind the main menu ·
  quality tiers in Settings → GraphicsQuality.Apply.
- 2026-07-10 10.4 Goals hub COMPLETE (§7.5): Claimables/ClaimAll read
  model + PreviewNext; Goals window on PanelKit; control-bar pip;
  Achievements panel retired. Play-verified claim loop end-to-end.
- 2026-07-10/11 10.3 UI foundation COMPLETE: Theme tokens + PanelKit
  layout-group kit (+Modal); Heroes/Inventory/three modals migrated —
  zero positional literals; HUD anchoring; font audit (single
  UiKit.Font, 24/24 glyphs); verified at 16:9/16:10/21:9.
- 2026-07-10 10.2 FTUE COMPLETE (§7.4): staged-reveal gating, five guided
  intro beats, staged button reveal, reveal toasts, intro strip,
  celebration beats, breadcrumb. Play-verified S0→S12.
- 2026-07-09 10.1 The Great Rebalance COMPLETE: thorns capped mirror,
  gear/level ~50/50, per-tier HP+damage taper, major-boss taper (soft
  wall ~80-90; stage 100 = the ~L100 mythic capstone), BalanceSim
  account stacks + `pace` mode.
- 2026-07-09 10.7 crypt overhaul COMPLETE: room roles/keys, wave phases,
  chests/mimics/reward vault, client tells, mid-run persistence+resume+
  summary, BalanceSim `crypt` chart (10.7a–g).
- 2026-07-07 10.11 hero look & FX COMPLETE: wardrobe browser, hero
  relooks (user-picked), FxKit procedural FX for every projectile.
- 2026-07-03..07 foundation week (receipts in git): gacha MVP + Ice
  Mage banner · SDF blend-shell + party feel · terrain slices + Tower
  gems/quest rework · crypt meta + packed maze · balance sim + audit ·
  projectile release-frame launch · anim feel fixes.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
