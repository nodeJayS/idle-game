#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// What an offline (or any time-skip) accrual yielded. Transient — never saved;
    /// it's the payload for the "while you were away" claim modal. M5.1 fills the
    /// scalar totals (Preview); M5.2 (Claim) also rolls <see cref="Items"/>.
    /// </summary>
    public sealed class IdleReport
    {
        public long ElapsedMs;   // capped elapsed time the rewards were computed over
        public bool Capped;      // true if the real gap exceeded IdleCapHours
        public long Gold;
        public long Xp;
        public int LootCount;    // number of items to roll (rolled in Claim)
        public List<Item> Items = new List<Item>(); // all rolled items (pre cap/salvage)
        public long ScrapGained; // scrap from auto-salvaged offline drops

        public bool IsEmpty => ElapsedMs <= 0 || (Gold == 0 && Xp == 0 && LootCount == 0);
    }

    /// <summary>
    /// Idle accrual — pure math, not a running timer (design §5.1). Income keys off
    /// the player's HIGHEST CLEARED stage, so being stuck never starves idle income.
    /// Offline yield is <see cref="BalanceConstants.OfflineRate"/> of the online rate.
    /// </summary>
    public static class Idle
    {
        /// <summary>
        /// Compute the rewards for the elapsed time since <c>save.LastClaimAt</c> up to
        /// <c>now</c> (both epoch ms), without rolling items or mutating anything.
        /// Elapsed is clamped to [0, IdleCapHours]; a clock running backwards (now &lt;
        /// LastClaimAt) and an uncleared account (HighestStage == 0) both yield nothing.
        /// </summary>
        public static IdleReport Preview(SaveState save, GameConfig cfg, long now)
        {
            var report = new IdleReport();

            long capMs = (long)(cfg.Balance.IdleCapHours * 3600_000.0);
            long rawMs = now - save.LastClaimAt;
            if (rawMs <= 0) return report;            // no time passed / clock skew

            report.Capped = rawMs > capMs;
            report.ElapsedMs = Math.Min(rawMs, capMs);

            int highest = save.Progress.HighestStage;
            if (highest <= 0) return report;          // nothing cleared yet => no income

            double sec = report.ElapsedMs / 1000.0;
            double hours = report.ElapsedMs / 3600_000.0;
            double rate = cfg.Balance.OfflineRate;

            report.Gold = (long)Math.Floor(cfg.Balance.GoldPerSec(highest) * sec * rate);
            report.Xp = (long)Math.Floor(cfg.Balance.XpPerSec(highest) * sec * rate);
            report.LootCount = (int)Math.Floor(cfg.Balance.LootRollsPerHour(highest) * hours * rate);

            return report;
        }

        /// <summary>
        /// Apply an offline accrual: roll the report's loot, credit gold + party XP +
        /// items, and advance LastClaimAt to <c>now</c> (forfeiting any excess past the
        /// cap). Pure — returns a NEW save; the input is untouched. The returned report
        /// carries the rolled <see cref="IdleReport.Items"/> for the claim modal.
        /// Loot uses an ephemeral Rng seeded from (RngSeed, LastClaimAt) so the result
        /// is deterministic without touching the persisted combat cursor.
        /// </summary>
        public static (SaveState next, IdleReport report) Claim(SaveState save, GameConfig cfg, long now,
                                                                Rarity? autoSalvageMax = null)
        {
            var report = Preview(save, cfg, now);
            if (report.ElapsedMs <= 0) return (save, report); // nothing to claim / clock skew

            int highest = save.Progress.HighestStage;
            if (report.LootCount > 0)
            {
                var stage = cfg.Stages.Find(s => s.Stage == highest);
                if (stage != null)
                {
                    var ctx = LootContext.ForStage(stage);
                    uint seed = unchecked((uint)(save.RngSeed ^ (ulong)save.LastClaimAt));
                    var rng = new Rng(seed);
                    for (int i = 0; i < report.LootCount; i++)
                        report.Items.Add(Loot.RollContextItem(rng, ctx, cfg));
                }
            }

            // Reuse the tested reducers, then clone once more for gold + the new clock.
            var next = Progression.GrantPartyXp(save, (int)Math.Min(report.Xp, int.MaxValue), cfg);
            var loot = Inventory.AddLoot(next, report.Items, cfg, autoSalvageMax, allowOverflow: true); // idle may overfill
            next = loot.Save;
            report.ScrapGained = loot.ScrapGained;

            var currencies = new Dictionary<string, long>(next.Currencies);
            currencies["gold"] = currencies.GetValueOrDefault("gold") + report.Gold;

            return (new SaveState
            {
                Version = next.Version,
                RngSeed = next.RngSeed,
                RngCursor = next.RngCursor,
                Heroes = next.Heroes,
                Party = next.Party,
                LeaderHeroId = next.LeaderHeroId,
                Inventory = next.Inventory,
                Currencies = currencies,
                Progress = next.Progress,
                Quests = next.Quests,
                Modifiers = next.Modifiers,
                LastClaimAt = now,
            }, report);
        }
    }
}
