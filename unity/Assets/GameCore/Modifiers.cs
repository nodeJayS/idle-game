#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Monster modifiers (Lever 1 — the player-controlled risk/reward knob, PoE map-mod style).
    /// Acquisition + upgrade are driven by FARM DEPTH (highest stage reached), not hero level: you
    /// unlock the next modifier in <see cref="GameConfig.ModifierUnlockOrder"/> every
    /// <see cref="BalanceConstants.ModifierNewEveryStages"/> stages, and ALL owned modifiers gain +1
    /// strength every <see cref="BalanceConstants.ModifierUpgradeEveryStages"/> stages — all *derived*
    /// from stage, so nothing can desync. The player slots up to
    /// <see cref="BalanceConstants.MaxActiveModifiers"/> of them (the account-wide loadout) to apply
    /// to farm trash — harder mobs for a thematic reward. Pure reducers (return a new SaveState).
    /// </summary>
    public static class Modifiers
    {
        /// <summary>Modifiers owned at a given highest-stage: one per ModifierNewEveryStages, capped at
        /// the catalog size.</summary>
        public static int OwnedCountForStage(int highestStage, GameConfig cfg)
            => Math.Min(cfg.ModifierUnlockOrder.Count, highestStage / Math.Max(1, cfg.Balance.ModifierNewEveryStages));

        /// <summary>The (uniform) strength every owned modifier has at a given highest-stage.</summary>
        public static int StrengthForStage(int highestStage, GameConfig cfg)
            => Math.Max(1, highestStage / Math.Max(1, cfg.Balance.ModifierUpgradeEveryStages));

        /// <summary>The highest stage at which the NEXT modifier unlocks (0 if all are unlocked).</summary>
        public static int NextUnlockStage(SaveState save, GameConfig cfg)
        {
            int count = OwnedCountForStage(save.Progress.HighestStage, cfg);
            return count >= cfg.ModifierUnlockOrder.Count ? 0 : (count + 1) * Math.Max(1, cfg.Balance.ModifierNewEveryStages);
        }

        /// <summary>Bring owned modifiers in line with the current highest stage: own the first
        /// OwnedCountForStage of <see cref="GameConfig.ModifierUnlockOrder"/>, each at
        /// StrengthForStage, and prune the active loadout to owned ∩ the cap. Idempotent — a no-op
        /// (shares the ref) when already in sync. Called as farm depth grows. Pure.</summary>
        public static SaveState SyncToStage(SaveState save, GameConfig cfg)
        {
            int highest = save.Progress.HighestStage;
            int count = OwnedCountForStage(highest, cfg);
            int strength = StrengthForStage(highest, cfg);

            var owned = new Dictionary<string, int>(count);
            for (int i = 0; i < count; i++) owned[cfg.ModifierUnlockOrder[i]] = strength;

            // Tower-gated mechanical modifiers (e.g. Volatile): earned by clearing a Tower milestone
            // floor, not by farm depth. Same uniform strength so the owned set stays single-strength.
            int floor = save.Progress.Tower.HighestFloor;
            foreach (var kv in cfg.Modifiers)
                if (kv.Value.TowerUnlockFloor > 0 && floor >= kv.Value.TowerUnlockFloor)
                    owned[kv.Key] = strength;

            var active = new List<string>();
            foreach (var id in save.Modifiers.Active)
                if (owned.ContainsKey(id) && active.Count < cfg.Balance.MaxActiveModifiers) active.Add(id);

            if (SameOwned(save.Modifiers.Owned, owned) && SameList(save.Modifiers.Active, active))
                return save; // already in sync

            return WithModifiers(save, new MonsterModifiers { Owned = owned, Active = active });
        }

        /// <summary>Toggle a modifier type on/off (applies to farm trash while active). No-op (shares
        /// the ref) when: activating an unowned type, the loadout is already at
        /// <see cref="BalanceConstants.MaxActiveModifiers"/>, or it's already in the requested state.
        /// Pure.</summary>
        public static SaveState SetActive(SaveState save, string typeId, bool on, GameConfig cfg)
        {
            bool active = save.Modifiers.Active.Contains(typeId);
            if (on)
            {
                if (active || !save.Modifiers.Owned.ContainsKey(typeId)) return save;
                if (save.Modifiers.Active.Count >= cfg.Balance.MaxActiveModifiers) return save; // loadout full
                var list = new List<string>(save.Modifiers.Active) { typeId };
                return WithModifiers(save, new MonsterModifiers { Owned = save.Modifiers.Owned, Active = list });
            }
            if (!active) return save;
            var without = new List<string>(save.Modifiers.Active);
            without.Remove(typeId);
            return WithModifiers(save, new MonsterModifiers { Owned = save.Modifiers.Owned, Active = without });
        }

        private static bool SameOwned(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a) if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Resolve the active modifiers into combat-ready instances (def + banked strength),
        /// in toggle order. Skips any active id no longer owned or absent from config. The client
        /// hands this to the sim (<see cref="CombatState.ActiveModifiers"/>).</summary>
        public static List<ModifierInstance> ResolveActive(SaveState save, GameConfig cfg)
        {
            var result = new List<ModifierInstance>();
            foreach (var typeId in save.Modifiers.Active)
            {
                if (!save.Modifiers.Owned.TryGetValue(typeId, out var strength)) continue;
                if (!cfg.Modifiers.TryGetValue(typeId, out var def)) continue;
                result.Add(new ModifierInstance { Def = def, Strength = strength });
            }
            return result;
        }

        private static SaveState WithModifiers(SaveState save, MonsterModifiers modifiers) => new SaveState
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
            Quests = save.Quests,
            Modifiers = modifiers,
            LastClaimAt = save.LastClaimAt,
        };
    }
}
