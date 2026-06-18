#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Mode-agnostic loot parameters. Any game mode (stage, event, multiplayer queue)
    /// builds one of these and hands it to <see cref="Loot"/> — loot never references
    /// stages or any specific mode. Extend additively (drop-chance override, loot table
    /// id, mode id…) without churning the loot signatures.
    /// </summary>
    public struct LootContext
    {
        public int ItemLevel;        // power/scaling of dropped items
        public double DropRateMult;  // bias toward higher rarity

        public static LootContext ForStage(StageDef stage) => new LootContext
        {
            ItemLevel = stage.AffixItemLevel,
            DropRateMult = stage.DropRateMult,
        };
    }

    /// <summary>
    /// The loot "dopamine engine" — seeded, deterministic, pure. Built bottom-up:
    /// M2.1 rarity roll → M2.2 affixes → M2.3 item/drop assembly (here). All
    /// randomness flows through the one <see cref="Rng"/>, so loot is unit-testable
    /// now and server-verifiable later.
    /// </summary>
    public static class Loot
    {
        /// <summary>
        /// Roll an item's rarity. Base weights come from balance
        /// (<see cref="BalanceConstants.RarityBaseWeights"/>); the context's
        /// <see cref="LootContext.DropRateMult"/> biases the distribution toward
        /// higher rarities — each rarity of rank r is scaled by DropRateMult^r. So
        /// at DropRateMult == 1 you get the raw base distribution, and richer modes
        /// shift weight upward multiplicatively (rarer items climb fastest).
        /// </summary>
        public static Rarity RollRarity(Rng rng, LootContext ctx, GameConfig cfg)
        {
            var weights = cfg.Balance.RarityBaseWeights;
            double mult = Math.Max(0.0, ctx.DropRateMult);

            // weights.Length defines how many rarities are in play; (Rarity)rank maps
            // the index to the enum (Normal = 0 … Legendary = 4).
            var entries = new List<(Rarity item, double weight)>(weights.Length);
            for (int rank = 0; rank < weights.Length; rank++)
            {
                double w = weights[rank] * Math.Pow(mult, rank);
                entries.Add(((Rarity)rank, w));
            }
            return rng.WeightedPick(entries);
        }

        /// <summary>
        /// Roll an item's affixes. Count comes from balance per rarity
        /// (<see cref="BalanceConstants.AffixCountByRarity"/>), capped at the number
        /// of eligible affixes. An affix is eligible when its stat is in the base's
        /// <see cref="ItemBaseDef.AllowedAffixes"/> AND its
        /// <see cref="AffixDef.RarityFloor"/> is met. Picks are weighted and made
        /// WITHOUT replacement (no duplicate stats); each value scales with itemLevel.
        /// TODO (later): Unique/Legendary use random counts for now; true bespoke
        /// uniques need hand-authored fixed affixes from unique-item definitions.
        /// </summary>
        public static List<Affix> RollAffixes(Rng rng, ItemBaseDef itemBase, Rarity rarity, int itemLevel, GameConfig cfg)
        {
            int count = RollAffixCount(rng, rarity, cfg);
            if (count <= 0) return new List<Affix>();

            // eligible = allowed by the base AND rarity floor met
            var eligible = new List<AffixDef>();
            foreach (var def in cfg.AffixPool)
                if ((int)def.RarityFloor <= (int)rarity && itemBase.AllowedAffixes.Contains(def.Stat))
                    eligible.Add(def);

            count = Math.Min(count, eligible.Count);

            var result = new List<Affix>(count);
            for (int i = 0; i < count; i++)
            {
                var entries = new List<(AffixDef item, double weight)>(eligible.Count);
                foreach (var d in eligible) entries.Add((d, d.Weight));

                var pick = rng.WeightedPick(entries);
                eligible.Remove(pick); // no duplicate stats on one item
                double value = rng.RandRange(pick.ValueMinPerItemLevel, pick.ValueMaxPerItemLevel) * itemLevel;
                result.Add(new Affix { Stat = pick.Stat, Value = value });
            }
            return result;
        }

        private static int RollAffixCount(Rng rng, Rarity rarity, GameConfig cfg)
        {
            var (min, max) = cfg.Balance.AffixCountByRarity[(int)rarity];
            return rng.RandInt(min, max);
        }

        /// <summary>Assemble a full item from a base + rarity + rolled affixes.</summary>
        public static Item RollItem(Rng rng, string baseId, int itemLevel, Rarity rarity, GameConfig cfg)
        {
            if (!cfg.ItemBases.TryGetValue(baseId, out var itemBase))
                throw new ArgumentException($"Unknown item base '{baseId}'", nameof(baseId));

            // Deterministic id: (seed, cursor) is unique within an rng stream and
            // replayable by a server. Snapshot the cursor before rolling affixes.
            int cursorAtCreation = rng.Cursor;
            var affixes = RollAffixes(rng, itemBase, rarity, itemLevel, cfg);

            return new Item
            {
                Id = $"i{rng.Seed:x}_{cursorAtCreation}",
                BaseId = baseId,
                Rarity = rarity,
                ItemLevel = itemLevel,
                Affixes = affixes,
            };
        }

        /// <summary>
        /// Roll a single monster's drop: nothing (returns null) or a full item. Bosses
        /// always drop; common monsters use <see cref="BalanceConstants.DropChance"/>.
        /// The base is picked uniformly (loot-table weighting is a later refinement).
        /// </summary>
        public static Item? RollDrop(Rng rng, MonsterDef monster, LootContext ctx, GameConfig cfg)
        {
            bool isBoss = monster.LootTableId == "boss";
            double chance = isBoss ? 1.0 : cfg.Balance.DropChance;
            if (rng.Next() >= chance) return null;

            // sort keys for deterministic selection (dictionary order isn't stable)
            var baseIds = new List<string>(cfg.ItemBases.Keys);
            baseIds.Sort(StringComparer.Ordinal);
            string baseId = baseIds[rng.RandInt(0, baseIds.Count - 1)];

            var rarity = RollRarity(rng, ctx, cfg);
            return RollItem(rng, baseId, ctx.ItemLevel, rarity, cfg);
        }
    }
}
