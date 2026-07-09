using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>FTUE slice (b): the five guided-intro quests — seeded at New Game, imperative,
    /// predicate-driven, retro-completing so the intro can never wedge.</summary>
    public class IntroQuestsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        private static SaveState Fresh() => Save.NewGame(777u, Cfg, 0L);

        private static void SetKills(SaveState save, long n) =>
            save.Progress.Achievements.Counters[AchievementMetric.MonstersKilled] = n;

        private static void AddBagItem(SaveState save) =>
            save.Inventory.Add(new Item { Id = "i1", BaseId = "sword", Rarity = Rarity.Normal });

        private static void EquipSomething(SaveState save) =>
            save.Heroes[0].Equipped[EquipSlot.Weapon] = "i1";

        private static long Gold(SaveState s) => s.Currencies.TryGetValue("gold", out var g) ? g : 0;
        private static long Scrap(SaveState s) => s.Currencies.TryGetValue("scrap", out var v) ? v : 0;

        // ---- content sanity ----

        [Fact]
        public void FiveBeats_UniqueIds_PositiveRewards()
        {
            Assert.Equal(5, IntroQuests.All.Count);
            Assert.Equal(5, IntroQuests.All.Select(q => q.Id).Distinct().Count());
            foreach (var q in IntroQuests.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(q.Title));
                Assert.True(q.RewardGold > 0 && q.RewardScrap > 0 && q.RewardXp > 0);
            }
        }

        // ---- seeded at New Game only ----

        [Fact]
        public void NewGame_ShowsFivePendingBeats()
        {
            var save = Fresh();
            Assert.True(IntroQuests.Active(save));
            var board = IntroQuests.Board(save);
            Assert.Equal(5, board.Count);
            Assert.All(board, r => Assert.False(r.Complete)); // stage 0, no kills, no loot
            Assert.All(board, r => Assert.False(r.Claimed));
        }

        [Fact]
        public void UnarmedSave_HasNoIntro()
        {
            var save = Save.Migrate(new SaveState { Version = 1 }); // pre-FTUE save
            Assert.False(IntroQuests.Active(save));
            // Even with every deed done, an unarmed save never pays the intro.
            SetKills(save, 999);
            save.Progress.HighestStage = 50;
            var (after, done) = IntroQuests.Sync(save, Cfg);
            Assert.Same(save, after);
            Assert.Empty(done);
        }

        [Fact]
        public void ReMigration_NeitherSeedsNorPays()
        {
            var save = Save.Migrate(Save.Migrate(new SaveState { Version = 1 }));
            var (after, done) = IntroQuests.Sync(save, Cfg);
            Assert.Same(save, after);
            Assert.Empty(done);
            Assert.False(IntroQuests.Active(after));
        }

        // ---- each beat completes from the matching evidence ----

        [Fact]
        public void SlayBeat_CompletesAtKillTarget()
        {
            var save = Fresh();
            SetKills(save, IntroQuests.SlayTarget - 1);
            Assert.False(IntroQuests.IsComplete("intro_slay", save));
            SetKills(save, IntroQuests.SlayTarget);
            Assert.True(IntroQuests.IsComplete("intro_slay", save));
        }

        [Fact]
        public void LootBeat_CompletesFromAnyDropFate()
        {
            var bag = Fresh(); AddBagItem(bag);
            Assert.True(IntroQuests.IsComplete("intro_loot", bag));

            var worn = Fresh(); EquipSomething(worn);
            Assert.True(IntroQuests.IsComplete("intro_loot", worn));

            var salv = Fresh(); salv.Progress.Achievements.Counters[AchievementMetric.ItemsSalvaged] = 1;
            Assert.True(IntroQuests.IsComplete("intro_loot", salv));

            Assert.False(IntroQuests.IsComplete("intro_loot", Fresh()));
        }

        [Fact]
        public void EquipBeat_CompletesWhenAHeroWearsGear()
        {
            var save = Fresh();
            Assert.False(IntroQuests.IsComplete("intro_equip", save));
            EquipSomething(save);
            Assert.True(IntroQuests.IsComplete("intro_equip", save));
        }

        [Fact]
        public void BossAndReachBeats_GateOnHighestStage()
        {
            var save = Fresh();
            Assert.False(IntroQuests.IsComplete("intro_boss", save));
            Assert.False(IntroQuests.IsComplete("intro_reach", save));

            save.Progress.HighestStage = 1;
            Assert.True(IntroQuests.IsComplete("intro_boss", save));
            Assert.False(IntroQuests.IsComplete("intro_reach", save));

            save.Progress.HighestStage = 2;
            Assert.True(IntroQuests.IsComplete("intro_reach", save));
        }

        // ---- Sync pays, retro-completes, pays once ----

        [Fact]
        public void Sync_PaysCompletedBeatOnce()
        {
            var save = Fresh();
            long gold0 = Gold(save);
            SetKills(save, IntroQuests.SlayTarget);

            var (after, done) = IntroQuests.Sync(save, Cfg);
            Assert.Single(done);
            Assert.Equal("intro_slay", done[0].Id);
            Assert.Equal(gold0 + 100, Gold(after));
            Assert.Equal(5, Scrap(after));
            Assert.True(IntroQuests.IsClaimed("intro_slay", after));

            // A second Sync with no new deeds is a no-op and pays nothing more.
            var (again, done2) = IntroQuests.Sync(after, Cfg);
            Assert.Same(after, again);
            Assert.Empty(done2);
            Assert.Equal(gold0 + 100, Gold(again));
        }

        [Fact]
        public void Sync_RetroCompletesAlreadyDoneBeats()
        {
            // A save that already did beats 1-3 before the intro ever synced.
            var save = Fresh();
            SetKills(save, 50);
            AddBagItem(save);
            EquipSomething(save);

            var (after, done) = IntroQuests.Sync(save, Cfg);
            Assert.Equal(3, done.Count);
            Assert.Equal(new[] { "intro_slay", "intro_loot", "intro_equip" }, done.Select(q => q.Id).ToArray());
            Assert.Equal(100 + 150 + 150, Gold(after)); // sum of the three rewards
            Assert.True(IntroQuests.IsClaimed("intro_equip", after));
            Assert.False(IntroQuests.IsClaimed("intro_boss", after));
            Assert.True(IntroQuests.Active(after)); // boss + reach still pending
        }

        [Fact]
        public void Sync_FullProgress_CompletesAllAndRetiresIntro()
        {
            var save = Fresh();
            SetKills(save, 50);
            EquipSomething(save); // also satisfies loot
            save.Progress.HighestStage = 2;

            var (after, done) = IntroQuests.Sync(save, Cfg);
            Assert.Equal(5, done.Count);
            Assert.True(IntroQuests.AllClaimed(after));
            Assert.False(IntroQuests.Active(after)); // strip retires once every beat is claimed
        }

        [Fact]
        public void Sync_NoDeeds_SharesRef()
        {
            var save = Fresh();
            var (after, done) = IntroQuests.Sync(save, Cfg);
            Assert.Same(save, after);
            Assert.Empty(done);
        }

        // ---- the rolling board is untouched ----

        [Fact]
        public void Sync_LeavesRollingBoardIntact()
        {
            var save = Fresh();
            var boardBefore = save.Quests;
            SetKills(save, IntroQuests.SlayTarget);
            var (after, _) = IntroQuests.Sync(save, Cfg);
            Assert.Same(boardBefore, after.Quests); // intro pays via currency reducers, never rerolls the board
        }

        // ---- purity ----

        [Fact]
        public void Sync_DoesNotMutateInput()
        {
            var save = Fresh();
            SetKills(save, IntroQuests.SlayTarget);
            int cursor = save.RngCursor;
            long gold0 = Gold(save);

            IntroQuests.Sync(save, Cfg);

            Assert.Equal(cursor, save.RngCursor);
            Assert.Equal(gold0, Gold(save)); // input balance unchanged
            Assert.False(IntroQuests.IsClaimed("intro_slay", save)); // input not marked claimed
        }
    }
}
