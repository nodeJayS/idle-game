#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Inventory + equipment reducers — pure: each returns a NEW SaveState, input
    /// untouched (same convention as <see cref="Party"/>). Storage model: the
    /// inventory is the pool of ALL owned items; a hero's Equipped maps slot→itemId
    /// referencing items in that pool (equipping does not remove them from it).
    /// </summary>
    public static class Inventory
    {
        /// <summary>Append items (e.g. a run's PendingLoot) to the inventory.</summary>
        public static SaveState AddItems(SaveState save, IReadOnlyList<Item> items)
        {
            var nextInventory = new List<Item>(save.Inventory);
            nextInventory.AddRange(items);

            return WithInventory(save, nextInventory);
        }

        /// <summary>
        /// Equip an owned item on a hero. The slot is derived from the item's base.
        /// Replaces whatever was in that slot (the old item stays in the pool).
        /// Throws on unknown hero, item not in inventory, unknown base, or an item
        /// already equipped by some hero.
        /// </summary>
        public static SaveState EquipItem(SaveState save, string heroId, string itemId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"EquipItem: hero \"{heroId}\" not owned");

            var item = save.Inventory.Find(i => i.Id == itemId)
                ?? throw new InvalidOperationException($"EquipItem: item \"{itemId}\" not in inventory");

            if (!cfg.ItemBases.TryGetValue(item.BaseId, out var baseDef))
                throw new InvalidOperationException($"EquipItem: unknown item base \"{item.BaseId}\"");

            if (IsEquippedAnywhere(save, itemId))
                throw new InvalidOperationException($"EquipItem: item \"{itemId}\" is already equipped");

            var nextEquipped = new Dictionary<EquipSlot, string>(hero.Equipped) { [baseDef.Slot] = itemId };
            return WithHero(save, CloneHero(hero, nextEquipped));
        }

        /// <summary>Clear a hero's slot (the item returns to the available pool).</summary>
        public static SaveState UnequipItem(SaveState save, string heroId, EquipSlot slot)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"UnequipItem: hero \"{heroId}\" not owned");

            var nextEquipped = new Dictionary<EquipSlot, string>(hero.Equipped);
            nextEquipped.Remove(slot);
            return WithHero(save, CloneHero(hero, nextEquipped));
        }

        /// <summary>
        /// Stat delta (after − before) of equipping <paramref name="candidate"/> on a
        /// hero, swapping it into its slot. Drives the green▲/red▼ compare UI later.
        /// </summary>
        public static StatBlock CompareForHero(SaveState save, string heroId, Item candidate, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"CompareForHero: hero \"{heroId}\" not owned");

            if (!cfg.ItemBases.TryGetValue(candidate.BaseId, out var candBase))
                throw new InvalidOperationException($"CompareForHero: unknown item base \"{candidate.BaseId}\"");

            var current = Stats.ResolveEquipped(save, hero);
            var before = Stats.ComputeHeroStats(hero, cfg, current);

            // after = current gear minus whatever occupies the candidate's slot, plus the candidate
            var after = new List<Item>();
            foreach (var it in current)
                if (!(cfg.ItemBases.TryGetValue(it.BaseId, out var b) && b.Slot == candBase.Slot))
                    after.Add(it);
            after.Add(candidate);
            var afterStats = Stats.ComputeHeroStats(hero, cfg, after);

            var delta = new StatBlock();
            foreach (StatKey k in Enum.GetValues(typeof(StatKey)))
            {
                double d = afterStats.Get(k) - before.Get(k);
                if (d != 0) delta[k] = d;
            }
            return delta;
        }

        private static bool IsEquippedAnywhere(SaveState save, string itemId)
        {
            foreach (var h in save.Heroes)
                if (h.Equipped.ContainsValue(itemId)) return true;
            return false;
        }

        private static HeroInstance CloneHero(HeroInstance hero, Dictionary<EquipSlot, string> equipped) => new HeroInstance
        {
            Id = hero.Id,
            DefId = hero.DefId,
            Level = hero.Level,
            Xp = hero.Xp,
            Equipped = equipped,
            SkillLoadout = hero.SkillLoadout,
        };

        private static SaveState WithHero(SaveState save, HeroInstance updated)
        {
            var nextHeroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes) nextHeroes.Add(h.Id == updated.Id ? updated : h);

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = nextHeroes,
                Party = save.Party,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                LastClaimAt = save.LastClaimAt,
            };
        }

        private static SaveState WithInventory(SaveState save, List<Item> nextInventory) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            Inventory = nextInventory,
            Currencies = save.Currencies,
            Progress = save.Progress,
            LastClaimAt = save.LastClaimAt,
        };
    }
}
