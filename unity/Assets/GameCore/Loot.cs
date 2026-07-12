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
        public Rarity? MaxRarity;    // ceiling for rolled rarity; null = uncapped (Mythic)
        // Zone drop table (roadmap 4): drops favor this equip slot by FavoredSlotMult in
        // the base pick ("boots drop best in the ruins"). null / mult<=1 = uniform. Set
        // from the stage's zone in ForStage — Loot itself stays zone-unaware.
        public EquipSlot? FavoredSlot;
        public double FavoredSlotMult;
        // §6.2 set bonuses: 1-BASED zone tier whose three sets a Rare+ drop here may join;
        // 0 = no set roll. 1-based so a default-constructed context (LootContext is a
        // struct — copies like the chest/rank paths take `var c = ctx;`) safely means
        // "no sets", not "tier 0's sets". Every mode's context funnels through ForStage,
        // so farm/boss/idle/tower/crypt drops all carry it.
        public int SetBand;

        public static LootContext ForStage(StageDef stage, GameConfig cfg) => new LootContext
        {
            ItemLevel = stage.AffixItemLevel,
            DropRateMult = stage.DropRateMult,
            MaxRarity = Rarity.Rare, // trash/idle ceiling; Unique/Legendary/Mythic are boss-only
            FavoredSlot = cfg.ZoneForStage(stage.Stage)?.FavoredSlot,
            FavoredSlotMult = cfg.Balance.ZoneFavoredSlotMult,
            SetBand = cfg.Balance.Tier(stage.Stage) + 1,
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
            // the index to the enum (Normal = 0 … Mythic = 4). MaxRarity (when set)
            // clamps the ceiling so trash/idle can't roll boss-exclusive rarities.
            int maxRank = weights.Length - 1;
            if (ctx.MaxRarity != null) maxRank = Math.Min(maxRank, (int)ctx.MaxRarity.Value);

            var entries = new List<(Rarity item, double weight)>(maxRank + 1);
            for (int rank = 0; rank <= maxRank; rank++)
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
        /// TODO (later): Unique/Legendary/Mythic use random counts for now; true bespoke
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

            return RollContextItem(rng, ctx, cfg);
        }

        /// <summary>
        /// Roll one guaranteed item for a loot context (no drop-chance gate): pick a
        /// base (favoring the context's zone slot, if any), roll rarity, assemble. Used
        /// by <see cref="RollDrop"/> once a drop is confirmed, and directly by idle
        /// accrual which grants a fixed count.
        /// </summary>
        public static Item RollContextItem(Rng rng, LootContext ctx, GameConfig cfg)
        {
            var rarity = RollRarity(rng, ctx, cfg);
            var item = RollItem(rng, PickBaseId(rng, ctx, cfg), ctx.ItemLevel, rarity, cfg);
            RollSetMembership(rng, item, ctx, cfg);
            return item;
        }

        /// <summary>
        /// §6.2 set membership: a Rare-or-better drop from a context with a tier identity
        /// joins one of the tier's three sets with <see cref="BalanceConstants.SetDropChance"/>
        /// (uniform pick among a/b/c). Draws ride the drop's own <paramref name="rng"/>
        /// immediately after assembly — same cursor discipline as every other roll, so a
        /// membership can never be re-rolled. Ids are addressed by the set_t{tier}_{a|b|c}
        /// convention (never dictionary iteration order — determinism). Zero rng draws for
        /// sub-Rare drops or tierless contexts, so those paths keep their exact sequences.
        /// </summary>
        public static void RollSetMembership(Rng rng, Item item, LootContext ctx, GameConfig cfg)
        {
            if (ctx.SetBand <= 0 || item.Rarity < Rarity.Rare) return;
            if (rng.Next() >= cfg.Balance.SetDropChance) return;
            string id = $"set_t{ctx.SetBand - 1}_{(char)('a' + rng.RandInt(0, 2))}";
            if (cfg.Sets.ContainsKey(id)) item.SetId = id; // unknown id (content trimmed) = no set
        }

        /// <summary>
        /// A boss's guaranteed loot bundle: a count of Unique+ items (by boss tier —
        /// <paramref name="isMajor"/>) plus a few ordinary Normal–Rare extras. Each
        /// bundle item makes ONE rng draw: Mythic with <see cref="BalanceConstants.BossMythicChance"/>,
        /// else Legendary with <see cref="BalanceConstants.BossLegendaryChance"/>,
        /// otherwise Unique. Deterministic via <paramref name="rng"/>.
        /// </summary>
        public static List<Item> RollBossDrops(Rng rng, LootContext ctx, GameConfig cfg, bool isMajor)
        {
            var b = cfg.Balance;
            var (umin, umax) = isMajor ? b.MajorBossUniques : b.MiniBossUniques;
            int uniques = rng.RandInt(umin, umax);
            int extras = isMajor ? b.MajorBossExtras : b.MiniBossExtras;

            var items = new List<Item>(uniques + extras);
            for (int i = 0; i < uniques; i++)
            {
                double roll = rng.Next();
                var rarity = roll < b.BossMythicChance ? Rarity.Mythic
                           : roll < b.BossMythicChance + b.BossLegendaryChance ? Rarity.Legendary
                           : Rarity.Unique;
                var drop = RollItem(rng, PickBaseId(rng, ctx, cfg), ctx.ItemLevel, rarity, cfg);
                RollSetMembership(rng, drop, ctx, cfg);
                items.Add(drop);
            }
            for (int i = 0; i < extras; i++)
                items.Add(RollContextItem(rng, ctx, cfg)); // ordinary extras (ctx caps at Rare)
            return items;
        }

        /// <summary>
        /// A §7.3 dungeon chest's item bundle: <see cref="BalanceConstants.DungeonChestItems"/>[tier]
        /// items. The FIRST item is the headline — iron floors it at Rare; golden floors it at
        /// Unique (lifting the trash Rare cap for that one roll) and may jump straight to Mythic
        /// with <paramref name="goldenMythicChance"/> (deep bands). The rest roll the plain
        /// context. Deterministic via <paramref name="rng"/>.
        /// </summary>
        public static List<Item> RollChestDrops(Rng rng, LootContext ctx, GameConfig cfg, int tier,
                                                double goldenMythicChance)
        {
            var table = cfg.Balance.DungeonChestItems;
            var (cmin, cmax) = table[Math.Max(0, Math.Min(tier, table.Length - 1))];
            int count = rng.RandInt(cmin, cmax);
            var items = new List<Item>(Math.Max(0, count));
            for (int i = 0; i < count; i++)
            {
                if (i == 0 && tier >= ChestTier.Iron)
                {
                    Rarity floor = tier >= ChestTier.Golden ? Rarity.Unique : Rarity.Rare;
                    var headline = ctx;
                    if (tier >= ChestTier.Golden) headline.MaxRarity = null; // may exceed the trash cap
                    Rarity rarity = tier >= ChestTier.Golden && rng.Next() < goldenMythicChance
                        ? Rarity.Mythic
                        : (Rarity)Math.Max((int)RollRarity(rng, headline, cfg), (int)floor);
                    var head = RollItem(rng, PickBaseId(rng, ctx, cfg), ctx.ItemLevel, rarity, cfg);
                    RollSetMembership(rng, head, ctx, cfg);
                    items.Add(head);
                }
                else items.Add(RollContextItem(rng, ctx, cfg));
            }
            return items;
        }

        /// <summary>
        /// An elite/rare trash mob's loot bundle (Lever 1): <paramref name="count"/> guaranteed
        /// items rolled at a boosted drop rate (<paramref name="rateMult"/> × the context's
        /// DropRateMult) so rarities skew richer. Still bounded by the context's MaxRarity
        /// (Rare for trash) — Unique/Legendary/Mythic remain boss-exclusive. Deterministic.
        /// </summary>
        public static List<Item> RollRankDrops(Rng rng, LootContext ctx, GameConfig cfg, int count, double rateMult)
        {
            var boosted = ctx;
            boosted.DropRateMult = ctx.DropRateMult * Math.Max(1.0, rateMult);
            var items = new List<Item>(Math.Max(0, count));
            for (int i = 0; i < count; i++) items.Add(RollContextItem(rng, boosted, cfg));
            return items;
        }

        /// <summary>
        /// Loot-imprint (mechanical modifiers — the headline hook). A drop from a monster carrying a
        /// mechanical modifier can be stamped with that modifier's signature affix: a build-defining
        /// stat (<see cref="ModifierDef.ImprintStat"/>) the normal affix pool never rolls, so imprinted
        /// gear is obtainable ONLY by farming the dangerous mod. One rng draw per active mechanical mod
        /// the monster actually had (<paramref name="monsterMods"/>); on a hit, the stamped value =
        /// ImprintPerStrength × that mod's strength, MERGED into a same-stat affix if one exists (so a
        /// double-imprint stacks rather than duplicating). Mutates and returns the item. Deterministic.
        /// </summary>
        public static Item ImprintDrop(Rng rng, Item item, IReadOnlyList<string> monsterMods,
                                       IReadOnlyList<ModifierInstance> active, GameConfig cfg)
        {
            // Roll each rare mod the mob carried (one rng draw per candidate, deterministic), splitting
            // the hits by slot. An item holds at most ONE prefix + ONE suffix imprint; when multiple of
            // a slot hit, pick ONE at random (anti-target-farming — you can't aim for a specific one).
            var prefixHits = new List<ModifierInstance>();
            var suffixHits = new List<ModifierInstance>();
            foreach (var mi in active)
            {
                var def = mi.Def;
                if (!def.Mechanical || !ListContains(monsterMods, def.Id)) continue;
                if (rng.Next() >= def.ImprintChance) continue;
                (def.ImprintSlot == ImprintSlot.Prefix ? prefixHits : suffixHits).Add(mi);
            }

            if (prefixHits.Count > 0 && !HasImprint(item, cfg, ImprintSlot.Prefix))
                ApplyImprint(item, prefixHits[rng.RandInt(0, prefixHits.Count - 1)]);
            if (suffixHits.Count > 0 && !HasImprint(item, cfg, ImprintSlot.Suffix))
                ApplyImprint(item, suffixHits[rng.RandInt(0, suffixHits.Count - 1)]);
            return item;
        }

        private static void ApplyImprint(Item item, ModifierInstance mi)
        {
            double value = mi.Def.ImprintPerStrength * mi.Strength;
            var existing = item.Affixes.Find(a => a.Stat == mi.Def.ImprintStat);
            if (existing != null) existing.Value += value;
            else item.Affixes.Add(new Affix { Stat = mi.Def.ImprintStat, Value = value });
        }

        /// <summary>The imprint slot a stat belongs to (via its mechanical mod), or null if not an
        /// imprint stat. Each imprint stat maps to exactly one slot.</summary>
        public static ImprintSlot? ImprintSlotOfStat(StatKey stat, GameConfig cfg)
        {
            foreach (var kv in cfg.Modifiers)
                if (kv.Value.Mechanical && kv.Value.ImprintStat == stat) return kv.Value.ImprintSlot;
            return null;
        }

        private static bool HasImprint(Item item, GameConfig cfg, ImprintSlot slot)
        {
            foreach (var a in item.Affixes) if (ImprintSlotOfStat(a.Stat, cfg) == slot) return true;
            return false;
        }

        /// <summary>True if a stat is some mechanical modifier's imprint signature — i.e. it only ever
        /// reaches gear via <see cref="ImprintDrop"/> (it's in no base's AllowedAffixes). Drives the
        /// client's "imprinted item" tells (badge, tooltip line, loot-feed callout).</summary>
        public static bool IsImprintStat(StatKey stat, GameConfig cfg)
        {
            foreach (var kv in cfg.Modifiers)
                if (kv.Value.Mechanical && kv.Value.ImprintStat == stat) return true;
            return false;
        }

        /// <summary>True if an item carries any imprint affix (it was stamped by a mechanical modifier).</summary>
        public static bool IsImprinted(Item item, GameConfig cfg)
        {
            foreach (var a in item.Affixes) if (IsImprintStat(a.Stat, cfg)) return true;
            return false;
        }

        /// <summary>The mechanical modifier that imprinted this item (its "title", e.g. Volatile), or
        /// null if the item carries no imprint. Drives the titled name on imprinted gear ("Volatile
        /// Rusty Sword"). Returns the first match in case of a multi-imprint.</summary>
        public static ModifierDef? ImprintSource(Item item, GameConfig cfg)
        {
            foreach (var a in item.Affixes)
                foreach (var kv in cfg.Modifiers)
                    if (kv.Value.Mechanical && kv.Value.ImprintStat == a.Stat) return kv.Value;
            return null;
        }

        /// <summary>The mechanical modifier imprinted in a given slot (prefix → leading title word,
        /// suffix → trailing "of X"), or null if that slot is empty. Drives the titled item name.</summary>
        public static ModifierDef? ImprintForSlot(Item item, GameConfig cfg, ImprintSlot slot)
        {
            foreach (var a in item.Affixes)
                foreach (var kv in cfg.Modifiers)
                    if (kv.Value.Mechanical && kv.Value.ImprintStat == a.Stat && kv.Value.ImprintSlot == slot)
                        return kv.Value;
            return null;
        }

        // LINQ-free membership test: Unity compiles GameCore without System.Linq's implicit global
        // using, so IReadOnlyList<string>.Contains would mis-resolve to a span overload (CS7036).
        private static bool ListContains(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }

        /// <summary>Pick an item base (deterministic: keys sorted first). Uniform unless the
        /// context favors a slot (the zone drop table), which outweighs its bases.</summary>
        private static string PickBaseId(Rng rng, LootContext ctx, GameConfig cfg)
        {
            var baseIds = new List<string>(cfg.ItemBases.Keys);
            baseIds.Sort(StringComparer.Ordinal); // dictionary order isn't stable
            if (ctx.FavoredSlot == null || ctx.FavoredSlotMult <= 1.0)
                return baseIds[rng.RandInt(0, baseIds.Count - 1)];

            var entries = new List<(string item, double weight)>(baseIds.Count);
            foreach (var id in baseIds)
                entries.Add((id, cfg.ItemBases[id].Slot == ctx.FavoredSlot ? ctx.FavoredSlotMult : 1.0));
            return rng.WeightedPick(entries);
        }
    }
}
