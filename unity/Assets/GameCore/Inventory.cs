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
            public List<Item> Salvaged = new List<Item>();  // auto-salvaged to scrap (loot filter)
            public List<Item> Rejected = new List<Item>();  // bag full + kept by the filter => left behind
            public long ScrapGained;
            // Codex (10.15): set ids whose slot bitmask reached ALL of a set's slots in THIS commit
            // (the last piece landed) — for the client's "set complete" toast. Empty on most commits.
            public List<string> SetsCompleted = new List<string>();
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
        /// items only). Any drop the save's loot filter marks for auto-salvage
        /// (<see cref="WouldAutoSalvage"/>: per-slot rarity floors + the imprint guard)
        /// converts to scrap instead of taking a slot.
        /// Remaining items are stored; when the bag is full they're stored anyway if
        /// <paramref name="allowOverflow"/> (idle / boss / special-stage rewards may push
        /// past the cap) or otherwise REJECTED (live farm pickups). Items already owned
        /// are never destroyed. Pure: returns a new save in the result.
        /// </summary>
        public static LootResult AddLoot(SaveState save, IReadOnlyList<Item> items, GameConfig cfg,
                                         bool allowOverflow = false)
        {
            var result = new LootResult();
            var nextInventory = new List<Item>(save.Inventory);
            int loose = LooseCount(save);
            int cap = cfg.Balance.InventoryCap;
            long scrap = 0;

            // Codex set discovery (10.15): the ONE loot-commit path stamps every incoming set piece's
            // slot bit. Cloned lazily — only when a genuinely new bit lands — so a commit with no new
            // discovery shares the Codex ref (the pure-reducer convention). completeMask = every slot a
            // set can be collected across (derived from the SETS' actual slots — see Codex.SetCompleteMask).
            Dictionary<string, int>? setSeen = null;
            int completeMask = Codex.SetCompleteMask(cfg);

            foreach (var item in items)
            {
                // Stamp BEFORE the salvage decision: seen is seen, so a set piece the filter
                // AUTO-SALVAGES still counts as discovered (deliberate). Needs the base to resolve the
                // slot; an item whose base is unknown can't be classified, so it's skipped.
                if (item.SetId != null && cfg.ItemBases.TryGetValue(item.BaseId, out var setBase))
                {
                    var cur = setSeen ?? save.Progress.Codex.SetSeen;
                    int had = cur.TryGetValue(item.SetId, out var m) ? m : 0;
                    int bit = 1 << (int)setBase.Slot;
                    if ((had & bit) == 0)
                    {
                        setSeen ??= new Dictionary<string, int>(save.Progress.Codex.SetSeen);
                        int now = had | bit;
                        setSeen[item.SetId] = now;
                        // Completion crossing: the piece that lands the final slot flips the set complete.
                        if (had != completeMask && now == completeMask) result.SetsCompleted.Add(item.SetId);
                    }
                }

                if (WouldAutoSalvage(save, item, cfg))
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
            var nextCurrencies = AddScrap(save.Currencies, scrap);
            // Thread the new Codex only when a new set bit actually landed (else share the ref via Build).
            result.Save = setSeen == null
                ? Build(save, nextInventory, nextCurrencies)
                : BuildWithCodex(save, nextInventory, nextCurrencies,
                    new CodexState { Kills = save.Progress.Codex.Kills, SetSeen = setSeen });
            return result;
        }

        // ---- Loot filter (10.5a): per-slot auto-salvage floors + the imprint guard ----

        /// <summary>True if the item carries any affix whose stat is outside <c>cfg.AffixPool</c> —
        /// the Tower-gated imprint prize (the same predicate <see cref="Reforge"/> uses to spare
        /// imprint affixes). Deliberately wider than Loot.IsImprinted's registered-modifier check:
        /// anything the pool can't roll is irreplaceable, so the salvage guard treats it as such.</summary>
        public static bool IsImprinted(Item item, GameConfig cfg)
        {
            foreach (var a in item.Affixes)
                if (!cfg.AffixPool.Exists(d => d.Stat == a.Stat)) return true;
            return false;
        }

        /// <summary>
        /// THE auto-salvage predicate — every auto-salvage decision (live drops, idle claim)
        /// reads this one gate. True when the save's loot filter marks the drop for scrap:
        /// its slot has a floor set and the item's rarity is at or below it ("X &amp; below").
        /// Never true for a locked item, for imprinted gear while
        /// <see cref="LootFilterState.NeverSalvageImprinted"/> is on, or for an item whose
        /// base is unknown (defensive: never destroy what we can't classify).
        /// </summary>
        public static bool WouldAutoSalvage(SaveState save, Item item, GameConfig cfg)
        {
            if (item.Locked) return false;
            var filter = save.Progress.Loot;
            if (filter.NeverSalvageImprinted && IsImprinted(item, cfg)) return false;
            if (!cfg.ItemBases.TryGetValue(item.BaseId, out var baseDef)) return false;
            if (!filter.SalvageMaxBySlot.TryGetValue(baseDef.Slot, out var floor)) return false; // no floor => off
            return item.Rarity <= floor;
        }

        /// <summary>Set one slot's auto-salvage floor ("max &amp; below" scraps on drop); null removes
        /// the entry (that slot stops auto-salvaging). Pure — Progress-copy convention.</summary>
        public static SaveState SetSalvageFloor(SaveState save, EquipSlot slot, Rarity? max)
        {
            var floors = new Dictionary<EquipSlot, Rarity>(save.Progress.Loot.SalvageMaxBySlot);
            if (max == null) floors.Remove(slot);
            else floors[slot] = max.Value;
            return WithLootFilter(save, new LootFilterState
            {
                SalvageMaxBySlot = floors,
                NeverSalvageImprinted = save.Progress.Loot.NeverSalvageImprinted,
            });
        }

        /// <summary>Set every active slot's floor at once (the "All slots" verb); null clears them all. Pure.</summary>
        public static SaveState SetSalvageFloorAll(SaveState save, Rarity? max)
        {
            var floors = new Dictionary<EquipSlot, Rarity>();
            if (max != null)
                foreach (var slot in EquipSlots.Active) floors[slot] = max.Value;
            return WithLootFilter(save, new LootFilterState
            {
                SalvageMaxBySlot = floors,
                NeverSalvageImprinted = save.Progress.Loot.NeverSalvageImprinted,
            });
        }

        /// <summary>Toggle the never-salvage-imprinted guard (see <see cref="LootFilterState"/>). Pure.</summary>
        public static SaveState SetImprintGuard(SaveState save, bool on)
            => WithLootFilter(save, new LootFilterState
            {
                SalvageMaxBySlot = new Dictionary<EquipSlot, Rarity>(save.Progress.Loot.SalvageMaxBySlot),
                NeverSalvageImprinted = on,
            });

        // Clone the save with a new LootFilterState, nested under a cloned ProgressState (its
        // siblings share refs). Everything else shares refs — the pure-reducer convention
        // (mirrors Achievements.WithAchievements / Tower.WithFloor).
        private static SaveState WithLootFilter(SaveState save, LootFilterState loot) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            LeaderHeroId = save.LeaderHeroId,
            Inventory = save.Inventory,
            Currencies = save.Currencies,
            Progress = new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = save.Progress.CurrentStage,
                AccountLevel = save.Progress.AccountLevel,
                EndlessBest = save.Progress.EndlessBest,
                Tower = save.Progress.Tower,
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
                Crypt = save.Progress.Crypt,
                Intro = save.Progress.Intro,
                Loot = loot,
                Codex = save.Progress.Codex,
                Season = save.Progress.Season,
                Ascension = save.Progress.Ascension,
            },
            Quests = save.Quests,
            Modifiers = save.Modifiers,
            GachaPity = save.GachaPity,
            LastClaimAt = save.LastClaimAt,
        };

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

            var newItem = new Item { Id = item.Id, BaseId = item.BaseId, Rarity = item.Rarity, ItemLevel = item.ItemLevel, Affixes = newAffixes, Enhance = item.Enhance, Locked = item.Locked, SetId = item.SetId };
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
                GachaPity = save.GachaPity,
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
                SetId = item.SetId,
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
        /// Manually salvage one loose item to scrap. Throws on unknown item, one that's
        /// equipped (so the player can never accidentally scrap worn gear), a locked one, or
        /// an imprinted one while the imprint guard is on (all salvage paths refuse — the
        /// Locked precedent). Pure.
        /// </summary>
        public static SaveState SalvageItem(SaveState save, string itemId, GameConfig cfg)
        {
            var item = save.Inventory.Find(i => i.Id == itemId)
                ?? throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" not in inventory");
            if (IsEquippedAnywhere(save, itemId))
                throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" is equipped");
            if (item.Locked)
                throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" is locked");
            if (save.Progress.Loot.NeverSalvageImprinted && IsImprinted(item, cfg))
                throw new InvalidOperationException($"SalvageItem: item \"{itemId}\" is imprinted and the imprint guard is on");

            var nextInventory = new List<Item>(save.Inventory);
            nextInventory.RemoveAll(i => i.Id == itemId);
            long scrap = cfg.Balance.ScrapValue(item.Rarity, item.ItemLevel);
            return Build(save, nextInventory, AddScrap(save.Currencies, scrap));
        }

        /// <summary>
        /// Mass-salvage: convert EVERY loose item that is (1) not equipped, (2) not locked, and
        /// (3) not imprint-guarded to scrap in one action — regardless of rarity (the "Salvage all"
        /// verb; the client arms a confirm step because this destroys rares/uniques/legendaries too).
        /// Equipped gear, locked items, and — while the imprint guard is on — imprinted gear are
        /// never touched (per-item guards, same as <see cref="SalvageItem"/>). Returns the new save
        /// plus how many items were scrapped and the scrap gained; a no-match call returns the input
        /// save unchanged. Pure.
        /// </summary>
        public static (SaveState Save, int Count, long Scrap) SalvageAll(SaveState save, GameConfig cfg)
        {
            var equipped = EquippedIds(save);
            bool guard = save.Progress.Loot.NeverSalvageImprinted;
            var nextInventory = new List<Item>(save.Inventory.Count);
            int count = 0;
            long scrap = 0;
            foreach (var it in save.Inventory)
            {
                if (!it.Locked && !equipped.Contains(it.Id) && !(guard && IsImprinted(it, cfg)))
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
        /// Bulk-salvage a hand-picked set of items (the select-mode "Salvage N selected" verb).
        /// Ineligible ids — unknown, equipped, locked, or imprinted while the guard is on — are
        /// SKIPPED silently rather than throwing: bulk is a sweep, not a command. The UI pre-filters
        /// the selection, but the reducer must stay safe against races/stale ids — unlike
        /// <see cref="SalvageItem"/>, which throws because a single deliberate action deserves a
        /// loud failure. Duplicate ids credit once (the inventory is walked, not the list). Returns
        /// the new save plus the salvaged count and scrap gained; a no-match call returns the input
        /// save unchanged. Pure.
        /// </summary>
        public static (SaveState Save, int Count, long Scrap) SalvageMany(SaveState save, IReadOnlyList<string> itemIds, GameConfig cfg)
        {
            var wanted = new HashSet<string>(itemIds);
            if (wanted.Count == 0) return (save, 0, 0);

            var equipped = EquippedIds(save);
            bool guard = save.Progress.Loot.NeverSalvageImprinted;
            var nextInventory = new List<Item>(save.Inventory.Count);
            int count = 0;
            long scrap = 0;
            foreach (var it in save.Inventory)
            {
                if (wanted.Contains(it.Id) && !it.Locked && !equipped.Contains(it.Id) && !(guard && IsImprinted(it, cfg)))
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
                SetId = item.SetId,
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
            Loadout = hero.Loadout,
            Stars = hero.Stars,
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
                GachaPity = save.GachaPity,
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
            GachaPity = save.GachaPity,
            LastClaimAt = save.LastClaimAt,
        };

        /// <summary>Like <see cref="Build"/>, but swaps in a new <see cref="CodexState"/> under a cloned
        /// ProgressState (its siblings share refs) — the AddLoot set-discovery stamp path. Only used when
        /// a set piece actually revealed a new slot bit (else Build shares the whole Progress ref).</summary>
        private static SaveState BuildWithCodex(SaveState save, List<Item> nextInventory,
                                                Dictionary<string, long> nextCurrencies, CodexState codex) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = save.Party,
            LeaderHeroId = save.LeaderHeroId,
            Inventory = nextInventory,
            Currencies = nextCurrencies,
            Progress = new ProgressState
            {
                HighestStage = save.Progress.HighestStage,
                CurrentStage = save.Progress.CurrentStage,
                AccountLevel = save.Progress.AccountLevel,
                EndlessBest = save.Progress.EndlessBest,
                Tower = save.Progress.Tower,
                Achievements = save.Progress.Achievements,
                Daily = save.Progress.Daily,
                Crypt = save.Progress.Crypt,
                Intro = save.Progress.Intro,
                Loot = save.Progress.Loot,
                Codex = codex,
                Season = save.Progress.Season,
                Ascension = save.Progress.Ascension,
            },
            Quests = save.Quests,
            Modifiers = save.Modifiers,
            GachaPity = save.GachaPity,
            LastClaimAt = save.LastClaimAt,
        };
    }
}
