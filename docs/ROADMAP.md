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
Ninja/Archer (Rogue), Summoner/Ice Mage (Magician). 426 GameCore tests green.

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
2. ✅ SHIPPED 2026-07-02 — client reskin: `ZoneDress.Sync` retints the faceted
   ground + scattered props from the ZoneDef hints on stage/floor change (zone 1
   = the shipped palette exactly), plus a "Now entering <Zone>" feed beat.
   Verified in Play across woods/ruins/swamp. Prop SHAPE swaps ride slice 3.
   ✅ PLUS (same day): hand-drawn ground detail — `GroundDetail` paints tileable
   brightness maps in code (grass strokes / chunky stone pavers / sand ripples /
   snow speckle / cooled cracks per PropSet), world-XZ projected by TunicSurface's
   new `_DetailTex` on up-faces only. CHUNKY marks on purpose: fine ones mip away
   at gameplay distance.
3. ✅ SHIPPED 2026-07-02 — zone drop tables: each zone (except the intro) favors
   one equip slot ×3 in the drop base pick (`ZoneDef.FavoredSlot` →
   `LootContext`; boots DO drop best in the ruins). Every slot has a favoring
   zone in both halves of the ladder. UI surfacing (stage picker hint) pending.
4. Monster art (`art/monsters.py`, one FBX per monster →
   `Resources/Models/monsters/`, `MonsterModel` + SpawnView wiring with
   primitive fallback; rank/mod tells = gentle tint + faint glow, NOT the
   primitives' flat repaint). ✅ Zone 2 shipped 2026-07-02 (Bone Rattler /
   Stone Sentry / Grave Knight, verified in Play incl. boss + facing).
   ✅ Zone 3 shipped 2026-07-02 (Bog Toad / Marsh Wisp / Bog Horror; `rock()`
   grew a taper param for teardrop/flame silhouettes). ✅ Zone 4 shipped
   2026-07-02 (Dust Scarab / hooded Dune Stalker / Dune Wurm burst-worm boss).
   ✅ Zone 5 shipped 2026-07-02 (Ice Sprite / Frost Wolf — first quadruped /
   Glacier Golem cyclops). ✅ Zone 6 shipped 2026-07-02 (Magma Imp / Cinder
   Hound — lava-mane wolf chassis / Ash Tyrant demon-lord). ✅ Zone 7 shipped
   2026-07-02 (Cave Bat / Gloom Shade wraith / Nightmare Maw void-head;
   MonsterModel.Tint now scales aura emission by material luma so dark
   palettes don't wash to the mod colour). ✅ Zone 8 shipped 2026-07-02 (Tide
   Crab / Storm Caller cloud elemental / Tempest Naga with storm trident).
   ✅ Zone 9 shipped 2026-07-02 (Void Wisp / Rune Construct — floating block
   golem / Riftwalker with a broken astral halo). ✅ Zone 10 shipped 2026-07-02
   (Crown Seraph / Chaos Spawn / World Ender crowned colossus). ✅ Zone 1
   shipped 2026-07-02 (Slime / Goblin / crowned Goblin King — the classics,
   upgraded from capsules). ALL TEN zone rosters are modeled — 30 monsters.
5. ✅ SHIPPED 2026-07-02 — per-zone PROP SHAPES: Scenery rebuilds the scatter
   per ZoneDef.PropSet with its own faceted family (ruins: broken columns/
   gravestones/rubble · desert: cacti/sandstone spires · tundra: snow pines/
   ice shards · volcano: basalt clusters · cavern: stalagmites/glow-shrooms ·
   coast: driftwood/beach tufts · astral: rune obelisks/void shards · summit:
   marble columns/gold crystals), palettes baked per zone, layouts stable per
   set. ITEM 4 IS COMPLETE.

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
