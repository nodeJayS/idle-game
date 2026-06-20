# Backlog — requested changes (handover)

Captured 2026-06-20. Categorized, anchored to code, with rough scope and dependencies.
Renderer/UI-only unless noted. GameCore-first for anything touching rules (build +
`dotnet test gamecore/GameCore.Tests`, then wire Unity).

## A. Systems / GameCore (rules + data model)

### A1. Party cap 4 → 3  ·  GameCore + UI  ·  ✅ done
- New `Save.PartySize` constant (= 3) is the single source; `Models.SaveState.Party` and
  `Save.NewGame` build to it. The rest of GameCore already reads `save.Party.Length`
  (`Party.cs`, `Combat.ReconcileParty`), so it adapted automatically.
- `Save.Migrate` now resizes the party to `PartySize`, **preserving the first slots** and
  benching any overflow hero (a legacy length-4 save keeps its first 3 fielded heroes;
  the 4th stays owned in `Heroes`). Previously it blanked a mismatched-length party.
- UI: party HUD (`CombatView.DrawPartyHud`) + `EquipmentView` field slots already iterate
  `save.Party.Length`, so they show 3 with no change.
- Tests: `MigrateShrinksLongPartyBenchingOverflow` added; length asserts use `Save.PartySize`. 217 pass.

### A2. Derived combat stats — DPS + Effective Life  ·  ✅ done (GameCore + UI)  ·  prereq for B2
- **GameCore:** `DerivedStats` static class — `Dps(stats)` = max(1,Atk)×AtkSpd×critMult
  (sheet DPS vs unarmored, mirrors `ApplyHit`); `EffectiveHp(stats, R)` = Hp×R/max(1, R−Def)
  (sim-exact flat-mitigation survivability); `StageReferenceHit(cfg, stage)` = the stage boss's
  scaled Atk, plus an `EffectiveHp(stats, cfg, stage)` convenience. 10 tests (227 total pass).
  - EHP model decision: **vs a reference hit** (the stage boss's scaled Atk) — the survival gate.
    Flat Def has no reference-free EHP, so the number reads "effective life vs a stage-N boss hit".
- **UI:** Stats tab + Equipment detail pane (`EquipmentView.RenderStatSheet`) lead with gold DPS
  + Effective Life rows (EHP vs current stage's boss hit). Next: B2 diffs these current vs
  item-swapped `StatBlock`.

## B. UI / UX

### B1. Auto-salvage rarity selector (bullet list)  ·  ✅ done (UI only)
- `InventoryView.BuildAutoSalvage`: header button expands an explicit dropdown list —
  Off / Normal / Magic & below / Rare & below — current marked with ●, tinted by rarity.
  Replaces the old cycling button. Built last in `Open()` so the popup renders on top.
- Unique/Legendary intentionally excluded (boss-only chase items; trash caps at Rare).
  `& below` matches `Inventory.AddLoot`'s `Rarity <= max`.

### B2. Equipment hover → stat-delta preview  ·  ✅ done (GameCore + UI)  ·  depended on A2
- `Inventory.ComparePairForHero` (new) returns the hero's (before, after) `StatBlock` for an
  item swap — the shared basis behind the raw compare + the derived preview (derived stats are
  non-linear, so the diff needs both full blocks). `CompareForHero` now wraps it.
- `EquipmentView` compare pane leads with bright ▲/▼ **ΔDPS** and **ΔEff. Life** rows
  (`DerivedDeltaRow`) before the raw stat deltas; EHP delta is vs the current stage's boss hit.
  Tests: `ComparePairBackingMatchesDeltaAndDrivesDerivedStats` (228 pass).

### B3. Inventory panel off-center  ·  ✅ done (UI only)
- Root cause was `UiKit.ScrollGrid`: the content drifted sideways under the scroll mask, clipping
  the first column (both the Inventory grid and the Heroes "Bag" grid). Fixed in one place — pin
  content to fill viewport width (`offsetMin/Max.x = 0`) + center the cell block
  (`childAlignment = UpperCenter`). Both bag grids now sit cleanly inset, no clipping.

## C. Game feel / juice

### C1. Challenge-Miniboss travel indicator  ·  renderer + flow  ·  medium
- Entering the boss challenge transitions instantly today. Want the party to visibly
  **move to the boss** (walk-in / camera lead-in) instead of teleporting. Touches the
  challenge transition in `CombatView`/`Bootstrap` flow + `CameraRig`.

## D. Design investigation (discuss together)

### D1. "The gameplay loop isn't satisfying"  ·  open-ended
Worth a dedicated session — watch a play session and isolate which hypothesis bites hardest:
- **Weak feedback cadence** — kills/loot don't punch (impact, sound, loot beams, crunch).
- **Illegible progression** — no clear "I just got stronger" moment / visible next goal (B2 helps).
- **Shallow choice** — skills are read-only, gear upgrades unclear; nothing to optimize toward.
- **Samey pacing** — uniform trash (pack variety is on the roadmap), few spikes.
- **No short-term goals** — nothing pulling you forward minute-to-minute (quests/milestones).

---

**Dependencies:** B2 needs A2. Everything except A2/B2 is renderer/UI-only.
