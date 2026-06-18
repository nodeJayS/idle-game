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

        /// <summary>
        /// A fresh game: 1 basic Warrior in party slot 0, three empty slots.
        /// `now` (epoch ms) is passed in, not read from the clock, to keep
        /// game-core pure/testable.
        /// </summary>
        public static SaveState NewGame(uint seed, GameConfig cfg, long now)
        {
            if (!cfg.Heroes.TryGetValue(StarterHeroDef, out var def))
                throw new InvalidOperationException($"NewGame: missing starter hero def \"{StarterHeroDef}\"");

            var warrior = new HeroInstance
            {
                Id = "h1",
                DefId = def.DefId,
                Level = 1,
                Xp = 0,
                Equipped = new Dictionary<EquipSlot, string>(),
                SkillLoadout = new List<string>(def.Skills),
            };

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

        /// <summary>Bring a loaded save up to the current version.</summary>
        public static SaveState Migrate(SaveState? save)
        {
            if (save == null) throw new ArgumentException("Migrate: null save");
            if (save.Version != SaveVersion)
                throw new InvalidOperationException($"Migrate: unsupported version {save.Version} (expected {SaveVersion})");
            return save;
        }
    }
}
