using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// Loot-imprint mechanical modifiers (the headline hook): a mechanical modifier makes monsters
    /// fight nastier via a real combat mechanic AND can imprint that signature onto their drops as a
    /// build-defining affix the normal pool never rolls. Covers the imprint reducer, the Volatile
    /// monster mechanic (splash), and that the imprinted stat is exclusive to this path.
    /// </summary>
    public class LootImprintTests
    {
        private static HeroInstance Champ() => new HeroInstance { Id = "h1", DefId = "warrior_basic", Level = 1 };

        // Default cfg but with Volatile's imprint guaranteed, so the rng can't make the test flaky.
        private static GameConfig CertainImprintCfg()
        {
            var c = GameConfig.Default();
            c.Modifiers["volatile"].ImprintChance = 1.0;
            return c;
        }

        private static Item PlainItem() =>
            new Item { Id = "x", BaseId = "rusty_sword", Rarity = Rarity.Rare, ItemLevel = 10, Affixes = new List<Affix>() };

        private static List<ModifierInstance> Active(GameConfig cfg, string id, int strength) =>
            new List<ModifierInstance> { new ModifierInstance { Def = cfg.Modifiers[id], Strength = strength } };

        // --- the imprint reducer ---

        [Fact]
        public void ImprintStampsSignatureAffixWhenMonsterCarriedTheMechanicalMod()
        {
            var cfg = CertainImprintCfg();
            var item = Loot.ImprintDrop(new Rng(1), PlainItem(),
                monsterMods: new List<string> { "volatile" }, active: Active(cfg, "volatile", 5), cfg);

            var aff = Assert.Single(item.Affixes);
            Assert.Equal(StatKey.SplashRadius, aff.Stat);
            Assert.Equal(cfg.Modifiers["volatile"].ImprintPerStrength * 5, aff.Value, 6); // value = perStrength × strength
        }

        [Fact]
        public void NoImprintWhenTheMonsterDidNotCarryTheMod()
        {
            var cfg = CertainImprintCfg();
            var item = Loot.ImprintDrop(new Rng(1), PlainItem(),
                monsterMods: new List<string>(), active: Active(cfg, "volatile", 5), cfg);
            Assert.Empty(item.Affixes); // mob never had volatile -> nothing to imprint
        }

        [Fact]
        public void NonMechanicalModsNeverImprint()
        {
            var cfg = GameConfig.Default();
            var item = Loot.ImprintDrop(new Rng(1), PlainItem(),
                monsterMods: new List<string> { "vampiric" }, active: Active(cfg, "vampiric", 9), cfg);
            Assert.Empty(item.Affixes); // vampiric is behavioral, not Mechanical -> no imprint
        }

        [Fact]
        public void ImprintValueScalesWithStrength()
        {
            var cfg = CertainImprintCfg();
            double Stamp(int strength) => Loot
                .ImprintDrop(new Rng(1), PlainItem(), new List<string> { "volatile" }, Active(cfg, "volatile", strength), cfg)
                .Affixes.Single().Value;
            Assert.Equal(Stamp(5) * 2, Stamp(10), 6);
        }

        [Fact]
        public void AnAlreadyFilledSlotIsNotOverwritten()
        {
            var cfg = CertainImprintCfg();
            var item = PlainItem();
            item.Affixes.Add(new Affix { Stat = StatKey.SplashRadius, Value = 1.0 }); // prefix slot already filled

            Loot.ImprintDrop(new Rng(1), item, new List<string> { "volatile" }, Active(cfg, "volatile", 5), cfg);

            var aff = Assert.Single(item.Affixes);
            Assert.Equal(1.0, aff.Value, 6); // unchanged — at most one prefix imprint per item
        }

        // Chance=1 for EVERY mechanical mod (so prefix + suffix tests aren't rng-flaky).
        private static GameConfig AllCertainImprintCfg()
        {
            var c = GameConfig.Default();
            foreach (var kv in c.Modifiers) if (kv.Value.Mechanical) kv.Value.ImprintChance = 1.0;
            return c;
        }

        [Fact]
        public void AnItemCanHoldOnePrefixAndOneSuffixImprint()
        {
            var cfg = AllCertainImprintCfg();
            var active = new List<ModifierInstance>
            {
                new ModifierInstance { Def = cfg.Modifiers["volatile"], Strength = 5 }, // prefix
                new ModifierInstance { Def = cfg.Modifiers["leeching"], Strength = 5 }, // suffix
            };
            var item = Loot.ImprintDrop(new Rng(1), PlainItem(),
                new List<string> { "volatile", "leeching" }, active, cfg);

            Assert.Equal("Volatile", Loot.ImprintForSlot(item, cfg, ImprintSlot.Prefix)!.Name);
            Assert.Equal("Leeching", Loot.ImprintForSlot(item, cfg, ImprintSlot.Suffix)!.Name);
        }

        [Fact]
        public void OnlyOneImprintLandsPerSlotEvenWhenBothCandidatesHit()
        {
            var cfg = AllCertainImprintCfg();
            var active = new List<ModifierInstance> // two suffix candidates, both certain to hit
            {
                new ModifierInstance { Def = cfg.Modifiers["leeching"], Strength = 5 },
                new ModifierInstance { Def = cfg.Modifiers["barbed"], Strength = 5 },
            };
            var item = Loot.ImprintDrop(new Rng(3), PlainItem(),
                new List<string> { "leeching", "barbed" }, active, cfg);

            int suffixes = item.Affixes.FindAll(a => Loot.ImprintSlotOfStat(a.Stat, cfg) == ImprintSlot.Suffix).Count;
            Assert.Equal(1, suffixes); // one slot — a single random pick among the hits, never both
        }

        [Fact]
        public void ChainingImprintsChainCountInThePrefixSlot()
        {
            var cfg = AllCertainImprintCfg();
            var active = new List<ModifierInstance> { new ModifierInstance { Def = cfg.Modifiers["chaining"], Strength = 5 } };
            var item = Loot.ImprintDrop(new Rng(1), PlainItem(), new List<string> { "chaining" }, active, cfg);

            Assert.Equal("Chaining", Loot.ImprintForSlot(item, cfg, ImprintSlot.Prefix)!.Name);
            Assert.True(item.Affixes.Exists(a => a.Stat == StatKey.ChainCount));
        }

        [Fact]
        public void OnlyOnePrefixLandsWhenBothPrefixModsHit()
        {
            var cfg = AllCertainImprintCfg();
            var active = new List<ModifierInstance> // volatile + chaining are both prefixes
            {
                new ModifierInstance { Def = cfg.Modifiers["volatile"], Strength = 5 },
                new ModifierInstance { Def = cfg.Modifiers["chaining"], Strength = 5 },
            };
            var item = Loot.ImprintDrop(new Rng(4), PlainItem(),
                new List<string> { "volatile", "chaining" }, active, cfg);

            int prefixes = item.Affixes.FindAll(a => Loot.ImprintSlotOfStat(a.Stat, cfg) == ImprintSlot.Prefix).Count;
            Assert.Equal(1, prefixes); // one prefix slot — random pick, never both
        }

        // --- the imprinted stat is EXCLUSIVE to this path ---

        [Fact]
        public void NormalLootNeverRollsTheImprintStat()
        {
            var cfg = GameConfig.Default();
            // The imprint stat is in no base's AllowedAffixes, so the normal roll can't produce it —
            // that's what makes imprinted gear obtainable only by farming the mechanical mod.
            Assert.All(cfg.ItemBases.Values, b => Assert.DoesNotContain(StatKey.SplashRadius, b.AllowedAffixes));

            var rng = new Rng(7);
            foreach (var b in cfg.ItemBases.Values)
                for (int i = 0; i < 200; i++)
                    Assert.DoesNotContain(StatKey.SplashRadius,
                        Loot.RollAffixes(rng, b, Rarity.Legendary, 50, cfg).Select(a => a.Stat));
        }

        // --- the monster-side mechanic + that the imprint folds into hero power for free ---

        [Fact]
        public void VolatileMonstersGainSplashSoTheirAttacksHitTheParty()
        {
            var cfg = GameConfig.Default();
            var vol = Active(cfg, "volatile", 10);
            var mob = Combat.InitFarm(new[] { Champ() }, 5, cfg, new Rng(1), vol)
                .Entities.First(e => e.Team == Team.Enemy);

            Assert.Contains("volatile", mob.ModTypes);
            Assert.True(mob.Stats.Get(StatKey.SplashRadius) > 0); // splash granted -> attacks splash
        }

        // --- predicates that drive the client "imprinted item" tells ---

        [Fact]
        public void ImprintStatPredicateMatchesOnlyMechanicalSignatures()
        {
            var cfg = GameConfig.Default();
            Assert.True(Loot.IsImprintStat(StatKey.SplashRadius, cfg)); // Volatile's signature
            Assert.False(Loot.IsImprintStat(StatKey.Atk, cfg));         // an ordinary rolled stat
        }

        [Fact]
        public void IsImprintedDetectsTheStampedAffix()
        {
            var cfg = GameConfig.Default();
            var plain = PlainItem();
            Assert.False(Loot.IsImprinted(plain, cfg));

            var stamped = Loot.ImprintDrop(new Rng(1), PlainItem(), new List<string> { "volatile" },
                Active(CertainImprintCfg(), "volatile", 5), cfg);
            Assert.True(Loot.IsImprinted(stamped, cfg));
        }

        [Fact]
        public void ImprintSourceNamesTheModifierForTheItemTitle()
        {
            var cfg = GameConfig.Default();
            Assert.Null(Loot.ImprintSource(PlainItem(), cfg)); // un-imprinted -> no title

            var stamped = Loot.ImprintDrop(new Rng(1), PlainItem(), new List<string> { "volatile" },
                Active(CertainImprintCfg(), "volatile", 5), cfg);
            var src = Loot.ImprintSource(stamped, cfg);
            Assert.NotNull(src);
            Assert.Equal("Volatile", src!.Name); // drives "Volatile Rusty Sword"
        }

        [Fact]
        public void ImprintedSplashAffixFoldsIntoHeroStats()
        {
            var cfg = GameConfig.Default();
            var imprinted = new Item
            {
                Id = "w", BaseId = "rusty_sword", Rarity = Rarity.Rare, ItemLevel = 10,
                Affixes = new List<Affix> { new Affix { Stat = StatKey.SplashRadius, Value = 1.5 } },
            };
            double baseSplash = Stats.ComputeHeroStats(Champ(), cfg, new List<Item>()).Get(StatKey.SplashRadius);
            double withImprint = Stats.ComputeHeroStats(Champ(), cfg, new List<Item> { imprinted }).Get(StatKey.SplashRadius);
            Assert.Equal(baseSplash + 1.5, withImprint, 6); // the imprinted affix folds into the stat sheet (and thus combat)
        }
    }
}
