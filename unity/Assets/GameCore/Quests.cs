#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Rolling goal board (the goal-ladder pillar): a few always-active short-term goals that
    /// pay out and immediately replace themselves, so there's always a near-term carrot. Pure
    /// reducers — the client feeds game events in via <see cref="Advance"/>. Goal kinds cycle
    /// deterministically by <see cref="QuestBoard.RollCount"/>; targets and rewards scale with the
    /// player's highest stage so a goal stays meaningful as the run grows. Mirrors the
    /// Party/Inventory/Skills reducer style (return a new SaveState, input untouched).
    /// </summary>
    public static class Quests
    {
        // Replacement goals cycle through these in order, so the board stays varied.
        private static readonly QuestKind[] Cycle =
        {
            QuestKind.KillMonsters, QuestKind.SalvageItems, QuestKind.EarnGold,
            QuestKind.ClearStages, QuestKind.FindRarePlus,
        };

        /// <summary>Top the board up to <see cref="BalanceConstants.QuestBoardSize"/> with fresh
        /// goals. Pure. Called at New Game and on load (backfills older saves with no board).</summary>
        public static SaveState EnsureBoard(SaveState save, GameConfig cfg)
        {
            int size = Math.Max(0, cfg.Balance.QuestBoardSize);
            if (save.Quests.Active.Count >= size) return save;

            int stage = Math.Max(1, save.Progress.HighestStage);
            var board = new QuestBoard { RollCount = save.Quests.RollCount, Active = new List<Quest>(save.Quests.Active) };
            while (board.Active.Count < size)
                board.Active.Add(NextQuest(board.RollCount++, stage, cfg));
            return WithQuests(save, board);
        }

        /// <summary>Record <paramref name="amount"/> progress toward every active goal of
        /// <paramref name="kind"/>. Any goal that reaches its target pays out (gold + party XP) and
        /// is replaced by a fresh one, keeping the board full. Returns the updated save plus the
        /// goals completed this call (for the client's feed / juice). Pure.</summary>
        public static (SaveState save, List<Quest> completed) Advance(SaveState save, QuestKind kind, long amount, GameConfig cfg)
        {
            var completed = new List<Quest>();
            if (amount <= 0 || save.Quests.Active.Count == 0) return (save, completed);

            int stage = Math.Max(1, save.Progress.HighestStage);
            int rollCount = save.Quests.RollCount;
            var next = new List<Quest>(save.Quests.Active.Count);
            foreach (var q in save.Quests.Active)
            {
                if (q.Kind != kind) { next.Add(q); continue; }

                long progress = q.Progress + amount;
                if (progress < q.Target)
                {
                    next.Add(new Quest { Kind = q.Kind, Target = q.Target, Progress = progress, RewardGold = q.RewardGold, RewardXp = q.RewardXp });
                }
                else
                {
                    completed.Add(q);
                    next.Add(NextQuest(rollCount++, stage, cfg)); // replacement keeps the board full
                }
            }

            // Pay out completed goals through the existing reward reducers (they carry the board
            // forward by ref), then swap in the new board as the final step.
            var result = save;
            foreach (var q in completed)
            {
                if (q.RewardGold > 0) result = Progression.GrantGold(result, q.RewardGold);
                if (q.RewardXp > 0) result = Progression.GrantPartyXp(result, q.RewardXp, cfg);
            }
            return (WithQuests(result, new QuestBoard { Active = next, RollCount = rollCount }), completed);
        }

        /// <summary>Build the goal for a given roll index, scaled to the player's stage.</summary>
        private static Quest NextQuest(int rollIndex, int stage, GameConfig cfg)
        {
            var kind = Cycle[((rollIndex % Cycle.Length) + Cycle.Length) % Cycle.Length];
            long target = kind switch
            {
                QuestKind.KillMonsters => 50 + 10L * stage,
                QuestKind.SalvageItems => 20 + 3L * stage,
                QuestKind.EarnGold     => Math.Max(100, cfg.Balance.GoldPerSec(stage) * 90), // ~90s of stage income
                QuestKind.ClearStages  => 3,
                QuestKind.FindRarePlus => 3,
                _ => 10,
            };
            // Reward is a bonus on top of normal income: ~45s of gold + ~30s of XP at this stage.
            long rewardGold = Math.Max(10, cfg.Balance.GoldPerSec(stage) * 45);
            int rewardXp = (int)Math.Max(5, cfg.Balance.XpPerSec(stage) * 30);
            return new Quest { Kind = kind, Target = target, Progress = 0, RewardGold = rewardGold, RewardXp = rewardXp };
        }

        private static SaveState WithQuests(SaveState save, QuestBoard quests) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            LeaderHeroId = save.LeaderHeroId,
            Inventory = save.Inventory,
            Currencies = save.Currencies,
            Progress = save.Progress,
            Quests = quests,
            Modifiers = save.Modifiers,
            LastClaimAt = save.LastClaimAt,
        };
    }
}
