using System;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Zones (roadmap item 4): themed ~10-stage bands, each with its own trash roster,
    // boss, and client palette hints. These pin the config contract (every referenced
    // monster exists, bands map correctly) and the spawn plumbing (farm/encounter/tower
    // trash comes from the stage's zone; legacy configs without zones still work).
    public class ZoneTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static HeroInstance[] Party() =>
            new[] { new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 } };

        [Fact]
        public void ZoneForStageMapsTenStageBandsAndClamps()
        {
            Assert.Equal(10, Cfg.Zones.Count); // one per 10-stage tier across the 100-stage ladder

            Assert.Same(Cfg.Zones[0], Cfg.ZoneForStage(1));
            Assert.Same(Cfg.Zones[0], Cfg.ZoneForStage(10));   // band edge stays in zone 1
            Assert.Same(Cfg.Zones[1], Cfg.ZoneForStage(11));   // next band
            Assert.Same(Cfg.Zones[9], Cfg.ZoneForStage(100));  // last band
            Assert.Same(Cfg.Zones[9], Cfg.ZoneForStage(250));  // past the table clamps to the last zone
        }

        [Fact]
        public void ZoneForStageIsNullWithoutZones()
        {
            var cfg = GameConfig.Default();
            cfg.Zones.Clear();
            Assert.Null(cfg.ZoneForStage(1));
        }

        [Fact]
        public void EveryZoneReferencesRealMonstersInTheRightBands()
        {
            foreach (var zone in Cfg.Zones)
            {
                Assert.NotEmpty(zone.TrashMonsters);
                foreach (var id in zone.TrashMonsters)
                {
                    Assert.True(Cfg.Monsters.ContainsKey(id), $"{zone.Id}: unknown trash '{id}'");
                    Assert.Equal("common", Cfg.Monsters[id].LootTableId); // trash pay band
                }
                Assert.True(Cfg.Monsters.ContainsKey(zone.BossId), $"{zone.Id}: unknown boss '{zone.BossId}'");
                Assert.Equal("boss", Cfg.Monsters[zone.BossId].LootTableId); // boss bundles key off this
            }
        }

        [Fact]
        public void FirstZoneKeepsTheOriginalEarlyGame()
        {
            var z = Cfg.ZoneForStage(1)!;
            Assert.Equal(new[] { "slime", "goblin" }, z.TrashMonsters);
            Assert.Equal("goblin_king", z.BossId);
        }

        [Fact]
        public void StageBossesComeFromTheirZone()
        {
            foreach (var rt in Cfg.Stages)
                Assert.Equal(Cfg.ZoneForStage(rt.Stage)!.BossId, rt.BossId);
        }

        [Fact]
        public void FarmTrashSpawnsFromTheStagesZoneRoster()
        {
            var zone = Cfg.ZoneForStage(15)!;
            var s = Combat.InitFarm(Party(), 15, Cfg, new Rng(7));

            var trash = s.Entities.Where(e => e.Team == Team.Enemy).ToList();
            Assert.NotEmpty(trash);
            foreach (var e in trash) Assert.Contains(e.RefId, zone.TrashMonsters);
            // the roster is CYCLED, so a full batch fields every member, not just one
            foreach (var id in zone.TrashMonsters) Assert.Contains(trash, e => e.RefId == id);
        }

        [Fact]
        public void EncounterTrashAndBossFollowTheZone()
        {
            var zone = Cfg.ZoneForStage(25)!;
            var s = Combat.InitCombat(Party(), 25, Cfg, new Rng(1));

            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy && !e.IsBoss))
                Assert.Contains(e.RefId, zone.TrashMonsters);
            Assert.Equal(zone.BossId, s.Entities.Single(e => e.IsBoss).RefId);
        }

        [Fact]
        public void TowerFloorsTravelTheZones()
        {
            var s = Combat.InitFarm(Party(), 1, Cfg, new Rng(3));

            Combat.EnterTower(s, 11, Cfg, new Rng(3)); // floors 11-20 = zone 2
            var zone = Cfg.ZoneForStage(11)!;
            foreach (var e in s.Entities.Where(e => e.Team == Team.Enemy && !e.IsBoss))
                Assert.Contains(e.RefId, zone.TrashMonsters);

            // milestone guardian = the floor's zone boss
            Combat.EnterTower(s, 20, Cfg, new Rng(3));
            Assert.Equal(Cfg.ZoneForStage(20)!.BossId, s.Entities.Single(e => e.IsBoss).RefId);
        }

        // --- Zone drop tables ("boots drop best in the ruins") ---

        [Fact]
        public void ForStageCarriesTheZonesFavoredSlot()
        {
            // zone 1 (intro) is uniform; zone 2 (Ruined Courtyard) favors Boots
            Assert.Null(LootContext.ForStage(Cfg.Stages.First(s => s.Stage == 5), Cfg).FavoredSlot);
            Assert.Equal(EquipSlot.Boots,
                LootContext.ForStage(Cfg.Stages.First(s => s.Stage == 15), Cfg).FavoredSlot);
        }

        [Fact]
        public void EveryActiveSlotHasAFavoringZone()
        {
            foreach (var slot in EquipSlots.Active)
                Assert.Contains(Cfg.Zones, z => z.FavoredSlot == slot);
        }

        [Fact]
        public void FavoredSlotSkewsTheDropBasePick()
        {
            int BootCount(int stage, uint seed)
            {
                var ctx = LootContext.ForStage(Cfg.Stages.First(s => s.Stage == stage), Cfg);
                var rng = new Rng(seed);
                int boots = 0;
                for (int i = 0; i < 1000; i++)
                {
                    var item = Loot.RollContextItem(rng, ctx, Cfg);
                    if (Cfg.ItemBases[item.BaseId].Slot == EquipSlot.Boots) boots++;
                }
                return boots;
            }

            int uniform = BootCount(5, 42);  // woods: ~1/5 of drops
            int favored = BootCount(15, 42); // ruins: ~3/7 of drops
            Assert.True(favored > uniform * 3 / 2,
                $"ruins boots {favored} not clearly above woods boots {uniform}");
        }

        // --- SDF blob-shell family (ROADMAP 4, slice 3): the slime/shambler/spirit trio JOINS
        // the swamp roster. Pins the three new ids exist, are in the swamp roster (alongside the
        // originals), pay the trash band, and that fen_spirit is genuinely RANGED (AttackRange
        // above Combat's melee cutoff — that's what makes the client fire a bolt, not a swing).
        [Fact]
        public void SwampBlobFamilyJoinsTheRoster()
        {
            var swamp = Cfg.Zones.Single(z => z.Id == "murkwater_swamp");

            // originals stay — the family JOINS, it doesn't replace
            Assert.Contains("bog_toad", swamp.TrashMonsters);
            Assert.Contains("marsh_wisp", swamp.TrashMonsters);

            foreach (var id in new[] { "mire_slime", "bog_shambler", "fen_spirit" })
            {
                Assert.True(Cfg.Monsters.ContainsKey(id), $"missing monster def '{id}'");
                Assert.Equal("common", Cfg.Monsters[id].LootTableId); // trash pay band
                Assert.Contains(id, swamp.TrashMonsters);
            }

            // fen_spirit is ranged: AttackRange above Combat.MeleeRange-cutoff (> 2.0). The Trash
            // helper can't set this, so a regression that reverted it to a plain Trash() would
            // silently make it melee — this guards exactly that.
            Assert.True(Cfg.Monsters["fen_spirit"].BaseStats.Get(StatKey.AttackRange) > 2.0,
                "fen_spirit must be ranged (AttackRange > 2.0)");
            // and its neighbours are NOT ranged (they melee)
            Assert.True(Cfg.Monsters["mire_slime"].BaseStats.Get(StatKey.AttackRange) <= 2.0);
            Assert.True(Cfg.Monsters["bog_shambler"].BaseStats.Get(StatKey.AttackRange) <= 2.0);
        }

        [Fact]
        public void SpawnsFallBackWithoutZones()
        {
            var cfg = GameConfig.Default();
            cfg.Zones.Clear();

            var s = Combat.InitFarm(Party(), 15, cfg, new Rng(7));
            var ids = s.Entities.Where(e => e.Team == Team.Enemy).Select(e => e.RefId).Distinct()
                       .OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "goblin", "slime" }, ids); // legacy alternation still fields both
        }
    }
}
