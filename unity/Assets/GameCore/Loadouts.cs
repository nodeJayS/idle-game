#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Per-hero gear loadout snapshots (10.5e): <see cref="SaveSnapshot"/> remembers the
    /// hero's current outfit as slot→itemId; <see cref="Apply"/> re-equips every piece that
    /// still legally can be. Pure reducers, same conventions as <see cref="Inventory"/>.
    /// </summary>
    public static class Loadouts
    {
        /// <summary>Snapshot the hero's current outfit (a copy — later equips don't mutate
        /// it). No-op (shares the ref) on an unknown hero. Pure.</summary>
        public static SaveState SaveSnapshot(SaveState save, string heroId)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero == null) return save;
            return WithHero(save, hero, new Dictionary<EquipSlot, string>(hero.Equipped), hero.Equipped);
        }

        /// <summary>
        /// Wear the snapshot: every piece that survives the bag-integrity checks is equipped;
        /// the rest are SKIPPED silently (the sweep contract, like <see cref="Inventory.SalvageMany"/> —
        /// a snapshot ages while the bag churns, so stale entries are expected, not errors).
        /// A piece survives when it still exists in the inventory, its base still exists and
        /// still maps to the snapshot slot, and nobody ELSE is wearing it (the hero re-wearing
        /// their own piece is of course fine). Apply only EQUIPS — a slot the snapshot has no
        /// surviving piece for keeps whatever it wears now; nothing is ever stripped.
        /// Returns the new save plus applied/skipped counts; empty/no-snapshot applies share
        /// the input ref. Pure.
        /// </summary>
        public static (SaveState Save, int Applied, int Skipped) Apply(SaveState save, string heroId, GameConfig cfg)
        {
            var hero = save.Heroes.Find(h => h.Id == heroId);
            if (hero?.Loadout == null || hero.Loadout.Count == 0) return (save, 0, 0);

            // What everyone ELSE wears (this hero's own current gear never blocks its snapshot).
            var wornByOthers = new HashSet<string>();
            foreach (var h in save.Heroes)
                if (h.Id != heroId)
                    foreach (var id in h.Equipped.Values) wornByOthers.Add(id);

            var next = new Dictionary<EquipSlot, string>(hero.Equipped);
            int applied = 0, skipped = 0;
            foreach (var kv in hero.Loadout)
            {
                var item = save.Inventory.Find(i => i.Id == kv.Value);
                bool ok = item != null
                          && cfg.ItemBases.TryGetValue(item.BaseId, out var baseDef)
                          && baseDef.Slot == kv.Key
                          && !wornByOthers.Contains(kv.Value);
                if (!ok) { skipped++; continue; }
                if (next.TryGetValue(kv.Key, out var cur) && cur == kv.Value) continue; // already worn: neither applied nor skipped
                next[kv.Key] = kv.Value;
                applied++;
            }
            if (applied == 0) return (save, 0, skipped);
            return (WithHero(save, hero, hero.Loadout, next), applied, skipped);
        }

        // Clone the save with one hero swapped (fresh Loadout/Equipped as given; everything
        // else shares refs) — the Inventory.CloneHero/WithHero convention.
        private static SaveState WithHero(SaveState save, HeroInstance hero,
                                          Dictionary<EquipSlot, string>? loadout,
                                          Dictionary<EquipSlot, string> equipped)
        {
            var updated = new HeroInstance
            {
                Id = hero.Id, DefId = hero.DefId, Level = hero.Level, Xp = hero.Xp,
                Equipped = equipped, SkillRanks = hero.SkillRanks, Loadout = loadout,
                Stars = hero.Stars,
            };
            var heroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes) heroes.Add(h.Id == hero.Id ? updated : h);

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = heroes,
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
    }
}
