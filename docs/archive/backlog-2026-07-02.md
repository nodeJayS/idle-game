# Backlog — open work

Current open/near-term items. Live system status is in [`../CLAUDE.md`](../CLAUDE.md);
the durable design + full roadmap in [`game-design.md`](game-design.md). GameCore-first
for anything touching rules (build + `dotnet test gamecore/GameCore.Tests`, then wire Unity).

> The previous backlog (a 2026-06-20 handover) is fully shipped — party cap 3, derived
> DPS/Eff-Life stats + hover compare, auto-salvage selector, in-place boss challenge,
> reward-density pass, the skills MVP, and the Thief hero all landed. History is in git.
>
> The 2026-07-01 **UX + rebalance batch is fully shipped** (7 slices, `f15571d`..`3dea1db`):
> rarity refactor (Normal/Rare/Unique/Legendary/**Mythic**=red, reseeds at New Game),
> boss rebalance (Boss/MajorBoss mults 2.5→2.0 — sim-tuned against the real save; 2.5
> WIPED a frontier 3-hero party on major bosses), mass salvage (`Inventory.SalvageAllUpTo`
> + bag button), auto-salvage up to Unique, top-left gold/scrap/gems wallet, party-bar
> polish (red HP / deep-blue mana / shadowed text / hero level) + grouped control bar,
> and the floor/ceil rounding sweep. Details in git history.

## ⭐ NEXT SESSION — MS2-style hero pipeline
The full handover plan is **[`ms2-hero-pipeline-plan.md`](ms2-hero-pipeline-plan.md)** —
read it first; it's self-contained (what's extracted where, working tools, measured
proportions, slices A–E, and the gotchas). Short version: rebuild the warrior with real
MapleStory-2 proportions (Slice A, geometry only), then a 16–18-bone skinned skeleton +
clips authored from measured MS2 motion (B–D), then scale the roster (E). Reference
assets live OUTSIDE the repo at `C:\Games\MapleStory2\Extracted\` (`ms2_knight.blend`
is the look target). Heroes only — monsters stay rigid/faceted.

## Next focus — content & polish
- **More content:** additional heroes/classes & kits, enemy variety, more normal + rare
  modifiers, deeper/more stages.
- **Balance & tuning:** the geometric curves are tuned by feel; heroes can out-level a
  stage. Wants the deferred **console balance-sim** to chart power vs difficulty.
- **Combat juice:** boss-kill flash, hitstop on big crits, kill-streaks, and SOUND (no
  audio assets in the repo yet).

## Systems / GameCore
- **Progression hooks (Lever 4)** — *slices 1–2 shipped:*
  - **Achievement ladder** (lifetime `AchievementState` under `ProgressState`; `Achievements.Record`
    fed the same events as quests; 8 tiered achievements auto-paying gold+scrap+XP; read-only panel).
  - **Daily login + premium currency** — `gems` (`Currencies["gems"]`), a third currency earnable
    ONLY via the daily login streak (`DailyLogin.Claim`, UTC-day gated, streak + 7-day milestone bonus);
    a launch `DailyLoginModal` and a gems HUD readout. The seed of the gacha/live-service economy.
  - Next slices: a **gem SINK** (gacha pull / premium shop — the microtransaction target), real-money
    purchase hook, a manual "collect" claim UX for achievements, and eventually prestige/rebirth.
- **Tower of Ascension — slice 3:** per-floor reward bundles (scrap/gold/gear? — TBD) + juice.

## Deferred (intentionally, until the depth above lands)
- **UI/UX polish pass** — a uGUI layout-group refactor + glyph/font audit + theming + real
  item/hero art hooks. Today's screens are functional hand-placed placeholders.
- **Real Blender hero models** — replace the code-built chibi placeholders (the `CombatView`
  spawn/animator seam is ready).
- **Gacha + live-service** — server-authoritative core, accounts/auth, global chat, gacha,
  store/LiveOps. Additive, not a rewrite (game-design.md §9).
