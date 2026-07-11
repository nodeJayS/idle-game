# Idle ARPG — Game Design

> A Diablo 3 / Path of Exile–style **idle ARPG**: a 3-hero party auto-clears
> dungeons, monsters drop loot, you build out gear/skills and push higher difficulty.
> Scope: personal/for-fun game first, **architected to scale to a global live-service
> mobile release later** without a rewrite.
>
> Stack: **Unity (C#, URP)** client + a pure-C# **`GameCore`** simulation library.
>
> **Doc routing:** this doc is the durable "what & why" — it changes only when a
> DESIGN DECISION changes. Execution lives in [`ROADMAP.md`](ROADMAP.md) — THE
> priority list with pre-sliced milestones (§10 there): **start work from ROADMAP,
> not from here.** When something ships, ROADMAP gets the receipt in the same
> commit; this doc only changes if the shipped thing settled a design question
> (then update the relevant section/table here too). Session orientation:
> [`../CLAUDE.md`](../CLAUDE.md).

---

## 0. Project decisions (locked)

| Decision | Choice |
|----------|--------|
| Genre / feel | **Idle ARPG** — Diablo 3 / PoE style, loot-and-build driven |
| Initial scope | Personal / for-fun game, built to scale to live-service |
| Engine | **Unity (C#)**, 3D URP |
| Art direction | **2.5D isometric, low-poly 3D** — fixed iso camera over low-poly models; readable, cheap to produce, light on mobile |
| Sim | **`GameCore`** — pure C# library, no `UnityEngine` refs; Unity is a read-only client |
| Party | **3 hero slots** from a multi-class roster; **start solo (Knight)**, unlocks at stages 3/5/10 (Fire Mage / Priest / Assassin), granted retroactively on load. Leveling is **per-hero** (see §4) |
| Classes | **Archetype → class taxonomy (2026-07-02):** three families — **Warrior** (Knight; later Swordsman/Brawler), **Rogue** (Assassin; later Ninja/Archer), **Magician** (Fire Mage, Priest; later Ice Mage/Summoner). An `ArchetypeDef` = stat template + shared passive pool; a class = per-key overrides (`GameConfig.FromArchetype`). `Role` (melee/ranged/support) stays the sim's separate mechanical axis — an Archer is a Rogue with Role=ranged. Each class has its **own skill set**; skills fire automatically in combat |
| Map / movement | Party **auto-navigates** stages, auto-fights, auto-loots |
| Stages | **1–100 main ladder**; clear a **miniboss** to advance, **major boss every 10**. Tower / endless / party modes later |
| Hero death | Heroes are **downed** with a **per-hero respawn timer** (scales up as they get stronger); a full wipe fails the run. No permanent loss. Frequent wipes = the stage is too strong |
| Items | Drop with random affixes; **upgradeable later via enhancement scrolls** (risk/reward, MapleStory-style) |
| **Gacha** | **ON HOLD** — heroes *and* weapons later. Architecture must scale to it (see §9) |
| Accounts | **One account per game** (live-service). Menu: Continue / New Game |
| Long-term goal | Scale to a **global, server-authoritative live-service** release without a rewrite |

---

## 1. The core loop (loot-driven)

The reward loop **is the loot** — no gacha needed to make it satisfying:

**Party auto-clears a dungeon → monsters drop gear → equip/compare upgrades → build gets stronger → push higher monster density & difficulty → better loot drops → repeat.**

Three nested loops:

| Loop | Time scale | What the player does | The hook |
|------|-----------|---------------------|----------|
| **Moment-to-moment** | seconds | Watch the party clear packs, loot drops, numbers pop | kill + loot dopamine |
| **Session** | 2–10 min | Claim idle loot/XP, equip upgrades, respec/level skills, push a higher stage | build gets visibly stronger |
| **Meta / long-term** | days–weeks | Chase Unique/rare affixes, complete builds, climb endless difficulty | loot grind + power fantasy |

If this feels good with placeholder primitives and grey/blue/yellow item rectangles, you've won. Build the loop before any art.

---

## 2. MVP — the vertical slice (build this and nothing more in v1)

1. **Stage zone** — a small 3D zone the party auto-walks through.
2. **Auto-combat** — 3-slot party (start solo Knight; unlocks by stage), auto-target nearest monsters, auto-attack/auto-skill on cooldown. Heroes can be **downed**; a full party wipe fails the run.
3. **Monster packs + a miniboss** — clear the packs, **miniboss gates the next stage**; a **major boss every 10 stages**.
4. **Loot drops** — monsters drop gear with rarity + random affixes; auto-pickup.
5. **Equip & stats** — equip gear on a hero; stats recompute; party gets stronger.
6. **Leveling** — kills grant XP; **fielded heroes level up**, stats scale.
7. **Stage ladder** — clear stages in order; higher stage = tougher monsters + better loot. The progression spine.
8. **Idle accumulation** — offline → loot/gold/XP accrue based on highest cleared stage, capped (12h).
9. **Save/load + main menu** (Continue / New Game).

No gacha, no summoning. Heroes 2–4 unlock through progression for now (gacha slots in later — §9).

### 2.1 Stage structure, bosses & hero death
- **100 main stages.** Each stage is an endless farm zone ending in a **60s timed boss challenge**; beating it marks the stage cleared and unlocks the next. Idle income keys off your **highest cleared** stage, so being stuck never breaks idle income.
- **Major boss every 10 stages** — a difficulty *wall* (and, later, a biome boundary). These force gear/level optimization instead of pure pushing, and are the natural home for future themed zones.
- **Hero death = downed with a respawn timer, not permadeath.** A hero at 0 HP is downed and **respawns after a per-hero timer**; if all four are down the run **fails** (no clear, no loot). The timer **scales up with the hero's strength/level** — stronger heroes take longer to get back, so you can't lean on a single carry forever, and it doubles as a balancing lever. No permanent loss (an idle game shouldn't punish AFK play). **Repeated wipes = the stage is too strong** — go farm, level, and re-gear.
- **Alt modes (later):** an **endless mode** (infinite scaling for a "deepest stage" chase) and a **party / co-op mode** (queue with others). Both reuse the same `GameCore` — they just build different encounters + a different `LootContext`.

---

## 3. Tech architecture

The one rule (also in CLAUDE.md): **all game logic in `GameCore` (pure C#, zero engine
refs).** The client, a future mobile client, and a future authoritative server all
reference the same library. Specifics:

- **Three kinds of state, never mixed:** `SaveState` (persisted), `GameConfig` (static
  content, injected as a param), `CombatState` (transient sim). See CLAUDE.md §principles.
- **Combat is deterministic & decoupled from rendering.** `Combat.StepCombat` is a
  fixed-timestep step; the Unity `CombatView` calls it and interpolates GameObjects
  between steps. Renderer never decides damage or drops.
- **Idle = math, not a running timer.** On load: `elapsed = min(now - lastClaimAt, cap)`
  → rewards. Never simulate hours of real time.
- **Loot/affixes = seeded weighted rolls** via the one `Rng` engine. *Same engine gacha
  will use later* — build once.
- **Content as data:** monsters, affixes, item bases, skills, stages, balance live
  in `GameConfig` (today `GameConfig.Default()`; ScriptableObjects or content tables
  later). Logic reads config → tune without touching logic.

### Structure (C#)
The sim is one assembly (`GameCore.asmdef`, no engine refs) living in
`unity/Assets/GameCore/` — the **single source of truth**. The test project in
`gamecore/` compiles those exact files via a csproj glob (no copy).
```
unity/Assets/GameCore/             THE sim (pure C#)
  Models.cs        heroes, items, stats (StatKey/EquipSlot), save state
  Rng.cs           mulberry32 + WeightedPick  (loot now, gacha later)
  GameConfig.cs    Default() content: heroes/items/affixes/monsters/stages/skills/balance
  Save.cs          NewGame / Migrate (versioned)
  Party.cs         SetPartySlot, AcquireHero (the gacha plug point)
  Stats.cs         ComputeHeroStats / ComputePartyPower
  Inventory.cs     AddLoot (cap + auto-salvage), Equip/Unequip, SalvageItem, CompareForHero
  Loot.cs          rarity/affix rolls, RollDrop, RollBossDrops (guaranteed bundles)
  Progression.cs   GrantPartyXp, GrantGold/Currency, stage advance
  Idle.cs          offline accrual (Preview / Claim)
  Num.cs           compact number formatting (1.2K / 3.4M …)
  Combat.cs        InitCombat/Farm/BossChallenge, StepCombat, skill casting, RunToEnd
  CombatModels.cs  transient CombatState / CombatEntity (mana, buffs, skill cds) / CombatEvent
unity/Assets/Game/                 MonoBehaviours (read-only client)
  Bootstrap.cs     builds the scene in code on Play; menu-driven session
  CombatView.cs    drives + visualizes combat; HUD + Party HUD (IMGUI)
  EquipmentView / InventoryView    per-hero equip doll + shared bag (uGUI)
  UiKit / StatDisplay / Palette    code-built widgets, stat presentation, rarity colors
  ChatPanel / TopBar / IdleClaimModal / Settings / Autosave / SaveStore
gamecore/GameCore.Tests/           xUnit, compiles Assets/GameCore + Persistence
gamecore/Adapters/Persistence.cs   System.Text.Json (the one host-specific adapter)
```

---

## 4. Data model

**Hero (owned instance):** `id, defId, level, xp, equipped{slot→itemId}, skillLoadout[]`
- **Leveling:** **per-hero** — each hero levels from kill XP; `level` + `growthPerLevel` feed
  `ComputeHeroStats`. Cap 100; curve in `Balance`. (Per-slot leveling was considered and dropped.)

**Hero definition (static config):** `defId, name, class, role (Melee/Ranged/Support), baseStats, growthPerLevel, skills[], sprite hint`
- **Class skills (live, M11):** each class's `skills[]` is its kit. A `SkillDef` has cooldown,
  **mana cost**, range, targeting, and an effect — **damage** (single / targeted AoE),
  **heal** (most-hurt ally), or **self stat-buff** (timed). Skills fire automatically when
  off cooldown with mana + a target; a cast **replaces that step's basic attack**. Cooldowns
  scale with `AtkSpd`. Heroes and bosses cast.

**Item (dropped/owned):** `id, baseId, rarity (Normal/Magic/Rare/Unique/Legendary), itemLevel, affixes[{stat, value}], enhanceLevel`
- **Enhancement:** `enhanceLevel` (0→N) adds stats on top of base + affixes; raised via **scrolls** (§6.1) — a risk/reward gold/currency sink. *(Deferred.)*

**Item base (config):** `baseId, slot, baseStats, allowedAffixes`. **9 equip slots:** Weapon,
Offhand, Helm, Chest, Gloves, Boots, Cape, Ring, Amulet — each hero equips independently from
**one shared account bag** (`SaveState.Inventory`; equipping just references an item id, and an
item can't be worn by two heroes).

**Affix (config):** `stat, weight, valuePerItemLevel range, rarityFloor`

**Stats (`StatKey`):** `Hp, Atk, Def, MoveSpd, AtkSpd, CritChance, CritDmg, HpRegen,
AttackRange, SplashRadius, MaxMana, ManaRegen` — `MoveSpd` = movement, `AtkSpd` = attack/cast
cadence, mana fuels skills. Player-facing **order, labels, and formatting** live in the client's
`StatDisplay` (role-grouped). *(Damage may later split phys/magic — additive, no logic change.)*

**Currencies:** a map — `gold` + `scrap` (salvage) today; `gems`/`tickets`/`shards`/`scrolls` additive later.

**Progression:** `highestStage, currentStage, accountLevel, lastClaimAt`.

---

## 5. The two systems to get mathematically right

### 5.1 Idle reward formula
Tie income to highest cleared stage so progression *feels* like it raises your rate:
```
goldPerSec(stage)       = base * growth^stage
xpPerSec(stage)         = ...
lootRollsPerHour(stage) = ...     // offline still rolls real loot
cap                     = 12 hours (locked; rate still TBD)
offlineRate             = 100% (or 70–80% to nudge active play)
```
Add a "quick run" button later: grants e.g. 2h of yield instantly on a daily cooldown.

### 5.2 Loot rarity + affixes (the dopamine engine)

| Rarity | Border | # affixes | Source |
|--------|--------|-----------|--------|
| Normal | none (gray) | 0 | trash / idle |
| Magic | teal | 1–2 | trash / idle |
| Rare | blue | 3–4 | trash / idle (the ceiling for these) |
| Unique | yellow | 4–5 | **bosses only** |
| Legendary | green | 5–6 | **bosses only** (~1% within a boss bundle) |

*(Weights/counts in `Balance.RarityBaseWeights` + `AffixCountByRarity`; rarity colors in the
client `Palette`. Unique/Legendary use random affix counts for now; hand-authored "fixed
special affixes" for true uniques come later.)*

- **Drops are deliberately scarce** (~1 item per few minutes of active farming; `Balance.DropChance`),
  so each one matters. Trash and idle drops are **capped at Rare**.
- **Unique/Legendary are boss-exclusive:** each boss drops a guaranteed bundle (major 5–7,
  mini 1–2) of Unique/Legendary plus a few Normal–Rare extras (`Loot.RollBossDrops`).
- **Soft inventory cap (100 *loose* items):** live farm pickups stop when full; idle and
  boss/special drops may **overfill** past it. **Auto-salvage** (opt-in rarity threshold)
  converts low drops to `scrap` instead of taking a slot — owned items are never auto-destroyed.
- Drop rates / affix values **scale with stage / item level**; affixes roll from a weighted
  pool with value ranges → near-infinite variety.
- **Loot filter** (later) for high stages. Implemented as **seeded, testable pure functions**;
  the weighted-roll engine (`Rng.WeightedPick`) is reused verbatim for gacha later.

### 5.3 Difficulty-curve intents (locked 2026-07-09)

**Why:** BalanceSim's first run proved the ladder's math dies near stage 53 — geometric
monster HP (`MonsterHpGrowth 1.18^stage`) vs strictly linear player power (level growth +
`value × ItemLevel` affixes) — with thorns bosses anti-scaling into unwinnable walls and gear
utterly dominating level. These are the durable fix targets (ROADMAP 10.1 implements; the
walls chart is the acceptance):

- **Thorns — capped mirror.** Reflect stays damage-proportional but is capped per hit at a
  small fraction of the **attacker's** MaxHp (~2–3%), for every thorns source (boss self-mod,
  farm mod, gear imprints). It can never one-shot; **sustain (lifesteal, Priest) is the
  intended counter-build**, so thorns bosses become a build check instead of a math wall.
- **Curve pacing — soft wall at ~80.** The per-stage growth exponent tapers by tier so
  on-curve play (level + reasonable gear) reaches ~stage 80; **81–100 is the prestige band**,
  expecting near-mythic gear plus the account-wide stacks (tower buffs, crypt boons, enhance).
  Endless mode's stage-100 entry is therefore an elite unlock by design. Two-tier acceptance:
  on-curve gear columns green to ~80, the mythic column green to 100 (the sim must first
  learn the account stacks — 10.1's sim slice — since it under-counts live players today).
- **Gear vs level — ~50/50.** On-curve power contribution rebalances to roughly equal parts
  hero level and gear (`GrowthPerLevel` up, `ValueMin/MaxPerItemLevel` down). Acceptance:
  the bare-hero and full-rare walls columns land within ~15 levels of each other (pre-fix:
  a full rare set at hero level 1 cleared stages 1–27). No gear level-gates — that friction
  fights the idle loop; the split is corrected in the numbers, not with locks.

---

## 6. Depth roadmap (post-MVP, roughly by value)

1. **Class skills** — each class fires its own kit (cooldown/targeting/effect) so heroes aren't stat sticks.
2. **Item enhancement (scrolls)** — optional upgrade gamble + gold sink (§6.1); *likely end-game, deferred*.
3. **More gear slots + set bonuses** — bigger build space.
4. **Crafting / currency (PoE-style)** — reroll/upgrade affixes.
5. **Alt modes** — endless mode ("deepest stage" chase), then party / co-op.
6. **Loot filter** — QoL that becomes essential at high stages.
7. **Boss mechanics & difficulty walls** — force build optimization, not just push.
8. **Daily/weekly quests + login rewards** — retention backbone.
9. **Auto-progression / auto-push.**
10. **Unlock heroes 2–4** through progression milestones (pre-gacha).
11. **Prestige / "rebirth"** — reset for permanent multiplier; design economy with it in mind.
12. **Account level, achievements, codex/collection.**
13. *(Later)* **Gacha layer** — heroes *and* weapons (see §9). *(Much later)* Arena/PvP, guilds, events.

### 6.1 Item enhancement (scrolls) — *deferred, likely end-game*
A potential MapleStory-style upgrade gamble: spend a **scroll** to raise an item's
`enhanceLevel` for a stat bump, with success odds that fall (and risk that rises) as the
level climbs. It's a gold/currency **sink** and a later monetization hook (protection items).
**Low priority on purpose — finding better loot should stay the main upgrade path**, so this
likely only lands once end-game players have run dry on natural upgrades. Mechanics to refine
later; whenever it's built, put it on the same seeded `Rng` so it's deterministic/server-verifiable.

---

## 7. Suggestions to make it actually good

- **Build a balance sim before tuning by feel.** Idle games are ~80% economy tuning;
  `GameCore` being pure + deterministic makes this scriptable (`dotnet test` / a console runner).
- **Make the first 10 minutes generous** — fast early upgrades, frequent drops.
- **Number formatting from day one** — `1.2K / 3.4M / 5.6B…`.
- **Rounding direction is a correctness rule, not a style choice.** A displayed number that rounds the
  *wrong* way creates a lie: "it says I have enough but I can't buy it," or "the timer says 0 but I
  still have a second." Pick the direction that can never contradict the underlying data:
  - **A resource/currency the player HAS → floor** (round DOWN). Never show more than they actually own.
  - **The COST of something → ceil** (round UP). Never show less than what will actually be charged.
    (Floor-what-you-have + ceil-what-it-costs together guarantee: if the display says you can afford it,
    you truly can.)
  - **A timer counting DOWN → ceil** (so it only hits 0 when the time is genuinely gone).
  - **A timer counting UP → floor** (so elapsed/playtime stats never overstate).
  - Plain `round` is for neutral/illustrative values only (stat sheets, percentages) — never for a
    number the player will act on against a threshold. `Num` provides `Compact` (round) plus
    `CompactFloor` / `CompactCeil` for the two directional cases.
- **Offline-return moment** — the "while you were away" modal with loot summary + count-up is the emotional core.
- **Juice** — loot beams, rarity colors/sounds, damage numbers, crit flashes, screen shake.
- **Item comparison UI** — equip/compare must be instant and obvious (green ▲ / red ▼).
- **Separate intended config from live tuning** — all constants in the balance section of `GameConfig`.
- **Test loot statistically**, not anecdotally (§5.2).

### 7.1 The four loop levers (the "feels empty/boring" plan)

When the moment-to-moment feels thin, the fix is the **loop**, not QoL/menus — polish
makes a thin loop easier to navigate, it can't make it fun. For an **idle auto-battler**
(the player mostly watches), "fun" comes from two questions: *is the screen interesting
to watch?* and *is there a reason to keep going?* These four levers, roughly in order of
build value, are the long-run plan; **all four are goals.** Don't reach for QoL or
big-architecture (prestige) until the minute-to-minute is fun.

1. **Combat variety** *(✅ shipped — and grew into the core loop).* (a) elite/rare **ranks** —
   tougher, highlighted mobs with better loot; (b) **monster modifiers** — a player-controlled
   risk/reward knob (PoE map-mods). This is now the game's central loop:
   - **Two pools.** *Normal* stat mods unlock by farm depth (one per 10 stages) for a 3-slot
     account loadout; *Rare* mechanical mods are **Tower-gated, unlocked in prefix/suffix pairs**
     with a separate 2-prefix + 2-suffix loadout.
   - **Loot-imprint (the headline hook).** A rare mod fights nastier via a real sim mechanic
     (splash / chain / lifesteal / thorns) AND stamps that trait onto its drops as **exclusive
     gear** — a stat no normal affix rolls. Anti-target-farming: a slot only applies with ≥2 active,
     an item holds ≤1 prefix + ≤1 suffix imprint, and the imprint is a random pick among hits.
   - **Gamble economy.** Spend **gold + scrap** to roll a ±5% delta (floored at base, escalating
     cost) on a mod's **tuning** (scales its danger + reward; rewards can be hybrid splits) or on an
     **item's affix values** (Reforge). One gamble verb, two surfaces — no full crafting system.

   Hits *empty* and *boring* at once, and gives gold/scrap real sinks. See §8.
2. **Loot & power chase** *(✅ shipped).* Drops legible at a glance: `Upgrades.PowerScore`
   collapses an item swap into one honest scalar (geometric mean of DPS + Effective-Life) and a
   banded Upgrade/Sidegrade/Downgrade verdict, so the bag badges upgrades (green ▲), the loot feed
   tags them ("▲ +N% for <hero>"), the compare leads with the verdict, and opt-in
   auto-equip-if-better removes the chore. Each kill visibly matters. Built on §7 "Item comparison
   UI" + §5.2.
3. **Build depth** *(✅ shipped, then simplified).* The party is a *build you shape* via skill
   points (`Skills.InvestSkill`, free respec; rank 0 = base, purely additive) and always-on
   passives folded into `Stats.ComputeHeroStats`. The original 4-of-6 loadout + prereq-gated
   tree shipped and was then **superseded by the 2+2 hero template (§7.2)** — the roster-scaling
   decision for the hero-gacha arc.
4. **Progression hooks.** Milestone rewards, escalating goals, a "one more stage" pull
   (goal-ladder slices 3–4). Pulls you forward but *relies on the core fight already
   being fun* — hence last. See §8 "Prestige & retention".

### 7.2 Hero template — the 2+2 kit (locked 2026-07-01)

**Why:** the next arc is a hero-production pipeline feeding an eventual **hero gacha** (the
gem sink — heroes, not gear, so the gacha monetizes roster *breadth* while the farm keeps
monetizing gear *depth*; the two never compete). A solo dev can only scale heroes if each
one is a **data row, not a system** — so the kit is fixed-shape and trees are gone.

**The template (every hero, no exceptions):**
- **2 active + 2 passive skills**, drawn from a shared **archetype library** (actives:
  nuke / AoE / heal / buff; passives: stat nodes, lifesteal/thorns-style hooks — all
  mechanics that already exist in the sim). A hero = class/role + element flavor +
  4 archetype picks with per-hero params and palette. New hero ≈ config entry + chibi tint.
- **Skill points: 1 per 5 hero levels** (derived `Level / 5`, never persisted). **Rank cap 5
  per skill** ⇒ 20 points at level 100 = *exactly* enough to max the kit. Endgame is
  "eventually complete"; the build choice is **ordering** across a months-long XP curve —
  no respec regret, no permanent-scarcity UX tax.
- **Ranks 1–4 are numeric growth; rank 5 is a mastery bump** (a chunkier, parameterized
  jump — bigger multiplier / +1 chain / wider splash). "Rush one skill to mastery vs
  spread" is the build texture, at zero bespoke code per hero.
- **No loadouts, no prereqs.** Both actives always slotted, passives always on
  (`SkillLoadout`/`MaxActiveSkills` retired). Keep the light `UnlockLevel` reveal cadence
  (active₁ L1 · passive₁ L5 · active₂ L10 · passive₂ L15) — kit-assembly feel for free.
- **Gacha hookup (later):** hero *grade* reuses the rarity ramp (Rare→Mythic colors) and
  scales base stats/growth only — never skill count, so a Mythic hero stays cheap to author.
  Dupes → universal shards → pity shop; dupes buy access/cosmetics, **never raw stats**.
- **Monetization invariant (applies to all future systems):** no design may make *deferring*
  a gem spend the rational choice; banking gems toward a known banner launch is fine.

**Current kits** (exact params live in `GameConfig.Default()`): Knight = spinning AoE +
shield-charge dash, armor + health passives. Fire Mage = fire nuke + AoE fireball,
spell-power + mana passives. Assassin = fast stab + heavy vital strike, crit passives.
Priest = party heal-over-time + AoE smite, sustain + mana-flow passives.

### 7.3 Crypt dungeons — Mabinogi-style rooms (locked 2026-07-07)

**Why:** the shipped crypt reads as one continuous 26-room sweep — no anticipation, no
rhythm. The overhaul (ROADMAP 10.7) makes each floor a legible room-by-room crawl in the
spirit of Mabinogi's Ciar dungeon: sealed-door fights, a key hunt, breather rooms, a
locked boss gate, a reward room. The 2026-07-06 meta lock is UNCHANGED (1 key/UTC day
bank 2 · 3-floor runs from depth record+1 · gems per first clear · dust chest · permanent
boons · wipe keeps drops, forfeits the chest).

**Floor grammar** (~12 rooms on the existing linear chain; user picks 2026-07-07;
AMENDED 2026-07-09 — physical door seals REMOVED, user call: the containment clamp
teleported units. The crawl is clamp-free: entering a mob room wakes its whole pack
and marks it ENGAGED; waves and the room-clear beat ride that, and the sweep AI's
shallowest-living-room order keeps the room-by-room rhythm without a trap):
- **Entrance** (safe) · **6–7 Combat** · **1 Elite** · **1 Key room** · **1 Chest room**
  · **Boss**. Mob rooms spawn in waves; a cleared room pays a small gold/loot burst.
- **Key room** = a combat room with a marked, glowing **key bearer** mob that drops the
  Boss Key on death — a tell/beat (the sweep clears it before the deeper boss room
  anyway, so no physical gate is needed): *fight → find the bearer → boss*.
- **Chest room**: 1–3 chests by depth; **~15% of chest-room chests are GOOFY mimics**
  (googly-eyed elite-rank fight paying the chest contents + a bonus; never in the
  reward room).
- Floors 1–2 of a run end at a **floor-guardian mini-boss** (elite-tier); floor 3 ends
  at the tier's real boss, and behind it the **REWARD ROOM**: 1 golden + 2 iron chests
  + the dust urn (the existing `GrantChest` formula made diegetic).
- Mid-run persistence: quitting suspends the run (key not forfeited); resuming replays
  the same seed at the same room. A run summary screen closes every run.

**Encounter tables** (starting values; BalanceSim dungeon mode is the tuning
acceptance): depths 1–10 combat = 1 wave × 5, elite + 3, boss + 2 adds; 11–20 = 2×4,
elite + 4, +3 adds; 21–40 = 2×5, 2 elites + 3, +4 adds; 41–60 = 3×4, 2 elites + 4,
+4 adds with one re-wave at 50% boss HP. Waves spawn on the previous wave's clear,
rising from floor tells at the room edges. Specs are content-as-data keyed off
`CryptTierDef` + depth band; new content seeds at New Game.

**Chest tiers** (contents ride the normal drop tables): **Wooden** gold + 1–2 items ·
**Iron** gold + 2–3 items (≥1 rare) + 5–10 dust · **Golden** 3 items (≥1 unique) +
15–25 dust, mythic chance at depth 40+. Chest-room count and tier weights scale with
depth.

**Cut for now (user call 2026-07-07): the mid-run MERCHANT** (run-scoped boon shop,
which had absorbed the old boon-draft idea) — parked, not designed in. If it ever
returns, it must not dilute grave dust's permanent-boon role.

### 7.4 FTUE & staged reveal (locked 2026-07-09)

**Why:** a new player meets ~10 buttons and 100 numbers in minute one. The fix is a
staged reveal + a quest-driven intro — no tutorial system, no overlays (user pick:
**quest board only**, cozy over hand-holdy).

- **Minute one shows only the intro's surface**: the fight, Inventory, Heroes, the
  quest board, stage nav + boss challenge, Settings, chat/feed. Everything else is
  HIDDEN (not greyed) until earned.
- **Reveal schedule (user pick: FAST — everything by ~stage 12)**: auto-advance @S2
  (after the first boss) · idle claim + daily login @S3 (kills both launch popups in
  minute one; streaks still start same session) · achievements @S5 · modifiers @S10
  (= where the first real modifier unlocks) · Modes menu (Tower+Crypt) @S10 ·
  gacha @S12. Each reveal = a one-line toast ("Modifiers unlocked — risk for reward").
- **Gating is sim-side** (`Progression.FeatureUnlocked(feature, save)`) so a future
  server agrees with the client. **Fresh games only**: gating arms via a flag set at
  New Game (additive save field, no version bump) — existing saves deserialize
  unarmed and see everything, forever.
- **Guided intro = the first five quests**, seeded only at New Game ahead of the
  rolling board, worded imperatively: kill a pack → collect your first drop → equip
  it → beat the stage-1 boss → reach the first reveal. Each retro-completes if its
  deed already happened (the `SyncHeroUnlocks` pattern) and pays strictly in beat
  order (an out-of-order deed waits for its predecessors) — the intro can never wedge.
- **Celebration beats**: first boss kill and first hero unlock reuse the existing
  juice, one size bigger. **Breadcrumb**: one contextual HUD hint line fed by game
  state (idle claim ready / boss looks beatable / unspent skill point), lowest-key
  guidance that persists after the intro ends.

### 7.5 Goals hub (locked 2026-07-10)

**Why:** quests, achievements, and daily login are three doors to one habit —
"come back and see what paid." Quests and achievements deliberately AUTO-PAY
(no claim chores in an idle game); the only manual claim is the daily login.
So the hub is a **consolidation + visibility** surface, not a claim queue: one
window that shows progress toward everything, one pip that says something's
waiting, two HUD buttons retired.

- **One "Goals" control-bar button** replaces the Achievements button; the
  ambient quest HUD panel stays (glanceability is its job, the hub's is depth).
  A gold **pip** on the button whenever `Goals.Claimables` is non-empty.
- **Three tabs — Today / Achievements / Login** (PanelKit window). Today = the
  rolling board with progress bars + an "auto-pays on completion" caption.
  Achievements = the lifetime ladder (absorbs and retires AchievementsPanel).
  Login = streak state, claim button, and tomorrow's reward preview.
- **Sim-side read model** (`Goals`, pure): `Claimables(save, cfg, now)` — the
  one list of manual claims (today: daily login; the seam for future manual
  systems) — and `ClaimAll` which applies them via the existing reducers.
  Reward previews come from GameCore helpers (never recomputed client-side).
- **FTUE**: the button reveals with `Feature.DailyLogin` (S3); the
  Achievements tab hides until `Feature.Achievements` (S5). Unarmed saves see
  all, as everywhere. The launch DailyLoginModal stays (same idempotent
  reducer; the modal is arrival juice, the tab is the durable home).

---

## 8. Milestone roadmap

**This table is the durable milestone LEDGER (scope + eventual outcome), not the
work queue — what to do NEXT (and its pre-sliced tasks) is [`ROADMAP.md`](ROADMAP.md),
which wins wherever the two disagree.** Three phases: **Core** (a fun,
single-player, local game), **Depth** (build variety + retention), **Live-service**
(server authority + global). Each milestone is pure-`GameCore` first (tested with
`dotnet test`), then wired into Unity.

### Phase A — Core gameplay (single-player, local)
| Milestone | Deliverable | State |
|-----------|-------------|-------|
| **M0 – Skeleton** | Scene + camera, render party + dummy monsters as placeholders. | ✅ |
| **M1 – Auto-combat** | Deterministic auto-target/attack, kill packs, clear a zone + boss. | ✅ |
| **M2 – Loot** | Drops with rarity + affixes, inventory, equip → stats recompute. | ✅ |
| **M3 – Leveling** | Kills grant XP; fielded heroes level up; stats scale with level. | ✅ |
| **M4 – Stages & progression** | 50-stage ladder, **miniboss gate** to advance, **major boss every 10**, hero **downing / run-fail**, stage select. | ✅ |
| **M5 – Idle** | Offline accrual off **highest cleared stage** + "while you were away" claim modal. | ✅ |
| **M6 – Persistence & menu** | Save/load + main menu (Continue / New Game, single local account). | ✅ |
| **M7 – Feel pass** | Number formatting, loot juice, item-compare UI, offline-modal animation. | ✅ |
| **M8 – Farm zones + boss gates** | Each stage is an endless farm zone (continuous trash, concurrency cap); advancing N→N+1 is a **60s timed boss challenge** (mini boss; **major boss every 10**); loot/XP/gold rates step per stage and jump per 10-tier. Replaces the old "clear pack+boss = win" model. | ✅ |
| **M9 – Core-loop polish** | Bigger play area (precursor to terrain/maps), batch monster spawns, **magician** ranged class + warrior/magician splash AoE, group-vs-solo party movement, and a left-side chat/feed panel (loot/XP feed as a toggleable tab; social tabs stubbed pending the server). | ✅ |

→ At the end of Phase A you have a complete, satisfying solo idle ARPG.

### Phase B — Depth (build variety + retention) — in progress
| Milestone | Deliverable | State |
|-----------|-------------|-------|
| **M10 – Multi-character foundation** | Mana; 9 per-hero equip slots from one shared bag; inventory cap + auto-salvage (`scrap`); scarce loot with Unique/Legendary **boss-only** (guaranteed bundles); Party HUD + per-hero Equipment HUD; rarity-bordered item tiles; canonical stat display. | ✅ |
| **M11 – Class skills** | Skills fire in the sim (damage/AoE, heal, self-buff; mana + cooldowns; heroes + bosses); `MoveSpd`/`AtkSpd` split. Client: skill FX + `AtkSpd`-scaled lunge tell + skill-ready cue + **hit-recoil & death-crumple** FX (no instant-vanish). | ✅ |
| **Salvage UI** | Manual salvage (Unique/Legendary confirm) + auto-salvage threshold toggle. | ✅ |
| **Heroes hub (unified)** | One **Heroes** screen: left hero rail (party slots + all owned) + Equipment / Skills (read-only) / Stats sub-tabs + Field/Bench; body-mapped doll. Replaced the separate Roster + Equipment windows; live farm-only swaps via `Combat.ReconcileParty`. | ✅ |
| **Hero acquisition pipeline** | Start solo (Warrior); `GameConfig.HeroUnlocks` grants heroes on stage clear (stage 3 → Magician) via `Party.AcquireHero` + auto-field (`Party.FieldHero` dedupe-safe). The plug point gacha reuses later. | ✅ |
| **World & combat feel** | Geometric difficulty (steep HP/dmg growth + `BossHpMult`), **100-stage ladder**; big open field; **party-relative PACK spawning** (clusters ring the group, quiet gaps, sparse, no distance cull); party **always-group**; follow camera + wheel zoom + shake; top-centre stage nav/Challenge + boss-clear popup. | ✅ |
| **Art direction — *Tunic* pivot** | `TunicSurface` height-blend shader (grass-top/dirt-side + inked facet edges + crisp light); faceted vertex-coloured ground + props; clean lighting + procedural dappled light cookie. Heroes are code-built **chibi placeholders** — Blender skinned models are the eventual goal, plugging into the `CombatView` spawn/animator seam. Mixamo removed. | ✅ |
| **Monster-modifier core loop** — *✅* | Elite/rare ranks; the risk/reward **modifier** system: two pools (farm-depth normal stat mods + Tower-gated rare **loot-imprint** mods that stamp exclusive gear), a **modifier shop + item reforge** gold+scrap gamble economy, hybrid reward splits. Lever #1 of §7.1. | ✅ |
| **Loot legibility** — *✅* | `Upgrades` power-score + verdict core (geometric DPS×Eff-Life); bag ▲ badges, loot-feed upgrade tags, compare verdict headline, opt-in auto-equip-if-better. Lever #2 of §7.1. | ✅ |
| **Skills & skill trees** *(✅)* | Per-hero **unique** skills, leveled with skill points. **Slice 1 ✅** — 1 point/level (`Skills.InvestSkill`/`RespecHero`; ranks scale effect `×(1+EffectPerRank·rank)`, rank 0 = base; UI invest/respec in Heroes→Skills). **Slice 2 ✅ active/passive** — 6 active (≤4 equipped) + 2 always-on passive nodes/class that fold into `Stats.ComputeHeroStats`. **Slice 3 ✅ gated tree** — actives branch from 2 roots; a node needs ≥1 rank in its prereq AND `hero.Level ≥ UnlockLevel` (`SkillDef.Prereq`/`UnlockLevel`, `Skills.IsUnlocked`); gates investment only. Lever #3 of §7.1. | ✅ |
| **Roster growth & classes** | **Archetype backbone** (Warrior/Rogue/Magician templates + passive pools, §1 Classes row): Knight + Fire Mage + Assassin + Priest live, all on the MS2 skinned pipeline; Ice Mage shelved as the gacha-banner candidate. New classes = archetype + overrides + one art bake. | ◑ |
| **Social / chat IA** | Pre-release shows **System only**; Global · Friends · Guild and per-person **Whispers** (DMs) stay hidden until the server (Phase C) so players aren't shown dead features. Target IA + the re-enable seam are documented in `ChatPanel`. | ◑ |
| **Crafting / sets / loot filter** | **Item Reforge** (affix-value re-roll gamble) + **modifier shop** shipped; set bonuses, loot filter, enhancement scrolls (§6.1) still open. | ◑ |
| **Alt modes** | **Tower of Ascension** ✅ playable — its own one-clear-per-floor track, steeper curve, no idle income, permanent account-wide buff every 10 floors, and the **gate for the rare modifier pairs** (`Tower.cs` + `TowerState` under `ProgressState`). Remaining: per-floor reward bundles (slice 3, TBD). **Crypt (roguelite)** ✅ playable — procedural packed-maze floors (Dungeon Forge aesthetic), fully isolated mode state, room-scoped aggro. Meta (locked 2026-07-06): **1 key/UTC day** (bank 2) per **3-floor run** starting at the depth record +1; every first clear pays gems; completing the run opens a **grave-dust chest** (wipe forfeits the chest, keeps drops); dust buys permanent **boon** tracks (Hp/Atk/Def, Tower-buff sibling); floors ramp geometrically on current-stage scaling and travel **themed tiers** (crypt→molten→frost per 10 depths, zone casts reused) (`Crypt.cs` + `CryptState` under `ProgressState`). Also planned: Endless ("deepest stage"); later party / co-op. | ◑ |
| **Prestige & retention** | Rebirth multiplier; daily/weekly quests, login rewards, achievements, codex. | |
| **UI/UX polish pass** | Dedicated pass **after the gameplay depth above** — the current screens are functional placeholders (IMGUI HUD + code-built uGUI, hand-placed coords). The real fix is a **uGUI layout-group refactor**; plus glyph/font audit, theming, real item/hero art hooks. | deferred |
| **Balance sim (tooling)** | ✅ `gamecore/BalanceSim` console runner over pure `GameCore` (`dotnet run --project gamecore/BalanceSim -- walls\|sweep\|farm`): min-clear-level wall chart per stage × gear tier, stage×level win grids, farm throughput (kills/min, hits-per-kill one-shot signal, XP/gold rates). Deterministic per cell seed; scenario saves are built through the live reducers (unlocks, skill invest, equip). First run found real walls — see ROADMAP backlog 10.1. | ✅ |

### Phase C — Live-service (server + global)
| Milestone | Deliverable |
|-----------|-------------|
| **L1 – Server-authoritative core** | ASP.NET Core service referencing `GameCore`; client sends *intents*; first endpoint resolves a stage run (§9.3). |
| **L2 – Accounts & auth** | Sign in with Apple/Google + guest; **one account per player**; cloud save. |
| **L3 – Global chat** | WebSocket gateway + pub/sub; **shard servers** as load requires (§9). |
| **L4 – Gacha** | Heroes *and* weapons on the weighted-roll engine; pity/rates; disclosed odds. |
| **L5 – Store & LiveOps** | IAP + receipt validation, remote config, analytics, i18n; mobile build. |

---

## 9. Scaling later — gacha + global live-service

### 9.1 Keeping the gacha door open (even though it's on hold)
- **Heroes are already data-driven instances** owned by the player → "acquire a hero"
  is a swappable source. `Party.AcquireHero` is the single plug point; v1 acquires via
  progression, gacha later just becomes another path that calls the same function.
- **Weapon gacha is also already half-built:** loot already rolls items via
  `RollItem`/`WeightedPick`; a weapon banner is just a premium-currency source on the
  same roller, with curated bases/rarity weights.
- **The weighted-roll engine (`Rng.WeightedPick`) built for loot is the same engine
  gacha uses** — rates, pity, rarity tiers sit on top of it.
- **Currencies are a map** → adding `gems` / `tickets` / `shards` / `scrolls` is additive.

### 9.2 The decision that matters most for scaling
**All game logic stays in `GameCore` (pure C#, no engine/UI/network deps).** This lets
the Unity client, a future mobile client, and a future authoritative .NET server all
reference the same simulation. Gacha and server-authority both ride on this.

### 9.3 Server-authoritative live-service path (additive, not a rewrite)
Goal: global chat, multiple servers, and **leveling/loot computed server-side so a
modded client can't cheat.** Because `GameCore` is engine-agnostic and deterministic,
this is build-out on top of the existing core:

1. **Now:** Unity client + `GameCore` + local saves. Prove the loop is fun.
2. **Server:** an **ASP.NET Core** service referencing `GameCore`. Client sends
   *intents* ("push tier N", "equip X", "claim idle"); the server runs the sim, owns
   the `SaveState`, and returns results. First proof-of-concept: one authoritative
   "resolve a stage run" endpoint.
3. **Authority model:** the server's result is the truth; the client sim is cosmetic
   prediction. (This avoids depending on bit-exact cross-platform float determinism —
   `GameCore` uses `double`; only the server needs to be self-consistent.)
4. **Scale-out:** stateless app servers behind a load balancer; **Postgres** (durable
   saves/accounts) + **Redis** (hot state, leaderboards, chat fan-out). Idle gameplay is
   request/response and combat resolves on demand (`RunToEnd`), so the app tier scales
   statelessly — a modest VPS hosts the first version, **sharded by region/account
   only when load requires it**.
5. **Global chat:** its own real-time service (WebSocket gateway + pub/sub), **shardable
   by channel/region**. It never touches `GameCore` — keep it separate.
6. **App stores / mobile:** the same `GameCore` + a mobile shell; add IAP + store auth.

### 9.4 Non-negotiables once it's a real product (bake hooks in early)
- **Server time authority** for idle accrual (don't trust the device clock).
- **Server-side RNG** for loot *and* gacha — anti-cheat + (for gacha) legally-mandated
  disclosed rates (Japan, China, South Korea; loot boxes restricted in Belgium/Netherlands).
- **Remote config / LiveOps** — push stages/events/balance without app updates.
- **IAP** via Apple/Google billing + server-side receipt validation.
- **Store-compliant auth** — Sign in with Apple + Google + guest linking.
- **i18n from the start**; **analytics + crash reporting**; **cloud save / cross-device**.

Unity covers most of these first-party: Addressables (content/LiveOps), Localization,
Unity IAP, Remote Config, Analytics.

---

## 10. Open next steps
The three combat/build/loot levers (§7.1 #1–3) are shipped: monster-modifier core loop +
loot-imprint gear, loot legibility + auto-equip, skills/passives/gated trees. The Tower alt
mode, the gold+scrap gamble economy (modifier shop + item reforge), a 4-class roster on the
archetype backbone (Knight/Fire Mage/Assassin/Priest), and a months-long hero-leveling
curve are in. Gameplay-first, next:
- **Content & polish (current focus):** more heroes/classes, enemy variety, more mods & stages;
  balance/tuning; combat juice + sound.
- **Progression hooks (Lever 4, §7.1 #4):** milestone rewards, dailies/logins, achievements/codex,
  then prestige/rebirth.
- **Tower slice 3:** per-floor reward bundles.
- **Gear depth (remaining):** set bonuses, loot filter, enhancement scrolls (§6.1).
- **Alt modes:** endless ("deepest stage"), then party/co-op.
- **Then** gacha + live-service per §8/§9.

**UI/UX polish is its own milestone, sequenced *after* the depth gameplay above** — today's
screens are functional placeholders (IMGUI HUD + code-built uGUI, hand-placed coords); the fix
is one focused pass: a uGUI layout-group refactor + glyph/font audit + theming + real art hooks.
