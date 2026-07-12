using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// §6.2 set bonuses: membership rolls only on Rare+ drops from a tiered context, on the
    /// drop's own rng; ComputeHeroStats adds Piece2 at 2 equipped pieces and Piece4 ON TOP at
    /// 4; the generated magnitudes stay within the §6.2 balance gate (full set ≤ ~8% power).
    /// </summary>
    public class SetBonusTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        // The four distinct-slot bases a single hero can wear as one 4pc set.
        private static readonly (string baseId, EquipSlot slot)[] FourSlots =
        {
            ("rusty_sword", EquipSlot.Weapon), ("leather_cap", EquipSlot.Helm),
            ("leather_vest", EquipSlot.Chest), ("leather_gloves", EquipSlot.Gloves),
        };

        private static Item It(string id, string baseId, Rarity rarity = Rarity.Rare, string? setId = null) =>
            new Item { Id = id, BaseId = baseId, Rarity = rarity, ItemLevel = 10, SetId = setId };

        private static GameConfig CertainSets()
        {
            var cfg = GameConfig.Default();
            cfg.Balance.SetDropChance = 1.0;
            return cfg;
        }

        private static LootContext Tiered(int band) => new LootContext
        {
            ItemLevel = 10, DropRateMult = 1.0, MaxRarity = Rarity.Rare, SetBand = band,
        };

        // ---- membership roll ----

        [Fact]
        public void MembershipRollsOnlyOnRarePlus()
        {
            var cfg = CertainSets();
            var rng = new Rng(7);

            var normal = It("n", "rusty_sword", Rarity.Normal);
            Loot.RollSetMembership(rng, normal, Tiered(1), cfg);
            Assert.Null(normal.SetId);
            Assert.Equal(0, rng.Cursor); // sub-Rare must not consume a draw (sequence parity)

            var rare = It("r", "rusty_sword", Rarity.Rare);
            Loot.RollSetMembership(rng, rare, Tiered(1), cfg);
            Assert.NotNull(rare.SetId);
            Assert.StartsWith("set_t0_", rare.SetId);
        }

        [Fact]
        public void TierlessContextNeverRolls()
        {
            var cfg = CertainSets();
            var rng = new Rng(7);
            var rare = It("r", "rusty_sword", Rarity.Rare);

            Loot.RollSetMembership(rng, rare, default, cfg); // struct default: SetBand 0 = no sets
            Assert.Null(rare.SetId);
            Assert.Equal(0, rng.Cursor); // and no draws consumed
        }

        [Fact]
        public void AllThreeSetsOfATierAreReachable()
        {
            var cfg = CertainSets();
            var rng = new Rng(1234);
            var seen = new HashSet<string>();
            for (int i = 0; i < 50; i++)
            {
                var item = It($"i{i}", "rusty_sword", Rarity.Rare);
                Loot.RollSetMembership(rng, item, Tiered(4), cfg);
                seen.Add(item.SetId!);
            }
            Assert.Equal(new HashSet<string> { "set_t3_a", "set_t3_b", "set_t3_c" }, seen);
        }

        [Fact]
        public void MembershipIsDeterministicForSameSeed()
        {
            var cfg = GameConfig.Default(); // real 0.25 chance: exercises both hit and miss paths
            var a = new Rng(99); var b = new Rng(99);
            for (int i = 0; i < 40; i++)
            {
                var ia = It($"a{i}", "rusty_sword");
                var ib = It($"b{i}", "rusty_sword");
                Loot.RollSetMembership(a, ia, Tiered(2), cfg);
                Loot.RollSetMembership(b, ib, Tiered(2), cfg);
                Assert.Equal(ia.SetId, ib.SetId);
            }
            Assert.Equal(a.Cursor, b.Cursor);
        }

        [Fact]
        public void FarmDropsCarryTheirTiersSets()
        {
            var cfg = CertainSets();
            var stage = cfg.Stages.Find(s => s.Stage == 25)!; // tier 2 (0-based), band 3
            var ctx = LootContext.ForStage(stage, cfg);
            var rng = new Rng(5);

            bool sawSet = false;
            for (int i = 0; i < 60 && !sawSet; i++)
            {
                var item = Loot.RollContextItem(rng, ctx, cfg);
                if (item.SetId == null) continue;
                sawSet = true;
                Assert.StartsWith("set_t2_", item.SetId); // the drop's own tier, nobody else's
            }
            Assert.True(sawSet, "60 certain-chance farm rolls produced no Rare drop with a set");
        }

        // ---- stat seam ----

        /// <summary>A hero wearing <paramref name="pieces"/> items of set <paramref name="setId"/>
        /// across distinct slots (plus nothing else), and the same wearing them set-less.</summary>
        private static (StatBlock with, StatBlock without) Dressed(int pieces, string setId)
        {
            var hero = new HeroInstance { Id = "h", DefId = Save.StarterHeroDef, Level = 30 };
            var withItems = new List<Item>();
            var plainItems = new List<Item>();
            for (int i = 0; i < pieces; i++)
            {
                withItems.Add(It($"s{i}", FourSlots[i].baseId, Rarity.Rare, setId));
                plainItems.Add(It($"p{i}", FourSlots[i].baseId, Rarity.Rare));
            }
            return (Stats.ComputeHeroStats(hero, Cfg, withItems), Stats.ComputeHeroStats(hero, Cfg, plainItems));
        }

        [Fact]
        public void OnePieceAddsNothing()
        {
            var (with, without) = Dressed(1, "set_t0_a");
            Assert.Equal(without.Get(StatKey.Atk), with.Get(StatKey.Atk));
        }

        [Fact]
        public void TwoPiecesAddPiece2Exactly()
        {
            var set = Cfg.Sets["set_t0_a"];
            var (with, without) = Dressed(2, set.Id);
            foreach (var kv in set.Piece2)
                Assert.Equal(without.Get(kv.Key) + kv.Value, with.Get(kv.Key), 9);
        }

        [Fact]
        public void FourPiecesAddPiece2AndPiece4()
        {
            var set = Cfg.Sets["set_t0_b"];
            var (with, without) = Dressed(4, set.Id);
            foreach (var kv in set.Piece2)
                Assert.Equal(without.Get(kv.Key) + kv.Value + set.Piece4.Get(kv.Key), with.Get(kv.Key), 9);
            foreach (var kv in set.Piece4)
                Assert.Equal(without.Get(kv.Key) + kv.Value + set.Piece2.Get(kv.Key), with.Get(kv.Key), 9);
        }

        [Fact]
        public void TwoDifferentSetsBothPayTheirTwoPiece()
        {
            var hero = new HeroInstance { Id = "h", DefId = Save.StarterHeroDef, Level = 30 };
            var items = new List<Item>
            {
                It("a1", FourSlots[0].baseId, Rarity.Rare, "set_t0_a"),
                It("a2", FourSlots[1].baseId, Rarity.Rare, "set_t0_a"),
                It("b1", FourSlots[2].baseId, Rarity.Rare, "set_t0_b"),
                It("b2", FourSlots[3].baseId, Rarity.Rare, "set_t0_b"),
            };
            var plain = new List<Item>
            {
                It("p1", FourSlots[0].baseId), It("p2", FourSlots[1].baseId),
                It("p3", FourSlots[2].baseId), It("p4", FourSlots[3].baseId),
            };
            var with = Stats.ComputeHeroStats(hero, Cfg, items);
            var without = Stats.ComputeHeroStats(hero, Cfg, plain);

            var a = Cfg.Sets["set_t0_a"]; var b = Cfg.Sets["set_t0_b"];
            foreach (var kv in a.Piece2)
                Assert.Equal(without.Get(kv.Key) + kv.Value, with.Get(kv.Key), 9);
            foreach (var kv in b.Piece2)
                Assert.Equal(without.Get(kv.Key) + kv.Value, with.Get(kv.Key), 9);
        }

        [Fact]
        public void StaleSetIdIsIgnored()
        {
            var hero = new HeroInstance { Id = "h", DefId = Save.StarterHeroDef, Level = 30 };
            var items = new List<Item>
            {
                It("x1", FourSlots[0].baseId, Rarity.Rare, "set_removed_from_content"),
                It("x2", FourSlots[1].baseId, Rarity.Rare, "set_removed_from_content"),
            };
            var plain = new List<Item> { It("p1", FourSlots[0].baseId), It("p2", FourSlots[1].baseId) };
            var with = Stats.ComputeHeroStats(hero, Cfg, items);
            var without = Stats.ComputeHeroStats(hero, Cfg, plain);
            foreach (StatKey k in System.Enum.GetValues(typeof(StatKey)))
                Assert.Equal(without.Get(k), with.Get(k));
        }

        // ---- copy sites: nothing may silently strip a membership ----

        [Fact]
        public void ToggleLockAndEnhanceAndReforgePreserveSetId()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var item = It("keeper", "rusty_sword", Rarity.Rare, "set_t0_a");
            item.Affixes.Add(new Affix { Stat = StatKey.Atk, Value = 3 }); // reforgeable
            save = Inventory.AddItems(save, new[] { item });
            save.Currencies["gold"] = 1_000_000_000;
            save.Currencies["scrap"] = 1_000_000_000;

            save = Inventory.ToggleLock(save, "keeper");
            Assert.Equal("set_t0_a", save.Inventory.Find(i => i.Id == "keeper")!.SetId);
            save = Inventory.ToggleLock(save, "keeper"); // unlock again so nothing else interferes

            var enh = Inventory.Enhance(save, "keeper", Cfg)!;
            save = enh.Save;
            Assert.Equal("set_t0_a", save.Inventory.Find(i => i.Id == "keeper")!.SetId);

            save = Inventory.Reforge(save, "keeper", Cfg);
            Assert.Equal("set_t0_a", save.Inventory.Find(i => i.Id == "keeper")!.SetId);
        }

        // ---- §6.2 balance gate ----

        [Theory]
        [InlineData(2)] // tier 3 (1-based) — early-mid game
        [InlineData(7)] // tier 8 (1-based) — late game
        public void FullSetStaysUnderThePowerGate(int tier)
        {
            // An on-curve-ish hero: level ≈ the tier's mid stage, wearing four Rare pieces of
            // the tier's ilvl with two mid affixes each — with and without the offense set.
            int midStage = tier * 10 + 5;
            var stage = Cfg.Stages.Find(s => s.Stage == midStage)!;
            int il = stage.AffixItemLevel;
            var hero = new HeroInstance { Id = "h", DefId = Save.StarterHeroDef, Level = midStage };

            List<Item> Gear(string? setId)
            {
                var list = new List<Item>();
                for (int i = 0; i < 4; i++)
                {
                    var it = new Item { Id = $"g{i}", BaseId = FourSlots[i].baseId, Rarity = Rarity.Rare, ItemLevel = il, SetId = setId };
                    it.Affixes.Add(new Affix { Stat = StatKey.Atk, Value = 0.45 * il });
                    it.Affixes.Add(new Affix { Stat = StatKey.Hp, Value = 1.8 * il });
                    list.Add(it);
                }
                return list;
            }

            var with = Stats.ComputeHeroStats(hero, Cfg, Gear($"set_t{tier}_a"));
            var without = Stats.ComputeHeroStats(hero, Cfg, Gear(null));

            double dpsSwing = DerivedStats.Dps(with) / DerivedStats.Dps(without) - 1.0;
            double ehpSwing = DerivedStats.EffectiveHp(with, Cfg, midStage) / DerivedStats.EffectiveHp(without, Cfg, midStage) - 1.0;

            Assert.InRange(dpsSwing, 0.0, 0.08); // §6.2: full set ≤ ~8% over equal setless gear
            Assert.InRange(ehpSwing, 0.0, 0.08);
        }
    }
}
