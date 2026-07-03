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

        // ---- Multi-hero auto-equip (regression for the "only the first hero ever gets gear" bug)
        // and the damage-first / effective-life-fallback ranking directive. ----

        // A 3-hero fielded party: knight (slot 0), a rogue, a mage. Returns the save + the three ids.
        private static (SaveState save, string knight, string rogue, string mage) FieldedTrio()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save = Party.AcquireHero(save, "thief_basic", Cfg, "rogue");
            save = Party.AcquireHero(save, "magician_basic", Cfg, "mage");
            string knight = save.Heroes[0].Id;
            save = Party.FieldHero(save, 1, "rogue");
            save = Party.FieldHero(save, 2, "mage");
            return (save, knight, "rogue", "mage");
        }

        [Fact]
        public void AutoEquipReachesTheSecondAndThirdFieldedHero()
        {
            // Give the knight (slot 0) a strong weapon so a new mid weapon is NOT an upgrade for him,
            // but IS a big upgrade for the still-bare-handed rogue and mage. The bug was that only the
            // first party slot ever received gear; here the item must land on a non-knight hero.
            var (save, knight, rogue, mage) = FieldedTrio();
            var fielded = new[] { knight, rogue, mage };

            save = Inventory.AddItems(save, new[] { Mk("kw", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 40)) });
            save = Inventory.EquipItem(save, knight, "kw", Cfg); // knight already strong

            var mid = Mk("mid", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 12));
            save = Inventory.AddItems(save, new[] { mid });

            var (next, equipped) = Upgrades.AutoEquipIfBetter(save, mid, Cfg, 1, fielded);

            Assert.NotNull(equipped);
            Assert.NotEqual(knight, equipped!.HeroId);          // NOT the first slot
            Assert.Contains(equipped.HeroId, new[] { rogue, mage });
            var owner = next.Heroes.Find(h => h.Id == equipped.HeroId)!;
            Assert.Equal("mid", owner.Equipped[EquipSlot.Weapon]); // actually equipped there
        }

        [Fact]
        public void RanksByDamageGainOverEffectiveLifeGain()
        {
            // Hero A gains DAMAGE from the item (an Atk weapon), hero B would gain more EFFECTIVE-LIFE
            // (an Hp/Def swap). The directive: the item goes to the damage gainer.
            var (save, knight, rogue, mage) = FieldedTrio();

            // Give mage a bare weapon slot but stack its survivability so an Hp-heavy weapon reads as a
            // huge EHP jump for it; give the rogue nothing so the same weapon's Atk is a damage jump.
            // Simplest construction: one weapon with BOTH big Atk and big Hp. Rogue (glass) sees the
            // Atk as its dominant gain; we assert the winner is whoever gets the larger raw DPS delta.
            var item = Mk("hybrid", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 25), (StatKey.Hp, 200));
            save = Inventory.AddItems(save, new[] { item });

            var best = Upgrades.BestForItem(save, item, Cfg, 1, new[] { knight, rogue, mage });
            Assert.NotNull(best);
            Assert.True(best!.DamageDelta > 0, "damage-first winner must itself gain damage");

            // The winner has the max damage delta of the party — verify no other fielded hero gains
            // more damage from the same item.
            foreach (var id in new[] { knight, rogue, mage })
            {
                var e = Upgrades.EvaluateForHero(save, id, item, Cfg, 1);
                Assert.True(best.DamageDelta >= e.DamageDelta - 1e-6,
                            $"{id} gains more damage ({e.DamageDelta}) than the chosen winner ({best.DamageDelta})");
            }
        }

        [Fact]
        public void FallsBackToEffectiveLifeWhenNoHeroGainsDamage()
        {
            // A pure-defensive item (Hp/Def only, zero Atk) gives NO damage to anyone. The ranking
            // must fall back to effective-life and still equip it on the biggest EHP gainer.
            var (save, knight, rogue, mage) = FieldedTrio();
            var armor = Mk("armor", "leather_vest", Rarity.Rare, 5, (StatKey.Hp, 120), (StatKey.Def, 8));
            save = Inventory.AddItems(save, new[] { armor });
            var fielded = new[] { knight, rogue, mage };

            var best = Upgrades.BestForItem(save, armor, Cfg, 1, fielded);
            Assert.NotNull(best);
            Assert.True(best!.EhpDelta > 0, "fallback winner must gain effective life");

            var (next, equipped) = Upgrades.AutoEquipIfBetter(save, armor, Cfg, 1, fielded);
            Assert.NotNull(equipped);
            var owner = next.Heroes.Find(h => h.Id == equipped!.HeroId)!;
            Assert.Equal("armor", owner.Equipped[EquipSlot.Chest]);
        }

        [Fact]
        public void BestForItemIsDeterministic()
        {
            var (save, knight, rogue, mage) = FieldedTrio();
            var item = Mk("d", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 14), (StatKey.Hp, 30));
            save = Inventory.AddItems(save, new[] { item });
            var fielded = new[] { knight, rogue, mage };

            var a = Upgrades.BestForItem(save, item, Cfg, 3, fielded);
            var b = Upgrades.BestForItem(save, item, Cfg, 3, fielded);
            Assert.NotNull(a);
            Assert.Equal(a!.HeroId, b!.HeroId);
            Assert.Equal(a.DamageDelta, b.DamageDelta);
            Assert.Equal(a.DeltaPercent, b.DeltaPercent);

            // And the equip is deterministic too: same save + item => same owner + same slot.
            var (n1, e1) = Upgrades.AutoEquipIfBetter(save, item, Cfg, 3, fielded);
            var (n2, e2) = Upgrades.AutoEquipIfBetter(save, item, Cfg, 3, fielded);
            Assert.Equal(e1!.HeroId, e2!.HeroId);
            Assert.Equal(n1.Heroes.Find(h => h.Id == e1.HeroId)!.Equipped[EquipSlot.Weapon],
                         n2.Heroes.Find(h => h.Id == e2.HeroId)!.Equipped[EquipSlot.Weapon]);
        }
    }
}
