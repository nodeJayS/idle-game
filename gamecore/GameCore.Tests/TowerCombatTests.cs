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
            // Fresh-init (mode isolation, 2026-07-06): a tower floor is its own CombatState, never a
            // converted farm. RefreshPartyStats after init applies gear + account buffs, as the client does.
            var s = Combat.InitTower(PartyHeroes(save), floor, Cfg, new Rng(1));
            Combat.RefreshPartyStats(s, save, Cfg);
            return s;
        }

        private static IEnumerable<CombatEntity> Enemies(CombatState s) => s.Entities.Where(e => e.Team == Team.Enemy);

        // ---- spawn placement ----

        [Fact]
        public void FloorPackSurroundsThePartyInsteadOfLiningUpOnOneSide()
        {
            // The pack used to spawn along +X, which with the camera's FIXED iso rotation put every
            // tower floor's monsters on one side of the screen. Assert the ring: with a pack this
            // size the mobs must occupy at least three of the four quadrants around the party, and
            // must not all share one sign of X.
            var save = Leveled();
            var s = TowerFight(save, 30); // deep enough for a pack worth measuring
            var mobs = Enemies(s).Where(e => e.Id != "EBOSS").ToList();
            Assert.True(mobs.Count >= 4, $"need a pack to measure, got {mobs.Count}");

            var quadrants = new HashSet<int>();
            foreach (var e in mobs)
                quadrants.Add((e.Pos.X >= 0 ? 1 : 0) | (e.Pos.Y >= 0 ? 2 : 0));

            Assert.True(quadrants.Count >= 3,
                $"pack only covers {quadrants.Count} quadrant(s) — it is bunched on one side");
            Assert.Contains(mobs, e => e.Pos.X < 0);
            Assert.Contains(mobs, e => e.Pos.X > 0);
        }

        [Fact]
        public void FloorPackKeepsItsEngageDistancesWhenRinged()
        {
            // The ring redistributes DIRECTION only — every mob keeps the radius the old +X line
            // gave it, so engage distance (and the failsafe timeout that depends on it) is unchanged.
            var s = TowerFight(Leveled(), 30);
            var mobs = Enemies(s).Where(e => e.Id != "EBOSS").ToList();
            // Radii are measured from the PARTY CENTROID, which is not the origin — PartyStartPos
            // lays the heroes out on a grid, so the cluster's centre sits off (0,0).
            var party = s.Entities.Where(e => e.Team == Team.Party).ToList();
            var centre = new Vec2(party.Average(e => e.Pos.X), party.Average(e => e.Pos.Y));
            for (int j = 0; j < mobs.Count; j++)
            {
                double expected = Cfg.Balance.BossSpawnDistance + j * 0.6;
                double actual = Vec2.Distance(centre, mobs[j].Pos);
                Assert.True(System.Math.Abs(actual - expected) < 0.5,
                    $"mob {j}: radius {actual:0.00} != the line's {expected:0.00}");
            }
        }

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
            // Floor 60's steep HP/dmg curve is unkillable for a fresh-leveled solo hero -> the fight
            // ends in a loss (the gate that forces farming + upgrading).
            var s = TowerFight(Leveled(), 60);
            Combat.RunToEnd(s, Cfg, new Rng(1));
            Assert.Equal(CombatStatus.Lost, s.Status);
        }

        [Fact]
        public void HeroesDoNotRespawnInTheTower()
        {
            // Unlike the farm, a hero downed in the tower stays dead for the run (no respawn timer),
            // so a wipe ends it — the tower is do-or-die, not a war of attrition.
            var s = TowerFight(Leveled(), 60);
            Combat.RunToEnd(s, Cfg, new Rng(1));
            var downed = s.Entities.Where(e => e.Team == Team.Party && !e.Alive).ToList();
            Assert.NotEmpty(downed);
            Assert.All(downed, e => Assert.True(e.RespawnMs <= 0, "tower death must not queue a respawn"));
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
