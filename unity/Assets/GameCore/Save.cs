#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>New-game creation + save schema migration.</summary>
    public static class Save
    {
        public const int SaveVersion = 1;
        public const string StarterHeroDef = "warrior_basic";
        public const string StarterMageDef = "magician_basic";

        /// <summary>
        /// A fresh game: just a Warrior in slot 0 (three empty slots). More heroes are
        /// earned through progression — e.g. the Magician unlocks at stage 3
        /// (<see cref="GameConfig.HeroUnlocks"/>). `now` (epoch ms) is passed in, not read
        /// from the clock, to keep game-core pure/testable.
        /// </summary>
        public static SaveState NewGame(uint seed, GameConfig cfg, long now)
        {
            var warrior = MakeHero("h1", StarterHeroDef, cfg);

            return new SaveState
            {
                Version = SaveVersion,
                RngSeed = seed,
                RngCursor = 0,
                Heroes = new List<HeroInstance> { warrior },
                Party = new string?[] { warrior.Id, null, null, null },
                Inventory = new List<Item>(),
                Currencies = new Dictionary<string, long> { ["gold"] = 0 },
                Progress = new ProgressState { HighestStage = 0, CurrentStage = 1, AccountLevel = 1 },
                LastClaimAt = now,
            };
        }

        private static HeroInstance MakeHero(string id, string defId, GameConfig cfg)
        {
            if (!cfg.Heroes.TryGetValue(defId, out var def))
                throw new InvalidOperationException($"NewGame: missing hero def \"{defId}\"");
            return new HeroInstance
            {
                Id = id,
                DefId = def.DefId,
                Level = 1,
                Xp = 0,
                Equipped = new Dictionary<EquipSlot, string>(),
                SkillLoadout = new List<string>(def.Skills),
            };
        }

        /// <summary>
        /// Update the heartbeat: stamp <see cref="SaveState.LastClaimAt"/> to <c>now</c>
        /// (epoch ms) so online play isn't later mistaken for offline time by idle
        /// accrual. Pure (returns a new save); never moves the clock backward.
        /// </summary>
        public static SaveState Touch(SaveState save, long now)
        {
            long stamped = Math.Max(save.LastClaimAt, now);
            if (stamped == save.LastClaimAt) return save; // no-op, share the ref

            return new SaveState
            {
                Version = save.Version,
                RngSeed = save.RngSeed,
                RngCursor = save.RngCursor,
                Heroes = save.Heroes,
                Party = save.Party,
                Inventory = save.Inventory,
                Currencies = save.Currencies,
                Progress = save.Progress,
                LastClaimAt = stamped,
            };
        }

        /// <summary>
        /// Bring a loaded save up to the current version. Rejects a save newer than this
        /// build, and back-fills any null collections so a partial/older JSON payload
        /// can't NRE the rest of the game.
        /// </summary>
        public static SaveState Migrate(SaveState? save)
        {
            if (save == null) throw new ArgumentException("Migrate: null save");
            if (save.Version > SaveVersion)
                throw new InvalidOperationException($"Migrate: save version {save.Version} is newer than supported {SaveVersion}");
            if (save.Version != SaveVersion)
                throw new InvalidOperationException($"Migrate: unsupported version {save.Version} (expected {SaveVersion})");

            // Defensive defaults: deserializers may leave collections null on partial input.
            save.Heroes ??= new List<HeroInstance>();
            save.Inventory ??= new List<Item>();
            save.Currencies ??= new Dictionary<string, long>();
            save.Progress ??= new ProgressState();
            if (save.Party == null || save.Party.Length != 4)
                save.Party = new string?[4];

            return save;
        }
    }
}
