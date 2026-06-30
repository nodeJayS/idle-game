using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Lever 3 slice 3: skill-tree gating. A node can be ranked only once its prereq has
    /// ≥1 rank AND the hero meets its UnlockLevel. Gating restricts point investment only (slotting
    /// / casting at rank 0 is unaffected). Roots (no prereq, UnlockLevel ≤ 1) are always open.</summary>
    public class SkillTreeTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static HeroInstance Warrior(int level, params (string id, int rank)[] ranks)
        {
            var h = new HeroInstance { Id = "h", DefId = "warrior_basic", Level = level };
            foreach (var r in ranks) h.SkillRanks[r.id] = r.rank;
            return h;
        }

        // ---- IsUnlocked gate logic (pure) ----

        [Fact]
        public void RootsAreAlwaysUnlocked()
        {
            var fresh = Warrior(1);
            Assert.True(Skills.IsUnlocked(fresh, "cleave", Cfg));
            Assert.True(Skills.IsUnlocked(fresh, "bash", Cfg));
        }

        [Fact]
        public void PrereqMustHaveARankToUnlock()
        {
            // warcry: prereq cleave, lvl 5. High enough level, but cleave unranked -> locked.
            Assert.False(Skills.IsUnlocked(Warrior(50), "warcry", Cfg));
            Assert.True(Skills.IsUnlocked(Warrior(50, ("cleave", 1)), "warcry", Cfg));
        }

        [Fact]
        public void LevelGateBlocksBelowUnlockLevel()
        {
            // warcry requires level 5; prereq satisfied but level 4 -> still locked.
            Assert.False(Skills.IsUnlocked(Warrior(4, ("cleave", 1)), "warcry", Cfg));
            Assert.True(Skills.IsUnlocked(Warrior(5, ("cleave", 1)), "warcry", Cfg));
        }

        [Fact]
        public void DeeperNodesChainThroughTheirPrereq()
        {
            // bulwark: prereq warcry, lvl 14. Needs warcry ranked (itself gated behind cleave).
            Assert.False(Skills.IsUnlocked(Warrior(20, ("cleave", 1)), "bulwark", Cfg)); // warcry unranked
            Assert.True(Skills.IsUnlocked(Warrior(20, ("cleave", 1), ("warcry", 1)), "bulwark", Cfg));
        }

        // ---- CanInvest integration (gate + points) ----

        [Fact]
        public void CannotInvestALockedNodeEvenWithPoints()
        {
            var save = Progression.GrantPartyXp(Save.NewGame(1, Cfg, 0), 2_000_000, Cfg); // well-leveled, has points
            var id = save.Heroes[0].Id;
            Assert.True(Skills.UnspentPoints(save.Heroes[0], Cfg) > 0);

            Assert.False(Skills.CanInvest(save, id, "warcry", Cfg));         // cleave unranked -> locked
            Assert.Same(save, Skills.InvestSkill(save, id, "warcry", Cfg));  // no-op shares the ref

            save = Skills.InvestSkill(save, id, "cleave", Cfg);              // open the gate
            Assert.True(Skills.CanInvest(save, id, "warcry", Cfg));
        }

        [Fact]
        public void RespecRelocksDownstreamNodes()
        {
            var save = Progression.GrantPartyXp(Save.NewGame(1, Cfg, 0), 2_000_000, Cfg);
            var id = save.Heroes[0].Id;
            save = Skills.InvestSkill(save, id, "cleave", Cfg);
            Assert.True(Skills.CanInvest(save, id, "warcry", Cfg));

            save = Skills.RespecHero(save, id, Cfg); // clears cleave -> warcry gate closes again
            Assert.False(Skills.CanInvest(save, id, "warcry", Cfg));
        }
    }
}
