using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Crypt meta (roguelite mode, user-approved design 2026-07-06): daily KEY recharge (1/UTC day,
    // banked to CryptKeyBank), key-consuming run starts, Tower-mirrored first-clear floor records
    // (+gem pay), the end-of-run grave-dust CHEST, the boon track (geometric costs, permanent
    // account-wide stat buffs), themed depth tiers, per-floor difficulty multipliers into
    // InitDungeon, and the save-migration backfill. All reducers pure; no-ops share the save ref.
    public class CryptTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private const long Day = 86_400_000L;
        // An arbitrary "today" far from zero so day-index math is representative.
        private const long Now = 1_780_000_000_000L;

        private static SaveState Fresh() => Save.NewGame(1, Cfg, Now);

        private static SaveState WithDust(SaveState save, long dust)
        {
            var s = Crypt.TickKeys(save, Cfg, Now); // ensure Crypt state exists / is stamped
            s.Currencies[Cfg.Balance.CryptDustCurrency] = dust;
            return s;
        }

        // ---------------- keys ----------------

        [Fact]
        public void FirstTickEverGrantsOneKeyImmediately()
        {
            var s = Fresh();
            Assert.Equal(0, Crypt.Keys(s));
            var t = Crypt.TickKeys(s, Cfg, Now);
            Assert.Equal(1, Crypt.Keys(t));
            Assert.Equal(Now / Day, t.Progress.Crypt.LastKeyDay);
        }

        [Fact]
        public void SameDayTickIsANoOpShare()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            var t = Crypt.TickKeys(s, Cfg, Now + 3_600_000); // an hour later, same UTC day
            Assert.Same(s, t);
        }

        [Fact]
        public void NextDayGrantsOneMoreAndBankCapsAccrual()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);          // 1 key
            var t = Crypt.TickKeys(s, Cfg, Now + Day);          // 2 keys (cap)
            Assert.Equal(Cfg.Balance.CryptKeyBank, Crypt.Keys(t));
            var u = Crypt.TickKeys(t, Cfg, Now + 10 * Day);     // long absence: still capped
            Assert.Equal(Cfg.Balance.CryptKeyBank, Crypt.Keys(u));
            // The stamp advanced, so no back-pay materialises after spending.
            var (spent, started) = Crypt.StartRun(u, Cfg);
            Assert.True(started);
            var v = Crypt.TickKeys(spent, Cfg, Now + 10 * Day + 3_600_000);
            Assert.Same(spent, v); // same day — nothing accrues
        }

        [Fact]
        public void MultiDayAbsenceFromEmptyGrantsUpToTheBank()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            var (afterStart, _) = Crypt.StartRun(s, Cfg);       // spend to 0
            var t = Crypt.TickKeys(afterStart, Cfg, Now + 5 * Day);
            Assert.Equal(Cfg.Balance.CryptKeyBank, Crypt.Keys(t)); // 5 days -> capped at the bank
        }

        [Fact]
        public void RolledBackClockGrantsNothing()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            var t = Crypt.TickKeys(s, Cfg, Now - 2 * Day);
            Assert.Same(s, t);
        }

        [Fact]
        public void NextKeyCountdownIsTheDayAfterTheLastGrant()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            Assert.Equal((Now / Day + 1) * Day, Crypt.NextKeyAtMs(s));
        }

        // ---------------- run lifecycle ----------------

        [Fact]
        public void StartRunConsumesExactlyOneKeyAndFailsAtZero()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            var (t, started) = Crypt.StartRun(s, Cfg);
            Assert.True(started);
            Assert.Equal(Crypt.Keys(s) - 1, Crypt.Keys(t));

            // Drain and refuse.
            while (Crypt.Keys(t) > 0) (t, _) = Crypt.StartRun(t, Cfg);
            var (u, again) = Crypt.StartRun(t, Cfg);
            Assert.False(again);
            Assert.Same(t, u);
        }

        [Fact]
        public void RecordFloorClearAdvancesSequentiallyAndPaysGems()
        {
            var s = Fresh();
            long gems0 = s.Currencies.TryGetValue(Cfg.Balance.PremiumCurrency, out var g) ? g : 0;

            var t = Crypt.RecordFloorClear(s, 1, Cfg);
            Assert.Equal(1, Crypt.DepthRecord(t));
            Assert.Equal(gems0 + Cfg.Balance.CryptGemsPerFloor,
                t.Currencies[Cfg.Balance.PremiumCurrency]);

            // Skip and re-clear are no-op shares that pay nothing.
            Assert.Same(t, Crypt.RecordFloorClear(t, 1, Cfg));
            Assert.Same(t, Crypt.RecordFloorClear(t, 5, Cfg));

            var u = Crypt.RecordFloorClear(t, 2, Cfg);
            Assert.Equal(2, Crypt.DepthRecord(u));
        }

        [Fact]
        public void EveryFloorOfARunIsAFirstClearBecauseTheRecordFollows()
        {
            // The run contract: floors are NextFloor at each step, so clearing 3 in a row
            // advances the record 3 times, each paying gems.
            var s = Fresh();
            for (int i = 0; i < Cfg.Balance.CryptFloorsPerRun; i++)
                s = Crypt.RecordFloorClear(s, Crypt.NextFloor(s), Cfg);
            Assert.Equal(Cfg.Balance.CryptFloorsPerRun, Crypt.DepthRecord(s));
            Assert.Equal(Cfg.Balance.CryptFloorsPerRun * (long)Cfg.Balance.CryptGemsPerFloor,
                s.Currencies[Cfg.Balance.PremiumCurrency]);
        }

        [Fact]
        public void CannotStartPastTheContentHeight()
        {
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            while (Crypt.DepthRecord(s) < Cfg.Balance.CryptMaxDepth)
                s = Crypt.RecordFloorClear(s, Crypt.NextFloor(s), Cfg);
            Assert.True(Crypt.IsComplete(s, Cfg));
            Assert.False(Crypt.CanStart(s, Cfg)); // keys banked but nothing left to clear
            // And the record can't run past the cap.
            Assert.Same(s, Crypt.RecordFloorClear(s, Crypt.NextFloor(s), Cfg));
        }

        // ---------------- mid-run persistence (§7.3, 10.7f) ----------------

        [Fact]
        public void FreshSaveHasNoActiveRun()
        {
            var s = Fresh();
            Assert.False(Crypt.HasActiveRun(s));
            Assert.Null(Crypt.ActiveRun(s));
        }

        [Fact]
        public void BeginRunFloorPersistsTheFloorSeedAndDescendsThenEndRunClears()
        {
            var s = Fresh();
            var t = Crypt.BeginRunFloor(s, floor: 4, floorsLeft: 2, seed: 12345, finalFloor: false, Cfg);
            var run = Crypt.ActiveRun(t);
            Assert.NotNull(run);
            Assert.Equal(4, run!.Floor);
            Assert.Equal(2, run.FloorsLeft);
            Assert.Equal(12345, run.Seed);
            Assert.False(run.FinalFloor);
            Assert.False(Crypt.HasActiveRun(s)); // original untouched (pure)

            // Descend: same reducer re-points ActiveRun at the next floor.
            var u = Crypt.BeginRunFloor(t, floor: 5, floorsLeft: 1, seed: 999, finalFloor: false, Cfg);
            Assert.Equal(5, Crypt.ActiveRun(u)!.Floor);
            Assert.Equal(999, Crypt.ActiveRun(u)!.Seed);

            var v = Crypt.EndRun(u, Cfg);
            Assert.False(Crypt.HasActiveRun(v));
            Assert.Same(v, Crypt.EndRun(v, Cfg)); // idle EndRun is a no-op share
        }

        [Fact]
        public void KeySpendAndRunStartCanShareOneStateSoNoKeyIsLostOnCrash()
        {
            // The client sequences StartRun (key−1) then BeginRunFloor (ActiveRun set) and writes
            // ONCE — so the persisted save always has BOTH the spent key and a resumable run.
            var s = Crypt.TickKeys(Fresh(), Cfg, Now);
            int keys0 = Crypt.Keys(s);
            var (t, started) = Crypt.StartRun(s, Cfg);
            Assert.True(started);
            int floor = Crypt.NextFloor(t);
            var persisted = Crypt.BeginRunFloor(t, floor, Cfg.Balance.CryptFloorsPerRun - 1, 777,
                Cfg.Balance.CryptFloorsPerRun == 1, Cfg);
            Assert.Equal(keys0 - 1, Crypt.Keys(persisted));
            Assert.True(Crypt.HasActiveRun(persisted));
            Assert.Equal(floor, Crypt.ActiveRun(persisted)!.Floor);
        }

        [Fact]
        public void MidRunReducersPreserveTheActiveRun()
        {
            // TickKeys, RecordFloorClear and BuyBoon must not clobber a run in progress.
            var s = Crypt.BeginRunFloor(Fresh(), floor: 1, floorsLeft: 2, seed: 42, finalFloor: false, Cfg);

            var afterTick = Crypt.TickKeys(s, Cfg, Now + Day); // a key recharges mid-run
            Assert.True(Crypt.HasActiveRun(afterTick));
            Assert.Equal(42, Crypt.ActiveRun(afterTick)!.Seed);

            var afterClear = Crypt.RecordFloorClear(s, Crypt.NextFloor(s), Cfg);
            Assert.True(Crypt.HasActiveRun(afterClear)); // descend/end updates it next, not this

            var rich = WithDust(s, 100000);
            rich = Crypt.BeginRunFloor(rich, 1, 2, 42, false, Cfg); // WithDust ticked keys; re-arm the run
            var (afterBuy, bought) = Crypt.BuyBoon(rich, "vigor", Cfg);
            Assert.True(bought);
            Assert.True(Crypt.HasActiveRun(afterBuy));
        }

        [Fact]
        public void MigrateBackfillsOlderSavesWithNoActiveRun()
        {
            // Additive field: an older save's CryptState has ActiveRun default null — no bump, no NRE.
            var s = Fresh();
            s.Progress.Crypt.ActiveRun = null;
            var migrated = Save.Migrate(s);
            Assert.False(Crypt.HasActiveRun(migrated));
            Assert.Equal(Save.SaveVersion, migrated.Version); // still v2 — ActiveRun needs no bump
        }

        [Fact]
        public void ChestGrantsDustScaledByDepth()
        {
            var s = Fresh();
            s = Crypt.RecordFloorClear(s, 1, Cfg);
            s = Crypt.RecordFloorClear(s, 2, Cfg);
            s = Crypt.RecordFloorClear(s, 3, Cfg);
            var (t, dust) = Crypt.GrantChest(s, Cfg);
            long expected = Cfg.Balance.CryptChestBaseDust + Cfg.Balance.CryptChestDustPerDepth * 3;
            Assert.Equal(expected, dust);
            Assert.Equal(expected, Crypt.Dust(t, Cfg));
        }

        // ---------------- difficulty + tiers ----------------

        [Fact]
        public void FloorMultipliersAreGeometricAndFloorOneIsBaseline()
        {
            Assert.Equal(1.0, Crypt.FloorHpMult(1, Cfg), 12);
            Assert.Equal(1.0, Crypt.FloorDmgMult(1, Cfg), 12);
            Assert.Equal(Cfg.Balance.CryptHpGrowth, Crypt.FloorHpMult(2, Cfg), 12);
            Assert.True(Crypt.FloorHpMult(20, Cfg) > Crypt.FloorHpMult(10, Cfg));
        }

        [Fact]
        public void TiersBandByTenAndCycle()
        {
            int band = Cfg.Balance.CryptTierFloors;
            Assert.Equal("crypt", Crypt.TierForFloor(1, Cfg)!.ThemeKey);
            Assert.Equal("crypt", Crypt.TierForFloor(band, Cfg)!.ThemeKey);
            Assert.Equal("molten", Crypt.TierForFloor(band + 1, Cfg)!.ThemeKey);
            Assert.Equal("frost", Crypt.TierForFloor(2 * band + 1, Cfg)!.ThemeKey);
            Assert.Equal("crypt", Crypt.TierForFloor(3 * band + 1, Cfg)!.ThemeKey); // cycles
            // Every tier names monsters/bosses that exist in the config.
            foreach (var tier in Cfg.CryptTiers)
            {
                Assert.True(Cfg.Monsters.ContainsKey(tier.BossId), tier.BossId);
                foreach (var id in tier.TrashRoster) Assert.True(Cfg.Monsters.ContainsKey(id), id);
            }
        }

        [Fact]
        public void EncounterForFloorPicksTheDeepestBandAtOrBelowTheFloor()
        {
            // §7.3 depth-band table: 1 / 11 / 21 / 41 in Default(). The deepest MinDepth ≤ floor wins.
            var f1 = Crypt.EncounterForFloor(1, Cfg)!;
            Assert.Equal(1, f1.CombatWaves);
            Assert.Equal(5, f1.MobsPerWave);

            var f11 = Crypt.EncounterForFloor(11, Cfg)!;
            Assert.Equal(2, f11.CombatWaves);
            Assert.Equal(4, f11.MobsPerWave);

            var f40 = Crypt.EncounterForFloor(40, Cfg)!;   // still the 21+ band
            Assert.Equal(2, f40.CombatWaves);
            Assert.Equal(2, f40.EliteCount);

            var f60 = Crypt.EncounterForFloor(60, Cfg)!;   // deep band caps the table
            Assert.Equal(3, f60.CombatWaves);
            Assert.Equal(4, f60.BossAdds);

            // No configured bands (or none shallow enough) ⇒ null: gen falls back to area counts.
            Assert.Null(Crypt.EncounterForFloor(1, new GameConfig()));
            Assert.Null(Crypt.EncounterForFloor(0, Cfg));
        }

        [Fact]
        public void InitDungeonMultipliersScaleMonsterStats()
        {
            var d = DungeonGen.Generate(new DungeonParams { Seed = 5, RoomCount = 14, Linear = true });
            var roster = new List<string> { "cave_bat" };
            var s1 = Combat.InitDungeon(new List<HeroInstance>(), 1, d, roster, "nightmare_maw", Cfg, new Rng(1));
            var s2 = Combat.InitDungeon(new List<HeroInstance>(), 1, d, roster, "nightmare_maw", Cfg, new Rng(1),
                hpMult: 2.0, dmgMult: 3.0);

            var m1 = s1.Entities.First(e => e.Team == Team.Enemy && !e.IsBoss);
            var m2 = s2.Entities.First(e => e.Id == m1.Id);
            Assert.Equal(m1.MaxHp * 2.0, m2.MaxHp, 6);
            Assert.Equal(m1.Stats.Get(StatKey.Atk) * 3.0, m2.Stats.Get(StatKey.Atk), 6);
        }

        // ---------------- boons ----------------

        [Fact]
        public void BoonCostsEscalateGeometricallyWithCeil()
        {
            Assert.Equal(Cfg.Balance.CryptBoonBaseCost, Crypt.BoonCost(0, Cfg));
            Assert.Equal((long)Math.Ceiling(Cfg.Balance.CryptBoonBaseCost * Cfg.Balance.CryptBoonCostGrowth),
                Crypt.BoonCost(1, Cfg));
            Assert.True(Crypt.BoonCost(5, Cfg) > Crypt.BoonCost(4, Cfg));
        }

        [Fact]
        public void BuyBoonDeductsDustAndRefusesWhenPoorUnknownOrMaxed()
        {
            var s = WithDust(Fresh(), Crypt.BoonCost(0, Cfg));
            var (t, bought) = Crypt.BuyBoon(s, "vigor", Cfg);
            Assert.True(bought);
            Assert.Equal(1, Crypt.BoonRank(t, "vigor"));
            Assert.Equal(0, Crypt.Dust(t, Cfg));

            // Too poor now.
            var (u, again) = Crypt.BuyBoon(t, "vigor", Cfg);
            Assert.False(again);
            Assert.Same(t, u);

            // Unknown id refuses.
            var (v, unknown) = Crypt.BuyBoon(WithDust(Fresh(), 1_000_000), "nonsense", Cfg);
            Assert.False(unknown);

            // Max rank refuses even when rich.
            var rich = WithDust(Fresh(), long.MaxValue / 2);
            for (int i = 0; i < Cfg.Balance.CryptBoonMaxRank; i++)
            {
                (rich, bought) = Crypt.BuyBoon(rich, "ferocity", Cfg);
                Assert.True(bought);
            }
            var (w, over) = Crypt.BuyBoon(rich, "ferocity", Cfg);
            Assert.False(over);
        }

        [Fact]
        public void ApplyBoonsScalesExactlyTheBoughtStats()
        {
            var s = WithDust(Fresh(), 1_000_000);
            (s, _) = Crypt.BuyBoon(s, "vigor", Cfg);     // rank 1 Hp
            (s, _) = Crypt.BuyBoon(s, "vigor", Cfg);     // rank 2 Hp
            (s, _) = Crypt.BuyBoon(s, "ferocity", Cfg);  // rank 1 Atk

            var baseBlock = new StatBlock { [StatKey.Hp] = 100, [StatKey.Atk] = 50, [StatKey.Def] = 20 };
            var buffed = Crypt.ApplyBoons(baseBlock, s, Cfg);
            double pct = Cfg.Balance.CryptBoonStatPct;
            Assert.Equal(100 * (1 + 2 * pct), buffed.Get(StatKey.Hp), 9);
            Assert.Equal(50 * (1 + 1 * pct), buffed.Get(StatKey.Atk), 9);
            Assert.Equal(20, buffed.Get(StatKey.Def), 9); // untouched track
            // Unbought save: no-op share.
            var plain = Fresh();
            Assert.Same(baseBlock, Crypt.ApplyBoons(baseBlock, plain, Cfg));
        }

        // ---------------- migration ----------------

        [Fact]
        public void MigrateBackfillsCryptStateOnOlderSaves()
        {
            var s = Fresh();
            s.Progress.Crypt = null!; // simulate a pre-crypt save (field absent in old JSON)
            var t = Save.Migrate(s);
            Assert.NotNull(t.Progress.Crypt);
            Assert.NotNull(t.Progress.Crypt.Boons);
            Assert.Equal(0, t.Progress.Crypt.Keys);
            Assert.Equal(0, t.Progress.Crypt.DepthRecord);
        }
    }
}
