# Roadmap — the ONE "what's next" doc (updated 2026-07-03)

Living priority list; update in the same commit that ships an item. Durable
design → [`game-design.md`](game-design.md); session orientation →
[`../CLAUDE.md`](../CLAUDE.md). Shipped details live in git history — entries
here get ONE receipt line when done, then get pruned next pass.

## Where the game stands

Core loop, build depth, zones (10 themed, 30 faceted monsters), Tower,
achievements, daily gems: all ✅. MS2 hero pipeline proven (manifest + 9 clips
+ 1 bake per hero). Roster: Knight / Fire Mage / Assassin / Priest on the
archetype backbone (Warrior/Rogue/Magician templates; class = overrides); Ice
Mage shelved for the gacha banner. Diorama look shipped (ortho 45°, split-tone
grade). 434 GameCore tests green. No mana — skills are cooldown-only.

## Priorities, in order

### 1. Combat-feel + QoL batch (user 2026-07-03) — ✅ 6/6 shipped
Receipts: mana removed (StatKey pinned ints, SaveVersion 2) · hit sync
(swing↔number 2ms, splash sounds deduped/quieter) · auto-equip damage-first
across all fielded heroes (badge and auto-equip share one call) · Salvage all
+ Item.Locked (arm/confirm button; all salvage paths refuse locked) · sound
sliders (PlayerPrefs volMaster/volSfx) · bigger characters (ortho factor
0.18 + dead-zone camera: 2.5u radius, capped glide, look-ahead; play-tested
by user).

### 2. Monster procedural animation
Rigid faceted monsters animate by CODE, no rigs (heroes keep MS2 clips):
per-family gait (slime hop / wolf stride / wisp float / heavy stomp), attack
telegraph + lunge on the sim attack event, hit flash + squash, death topple
or shrink-poof. One client-only MonsterAnimator reading sim state; zero
GameCore changes.

### 3. ⭐ Gem sink → hero gacha MVP (the strategic lever)
Gems accrue with NO spend — the economy's promise is unredeemed; don't let
anything else jump this again. Slices:
1. GameCore `Gacha.Roll`: gem cost, persisted-cursor RNG (no re-roll),
   dupe → hero XP/scrap, pity counter in save.
2. UI: banner panel + reveal beat (rarity flash, feed line).
3. Content: Ice Mage comeback banner. Def+kit already in config (frostbolt
   L1 / blizzard L10). Assets verified 2026-07-02: female wizard ice clips
   (`wizard_icestrike/frostnova/icebomb_01/icesphere`), staffs
   `15200037_snowgiantstaff`/`15200208_snowqueenstaff`, hats
   `11300181_cpsnowbell`/`11300185_cpsnowgianthat`, robes dyeable via
   manifest tints; sounds `Skill_Wizard_FrostNova_Cast`,
   `Skill_Wizard_IceStrike_*`, `Skill_Magician_IceBreath_Cast`.

### 4. Tower slice 3 — per-floor reward bundles
The one unfinished system slice; floors pay only via milestones today.
GameCore-first, sim-testable, ~1 session.

### 5. Combat presentation debt
- Per-hero impact sounds (`PlayImpact` hardcodes `Hit_SwordDefault`; same
  pattern as the shipped per-hero attack sounds).
- Sanctify heal visual + Holy Smite cast flourish (`_skillFx` add-on points).
- Check hero-float quirk (SyncViews writes v.Height into hero Y for capsules).

### 6. Terraced terrain + water (the big remaining Tunic-look gap — sim-gated)
Tunic reads as PLACES: terraces, cliff strata, stairs, water — ours is an
infinite plane, and painted ground-wear fails without structure (tried
2026-07-02, reverted). Slices: (1) GameCore per-stage arena layout (walkable
region + height tiers as data, movement clamps); (2) client terraces/cliffs
(TunicSurface side colour = free strata), water + shore ink, stairs;
(3) per-zone water/lava/void flavor + camera composition pass.

### 7. Content & tuning pass
More stages/mods/kits; balance sim in console; XP curve at roster size.
Caster pacing lever if ever needed: per-skill CooldownMs (mana removal
changed nothing observable).

### 8. Later / parked
Prestige/rebirth · manual achievement-claim UX · real-money gems (after
gacha proves fun) · server authority (design §9) · Xml.m2d item-table
extraction (when wardrobe grows) · zone drop-table hint in stage picker.

## Standing rules (short — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. Monsters faceted only (MS2 =
heroes only). No MS2 music (SFX only); skill names/numbers ours. Raw extracts
outside the repo. Back up save.json around Play verification. Agents don't
touch the Unity editor without a user-approved window.
