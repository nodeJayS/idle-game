# Backlog — open work

Current open/near-term items. Live system status is in [`../CLAUDE.md`](../CLAUDE.md);
the durable design + full roadmap in [`game-design.md`](game-design.md). GameCore-first
for anything touching rules (build + `dotnet test gamecore/GameCore.Tests`, then wire Unity).

> The previous backlog (a 2026-06-20 handover) is fully shipped — party cap 3, derived
> DPS/Eff-Life stats + hover compare, auto-salvage selector, in-place boss challenge,
> reward-density pass, the skills MVP, and the Thief hero all landed. History is in git.

## ⭐ NEXT SESSION — UX + rebalance batch (needs Unity reconnected)

A batch the user requested 2026-07-01. Unity's MCP bridge was down that session, so only the
docs + `Num` floor/ceil helpers landed (commit `8ecc74e`). The rest below needs Unity to
**compile the client** (`Assets/Game` is only built by the Editor, not `dotnet`) and to
screenshot-verify. GameCore-first + `dotnet test` for rules; then wire Unity and verify by
screenshot per the usual loop. **Do item 1 first** — everything else assumes the final rarity set.

**Cross-cutting — apply the rounding convention (now that `Num.CompactFloor`/`CompactCeil` exist,
game-design.md §7):** sweep every `Num.Compact(` display and fix by direction — owned balances
(gold/scrap/gems HUD, InventoryView scrap) → `CompactFloor`; costs (modifier `UpgradeCost`, reforge
cost, any future gem price) → `CompactCeil`; the boss countdown (`CombatView.DrawHud`, `{remain:0.0}s`)
→ ceil; playtime/elapsed → floor.

1. **Rarity refactor — DO FIRST.** `enum Rarity` (`Models.cs:21`) `Normal, Magic, Rare, Unique,
   Legendary` → **`Normal, Rare, Unique, Legendary, Mythic`** (drop Magic, add Mythic on top).
   Reorder changes persisted `(int)Rarity`, so **reseed at New Game — no migration.** Touch points:
   - Every `Rarity.Magic` ref (grep hits: `GameConfig.cs`, `InventoryView.cs`, `Palette.cs`, and tests
     `InventoryTests`/`UpgradeTests`/`EquipmentTests`/`LootTests`/`StatsTests`). Affix
     `RarityFloor = Rarity.Magic` (GameConfig `AffixPool` ~573–579) → retarget (likely `Rarity.Rare`).
   - Rarity-indexed arrays in `BalanceConstants` (stay length 5, RE-TUNE for the new set):
     `RarityBaseWeights` (~335, Mythic extremely rare), `AffixCountByRarity` (~340, Mythic the most),
     `ScrapValueByRarity` (~370, Mythic highest).
   - `TrashRarityCap` (~350) + boss-bundle knobs (`BossLegendaryChance`, add a Mythic chance,
     `MajorBossUniques`/`MiniBossUniques` ~355–359). `Loot.RollRarity` uses `(int)Rarity` + the cap —
     re-verify clamping with the new top tier.
   - Client: `Palette.Rarity` (`Palette.cs:12`) — **Mythic = RED** (e.g. `new Color(0.9f,0.15f,0.15f)`);
     `Borderless` (:23) unaffected. Grep any rarity→name string map and swap Magic→Mythic.
   - Update Magic-referencing tests; add a Mythic test (weight/scrap/affix-count).

2. **Boss rebalance — 30s cap + scale for the 3-hero cap.** `BossChallengeSeconds` (~209) is already
   30 (confirm mini AND major use ≤30s). Scale bosses DOWN so 3 fielded heroes clear within 30s: lower
   `BossHpMult` (~264, now 2.5) and `MajorBossMult` (~186, now 2.5); reconsider `MonsterHpGrowth` (~262).
   Party cap is 3 (`Save.PartySize`). By-feel — enter a boss in Play and watch the timer. (A console
   balance-sim, still deferred, would make this data-driven — the user flags balancing as the bottleneck.)

3. **Mass salvage (reducer + button).** GameCore: add `Inventory.SalvageAllUpTo(SaveState, Rarity cap,
   GameConfig)` — salvage every LOOSE (unequipped) item with `Rarity ≤ cap` to scrap; never touch
   equipped gear (mirror `SalvageItem`'s guard); return (save, count, scrap); pure + tests. Client:
   a "Salvage all ≤ [filter]" button in `InventoryView` using the panel's selected rarity FILTER as the
   cap, announced in the feed.

4. **Auto-dismantle Unique.** Raise the `Settings.AutoSalvageMax` selector (`Settings.cs` +
   InventoryView's auto-salvage picker) to allow up to **Unique**. `Inventory.AddLoot` already honors
   any threshold — this is UI-only.

5. **Currency HUD → top-left under the account chip.** Today gold+gems render in the top-CENTRE line
   (`CombatView.DrawHud` ~1268–1283); scrap only shows in InventoryView. Move a **gold / scrap / gems**
   wallet readout to the TOP-LEFT, stacked under the account chip + Settings button (see the :1276
   comment / `DrawTopControls`). Use `CompactFloor` for all three.

6. **Control-bar / panel order.** `DrawControlBar` (~1446): today Inventory · Heroes · Modifiers ·
   Tower · Achievements (Settings sits top-left). User wants Inventory/Heroes/Settings arranged
   sensibly — pick a clean grouping (e.g. Inventory, Heroes first; systems after; Settings least-used).

7. **Party bar polish.** `DrawPartyHud` (`CombatView.cs:1382`+): HP bar fill → **RED**, mana bar fill →
   **deeper BLUE**; fix the hard-to-read HP/mana text (`PartyBarTextStyle` :1481 — outline/shadow or
   brighter on a darker bar); **add each hero's LEVEL** to the bar label (`hero.Level`). HP value text
   is at :1432.

(The **gem SINK** — gacha pull / premium shop — stays scoped-for-later under Lever 4 below.)

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
