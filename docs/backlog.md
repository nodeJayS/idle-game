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

### C1. Challenge boss in place (no arena swap)  ·  ✅ done (GameCore + UI)
- Final design (walk-in arena was rejected): the boss challenge happens **on the same farm
  map**. Pressing Challenge despawns trash and the boss appears `BossSpawnDistance` (8) ahead
  of the party — no scene reset, camera stays put. Win → advance + next-stage trash returns at
  normal cadence; flee/fail/wipe → back to farming after a `BossFleeCooldownMs` (4s) lull so
  spamming challenge→flee can't refresh packs.
- Pure GameCore mutators `Combat.EnterBossChallenge` / `Combat.ResumeFarm` (+ `RestoreParty`);
  `CombatView` calls them in place, `ReconcileViews` syncs views. `InitBossChallenge` kept for
  tests. 3 tests added (231 pass).

## D. Design investigation (discuss together)

### D1. "The gameplay loop isn't satisfying"  ·  open-ended (in progress)
Target mash-up: MapleStory-idle loop + PoE2 ARPG depth + MapleStory2 feedback + Tunic look →
addictive incremental, gacha later. Diagnosis: the game was tuned **sparse/slow** while every
target genre is **dense/fast** — reward cadence too low to be addictive. Three levers, in order:

1. **Reward cadence + density** ✅ (lever 1) — `DropChance` 0.003→0.12 (~40× loot rain → scrap
   fountain + occasional keepers), `MobCap` 20→60, `SpawnBatchSize` 6→10, `SpawnIntervalMs`
   1500→900, spawn ring 16–36→10–26 (packs hug the party = constant combat, no dead walking).
   Confirmed "better" in playtest. All tunable in `BalanceConstants`.
2. **Juice the now-frequent moments** — in progress. Done: crit "!" removed (reads via color
   + size), world loot-pop for Rare+ keepers at the drop site (`CombatJuice.LootPop`),
   `LEVEL UP!` burst over heroes (`CombatJuice.LevelUpBurst`). Remaining: boss-kill flash,
   hitstop on big crits, kill-streak, and SOUND (no audio assets in repo yet — deferred).
3. **Decisions + acceleration** — make skills choosable (the PoE "build" pillar; currently
   read-only — likely where lasting addictiveness lives), short-term goals/milestones, and
   visibly accelerating progression (auto-advance out-geared stages).
   - **Skills milestone (= the planned active/passive/tree milestone), MVP scope = active loadout choice:**
     - Slice A ✅ GameCore loadout system: `Skills` reducers (`ToggleSkill`/`SetLoadout`/`Known`/
       `DefaultLoadout`) + `Balance.MaxActiveSkills` (4) cap; `HeroDef.Skills` = known pool,
       `HeroInstance.SkillLoadout` = chosen subset. No behavior change yet (pools ≤ cap). 8 tests.
     - Slice B ✅ — **skill content**: each hero now knows 6 (pool > 4 = real choice). Warrior:
       cleave/bash/warcry/whirlwind/bulwark(+Def)/frenzy(+AtkSpd). Fire Wizard: firebolt/fireball/
       mend/scorch/inferno/haste(+AtkSpd). Fully unique kits per hero (class = tag only; ice wizard
       will be a separate hero). New skills reuse existing FX for now. Also fixed: basic attacks now
       read *effective* AtkSpd so AtkSpd buffs/gear speed them live. Default loadout = first 4. Tests.
     - Slice C ✅ — interactive Skills tab: lists the 6 known skills with an Active N/4 counter,
       slot/unslot via `Skills.ToggleSkill`; `RefreshPartyStats` re-syncs combat skills so edits
       apply live. **Skills MVP (active loadout choice) complete.**
     - Later: passives, skill ranks/leveling, tree.
   - Boss HP halved: `BossHpMult` 10 → 5 (better pacing with the denser loop).

Still-open hypotheses to revisit after juice: samey pacing (pack/elite variety), short-term goals.

## E. Heroes / content

### E1. Third hero: Thief (assassin)  ·  GameCore + renderer  ·  NEXT (user-requested)
A fast, fragile, crit-focused melee assassin — high single-target burst. Fully unique kit (the
agreed model: class = tag, each hero its own skills; see [[idle-game-vision-and-loop]]).
- **GameCore (no new combat code if kit uses existing effect kinds):**
  - `GameConfig.Default()` — add `cfg.Heroes["thief_basic"]` (`Class="Thief"`, `Role="melee"`):
    low Hp/Def, high `AtkSpd`, high `CritChance`/`CritDmg`, decent `MoveSpd`, short melee range.
  - 6-skill kit (pick 4) from existing kinds: e.g. **Backstab**/**Eviscerate** (single-target nukes,
    high mult), **Fan of Knives** (AoE), **Ambush** (+CritChance buff), **Adrenaline** (+AtkSpd buff),
    plus one more. Add `SkillDef`s next to the others (~line 398+ in GameConfig). Reuse FX sprites.
  - `cfg.HeroUnlocks[N] = "thief_basic"` — pick an unlock stage (Magician is 3; Thief maybe 5).
  - `DefaultLoadout` caps the starting bar to 4 automatically.
- **Renderer:** add a Thief chibi variant in `ChibiHero.Build` (warrior/fire-mage exist today);
  capsule fallback works until then. Pick an `AttackFx`.
- **Wanted-but-needs-new-sim (optional, flag):** poison/DoT and a dash/blink — neither exists in
  the sim yet (only instant Damage/Heal/Buff). Ship the assassin on existing kinds first; add DoT/
  mobility as their own mechanic later if desired.
- New content shows at New Game; party cap is 3 so the Thief competes for a slot.

---

**Dependencies:** B2 needs A2. Everything except A2/B2 is renderer/UI-only.
