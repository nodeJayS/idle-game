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
        // Replacement goals cycle through these in order, so the board stays varied. WHY every rollable
        // kind must complete PASSIVELY from normal AFK play: the board tops itself up automatically, so a
        // goal the player can't progress just by leaving the game running would deadlock a slot. Kills,
        // gold, and rare drops all happen by themselves; SalvageItems is fed by AUTO-salvage
        // (CombatView.CommitPending ~line 849), not a manual verb. ClearStages was DROPPED from the
        // rotation (though QuestKind.ClearStages stays in the enum — persisted numerically, never removed)
        // because it bottlenecks on the player actively pushing new stages, which idle play doesn't do.
        private static readonly QuestKind[] Cycle =
        {
            QuestKind.KillMonsters, QuestKind.SalvageItems, QuestKind.EarnGold,
            QuestKind.FindRarePlus,
        };

        /// <summary>Top the board up to <see cref="BalanceConstants.QuestBoardSize"/> with fresh goals,
        /// AND sanitize any bad quests already on it: an active ClearStages (a now-retired kind) and any
        /// duplicate-kind quests (keep the first of each kind, replace the rest) are re-rolled with the
        /// no-duplicate rule. Progress on replaced quests is forfeit — they were the wrong kind anyway.
        /// Pure. Called at New Game and on load (also backfills older saves with no board).</summary>
        public static SaveState EnsureBoard(SaveState save, GameConfig cfg)
        {
            int size = Math.Max(0, cfg.Balance.QuestBoardSize);
            int stage = Math.Max(1, save.Progress.HighestStage);
            int rollCount = save.Quests.RollCount;

            // Keep only the first quest of each rollable kind; drop retired kinds (ClearStages) entirely.
            // Replacements roll below, deduping against what's kept.
            var kept = new List<Quest>(save.Quests.Active.Count);
            var seen = new HashSet<QuestKind>();
            foreach (var q in save.Quests.Active)
            {
                if (!Rollable(q.Kind)) continue;      // retired kind (ClearStages) — never keep
                if (!seen.Add(q.Kind)) continue;      // duplicate kind — keep the first, drop the rest
                kept.Add(q);
            }

            // Already clean and full? Share the ref (idempotent no-op on a good board).
            if (kept.Count == save.Quests.Active.Count && kept.Count >= size) return save;

            var board = new QuestBoard { RollCount = rollCount, Active = kept };
            var present = new HashSet<QuestKind>(seen);
            while (board.Active.Count < size)
            {
                var fresh = NextQuest(ref rollCount, stage, cfg, present);
                present.Add(fresh.Kind);
                board.Active.Add(fresh);
            }
            board.RollCount = rollCount;
            return WithQuests(save, board);
        }

        /// <summary>Is this kind still rolled onto the board? ClearStages is retired (kept in the enum for
        /// save compatibility, but never rolled — see the <see cref="Cycle"/> rationale).</summary>
        private static bool Rollable(QuestKind kind) => Array.IndexOf(Cycle, kind) >= 0;

        /// <summary>The cycle kind at a roll cursor (positive/negative safe).</summary>
        private static QuestKind KindAt(int rollIndex) => Cycle[((rollIndex % Cycle.Length) + Cycle.Length) % Cycle.Length];


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

            // Replacements must dedup against every kind that SURVIVES this call — not just the quests
            // processed so far. A completed quest early in the list would otherwise dedup against an
            // almost-empty prefix and could re-roll a kind still sitting later on the board. Precompute
            // the survivor kinds, then let each replacement claim its kind as it rolls.
            var present = new HashSet<QuestKind>();
            foreach (var q in save.Quests.Active)
                if (!(q.Kind == kind && q.Progress + amount >= q.Target)) present.Add(q.Kind);

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
                    var fresh = NextQuest(ref rollCount, stage, cfg, present); // replacement keeps the board full
                    present.Add(fresh.Kind); // claim it so a second replacement this call can't repeat it
                    next.Add(fresh);
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

        /// <summary>Build the goal for the current roll cursor, scaled to the player's stage, skipping any
        /// kind in <paramref name="present"/> so the board never carries a duplicate kind. Callers pass the
        /// FULL set of kinds that will share the board with this roll (survivors + earlier replacements)
        /// and claim the chosen kind afterwards. <paramref name="rollCount"/> is advanced (by ref) through
        /// every skipped kind AND the chosen one, so the persisted cursor consumes rolls the same way every
        /// time (determinism). With <see cref="Cycle"/> distinct kinds and a board size below that count
        /// this always terminates; if the set somehow already holds every distinct kind (size ≥ distinct
        /// kinds) we fall back to allowing a dupe rather than looping forever.</summary>
        private static Quest NextQuest(ref int rollCount, int stage, GameConfig cfg, HashSet<QuestKind> present)
        {
            QuestKind kind = KindAt(rollCount);
            // Advance through the cycle until we hit a kind not already present (guarded so a full
            // set of every distinct kind can't spin: at most Cycle.Length probes, then allow the dupe).
            for (int probe = 0; probe < Cycle.Length && present.Contains(kind); probe++)
                kind = KindAt(++rollCount);
            rollCount++; // consume the chosen roll too, so the next call starts one past it

            long target = kind switch
            {
                QuestKind.KillMonsters => 50 + 10L * stage,
                QuestKind.SalvageItems => 20 + 3L * stage,
                QuestKind.EarnGold     => Math.Max(100, cfg.Balance.GoldPerSec(stage) * 90), // ~90s of stage income
                QuestKind.ClearStages  => 3, // retired kind — unreachable via Cycle, kept for the switch's totality
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
            GachaPity = save.GachaPity,
            LastClaimAt = save.LastClaimAt,
        };
    }
}
