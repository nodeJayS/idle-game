using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    public class QuestsTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();

        [Fact]
        public void NewGameStartsWithAFullBoard()
        {
            var save = Save.NewGame(1, Cfg, 0);

            Assert.Equal(Cfg.Balance.QuestBoardSize, save.Quests.Active.Count);
            Assert.All(save.Quests.Active, q => Assert.True(q.Target > 0 && q.Progress == 0));
            Assert.Equal(Cfg.Balance.QuestBoardSize, save.Quests.RollCount); // one roll per slot
            // Distinct kinds out of the gate (the cycle hands out different goals).
            Assert.Equal(save.Quests.Active.Count, save.Quests.Active.Select(q => q.Kind).Distinct().Count());
        }

        [Fact]
        public void EnsureBoardBackfillsAnEmptyBoard()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Quests = new QuestBoard(); // simulate an older save with no goals

            var filled = Quests.EnsureBoard(save, Cfg);
            Assert.Equal(Cfg.Balance.QuestBoardSize, filled.Quests.Active.Count);
        }

        [Fact]
        public void AdvanceProgressesOnlyTheMatchingKindAndIsPure()
        {
            var save = Save.NewGame(1, Cfg, 0);
            var kill = save.Quests.Active.First(q => q.Kind == QuestKind.KillMonsters);

            var (next, completed) = Quests.Advance(save, QuestKind.KillMonsters, 5, Cfg);

            Assert.Empty(completed); // 5 < target, nothing finished
            Assert.Equal(0, save.Quests.Active.First(q => q.Kind == QuestKind.KillMonsters).Progress); // input untouched
            Assert.Equal(5, next.Quests.Active.First(q => q.Kind == QuestKind.KillMonsters).Progress);
            // A different kind is unaffected.
            var salvageBefore = save.Quests.Active.First(q => q.Kind == QuestKind.SalvageItems).Progress;
            Assert.Equal(salvageBefore, next.Quests.Active.First(q => q.Kind == QuestKind.SalvageItems).Progress);
        }

        [Fact]
        public void CompletingAGoalPaysOutAndRollsAReplacement()
        {
            var save = Save.NewGame(1, Cfg, 0);
            Assert.Equal(0, save.Currencies.GetValueOrDefault("gold"));
            var kill = save.Quests.Active.First(q => q.Kind == QuestKind.KillMonsters);

            var (next, completed) = Quests.Advance(save, QuestKind.KillMonsters, kill.Target, Cfg);

            Assert.Single(completed);
            Assert.Equal(QuestKind.KillMonsters, completed[0].Kind);
            Assert.Equal(kill.RewardGold, next.Currencies.GetValueOrDefault("gold")); // reward credited
            Assert.Equal(Cfg.Balance.QuestBoardSize, next.Quests.Active.Count);        // board stays full
            Assert.Equal(save.Quests.RollCount + 1, next.Quests.RollCount);            // one replacement rolled
            // The finished goal was replaced by a fresh (Progress 0) one, not left completed.
            Assert.DoesNotContain(next.Quests.Active, q => q.Progress >= q.Target);
        }
    }
}
