# Roadmap — the ONE "what's next" doc (updated 2026-07-02)

The living priority list. When something ships, update this file in the same
commit. Durable design (loops, economy, data model, live-service arc) stays in
[`game-design.md`](game-design.md); session orientation in [`../CLAUDE.md`](../CLAUDE.md);
finished plans live in git history, not in the tree.

## Where the game stands (one paragraph)

Core loop ✅ (farm ladder, loot/affixes/imprints, modifiers, Tower, quests,
achievements, daily login/gems). Build depth ✅ (2+2 kits, per-hero levels,
gamble economy). **MS2 asset pipeline ✅ and proven end-to-end**: a new hero =
one manifest + 9 decoded clips + one bake (both genders, dyeable gear via
manifest tints, per-hero animators/sounds). Roster on the **archetype backbone**
(Warrior/Rogue/Magician stat templates; class = overrides): Knight, Fire Mage,
Assassin, Priest (party heal-over-time) — Ice Mage shelved for a comeback.
Future classes slot in as archetype + overrides: Brawler/Swordsman (Warrior),
Ninja/Archer (Rogue), Summoner/Ice Mage (Magician). 423 GameCore tests green.

## Priorities, in order

### 0. ✅ SHIPPED 2026-07-02: gear enhancement + 5-slot trim + bag sort
Enhancement (+15, 5% base/level, risk bands, scrap sink), slots cut 9→5,
Sort button, cleaner equip doll. Next engagement beats when wanted: zones/
MS2 monsters (item 4 below), set bonuses, a daily-attempts world boss.

### 1. ⭐ Gem sink → hero gacha MVP (Lever 4, the strategic one)
Gems accrue from daily logins with NO spend since 2026-07-02 — the economy's
promise is unredeemed. Everything is staged for this: heroes are config rows
(2+2 template), `Party.AcquireHero` is the documented acquisition plug point,
and the pipeline makes banner heroes cheap to produce. Slices:
1. GameCore: `Gacha.Roll` reducer — gem cost, seeded RNG via the persisted
   cursor (can't re-roll), duplicate → hero XP or scrap, pity counter in save.
2. UI: a banner panel (control bar) + reveal beat (rarity flash, feed line).
3. Content: 1–2 banner heroes. **Candidate #1: the Ice Mage comeback** as the
   launch banner. Def + kit already in config (actives frostbolt L1 + blizzard
   L10). Presentation recon (verified 2026-07-02): female wizard clips cover a
   full ice kit (`wizard_icestrike/frostnova/icebomb_01/icesphere` + the
   standard locomotion set); staffs `15200037_snowgiantstaff` /
   `15200208_snowqueenstaff`; hats `11300181_cpsnowbell` /
   `11300185_cpsnowgianthat`; any robe can be dyed ice-blue via manifest
   `tints`. Sounds: `Skill_Wizard_FrostNova_Cast`, `Skill_Wizard_IceStrike_*`,
   `Skill_Magician_IceBreath_Cast`.

### 2. Tower slice 3 — per-floor reward bundles (small, unblocks a loop)
The one unfinished system slice. Floors currently pay only via milestones;
per-floor bundles make pushing feel rewarded run-to-run. GameCore-first,
sim-testable, ~1 session.

### 3. Combat presentation debt (polish the new roster)
- Per-hero impact sounds: `PlayImpact` hardcodes `Hit_SwordDefault` for every
  hit in the game (same fix pattern as the shipped per-hero attack sounds).
- Sanctify needs a heal visual (golden ground ring / sparkle on buffed heroes)
  and Holy Smite a cast flourish — both are `_skillFx` ADD-ON POINT entries.
- Hero-float quirk backlog item: CombatView.SyncViews writes v.Height into
  hero Y for capsules (pre-existing; check it still matters).

### 4. Zones + LOW-POLY monster variety (the "depth feels like travel" beat)
**Art-direction rule (user, 2026-07-02): monsters stay low-poly faceted, Tunic
style — the MS2 pipeline is for HEROES ONLY.** The smooth-chibi-vs-faceted-world
contrast IS the look; never port MS2 mobs. Every ~10 stages becomes a themed
ZONE. Pairs with set bonuses later. This is the "where do I park my farm
tonight" decision engine. Slices:
1. ✅ SHIPPED 2026-07-02 — GameCore zone backbone: `ZoneDef` (roster + boss +
   engine-free palette/prop hints), one zone per 10-stage tier, 10 themed zones
   (Verdant Woods → … → Crown of the World), 27 new monster defs in the
   slime/goblin stat band (flavor, not power), zone-driven trash/boss spawns
   (farm, encounter, Tower floors travel the same zones; legacy fallback kept).
2. Client reskin: faceted ground palette/prop swap from the ZoneDef hints
   (forest → ruins → swamp...).
3. Monster art: 2–3 zone-specific scripted-Blender designs per zone (the
   `art/<name>.py` low-poly pipeline), rolled out zone by zone.
4. Optional zone-flavored drop tables so farming has destinations ("boots drop
   best in the ruins").

### 5. Content & tuning pass (after 4)
More stages/mods/monster kits; balance sim in console (backlogged); XP-curve
check at the new roster size.

### 6. Later / parked
- Prestige/rebirth + manual achievement-claim UX (Lever 4 leftovers).
- Real-money gem purchase (needs the gacha proven fun first).
- Server authority arc (design §9) — GameCore stays pure for exactly this.
- Xml.m2d item-table extraction (manifests by item id; likely explains the
  odd hat socket conventions) — do when the roster/wardrobe grows.
- Ice Mage full kit pass if not used as gacha banner (#1).

## Standing rules (short version — CLAUDE.md has the full set)
GameCore-first, one verified slice per commit. No MS2 music (SFX only). No MS2
skill names/numbers (2+2 template is ours). Raw extracts stay outside the repo.
Back up save.json around Play verification.
