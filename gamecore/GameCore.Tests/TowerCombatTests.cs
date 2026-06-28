using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>Tower of Ascension slice 2: the bounded tower-floor fight. Steeper-than-ladder
    /// scaling, per-floor modifiers, no farm income, and the permanent account buff actually
    /// reaching the party's combat stats.</summary>
    public class TowerCombatTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState Leveled(int xp = 200000)
            => Progression.GrantPartyXp(Save.NewGame(1, Cfg, 0), xp, Cfg);

        private static SaveState ClearTo(SaveState save, int floor)
        {
            while (Tower.HighestFloor(save) < floor)
                save = Tower.RecordClear(save, Tower.NextFloor(save), Cfg);
            return save;
        }

        private static List<HeroInstance> PartyHeroes(SaveState save)
        {
            var list = new List<HeroInstance>();
            foreach (var id in save.Party)
                if (id != null) { var h = save.Heroes.Find(x => x.Id == id); if (h != null) list.Add(h); }
            return list;
        }

        private static CombatState TowerFight(SaveState save, int floor)
        {
            var s = Combat.InitFarm(PartyHeroes(save), 1, Cfg, new Rng(1));
            Combat.RefreshPartyStats(s, save, Cfg);   // apply gear + account buffs
            Combat.EnterTower(s, floor, Cfg, new Rng(1));
            return s;
        }

        private static IEnumerable<CombatEntity> Enemies(CombatState s) => s.Entities.Where(e => e.Team == Team.Enemy);

        // ---- bounded outcome ----

        [Fact]
        public void FloorOneIsClearedByALeveledParty()
        {
            var s = TowerFight(Leveled(), 1);
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Won, s.Status);
        }

        [Fact]
        public void ADeepFloorOverwhelmsTheSameParty()
        {
            // Floor 60's steep HP/dmg curve is unkillable inside the run timer for a fresh-leveled
            // solo hero -> the fight ends in a loss (the gate that forces farming + upgrading).
            var s = TowerFight(Leveled(), 60);
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Lost, s.Status);
        }

        // ---- floor scaling + modifiers ----

        [Fact]
        public void DeeperFloorsSpawnTankierMobs()
        {
            // Compare two ramp floors (both below TowerModifierFromFloor) so only the HP curve
            // differs — no modifier stat-mult muddies the ratio.
            double hp1 = TowerFight(Leveled(), 1).Entities.First(e => e.Id == "E0").MaxHp;
            double hp2 = TowerFight(Leveled(), 2).Entities.First(e => e.Id == "E0").MaxHp;
            Assert.Equal(hp1 * Tower.FloorHpMult(2, Cfg), hp2, 3); // FloorHpMult(1) == 1
        }

        [Fact]
        public void FloorModifierAppearsOnlyFromTheThreshold()
        {
            Assert.All(Enemies(TowerFight(Leveled(), 1)), e => Assert.Empty(e.ModTypes)); // ramp floors: none
            var mod = Cfg.TowerModifierForFloor(5);
            Assert.NotNull(mod);
            Assert.All(Enemies(TowerFight(Leveled(), 5)), e => Assert.Contains(mod!, e.ModTypes));
        }

        // ---- no farm income ----

        [Fact]
        public void TowerGrantsNoGoldXpOrLoot()
        {
            var s = TowerFight(Leveled(), 1);
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Won, s.Status); // mobs died...
            Assert.Equal(0, s.PendingXp);              // ...but paid out nothing
            Assert.Equal(0, s.PendingGold);
            Assert.Empty(s.PendingLoot);
        }

        // ---- the account-buff reward actually lands in combat ----

        [Fact]
        public void MilestoneBuffRaisesPartyCombatStats()
        {
            var baseSave = Leveled();
            var buffed = ClearTo(baseSave, Cfg.Balance.TowerMilestoneEvery); // 1 milestone (+5%)

            double hpBase = TowerFight(baseSave, 1).Entities.First(e => e.RefKind == "hero").MaxHp;
            double hpBuffed = TowerFight(buffed, 1).Entities.First(e => e.RefKind == "hero").MaxHp;

            Assert.Equal(hpBase * (1 + Tower.AccountBuffPct(buffed, Cfg)), hpBuffed, 3);
        }
    }
}
