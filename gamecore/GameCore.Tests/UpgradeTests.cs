using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class UpgradeTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static Item Mk(string id, string baseId, Rarity rarity, int ilvl,
                               params (StatKey stat, double val)[] affixes)
        {
            var item = new Item { Id = id, BaseId = baseId, Rarity = rarity, ItemLevel = ilvl };
            foreach (var (s, v) in affixes) item.Affixes.Add(new Affix { Stat = s, Value = v });
            return item;
        }

        // a fresh solo-warrior save with the given items in the bag
        private static (SaveState save, string heroId) Setup(params Item[] items)
        {
            var save = Inventory.AddItems(Save.NewGame(1, Cfg, 0), items);
            return (save, save.Heroes[0].Id);
        }

        [Fact]
        public void PowerScoreRisesWithBetterGear()
        {
            var (save, heroId) = Setup();
            var bare = Stats.ComputeHeroStats(save.Heroes[0], Cfg, Stats.ResolveEquipped(save, save.Heroes[0]));
            double bareP = Upgrades.PowerScore(bare, Cfg, 1);

            var strong = Mk("w", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 20), (StatKey.Hp, 50));
            var (_, after) = Inventory.ComparePairForHero(save, heroId, strong, Cfg);
            double afterP = Upgrades.PowerScore(after, Cfg, 1);

            Assert.True(afterP > bareP);
        }

        [Fact]
        public void EvaluateFlagsAStrongWeaponAsUpgrade()
        {
            var (save, heroId) = Setup();
            var sword = Mk("w", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 15));

            var eval = Upgrades.EvaluateForHero(save, heroId, sword, Cfg, 1);

            Assert.Equal(Upgrades.Verdict.Upgrade, eval.Verdict);
            Assert.True(eval.DeltaPercent > 0);
            Assert.True(eval.AfterPower > eval.BeforePower);
        }

        [Fact]
        public void EvaluateFlagsAWorseReplacementAsDowngrade()
        {
            // equip a strong sword, then evaluate swapping in a weak one => downgrade
            var (save, heroId) = Setup(Mk("strong", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 20)));
            save = Inventory.EquipItem(save, heroId, "strong", Cfg);

            var weak = Mk("weak", "rusty_sword", Rarity.Normal, 1);
            var eval = Upgrades.EvaluateForHero(save, heroId, weak, Cfg, 1);

            Assert.Equal(Upgrades.Verdict.Downgrade, eval.Verdict);
            Assert.True(eval.DeltaPercent < 0);
        }

        [Fact]
        public void ReEquippingTheSameItemIsASidegrade()
        {
            var (save, heroId) = Setup(Mk("w", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7)));
            save = Inventory.EquipItem(save, heroId, "w", Cfg);

            // an identical-stat item in the same slot => no net power change
            var twin = Mk("w2", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 7));
            var eval = Upgrades.EvaluateForHero(save, heroId, twin, Cfg, 1);

            Assert.Equal(Upgrades.Verdict.Sidegrade, eval.Verdict);
        }

        [Fact]
        public void BestForItemPicksTheHeroWhoGainsMost()
        {
            // two heroes: one already holding a strong sword, one bare-handed. A mid sword should
            // be the bigger upgrade for the bare hero.
            var save = Save.NewGame(1, Cfg, 0);
            save = Party.AcquireHero(save, "magician_basic", Cfg, "mage"); // grant a second hero
            string h0 = save.Heroes[0].Id, h1 = save.Heroes[1].Id;

            save = Inventory.AddItems(save, new[]
            {
                Mk("equipped", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 25)),
                Mk("candidate", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 10)),
            });
            save = Inventory.EquipItem(save, h0, "equipped", Cfg); // h0 already strong

            var best = Upgrades.BestForItem(save, save.Inventory.Find(i => i.Id == "candidate")!, Cfg, 1);

            Assert.NotNull(best);
            Assert.Equal(h1, best!.HeroId); // the bare hero gains more
            Assert.Equal(Upgrades.Verdict.Upgrade, best.Verdict);
        }

        [Fact]
        public void BestForItemRespectsHeroScope()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Party.AcquireHero(save, "magician_basic", Cfg, "mage");
            string h0 = save.Heroes[0].Id, h1 = save.Heroes[1].Id;
            save = Inventory.AddItems(save, new[] { Mk("w", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 10)) });
            var item = save.Inventory.Find(i => i.Id == "w")!;

            var scoped = Upgrades.BestForItem(save, item, Cfg, 1, new[] { h1 });

            Assert.NotNull(scoped);
            Assert.Equal(h1, scoped!.HeroId); // never considers h0 when scoped to h1
        }

        [Fact]
        public void AutoEquipEquipsWhenBetter()
        {
            var (save, heroId) = Setup(Mk("w", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 15)));
            var item = save.Inventory.Find(i => i.Id == "w")!;

            var (next, equipped) = Upgrades.AutoEquipIfBetter(save, item, Cfg, 1);

            Assert.NotNull(equipped);
            Assert.Equal(heroId, equipped!.HeroId);
            Assert.Equal("w", next.Heroes.Find(h => h.Id == heroId)!.Equipped[EquipSlot.Weapon]);
            Assert.Empty(save.Heroes.Find(h => h.Id == heroId)!.Equipped); // input untouched (pure)
        }

        [Fact]
        public void AutoEquipIsNoOpWhenNotBetter()
        {
            var (save, heroId) = Setup(Mk("strong", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 20)));
            save = Inventory.EquipItem(save, heroId, "strong", Cfg);
            save = Inventory.AddItems(save, new[] { Mk("weak", "rusty_sword", Rarity.Normal, 1) });
            var weak = save.Inventory.Find(i => i.Id == "weak")!;

            var (next, equipped) = Upgrades.AutoEquipIfBetter(save, weak, Cfg, 1);

            Assert.Null(equipped);
            Assert.Same(save, next); // unchanged
            Assert.Equal("strong", next.Heroes.Find(h => h.Id == heroId)!.Equipped[EquipSlot.Weapon]);
        }
    }
}
