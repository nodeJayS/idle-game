#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>What banking a stage's modifier did — for the client's "acquired/upgraded" feed.</summary>
    public sealed class ModifierGrant
    {
        public string TypeId = "";
        public int Strength;
        public bool IsNew;    // first time this type was banked
        public bool Upgraded; // banked at a higher strength than before (false if it was a no-op)
    }

    /// <summary>
    /// Monster modifiers (Lever 1 — the player-controlled risk/reward knob). Bosses are the
    /// SOURCE: each stage's boss exhibits a modifier (<see cref="GameConfig.ModifierTypeForStage"/>)
    /// and grants it on a kill at strength = stage. The player toggles owned types ACTIVE to apply
    /// them to farm trash — harder mobs for a thematic reward, stack freely. Pure reducers (return a
    /// new SaveState, input untouched), mirroring <see cref="Quests"/> / <see cref="Party"/>.
    /// </summary>
    public static class Modifiers
    {
        /// <summary>Bank the modifier the cleared stage's boss exhibits, at strength = stage, keeping
        /// the best (highest) strength owned for that type. Returns the new save plus a grant record
        /// describing what changed (for the client feed); grant is null if the stage defines no
        /// modifier. Pure.</summary>
        public static (SaveState save, ModifierGrant? grant) AcquireFromStage(SaveState save, int stage, GameConfig cfg)
        {
            string? typeId = cfg.ModifierTypeForStage(stage);
            if (typeId == null || !cfg.Modifiers.ContainsKey(typeId)) return (save, null);

            bool owned = save.Modifiers.Owned.TryGetValue(typeId, out var cur);
            int best = Math.Max(owned ? cur : 0, stage);
            if (owned && best == cur) // already banked at >= this strength — no change
                return (save, new ModifierGrant { TypeId = typeId, Strength = cur, IsNew = false, Upgraded = false });

            var nextOwned = new Dictionary<string, int>(save.Modifiers.Owned) { [typeId] = best };
            var mods = new MonsterModifiers { Owned = nextOwned, Active = new List<string>(save.Modifiers.Active) };
            var grant = new ModifierGrant { TypeId = typeId, Strength = best, IsNew = !owned, Upgraded = owned };
            return (WithModifiers(save, mods), grant);
        }

        /// <summary>Toggle a modifier type on/off (applies to farm trash while active). Activating an
        /// unowned type, or toggling to the state it's already in, is a no-op (shares the ref). Pure.</summary>
        public static SaveState SetActive(SaveState save, string typeId, bool on)
        {
            bool active = save.Modifiers.Active.Contains(typeId);
            if (on)
            {
                if (active || !save.Modifiers.Owned.ContainsKey(typeId)) return save;
                var list = new List<string>(save.Modifiers.Active) { typeId };
                return WithModifiers(save, new MonsterModifiers { Owned = save.Modifiers.Owned, Active = list });
            }
            if (!active) return save;
            var without = new List<string>(save.Modifiers.Active);
            without.Remove(typeId);
            return WithModifiers(save, new MonsterModifiers { Owned = save.Modifiers.Owned, Active = without });
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
