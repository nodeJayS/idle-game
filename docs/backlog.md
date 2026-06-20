# Backlog — requested changes (handover)

Captured 2026-06-20. Categorized, anchored to code, with rough scope and dependencies.
Renderer/UI-only unless noted. GameCore-first for anything touching rules (build +
`dotnet test gamecore/GameCore.Tests`, then wire Unity).

## A. Systems / GameCore (rules + data model)

### A1. Party cap 4 → 3  ·  GameCore + UI  ·  medium
- Core is two array sizes: `Models.cs:79` and `Save.cs:96` (`new string?[4]` → `[3]`).
  The rest of GameCore reads `save.Party.Length` (`Party.cs`, `Combat.ReconcileParty`),
  so it adapts automatically.
- Careful part: a **save migration** in `Save.Migrate` — existing saves have a length-4
  array (maybe 4 fielded heroes); resize to 3 and bench the overflow hero.
- UI: party HUD + `EquipmentView` field slots show 4 (the two "— empty —" rows) → 3.

### A2. Derived combat stats — DPS + Effective Life  ·  GameCore  ·  medium  ·  prereq for B2
- `StatBlock` holds raw stats; `Combat.cs` has `AttackSpeedOf`/`AttackInterval`, but no
  exposed **DPS** or **EHP/Life** number. Add pure GameCore helpers (DPS = atk × atkspd ×
  crit factor…; EHP from hp + mitigation) for the stat panel and the hover preview (B2).

## B. UI / UX

### B1. Auto-salvage rarity selector (bullet list)  ·  UI only  ·  small–medium
- Mechanic exists: `Settings.AutoSalvageMax` (`Rarity?`); `Inventory.AddLoot` salvages
  everything `Rarity <= max` (the "and below" semantics already match). Today it's a
  cycling button (`InventoryView.cs:73-74`, `AutoSalvageLabel`). Replace with an explicit
  list: Off / Normal / Magic & below / Rare & below / Unique & below / …

### B2. Equipment hover → stat-delta preview  ·  GameCore + UI  ·  medium  ·  depends on A2
- Hovering an item for the selected hero shows the **change** (e.g. "+DPS, +Life").
  Recompute the hero's `StatBlock` with the item swapped in, diff vs current, render the
  deltas via the A2 derived stats. Builds on the existing item-compare UI.

### B3. Inventory panel off-center  ·  UI only  ·  small
- Layout fix in `InventoryView` / its `UiKit` placement. (Screenshot pending for the exact offset.)

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
