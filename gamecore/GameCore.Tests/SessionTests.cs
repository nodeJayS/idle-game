using System.Collections.Generic;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// The 30-second-session super-verbs (mobile arc MM2): Arrive (one boot payoff) and Manage
    /// (one mid-session chore sweep). Every assertion pins the super-verb to the EXISTING reducers it
    /// aggregates — it may never grant more, less, or different than running those reducers by hand —
    /// plus the two contracts a client leans on: Preview predicts Apply exactly, and a total no-op
    /// shares the input ref.
    /// </summary>
    public class SessionTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private const long Day = DailyLogin.DayMs;
        private const long Now = 5 * Day + 12_345; // UTC day-index 5, mid-day — daily is claimable from a fresh save

        private static Item Mk(string id, string baseId, Rarity rarity, int ilvl,
                               params (StatKey stat, double val)[] affixes)
        {
            var item = new Item { Id = id, BaseId = baseId, Rarity = rarity, ItemLevel = ilvl };
            foreach (var (s, v) in affixes) item.Affixes.Add(new Affix { Stat = s, Value = v });
            return item;
        }

        // A bag with one strict upgrade ("up": Rare +Atk sword) followed by three junk swords. On the
        // bare fielded warrior the sweep equips "up" first (weapon slot), leaving the junk as downgrades
        // that the salvage step then scraps. Daily login is claimable (LastClaimDay 0, Now = day 5).
        private static SaveState RichFixture() => Inventory.AddItems(Save.NewGame(1u, Cfg, 0L), new[]
        {
            Mk("up", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 20)),
            Mk("j1", "rusty_sword", Rarity.Normal, 1),
            Mk("j2", "rusty_sword", Rarity.Normal, 1),
            Mk("j3", "rusty_sword", Rarity.Normal, 1),
        });

        // A fresh save that has BOTH offline income (highest stage 10, last claim at epoch 0) AND a
        // claimable daily login (Now is a later UTC day) — so Arrive exercises both halves.
        private static SaveState ArriveFixture()
        {
            var s = Save.NewGame(1u, Cfg, 0L);
            s.Progress.HighestStage = 10;
            return s;
        }

        private static long Gems(SaveState s) =>
            s.Currencies.TryGetValue(Cfg.Balance.PremiumCurrency, out var g) ? g : 0;

        // ============================ Arrive ============================

        [Fact]
        public void Arrive_Composes_IdleThenGoals_LockstepWithRunningThemSeparately()
        {
            var save = ArriveFixture();

            // The hand-run composition Arrive claims to be.
            var (i1, idleR) = Idle.Claim(save, Cfg, Now);
            var (g1, claimed) = Goals.ClaimAll(i1, Cfg, Now);

            var (a, report) = Session.Arrive(save, Cfg, Now);

            // Idle half — the report carries the same accrual, item for item.
            Assert.True(idleR.Gold > 0 && idleR.LootCount > 0); // fixture actually accrues something
            Assert.Equal(idleR.Gold, report.Idle.Gold);
            Assert.Equal(idleR.Xp, report.Idle.Xp);
            Assert.Equal(idleR.LootCount, report.Idle.LootCount);
            Assert.Equal(idleR.Items.Count, report.Items.Count);
            for (int i = 0; i < idleR.Items.Count; i++)
                Assert.Equal(idleR.Items[i].Id, report.Items[i].Id);

            // Goals half — the claim list matches field for field.
            Assert.Equal(claimed.Count, report.GoalsClaimed.Count);
            var c = Assert.Single(claimed);
            var rc = Assert.Single(report.GoalsClaimed);
            Assert.Equal(c.Kind, rc.Kind);
            Assert.Equal(c.Gems, rc.Gems);
            Assert.Equal(c.Label, rc.Label);
            Assert.Equal(c.Gems, report.Gems); // convenience total sums the goal gems

            // Resulting save — every field the composition touches equals the hand-run result.
            Assert.Equal(g1.Currencies["gold"], a.Currencies["gold"]);
            Assert.Equal(Gems(g1), Gems(a));
            Assert.Equal(Now, a.LastClaimAt); // idle advanced the clock
            Assert.Equal(g1.Progress.Daily.LastClaimDay, a.Progress.Daily.LastClaimDay);
            Assert.Equal(g1.Inventory.Count, a.Inventory.Count);
        }

        [Fact]
        public void Arrive_Deterministic_SameSaveAndNow_IdenticalItemRolls()
        {
            var save = ArriveFixture();
            var (_, r1) = Session.Arrive(save, Cfg, Now);
            var (_, r2) = Session.Arrive(save, Cfg, Now);

            Assert.Equal(r1.Items.Count, r2.Items.Count);
            for (int i = 0; i < r1.Items.Count; i++)
                Assert.Equal(r1.Items[i].Id, r2.Items[i].Id); // ephemeral-rng determinism survives the composition
            Assert.Equal(r1.Gold, r2.Gold);
        }

        [Fact]
        public void Arrive_TotalNoOp_SharesTheInputRef()
        {
            // Nothing elapsed (LastClaimAt == Now => idle no-op) AND nothing claimable (daily already
            // stamped for today) => both halves share their ref, so Arrive hands back the input save.
            var save = Save.NewGame(1u, Cfg, Now);
            save.Progress.HighestStage = 10;
            save.Progress.Daily.LastClaimDay = DailyLogin.DayIndex(Now);

            var (next, report) = Session.Arrive(save, Cfg, Now);

            Assert.Same(save, next);
            Assert.True(report.IsEmpty);
        }

        [Fact]
        public void Arrive_LeavesRngCursorExactlyAsComposedReducersDo()
        {
            var save = ArriveFixture();
            save.RngCursor = 42; // idle rolls via an EPHEMERAL rng; the persisted cursor must not move

            var (next, _) = Session.Arrive(save, Cfg, Now);

            Assert.Equal(42, next.RngCursor);
        }

        // ============================ Manage ============================

        [Fact]
        public void Manage_PreviewEqualsApply_OnARichFixture()
        {
            var save = RichFixture();
            long expectScrap = 3 * Cfg.Balance.ScrapValue(Rarity.Normal, 1); // the three junk swords

            var preview = Session.Preview(save, Cfg, Now);
            var (next, report) = Session.Apply(save, Cfg, Now);

            // Counts + scrap match exactly, both against each other and against the hand-computed values.
            Assert.Single(preview.Claims);
            Assert.Equal(preview.Claims.Count, report.Claimed.Count);
            Assert.Equal(1, preview.EquipCount);
            Assert.Equal(preview.EquipCount, report.Equipped.Count);
            Assert.Equal(3, preview.SalvageCount);
            Assert.Equal(preview.SalvageCount, report.SalvagedCount);
            Assert.Equal(expectScrap, preview.SalvageScrap);
            Assert.Equal(preview.SalvageScrap, report.ScrapGained);

            // The upgrade actually landed and the junk actually went.
            Assert.Equal("up", next.Heroes[0].Equipped[EquipSlot.Weapon]);
            Assert.DoesNotContain(next.Inventory, i => i.Id == "j1");
            Assert.DoesNotContain(next.Inventory, i => i.Id == "j2");
            Assert.DoesNotContain(next.Inventory, i => i.Id == "j3");
        }

        [Fact]
        public void Manage_SweepContract_SkipsLockedSilently_AndNeverScrapsEquipped()
        {
            var save = Save.NewGame(1u, Cfg, 0L);
            save = Inventory.AddItems(save, new[]
            {
                Mk("worn", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 30)), // strong => equip it, immune to salvage
                Mk("locked", "rusty_sword", Rarity.Normal, 1),                // junk, but locked => spared silently
                Mk("loose", "rusty_sword", Rarity.Normal, 1),                 // junk, unlocked => the only scrap
            });
            save = Inventory.EquipItem(save, "h1", "worn", Cfg);
            save = Inventory.ToggleLock(save, "locked");

            var preview = Session.Preview(save, Cfg, Now);
            var (next, report) = Session.Apply(save, Cfg, Now);

            // The locked junk is in NEITHER the preview nor the report, and survives.
            Assert.Equal(1, preview.SalvageCount);
            Assert.Equal(1, report.SalvagedCount);
            Assert.Empty(preview.Equips); // weak swords are downgrades vs the equipped strong one
            Assert.Contains(next.Inventory, i => i.Id == "locked"); // locked survived
            Assert.Contains(next.Inventory, i => i.Id == "worn");   // equipped survived
            Assert.DoesNotContain(next.Inventory, i => i.Id == "loose"); // only the unlocked junk died
        }

        [Fact]
        public void Manage_EquipSweep_OnlyEverImproves()
        {
            var save = RichFixture();

            var (next, report) = Session.Apply(save, Cfg, Now);

            // Every reported equip is a genuine gain.
            Assert.NotEmpty(report.Equipped);
            foreach (var e in report.Equipped)
                Assert.True(e.DeltaPercent > 0, $"equip {e.Item.Id} reported a non-positive delta {e.DeltaPercent}");

            // And there is nothing left to auto-equip afterwards.
            var after = Session.Preview(next, Cfg, Now);
            Assert.Equal(0, after.EquipCount);
        }

        [Fact]
        public void Manage_Idempotent_SecondApplyIsEmptyAndSharesRef()
        {
            var save = RichFixture();

            var (once, _) = Session.Apply(save, Cfg, Now);
            var (twice, report2) = Session.Apply(once, Cfg, Now);

            Assert.True(report2.IsEmpty);
            Assert.Same(once, twice); // nothing to claim/equip/salvage => the ref threads through untouched
        }

        [Fact]
        public void Manage_OrderingContract_AnUpgradeThatIsAlsoSalvageEligible_EndsUpEquippedNotScrapped()
        {
            // "up" is loose+unlocked (so SalvageAll would eat it) AND a strict upgrade for the bare
            // warrior. The equip-before-salvage order must rescue it: equipped, not scrapped.
            var save = Inventory.AddItems(Save.NewGame(1u, Cfg, 0L), new[]
            {
                Mk("up", "rusty_sword", Rarity.Rare, 5, (StatKey.Atk, 20)),
            });

            var (next, report) = Session.Apply(save, Cfg, Now);

            var equipped = Assert.Single(report.Equipped);
            Assert.Equal("up", equipped.Item.Id);
            Assert.Equal("h1", equipped.HeroId);
            Assert.Equal(0, report.SalvagedCount); // the only bag item was rescued, not scrapped
            Assert.Equal("up", next.Heroes[0].Equipped[EquipSlot.Weapon]);
        }

        [Fact]
        public void Manage_LeavesRngCursorExactlyAsComposedReducersDo()
        {
            var save = RichFixture();
            save.RngCursor = 42; // claim/equip/salvage never touch the rng cursor

            var (next, _) = Session.Apply(save, Cfg, Now);

            Assert.Equal(42, next.RngCursor);
        }
    }
}
