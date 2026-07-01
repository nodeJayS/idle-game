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
        /// <summary>
        /// Raw append — no cap, no salvage. The low-level primitive used for tests and
        /// admin/grant paths. Real loot drops should go through <see cref="AddLoot"/>.
        /// </summary>
        public static SaveState AddItems(SaveState save, IReadOnlyList<Item> items)
        {
            var nextInventory = new List<Item>(save.Inventory);
            nextInventory.AddRange(items);

            return WithInventory(save, nextInventory);
        }

        /// <summary>The outcome of committing dropped loot under the cap + auto-salvage.</summary>
        public sealed class LootResult
        {
            public SaveState Save = null!;
            public List<Item> Stored = new List<Item>();    // went into the bag
            public List<Item> Salvaged = new List<Item>();  // auto-salvaged to scrap (<= threshold)
            public List<Item> Rejected = new List<Item>();  // bag full + above threshold => left behind
            public long ScrapGained;
            public bool BagFull => Rejected.Count > 0;
        }

        /// <summary>
        /// Number of LOOSE (unequipped) items in the bag — what the cap counts. Equipped
        /// gear stays in the pool but doesn't occupy a bag slot.
        /// </summary>
        public static int LooseCount(SaveState save)
        {
            var equipped = EquippedIds(save);
            int n = 0;
            foreach (var it in save.Inventory)
                if (!equipped.Contains(it.Id)) n++;
            return n;
        }

        /// <summary>
        /// Commit dropped loot under <see cref="BalanceConstants.InventoryCap"/> (loose
        /// items only). With auto-salvage on, any drop at or below
        /// <paramref name="autoSalvageMax"/> converts to scrap instead of taking a slot.
        /// Remaining items are stored; when the bag is full they're stored anyway if
        /// <paramref name="allowOverflow"/> (idle / boss / special-stage rewards may push
        /// past the cap) or otherwise REJECTED (live farm pickups). Items already owned
        /// are never destroyed. Pure: returns a new save in the result.
        /// </summary>
        public static LootResult AddLoot(SaveState save, IReadOnlyList<Item> items, GameConfig cfg,
                                         Rarity? autoSalvageMax, bool allowOverflow = false)
        {
            var result = new LootResult();
            var nextInventory = new List<Item>(save.Inventory);
            int loose = LooseCount(save);
            int cap = cfg.Balance.InventoryCap;
            long scrap = 0;

            foreach (var item in items)
            {
                if (autoSalvageMax != null && item.Rarity <= autoSalvageMax.Value)
                {
                    scrap += cfg.Balance.ScrapValue(item.Rarity, item.ItemLevel);
                    result.Salvaged.Add(item);
                }
                else if (allowOverflow || loose < cap)
                {
                    nextInventory.Add(item);
                    loose++;
                    result.Stored.Add(item);
                }
                else
                {
                    result.Rejected.Add(item); // bag full + no overflow => left behind, nothing destroyed
                }
            }

            result.ScrapGained = scrap;
            result.Save = Build(save, nextInventory, AddScrap(save.Currencies, scrap));
            return result;
        }

        // ---- Reforge (item shop): the modifier-shop gamble verb, pointed at gear ----

        /// <summary>Gold + scrap to reforge an item — scales with its item level and rarity.</summary>
        public static (long gold, long scrap) ReforgeCost(Item item, GameConfig cfg)
        {
            long mult = (1 + item.ItemLevel) * (1 + (int)item.Rarity);
            return (cfg.Balance.ReforgeBaseGold * mult, cfg.Balance.ReforgeBaseScrap * mult);
        }

        /// <summary>True if the item is owned, has at least one reforgeable (normal, pool-rolled) affix,
        /// and the player can afford the cost. Imprint affixes aren't reforgeable.</summary>
        public static bool CanReforge(SaveState save, string itemId, GameConfig cfg)
        {
            var item = save.Inventory.Find(i => i.Id == itemId);
            if (item == null || !item.Affixes.Exists(a => cfg.AffixPool.Exists(d => d.Stat == a.Stat))) return false;
            var (g, s) = ReforgeCost(item, cfg);
            long gold = save.Currencies.TryGetValue("gold", out var gv) ? gv : 0;
            long scrap = save.Currencies.TryGetValue("scrap", out var sv) ? sv : 0;
            return gold >= g && scrap >= s;
        }

        /// <summary>Spend gold+scrap to re-roll an item's NORMAL affix values by ±ModShopRoll, clamped to
        /// each affix's legit [min,max] for its item level. Imprint affixes (not in the pool) are kept
        /// as-is. No-op (shares the ref) if it can't be reforged/afforded. Deterministic via the save's
        /// own rng cursor (advanced + persisted). Pure — returns a new save with a fresh item copy.</summary>
        public static SaveState Reforge(SaveState save, string itemId, GameConfig cfg)
        {
            if (!CanReforge(save, itemId, cfg)) return save;
            var item = save.Inventory.Find(i => i.Id == itemId)!;
            var (gold, scrap) = ReforgeCost(item, cfg);

            var rng = new Rng(save.RngSeed, save.RngCursor);
            var newAffixes = new List<Affix>(item.Affixes.Count);
            foreach (var a in item.Affixes)
            {
                var def = cfg.AffixPool.Find(d => d.Stat == a.Stat);
                if (def == null) { newAffixes.Add(new Affix { Stat = a.Stat, Value = a.Value }); continue; } // imprint: keep
                double min = def.ValueMinPerItemLevel * item.ItemLevel;
                double max = def.ValueMaxPerItemLevel * item.ItemLevel;
                double rolled = a.Value * (1.0 + rng.RandRange(cfg.Balance.ModShopRollMin, cfg.Balance.ModShopRollMax));
                newAffixes.Add(new Affix { Stat = a.Stat, Value = Math.Min(max, Math.Max(min, rolled)) });
            }

            var newItem = new Item { Id = item.Id, BaseId = item.BaseId, Rarity = item.Rarity, ItemLevel = item.ItemLevel, Affixes = newAffixes };
            var nextInventory = new List<Item>(save.Inventory);
            nextInventory[nextInventory.FindIndex(i => i.Id == itemId)] = newItem;

            var currencies = new Dictionary<string, long>(save.Currencies);
            currencies["gold"] = (save.Currencies.TryGetValue("gold", out var g) ? g : 0) - gold;
            currencies["scrap"] = (save.Currencies.TryGetValue("scrap", out var s) ? s : 0) - scrap;

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = rng.Cursor, // persist so the roll can't be re-rolled
                Heroes = save.Heroes,
                Party = save.Party,
                LeaderHeroId = save.LeaderHeroId,
                Inventory = nextInventory,
                Currencies = currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                LastClaimAt = save.LastClaimAt,
            };
        }

        /// <summary>
        /// Manually salvage one loose item to scrap. Throws on unknown item or one that's
        /// equipped (so the player can never accidentally scrap worn gear). Pure.
        /// </summary>
        public static SaveState SalvageItem(SaveState save, string itemId, GameConfig cfg)
        {
            var item = save.Inventory.Find(i => i.Id == itemId)
                ?? throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" not in inventory");
            if (IsEquippedAnywhere(save, itemId))
                throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" is equipped");

            var nextInventory = new List<Item>(save.Inventory);
            nextInventory.RemoveAll(i => i.Id == itemId);
            long scrap = cfg.Balance.ScrapValue(item.Rarity, item.ItemLevel);
            return Build(save, nextInventory, AddScrap(save.Currencies, scrap));
        }

        /// <summary>
        /// Mass-salvage: convert EVERY loose (unequipped) item with Rarity &lt;= <paramref name="cap"/>
        /// to scrap in one action. Equipped gear is never touched (the same guard as
        /// <see cref="SalvageItem"/>, applied per item instead of thrown). Returns the new save
        /// plus how many items were scrapped and the scrap gained; a no-match call returns the
        /// input save unchanged. Pure.
        /// </summary>
        public static (SaveState Save, int Count, long Scrap) SalvageAllUpTo(SaveState save, Rarity cap, GameConfig cfg)
        {
            var equipped = EquippedIds(save);
            var nextInventory = new List<Item>(save.Inventory.Count);
            int count = 0;
            long scrap = 0;
            foreach (var it in save.Inventory)
            {
                if (it.Rarity <= cap && !equipped.Contains(it.Id))
                {
                    count++;
                    scrap += cfg.Balance.ScrapValue(it.Rarity, it.ItemLevel);
                }
                else
                {
                    nextInventory.Add(it);
                }
            }
            if (count == 0) return (save, 0, 0);
            return (Build(save, nextInventory, AddScrap(save.Currencies, scrap)), count, scrap);
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
        /// The hero's stat blocks <b>before</b> (current gear) and <b>after</b> swapping
        /// <paramref name="candidate"/> into its slot. The shared basis for both the raw-stat
        /// compare (<see cref="CompareForHero"/>) and the derived DPS/Effective-Life deltas (B2) —
        /// derived stats are non-linear in the raw stats, so the hover preview needs both full
        /// blocks, not just the delta.
        /// </summary>
        public static (StatBlock before, StatBlock after) ComparePairForHero(SaveState save, string heroId, Item candidate, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId)
                ?? throw new InvalidOperationException($"ComparePairForHero: hero \"{heroId}\" not owned");

            if (!cfg.ItemBases.TryGetValue(candidate.BaseId, out var candBase))
                throw new InvalidOperationException($"ComparePairForHero: unknown item base \"{candidate.BaseId}\"");

            var current = Stats.ResolveEquipped(save, hero);
            var before = Stats.ComputeHeroStats(hero, cfg, current);

            // after = current gear minus whatever occupies the candidate's slot, plus the candidate
            var after = new List<Item>();
            foreach (var it in current)
                if (!(cfg.ItemBases.TryGetValue(it.BaseId, out var b) && b.Slot == candBase.Slot))
                    after.Add(it);
            after.Add(candidate);
            var afterStats = Stats.ComputeHeroStats(hero, cfg, after);
            return (before, afterStats);
        }

        /// <summary>
        /// Stat delta (after − before) of equipping <paramref name="candidate"/> on a
        /// hero, swapping it into its slot. Drives the green▲/red▼ compare UI.
        /// </summary>
        public static StatBlock CompareForHero(SaveState save, string heroId, Item candidate, GameConfig cfg)
        {
            var (before, afterStats) = ComparePairForHero(save, heroId, candidate, cfg);

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

        private static HashSet<string> EquippedIds(SaveState save)
        {
            var set = new HashSet<string>();
            foreach (var h in save.Heroes)
                foreach (var id in h.Equipped.Values) set.Add(id);
            return set;
        }

        /// <summary>Clone the currency map and credit <paramref name="amount"/> scrap (no-op if 0).</summary>
        private static Dictionary<string, long> AddScrap(Dictionary<string, long> currencies, long amount)
        {
            var next = new Dictionary<string, long>(currencies);
            if (amount != 0) next["scrap"] = (next.TryGetValue("scrap", out var s) ? s : 0) + amount;
            return next;
        }

        private static HeroInstance CloneHero(HeroInstance hero, Dictionary<EquipSlot, string> equipped) => new HeroInstance
        {
            Id = hero.Id,
            DefId = hero.DefId,
            Level = hero.Level,
            Xp = hero.Xp,
            Equipped = equipped,
            SkillLoadout = hero.SkillLoadout,
            SkillRanks = hero.SkillRanks,
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
                LeaderHeroId = save.LeaderHeroId,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                Quests = save.Quests,
                Modifiers = save.Modifiers,
                LastClaimAt = save.LastClaimAt,
            };
        }

        private static SaveState WithInventory(SaveState save, List<Item> nextInventory)
            => Build(save, nextInventory, save.Currencies);

        /// <summary>Copy a save swapping inventory + currencies (everything else shared by ref).</summary>
        private static SaveState Build(SaveState save, List<Item> nextInventory, Dictionary<string, long> nextCurrencies) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            LeaderHeroId = save.LeaderHeroId,
            Inventory = nextInventory,
            Currencies = nextCurrencies,
            Progress = save.Progress,
            Quests = save.Quests,
            Modifiers = save.Modifiers,
            LastClaimAt = save.LastClaimAt,
        };
    }
}
