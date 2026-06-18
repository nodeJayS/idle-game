# Idle ARPG — Game Design

> A Diablo 3 / Path of Exile–style **idle ARPG**: a 4-hero party auto-clears
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
| Initial scope | Personal / for-fun game |
| Engine | **Unity (C#)**, 3D URP. |
| Sim | **`GameCore`** — pure C# library, no `UnityEngine` refs; Unity is a read-only client |
| Party | **4 hero slots**; player starts with **1 basic Warrior**, 3 empty |
| Map / movement | Party **auto-navigates** dungeons, auto-fights, auto-loots |
| Art direction | **Cozy low-poly 3D, *Tunic*-ish** — stylized, readable, cheap to produce, light on mobile |
| **Gacha** | **ON HOLD** — not in v1. Architecture must scale to it later (see §9). |
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

1. **Dungeon zone** — a small 3D zone the party walks through.
2. **Auto-combat** — 4-slot party (1 Warrior to start), auto-target nearest monsters, auto-attack/auto-skill on cooldown.
3. **Monster packs + a boss** — clear the zone, boss at the end.
4. **Loot drops** — monsters drop gear with rarity + random affixes; auto-pickup.
5. **Equip & stats** — equip gear on a hero; stats recompute; party gets stronger.
6. **Stage/difficulty tiers** — pick a difficulty; higher stage = tougher monsters + better loot. The progression spine.
7. **Idle accumulation** — offline → loot/gold/XP accrue based on highest cleared stage, capped (~8–12h).
8. **Save/load.**

No gacha, no summoning. Heroes 2–4 unlock through progression for now (gacha slots in later — §9).

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
  Models.cs        heroes, items, stats, save state
  Rng.cs           mulberry32 + WeightedPick  (loot now, gacha later)
  GameConfig.cs    Default() content: heroes/items/affixes/monsters/stages/balance
  Save.cs          NewGame / Migrate (versioned)
  Party.cs         SetPartySlot, AcquireHero (the gacha plug point)
  Stats.cs         ComputeHeroStats / ComputePartyPower
  Combat.cs        InitCombat / StepCombat / RunToEnd (deterministic auto-battle)
  CombatModels.cs  transient CombatState / CombatEntity / CombatEvent
unity/Assets/Game/                 MonoBehaviours (read-only client)
  Bootstrap.cs     builds the scene in code on Play
  CombatView.cs    drives + visualizes the auto-battle (M1)
gamecore/GameCore.Tests/           xUnit, compiles Assets/GameCore + Persistence
gamecore/Adapters/Persistence.cs   System.Text.Json (the one host-specific adapter)
```

---

## 4. Data model

**Hero (owned instance):** `id, defId, level, xp, equipped{slot→itemId}, skillLoadout[]`

**Hero definition (static config):** `defId, name, class, role (Melee/Ranged/Support), baseStats, growthPerLevel, skills[], sprite hint`

**Item (dropped/owned):** `id, baseId, rarity (Normal/Magic/Rare/Unique), itemLevel, affixes[{stat, value}]`

**Item base (config):** `baseId, slot (weapon/helm/chest/…), baseStats, allowedAffixes`

**Affix (config):** `stat, weight, valuePerItemLevel range, rarityFloor`

**Stats** — keep small at first: `HP, ATK, DEF, SPD, CRIT%, CRITDMG`. Resist adding 15 stats day one.

**Currencies:** a map (`gold` today; `gems`/`tickets`/`shards` are additive later).

**Progression:** `highestStage, currentStage, accountLevel, lastClaimAt`.

---

## 5. The two systems to get mathematically right

### 5.1 Idle reward formula
Tie income to highest cleared stage so progression *feels* like it raises your rate:
```
goldPerSec(tier)       = base * growth^tier
xpPerSec(tier)         = ...
lootRollsPerHour(tier) = ...     // offline still rolls real loot
cap                    = 8–12 hours
offlineRate            = 100% (or 70–80% to nudge active play)
```
Add a "quick run" button later: grants e.g. 2h of yield instantly on a daily cooldown.

### 5.2 Loot rarity + affixes (the dopamine engine)

| Rarity | Drop weight | # affixes |
|--------|-------------|-----------|
| Normal (white) | high | 0 |
| Magic (blue) | medium | 1–2 |
| Rare (yellow) | low | 3–5 |
| Unique (orange) | very low | fixed special affixes |

- Drop rates and affix values **scale with stage / item level**.
- Affixes rolled from a weighted pool with value ranges → near-infinite item variety.
- **Loot filter** (later) so high tiers don't drown the player in whites.
- Implement as a **seeded, testable pure function**; sim 10,000+ drops to verify rates.
- This weighted-roll engine (`Rng.WeightedPick`) is reused verbatim for gacha later.

---

## 6. Depth roadmap (post-MVP, roughly by value)

1. **Skills / build depth** — skill gems (PoE) or rune-modified skills (D3) so heroes aren't stat sticks.
2. **More gear slots + set bonuses** — bigger build space.
3. **Crafting / currency (PoE-style)** — reroll/upgrade affixes; major gold/currency sink.
4. **Endless stage scaling + "deepest stage"** — the long-term chase.
5. **Loot filter** — QoL that becomes essential at high tiers.
6. **Boss mechanics & difficulty walls** — force build optimization, not just push.
7. **Daily/weekly quests + login rewards** — retention backbone.
8. **Auto-progression / auto-push.**
9. **Unlock heroes 2–4** through progression milestones (pre-gacha).
10. **Prestige / "rebirth"** — reset for permanent multiplier; design economy with it in mind.
11. **Account level, achievements, codex/collection.**
12. *(Later)* **Gacha layer** (see §9). *(Much later)* Arena/PvP, guilds, events.

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

## 8. Milestone order (v1)

Status is tracked in [`../CLAUDE.md`](../CLAUDE.md). Order:

| Milestone | Deliverable |
|-----------|-------------|
| **M0 – Skeleton** | Scene + camera, render the party + dummy monsters as placeholders. |
| **M1 – Auto-combat** | Deterministic auto-target/attack, kill packs, clear a zone + boss. |
| **M2 – Loot** | Drops with rarity + affixes, auto-pickup, inventory, equip → stats recompute. |
| **M3 – Stages** | Difficulty stages; higher stage = tougher monsters + better loot. |
| **M4 – Idle** | `lastClaimAt` math + offline loot/gold/xp + claim modal. |
| **M5 – Persistence** | Save/load (local now; server-authoritative later). |
| **M6 – Feel pass** | Number formatting, loot juice, item-compare UI, offline modal. |
| **→ then** | Pick from §6 (skills/build depth first), then the live-service arc (§9). |

---

## 9. Scaling later — gacha + global live-service

### 9.1 Keeping the gacha door open (even though it's on hold)
- **Heroes are already data-driven instances** owned by the player → "acquire a hero"
  is a swappable source. `Party.AcquireHero` is the single plug point; v1 acquires via
  progression, gacha later just becomes another path that calls the same function.
- **The weighted-roll engine (`Rng.WeightedPick`) built for loot is the same engine
  gacha uses** — rates, pity, rarity tiers sit on top of it.
- **Currencies are a map** → adding `gems` / `tickets` / `shards` is additive.

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
   statelessly — a modest VPS hosts the first version.
5. **Global chat:** its own real-time service (WebSocket gateway + pub/sub). It never
   touches `GameCore` — keep it separate.
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
- **M2 (loot):** drops + rarity + affixes + inventory + equip → stat recompute.
- Draft the balance numbers in `GameConfig` — economy constants + idle/loot/affix formulas.
- Build a small console/test harness to sim 10k+ drops and verify rates (§5.2).
