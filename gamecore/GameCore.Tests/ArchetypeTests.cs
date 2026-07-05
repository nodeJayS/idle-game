using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>The archetype backbone (2026-07-02): three families (Warrior/Rogue/
    /// Magician) act as stat templates + passive pools; classes carry per-key
    /// overrides resolved at config build. HeroDef.Role stays the sim's own axis.</summary>
    public class ArchetypeTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        [Fact]
        public void EveryHeroBelongsToARealArchetype()
        {
            Assert.Equal(3, Cfg.Archetypes.Count); // warrior, rogue, magician — no pre-created empties
            foreach (var h in Cfg.Heroes.Values)
            {
                Assert.True(Cfg.Archetypes.ContainsKey(h.Archetype),
                    $"{h.DefId} has unknown archetype '{h.Archetype}'");
                // a class's passives come from its family's pool (§7.2 shared library)
                var pool = Cfg.Archetypes[h.Archetype].PassivePool;
                foreach (var id in h.Skills.Where(s => Cfg.Skills[s].Passive))
                    Assert.Contains(id, pool);
            }
        }

        [Fact]
        public void TemplateClassesInheritTheFamilyStatsVerbatim()
        {
            // Knight IS the Warrior template; Fire Mage IS the Magician template.
            Assert.Equal(Cfg.Archetypes["warrior"].BaseStats, Cfg.Heroes["warrior_basic"].BaseStats);
            Assert.Equal(Cfg.Archetypes["magician"].BaseStats, Cfg.Heroes["magician_basic"].BaseStats);
            Assert.Equal(Cfg.Archetypes["rogue"].BaseStats, Cfg.Heroes["thief_basic"].BaseStats);
        }

        [Fact]
        public void OverridesResolveOnTopOfTheTemplate()
        {
            var priest = Cfg.Heroes["priest_basic"];
            Assert.Equal("magician", priest.Archetype);
            Assert.Equal(90, priest.BaseStats.Get(StatKey.Hp));         // overridden
            Assert.Equal(12, priest.BaseStats.Get(StatKey.Atk));        // overridden (low on purpose)
            Assert.Equal(6.0, priest.BaseStats.Get(StatKey.AttackRange)); // inherited from the family
            Assert.Equal(3.15, priest.BaseStats.Get(StatKey.MoveSpd));    // inherited
            Assert.Equal(1.5, priest.BaseStats.Get(StatKey.CritDmg));     // inherited

            var ice = Cfg.Heroes["icemage_basic"];
            Assert.Equal(82, ice.BaseStats.Get(StatKey.Hp));            // overridden
            Assert.Equal(6.0, ice.BaseStats.Get(StatKey.AttackRange));  // inherited
        }

        [Fact]
        public void RangedCastersOutpaceTheFrontLine()
        {
            // Ranged casters park farther out (standoff ~4.6); at equal MoveSpd they trailed
            // the melee dash on every pack move. Magician sits above the Warrior template but
            // below the Rogue, which stays the fastest. Pin the ordering, not the literals.
            var warrior = Cfg.Archetypes["warrior"].BaseStats.Get(StatKey.MoveSpd);
            var magician = Cfg.Archetypes["magician"].BaseStats.Get(StatKey.MoveSpd);
            var rogue = Cfg.Archetypes["rogue"].BaseStats.Get(StatKey.MoveSpd);
            Assert.True(warrior < magician, "magician should out-pace the warrior front line");
            Assert.True(magician < rogue, "rogue should stay the fastest archetype");
        }

        [Fact]
        public void ClassNamesMatchTheTaxonomy()
        {
            Assert.Equal("Knight", Cfg.Heroes["warrior_basic"].Name);
            Assert.Equal("Fire Mage", Cfg.Heroes["magician_basic"].Name);
            Assert.Equal("Assassin", Cfg.Heroes["thief_basic"].Name);
            Assert.Equal("Priest", Cfg.Heroes["priest_basic"].Name);
        }
    }
}
