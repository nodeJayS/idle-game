# Idle ARPG — Game Plan

> A Diablo 3 / Path of Exile–style **idle ARPG**: a party auto-clears dungeons, monsters drop loot, you build out gear/skills and push higher difficulty.
> Scope: personal/for-fun **web** game first, architected to scale to a global **mobile** release later.
> Stack today: Vite + React + TypeScript + Supabase.

---

## 0. Project decisions (locked)

| Decision | Choice |
|----------|--------|
| Genre / feel | **Idle ARPG** — Diablo 3 / PoE style, loot-and-build driven |
| Initial scope | Personal / for-fun **web** game |
| State storage (v1) | **Supabase** (cloud saves, accounts, cross-device) |
| Party | **4 hero slots**; player starts with **1 basic Warrior**, 3 empty |
| Map / movement | Party **auto-navigates** isometric dungeons, auto-fights, auto-loots |
| Art direction | **Cute/cozy pixel art + PoE-depth systems** ("Minecraft-friendly surface, PoE grind underneath" — cf. Loop Hero, Core Keeper). Low-res sprites = easy to draw, scale, and run on weak phones. |
| Render style | **2.5D isometric sprites (PixiJS)** with nearest-neighbor pixel scaling; logic stays renderer-agnostic so **3D (react-three-fiber)** is possible later |
| **Gacha** | **ON HOLD** — not in v1. Architecture must scale to it later (see §9). |
| Long-term goal | Scale to **global app-store release** without a rewrite |

---

## 1. The core loop (loot-driven)

The reward loop **is the loot** — no gacha needed to make it satisfying:

**Party auto-clears a dungeon → monsters drop gear → equip/compare upgrades → build gets stronger → push higher monster density & difficulty → better loot drops → repeat.**

Three nested loops:

| Loop | Time scale | What the player does | The hook |
|------|-----------|---------------------|----------|
| **Moment-to-moment** | seconds | Watch the party clear packs, loot drops, numbers pop | kill + loot dopamine |
| **Session** | 2–10 min | Claim idle loot/XP, equip upgrades, respec/level skills, push a higher rift tier | build gets visibly stronger |
| **Meta / long-term** | days–weeks | Chase Unique/rare affixes, complete builds, climb endless difficulty | loot grind + power fantasy |

If this feels good with placeholder sprites and grey/blue/yellow item rectangles, you've won. Build the loop before any art.

---

## 2. MVP — the vertical slice (build this and nothing more in v1)

1. **Isometric dungeon** — a small 2.5D iso zone the party walks through.
2. **Auto-combat** — 4-slot party (1 Warrior to start), auto-target nearest monsters, auto-attack/auto-skill on cooldown.
3. **Monster packs + a boss** — clear the zone, boss at the end.
4. **Loot drops** — monsters drop gear with rarity + random affixes; auto-pickup.
5. **Equip & stats** — equip gear on a hero; stats recompute; party gets stronger.
6. **Rift/difficulty tiers** — pick a difficulty; higher tier = tougher monsters + better loot. This is the progression spine.
7. **Idle accumulation** — offline → loot/gold/XP accrue based on highest cleared rift tier, capped (~8–12h).
8. **Save/load** to Supabase.

No gacha, no summoning. Heroes 2–4 can be unlocked through progression for now (gacha slots in later — §9).

---

## 3. Tech architecture (v1, web)

- **Game state**: single normalized store via **Zustand**. One `gameState`:
  `{ heroes, equippedGear, inventory, currencies, riftProgress, lastClaimAt }`.
- **`game-core` (engine-agnostic, pure TS)**: ALL combat, loot rolls, affix generation, idle math, progression. Zero UI/renderer/framework imports. This is the single most important rule — it's what keeps the renderer swappable (Pixi→3D) and the gacha path open.
- **Renderer reads from state only**: PixiJS draws the iso scene, sprites, loot beams, damage numbers — it never owns game logic. Swapping to react-three-fiber later means rewriting the renderer, not the game.
- **Combat**: deterministic, decoupled from React render. Don't drive combat through React state at 60fps. Compute combat outcomes/ticks in `game-core`; the renderer animates the result.
- **Idle = math, not a running timer.** On load: `elapsed = min(now - lastClaimAt, cap)` → rewards. Never simulate hours of real time.
- **Loot/affixes = seeded weighted rolls.** Build a reusable weighted-random module. (This is the *same* engine gacha will use later — see §9. Build once.)
- **Content as data**: monsters, affixes, item bases, skills, rift tiers in config (TS/JSON or Supabase tables). Logic reads config → tune without touching logic.
- **Supabase**: one `saves` row per user (JSONB blob fine for v1). Row-Level Security so a user only reads/writes their own save. Save on debounce + key events. Client-authoritative is fine for v1.

### Suggested structure (v1)
```
src/
  game/
    config/      monsters.ts, itemBases.ts, affixes.ts, skills.ts, rifts.ts, balance.ts
    systems/     combat.ts, loot.ts, affixRoll.ts, idle.ts, progression.ts, stats.ts
    state/       store.ts (zustand), selectors.ts
    sim/         tick.ts (fixed-timestep loop)
    rng/         weightedRoll.ts  // shared by loot now, gacha later
  render/        pixi/ (iso scene, sprites, fx)  // renderer-agnostic boundary
  components/    HUD, PartyBar, InventoryScreen, GearCompare, RiftSelect, IdleClaimModal
  lib/           supabase.ts, save.ts
```

---

## 4. Data model

**Hero (owned instance):**
`id, defId, level, xp, equippedGear{slot→itemId}, skillLoadout[], skillLevels`

**Hero definition (static config):**
`defId, name, class (Warrior/…), role (Melee/Ranged/Support), baseStats, growthPerLevel, skills[]`

**Item (dropped/owned):**
`id, baseId, rarity (Normal/Magic/Rare/Unique), itemLevel, affixes[{stat, value}], slot`

**Item base (config):** `baseId, slot (weapon/helm/chest/…), baseStats, allowedAffixPool`

**Affix (config):** `stat, weight, valueRangeByItemLevel, rarity tier`

**Stats** — keep small at first: `HP, ATK, DEF, SPD, CRIT%, CRITDMG`. Resist adding 15 stats day one.

**Currencies:** `gold` (sink for leveling/crafting). Add PoE-style crafting currency later.

**Progression:** `highestRiftCleared, currentRift, lastClaimAt`.

---

## 5. The two systems to get mathematically right

### 5.1 Idle reward formula
Tie income to highest cleared rift tier so progression *feels* like it raises your rate:
```
goldPerSec(tier)     = base * growth^tier
xpPerSec(tier)       = ...
lootRollsPerHour(tier) = ...     // offline still rolls real loot
cap                  = 8–12 hours
offlineRate          = 100% (or 70–80% to nudge active play)
```
Add a "quick run" button later: grants e.g. 2h of yield instantly on a daily cooldown.

### 5.2 Loot rarity + affixes (the dopamine engine)
Example rarity tiers:

| Rarity | Drop weight | # affixes |
|--------|-------------|-----------|
| Normal (white) | high | 0 |
| Magic (blue) | medium | 1–2 |
| Rare (yellow) | low | 3–5 |
| Unique (orange) | very low | fixed special affixes |

- Drop rates and affix values **scale with rift tier / item level**.
- Affixes rolled from a weighted pool with value ranges → near-infinite item variety.
- **Loot filter** (later) so high tiers don't drown the player in whites.
- Implement as a **seeded, testable pure function**; sim 10,000+ drops to verify rates match intent.
- This weighted-roll engine is reused verbatim for gacha later.

---

## 6. Depth roadmap (post-MVP, roughly by value)

1. **Skills / build depth** — skill gems (PoE) or rune-modified skills (D3) so heroes aren't stat sticks.
2. **More gear slots + set bonuses** — bigger build space.
3. **Crafting / currency (PoE-style)** — reroll/upgrade affixes; major gold/currency sink.
4. **Endless rift scaling + leaderboards-style "deepest tier"** — the long-term chase.
5. **Loot filter** — quality-of-life that becomes essential at high tiers.
6. **Boss mechanics & difficulty walls** — force build optimization, not just push.
7. **Daily/weekly quests + login rewards** — retention backbone.
8. **Auto-progression / auto-push** — party keeps clearing while you watch.
9. **Unlock heroes 2–4** through progression milestones (pre-gacha).
10. **Prestige / "rebirth"** — reset for permanent multiplier; design economy with it in mind.
11. **Account level, achievements, codex/collection.**
12. *(Later)* **Gacha layer** — summon heroes (see §9). *(Much later)* Arena/PvP, guilds, events.

---

## 7. Suggestions to make it actually good

- **Build a balance sim before tuning by feel.** Simulate "player reaches rift tier N at time T with this gear." Idle games are ~80% economy tuning.
- **Make the first 10 minutes generous** — fast early upgrades, frequent drops. Hook first, gate later.
- **Number formatting from day one** — `1.2K / 3.4M / 5.6B…`.
- **Offline-return moment** — the "while you were away" modal with loot summary + count-up is the emotional core.
- **Juice** — loot beams, item-rarity colors/sounds, damage numbers, crit flashes, screen shake. Combat is math but must *feel* alive.
- **Item comparison UI** — equip/compare must be instant and obvious (green up / red down arrows). This is the moment-to-moment of an ARPG.
- **Separate intended config from live tuning** — all constants in `balance.ts`.
- **Test loot statistically**, not anecdotally (§5.2).

---

## 8. Milestone order (v1)

| Milestone | Deliverable |
|-----------|-------------|
| **M0 – Skeleton** | Zustand store, config, render a static iso scene with 1 hero + dummy monsters. |
| **M1 – Auto-combat** | Deterministic auto-target/attack, kill packs, clear a zone + boss. Placeholder sprites. |
| **M2 – Loot** | Drops with rarity + affixes, auto-pickup, inventory, equip → stats recompute. |
| **M3 – Rifts** | Difficulty tiers; higher tier = tougher monsters + better loot. Progression spine. |
| **M4 – Idle** | `lastClaimAt` math + offline loot/gold/xp + claim modal. |
| **M5 – Persistence** | Supabase save/load with RLS. |
| **M6 – Feel pass** | Number formatting, loot juice, item-compare UI, offline modal animation. |
| **→ then** | Pick from §6 (skills/build depth first). |

---

## 9. Scaling later — gacha + global mobile

### 9.1 Keeping the gacha door open (even though it's on hold)
- **Heroes are already data-driven instances** owned by the player → "acquire a hero" is a swappable source. v1 acquires via progression; gacha later just becomes another acquisition path. No model change needed.
- **The weighted-roll engine (`rng/weightedRoll.ts`) built for loot is the same engine gacha uses** — rates, pity, rarity tiers all sit on top of it.
- **Currencies are a map** → adding `gems`, `summon tickets`, `shards` later is additive.
- Net: gacha is a feature you add *on top*, not a refactor.

### 9.2 The one decision that matters most for scaling
**Keep ALL game logic in `game-core` (pure TS, no UI/renderer deps).** This lets the web client, a future mobile client, and a future authoritative server all import the same simulation. Renderer choice (Pixi ↔ 3D) and gacha both ride on this.

### 9.3 Render path
- v1: **2.5D isometric sprites in PixiJS** (Diablo 1/2 look). Cheap art, light on mobile, fast to build.
- Later (optional): **react-three-fiber** for a modern D3/PoE 3D-isometric look — stays in React/TS, but heavier art pipeline + mobile cost. Only worth it if visuals become the bottleneck. Because logic is in `game-core`, this is a renderer rewrite, not a game rewrite.

### 9.4 Mobile + global path (additive, not a rewrite)
1. **Now:** Vite + React + Pixi + `game-core` + Supabase client saves. Prove the loop is fun.
2. **When fun:** monorepo (`game-core`, `web`, `server`); move loot/idle/currency computation to a server endpoint importing `game-core` → server-authoritative.
3. **App stores:** add **React Native + Expo** client importing the same `game-core`; add IAP + store auth. (Pixi runs on RN; sprites stay light.)
4. **Real traffic:** Redis (hot state/leaderboards), CDN content delivery, remote config/LiveOps, multi-region; consider game-BaaS (**Nakama** / **PlayFab**).

### 9.5 Non-negotiables once it's a real product (bake hooks in early)
- **Server time authority** for idle accrual (don't trust device clock).
- **Server-side RNG** for loot *and* gacha — anti-cheat + (for gacha) legally-mandated provable/disclosed rates (Japan, China, South Korea; loot boxes restricted in Belgium/Netherlands).
- **Remote config / LiveOps** — push rifts/events/balance without app updates.
- **IAP** via Apple/Google billing + server-side receipt validation.
- **Store-compliant auth** — Sign in with Apple + Google + guest linking.
- **i18n from the start**; **analytics + crash reporting**; **cloud save / cross-device**.

---

## 10. Open next steps
- **Sketch the v1 `game-core` API + state shape** (party, iso map, auto-combat, loot/affix roll) — recommended next; it's the foundation.
- Draft `balance.ts` — economy constants + idle/loot/affix formulas.
- Design the Supabase schema + RLS.
- Detail M0/M1: iso grid + auto-combat design.
