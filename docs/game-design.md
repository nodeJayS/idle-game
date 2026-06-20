# Idle ARPG — Game Design

> A Diablo 3 / Path of Exile–style **idle ARPG**: a 3-hero party auto-clears
> dungeons, monsters drop loot, you build out gear/skills and push higher difficulty.
> Scope: personal/for-fun game first, **architected to scale to a global live-service
> mobile release later** without a rewrite.
>
> Stack: **Unity (C#, URP)** client + a pure-C# **`GameCore`** simulation library.
> (The high-level orientation + milestone status lives in [`../CLAUDE.md`](../CLAUDE.md);
> this doc is the durable "what & why.")

---

## 0. Project decisions (locked)

| Decision | Choice |
|----------|--------|
| Genre / feel | **Idle ARPG** — Diablo 3 / PoE style, loot-and-build driven |
| Initial scope | Personal / for-fun game, built to scale to live-service |
| Engine | **Unity (C#)**, 3D URP |
| Art direction | **2.5D isometric, low-poly 3D** — fixed iso camera over low-poly models; readable, cheap to produce, light on mobile |
| Sim | **`GameCore`** — pure C# library, no `UnityEngine` refs; Unity is a read-only client |
| Party | **3 hero slots** from a multi-class roster; **start solo (Warrior)**, unlock the **Magician at stage 3**. Leveling is **per-hero** (see §4) |
| Classes | Each class has its **own skill set**; skills fire automatically in combat (live, M11) |
| Map / movement | Party **auto-navigates** stages, auto-fights, auto-loots |
| Stages | **1–50 main ladder**; clear a **miniboss** to advance, **major boss every 10**. Endless & party modes later |
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
2. **Auto-combat** — 3-slot party (start solo Warrior; Magician unlocks at stage 3), auto-target nearest monsters, auto-attack/auto-skill on cooldown. Heroes can be **downed**; a full party wipe fails the run.
3. **Monster packs + a miniboss** — clear the packs, **miniboss gates the next stage**; a **major boss every 10 stages**.
4. **Loot drops** — monsters drop gear with rarity + random affixes; auto-pickup.
5. **Equip & stats** — equip gear on a hero; stats recompute; party gets stronger.
6. **Leveling** — kills grant XP; **fielded heroes level up**, stats scale.
7. **Stage ladder** — clear stages in order; higher stage = tougher monsters + better loot. The progression spine.
8. **Idle accumulation** — offline → loot/gold/XP accrue based on highest cleared stage, capped (12h).
9. **Save/load + main menu** (Continue / New Game).

No gacha, no summoning. Heroes 2–4 unlock through progression for now (gacha slots in later — §9).

### 2.1 Stage structure, bosses & hero death
- **50 main stages.** Each stage is a few monster packs ending in a **miniboss**; beating the miniboss marks the stage cleared and unlocks the next. Idle income keys off your **highest cleared** stage, so being stuck never breaks idle income.
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
- **Offline-return moment** — the "while you were away" modal with loot summary + count-up is the emotional core.
- **Juice** — loot beams, rarity colors/sounds, damage numbers, crit flashes, screen shake.
- **Item comparison UI** — equip/compare must be instant and obvious (green ▲ / red ▼).
- **Separate intended config from live tuning** — all constants in the balance section of `GameConfig`.
- **Test loot statistically**, not anecdotally (§5.2).

---

## 8. Milestone roadmap

Live status is tracked in [`../CLAUDE.md`](../CLAUDE.md). Three phases: **Core** (a fun,
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
| **Pack variety** — *next* | Elite/rare mobs within packs (highlighted, tougher, better loot) — PoE magic/rare-pack feel on the sparse open field. | |
| **Skills & skill trees** *(its own milestone)* | Per-hero **unique** skills, leveled with skill points. **Active vs passive**: ≤4 active equipped at once, passives always apply. **Skill tree** — initially linear; a node needs ≥1 point in its prerequisite; more nodes unlock as the hero levels. Builds on the M11 `SkillDef`/loadout seed (the Heroes Skills tab is the read-only seed). | |
| **Roster growth & classes** | More hero unlocks (stage 5/7/…) and new classes/kits beyond Warrior + Magician. | |
| **Social / chat IA** | Pre-release shows **System only**; Global · Friends · Guild and per-person **Whispers** (DMs) stay hidden until the server (Phase C) so players aren't shown dead features. Target IA + the re-enable seam are documented in `ChatPanel`. | ◑ |
| **Crafting / sets / loot filter** | Affix rerolls, set bonuses, enhancement scrolls (§6.1), loot filter. | |
| **Alt modes** | Endless ("deepest stage"); later party / co-op. | |
| **Prestige & retention** | Rebirth multiplier; daily/weekly quests, login rewards, achievements, codex. | |
| **UI/UX polish pass** | Dedicated pass **after the gameplay depth above** — the current screens are functional placeholders (IMGUI HUD + code-built uGUI, hand-placed coords). The real fix is a **uGUI layout-group refactor**; plus glyph/font audit, theming, real item/hero art hooks. | deferred |
| **Balance sim (tooling)** | A console runner over pure `GameCore` to chart difficulty vs hero power across stages/levels/gear and find the walls. The steep geometric curves are tuned by feel today, and heroes can out-level a stage and one-shot trash. | deferred |

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
Skill FX, salvage UI, and the roster screen (with live farm swaps) are **done** — see §8
Phase B. Gameplay-first, the next depth work is:
- **Roster growth & classes:** acquire heroes 2–4 via progression; add classes/kits.
- **Gear depth:** sets, enhancement scrolls (§6.1), affix reroll, loot filter.
- **Alt modes:** endless ("deepest stage"), then party/co-op.
- **Prestige & retention:** rebirth multiplier; daily/weekly quests, login rewards, achievements, codex.
- **Then** gacha + live-service per §8/§9.

**UI/UX polish is its own milestone, sequenced *after* the depth gameplay above** (decided
June 2026). Today's screens are deliberately functional placeholders (IMGUI HUD + code-built
uGUI, primitive art); polish — layout/scale/theming, a glyph/font audit, control-bar redesign,
and real item/hero art hooks — lands as one focused pass rather than being interleaved now.
