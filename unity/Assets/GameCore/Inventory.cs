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
                if (autoSalvageMax != null && item.Rarity <= autoSalvageMax.Value && !item.Locked)
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

            var newItem = new Item { Id = item.Id, BaseId = item.BaseId, Rarity = item.Rarity, ItemLevel = item.ItemLevel, Affixes = newAffixes, Enhance = item.Enhance, Locked = item.Locked };
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

        /// <summary>One enhancement attempt's outcome (the save inside is post-attempt).</summary>
        public sealed class EnhanceResult
        {
            public SaveState Save = null!;
            public bool Success;
            public int Level;      // the item's enhancement level AFTER the attempt
            public bool Dropped;   // a high-tier fail cost a level
            public long Cost;      // scrap spent
        }

        /// <summary>True if the item is owned, below Balance.EnhanceMax, and the next
        /// attempt is affordable in scrap.</summary>
        public static bool CanEnhance(SaveState save, string itemId, GameConfig cfg)
        {
            var item = save.Inventory.Find(i => i.Id == itemId);
            if (item == null || item.Enhance >= cfg.Balance.EnhanceMax) return false;
            long scrap = save.Currencies.TryGetValue("scrap", out var s) ? s : 0;
            return scrap >= cfg.Balance.EnhanceCost(item);
        }

        /// <summary>
        /// Spend scrap on one +1 enhancement attempt. +1..+5 always land; +6..+9 can
        /// fail (only the scrap is lost); attempts at/above Balance.EnhanceDropFrom
        /// DROP one level on a fail — the item itself is never destroyed. Deterministic
        /// via the save's rng cursor (advanced + persisted, same as Reforge). Returns
        /// null if it can't be attempted; otherwise pure — a fresh save + item copy.
        /// </summary>
        public static EnhanceResult? Enhance(SaveState save, string itemId, GameConfig cfg)
        {
            if (!CanEnhance(save, itemId, cfg)) return null;
            var item = save.Inventory.Find(i => i.Id == itemId)!;
            long cost = cfg.Balance.EnhanceCost(item);

            var rng = new Rng(save.RngSeed, save.RngCursor);
            int attempted = item.Enhance + 1;
            bool success = rng.Next() < cfg.Balance.EnhanceSuccess[attempted - 1];
            bool dropped = !success && attempted >= cfg.Balance.EnhanceDropFrom;
            int level = success ? attempted
                      : dropped ? Math.Max(0, item.Enhance - 1)
                      : item.Enhance;

            var newItem = new Item
            {
                Id = item.Id, BaseId = item.BaseId, Rarity = item.Rarity,
                ItemLevel = item.ItemLevel, Affixes = item.Affixes, Enhance = level, Locked = item.Locked,
            };
            var nextInventory = new List<Item>(save.Inventory);
            nextInventory[nextInventory.FindIndex(i => i.Id == itemId)] = newItem;

            var currencies = new Dictionary<string, long>(save.Currencies);
            currencies["scrap"] = (save.Currencies.TryGetValue("scrap", out var s) ? s : 0) - cost;

            var next = Build(save, nextInventory, currencies);
            next.RngCursor = rng.Cursor; // persist so the roll can't be re-rolled
            return new EnhanceResult { Save = next, Success = success, Level = level, Dropped = dropped, Cost = cost };
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
            if (item.Locked)
                throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" is locked");

            var nextInventory = new List<Item>(save.Inventory);
            nextInventory.RemoveAll(i => i.Id == itemId);
            long scrap = cfg.Balance.ScrapValue(item.Rarity, item.ItemLevel);
            return Build(save, nextInventory, AddScrap(save.Currencies, scrap));
        }

        /// <summary>
        /// Mass-salvage: convert EVERY loose item that is (1) not equipped and (2) not locked to scrap
        /// in one action — regardless of rarity (the "Salvage all" verb; the client arms a confirm step
        /// because this now destroys rares/uniques/legendaries too). Equipped gear and locked items are
        /// never touched (per-item guards, same as <see cref="SalvageItem"/>). Returns the new save plus
        /// how many items were scrapped and the scrap gained; a no-match call returns the input save
        /// unchanged. Pure.
        /// </summary>
        public static (SaveState Save, int Count, long Scrap) SalvageAll(SaveState save, GameConfig cfg)
        {
            var equipped = EquippedIds(save);
            var nextInventory = new List<Item>(save.Inventory.Count);
            int count = 0;
            long scrap = 0;
            foreach (var it in save.Inventory)
            {
                if (!it.Locked && !equipped.Contains(it.Id))
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
        /// Toggle an item's salvage <see cref="Item.Locked"/> flag (a locked item can never be salvaged).
        /// Works on BAG and EQUIPPED items alike — locking worn gear is meaningful (it stays locked when
        /// unequipped). No-op (returns the input save) on an unknown id. Pure: returns a new save with a
        /// fresh item copy in place. </summary>
        public static SaveState ToggleLock(SaveState save, string itemId)
        {
            int idx = save.Inventory.FindIndex(i => i.Id == itemId);
            if (idx < 0) return save;
            var item = save.Inventory[idx];
            var flipped = new Item
            {
                Id = item.Id, BaseId = item.BaseId, Rarity = item.Rarity, ItemLevel = item.ItemLevel,
                Affixes = item.Affixes, Enhance = item.Enhance, Locked = !item.Locked,
            };
            var nextInventory = new List<Item>(save.Inventory);
            nextInventory[idx] = flipped;
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

        /// <summary>
        /// Dissolve gear whose base no longer exists in the config (e.g. the 2026-07-02
        /// slot trim deleted Ring/Amulet/Offhand/Cape and their bases) into a full scrap
        /// refund, unequipping it from every hero. Run at load, after Migrate; idempotent
        /// and a cheap no-op on clean saves. Pure.
        /// </summary>
        public static SaveState PruneUnknownGear(SaveState save, GameConfig cfg)
        {
            var dead = new HashSet<string>();
            long refund = 0;
            foreach (var it in save.Inventory)
                if (!cfg.ItemBases.ContainsKey(it.BaseId))
                {
                    dead.Add(it.Id);
                    refund += cfg.Balance.ScrapValue(it.Rarity, it.ItemLevel);
                }
            if (dead.Count == 0) return save;

            var nextInventory = new List<Item>(save.Inventory.Count - dead.Count);
            foreach (var it in save.Inventory)
                if (!dead.Contains(it.Id)) nextInventory.Add(it);

            var nextHeroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes)
            {
                bool touched = false;
                foreach (var id in h.Equipped.Values)
                    if (dead.Contains(id)) { touched = true; break; }
                if (!touched) { nextHeroes.Add(h); continue; }
                var equipped = new Dictionary<EquipSlot, string>();
                foreach (var kv in h.Equipped)
                    if (!dead.Contains(kv.Value)) equipped[kv.Key] = kv.Value;
                nextHeroes.Add(CloneHero(h, equipped));
            }

            var next = Build(save, nextInventory, AddScrap(save.Currencies, refund));
            next.Heroes = nextHeroes;
            return next;
        }

        /// <summary>
        /// Order the bag for reading: rarity (best first), then item level (highest
        /// first), then slot, then id (a stable tiebreak so repeated sorts are no-ops).
        /// Persisted — the inventory list IS the display order. Pure.
        /// </summary>
        public static SaveState Sort(SaveState save, GameConfig cfg)
        {
            var next = new List<Item>(save.Inventory);
            next.Sort((a, b) =>
            {
                int byRarity = b.Rarity.CompareTo(a.Rarity);
                if (byRarity != 0) return byRarity;
                int byLevel = b.ItemLevel.CompareTo(a.ItemLevel);
                if (byLevel != 0) return byLevel;
                var slotA = cfg.ItemBases.TryGetValue(a.BaseId, out var ba) ? ba.Slot : (EquipSlot)int.MaxValue;
                var slotB = cfg.ItemBases.TryGetValue(b.BaseId, out var bb) ? bb.Slot : (EquipSlot)int.MaxValue;
                int bySlot = slotA.CompareTo(slotB);
                if (bySlot != 0) return bySlot;
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return WithInventory(save, next);
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
