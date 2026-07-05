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

        // ---- retired kind + no-duplicate rules ----

        [Fact]
        public void CycleNeverRollsClearStages()
        {
            // Roll many replacements at varying cursors; ClearStages must never come up.
            var save = Save.NewGame(1, Cfg, 0);
            for (int roll = 0; roll < 200; roll++)
            {
                save.Quests = new QuestBoard { RollCount = roll }; // vary the roll cursor each iteration
                var filled = Quests.EnsureBoard(save, Cfg);
                Assert.DoesNotContain(filled.Quests.Active, q => q.Kind == QuestKind.ClearStages);
            }
        }

        [Fact]
        public void BoardFilledFromEmptyHasNoDuplicateKinds()
        {
            var save = Save.NewGame(1, Cfg, 0);
            save.Quests = new QuestBoard(); // empty
            var filled = Quests.EnsureBoard(save, Cfg);

            Assert.Equal(Cfg.Balance.QuestBoardSize, filled.Quests.Active.Count);
            Assert.Equal(filled.Quests.Active.Count,
                         filled.Quests.Active.Select(q => q.Kind).Distinct().Count()); // all distinct
        }

        [Fact]
        public void CompletingAGoalRollsANonDuplicateReplacement()
        {
            var save = Save.NewGame(1, Cfg, 0); // distinct board of QuestBoardSize kinds
            var kill = save.Quests.Active.First(q => q.Kind == QuestKind.KillMonsters);

            var (next, _) = Quests.Advance(save, QuestKind.KillMonsters, kill.Target, Cfg);

            // The replacement must not duplicate a kind still on the board.
            Assert.Equal(next.Quests.Active.Count,
                         next.Quests.Active.Select(q => q.Kind).Distinct().Count());
        }

        [Fact]
        public void EnsureBoardSanitizesClearStagesAndDuplicates()
        {
            var save = Save.NewGame(1, Cfg, 0);
            // Hand-build a dirty board: a retired ClearStages quest + a duplicate KillMonsters.
            save.Quests = new QuestBoard
            {
                RollCount = 10,
                Active = new List<Quest>
                {
                    new Quest { Kind = QuestKind.KillMonsters, Target = 100, Progress = 40 },
                    new Quest { Kind = QuestKind.KillMonsters, Target = 100, Progress = 5 }, // dupe kind
                    new Quest { Kind = QuestKind.ClearStages,  Target = 3,   Progress = 2 }, // retired kind
                },
            };

            var clean = Quests.EnsureBoard(save, Cfg);

            Assert.DoesNotContain(clean.Quests.Active, q => q.Kind == QuestKind.ClearStages); // retired dropped
            Assert.Equal(clean.Quests.Active.Count,
                         clean.Quests.Active.Select(q => q.Kind).Distinct().Count());          // no dupes
            Assert.Equal(Cfg.Balance.QuestBoardSize, clean.Quests.Active.Count);               // topped back up
            // The FIRST KillMonsters (Progress 40) is kept; the dupe's progress is forfeit.
            Assert.Contains(clean.Quests.Active, q => q.Kind == QuestKind.KillMonsters && q.Progress == 40);
        }

        [Fact]
        public void EnsureBoardIsANoOpOnAlreadyCleanFullBoard()
        {
            var save = Save.NewGame(1, Cfg, 0); // already clean + full
            Assert.Same(save, Quests.EnsureBoard(save, Cfg));
        }

        [Fact]
        public void EarlyCompletionMustNotDuplicateALaterSurvivor()
        {
            // Regression: replacements dedup against the kinds that SURVIVE the call, not just the
            // prefix of the board processed so far. Board: completing KillMonsters FIRST, with the
            // roll cursor (1) pointing straight at SalvageItems — which survives later in the list.
            // The buggy version dedup'd against an empty prefix and rolled a second SalvageItems.
            var save = Save.NewGame(1, Cfg, 0);
            save.Quests = new QuestBoard
            {
                RollCount = 1, // KindAt(1) = SalvageItems in the current cycle order
                Active = new List<Quest>
                {
                    new Quest { Kind = QuestKind.KillMonsters, Target = 10, Progress = 9 }, // completes first
                    new Quest { Kind = QuestKind.SalvageItems, Target = 50, Progress = 0 },
                    new Quest { Kind = QuestKind.EarnGold,     Target = 500, Progress = 0 },
                },
            };

            var (next, completed) = Quests.Advance(save, QuestKind.KillMonsters, 1, Cfg);

            Assert.Single(completed);
            Assert.Equal(next.Quests.Active.Count,
                         next.Quests.Active.Select(q => q.Kind).Distinct().Count()); // no dupes
        }
    }
}
