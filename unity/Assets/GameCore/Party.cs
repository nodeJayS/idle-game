#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Party / hero acquisition. THE GACHA PLUG POINT lives here later:
    /// AcquireHero will be called by progression now and by a gacha roll later
    /// (same entry point, no downstream changes).
    /// </summary>
    public static class Party
    {
        /// <summary>
        /// Place an owned hero into one of the party slots (or null to clear).
        /// Pure: returns a new SaveState; the input is not mutated.
        /// </summary>
        public static SaveState SetPartySlot(SaveState save, int slot, string? heroId)
        {
            if (slot < 0 || slot >= save.Party.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} out of range (0..{save.Party.Length - 1})");

            if (heroId != null && !save.Heroes.Exists(h => h.Id == heroId))
                throw new InvalidOperationException($"SetPartySlot: hero \"{heroId}\" not owned");

            // Guard rail: never let a reducer strand the party with zero fielded heroes.
            // Clearing the only fielded hero is a no-op (share the ref); the UI warns too,
            // but the sim must stay safe regardless of the UI.
            if (heroId == null && save.Party[slot] != null && CountFielded(save.Party) == 1)
                return save;

            var nextParty = (string?[])save.Party.Clone();
            nextParty[slot] = heroId;
            return WithParty(save, nextParty);
        }

        /// <summary>
        /// Field an owned hero into a party slot, keeping the party free of duplicates:
        /// the hero is first removed from any other slot it held, then placed in
        /// <paramref name="slot"/>; whoever occupied that slot is benched. Pure: returns a
        /// new SaveState. Throws on an out-of-range slot or an unowned hero. (To bench
        /// without fielding, use <see cref="SetPartySlot"/> with a null heroId.)
        /// </summary>
        public static SaveState FieldHero(SaveState save, int slot, string heroId)
        {
            if (slot < 0 || slot >= save.Party.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} out of range (0..{save.Party.Length - 1})");

            if (!save.Heroes.Exists(h => h.Id == heroId))
                throw new InvalidOperationException($"FieldHero: hero \"{heroId}\" not owned");

            var nextParty = (string?[])save.Party.Clone();
            for (int i = 0; i < nextParty.Length; i++)
                if (nextParty[i] == heroId) nextParty[i] = null; // pull it out of any prior slot
            nextParty[slot] = heroId;
            return WithParty(save, nextParty);
        }

        /// <summary>
        /// THE acquisition plug point: mint a fresh level-1 instance of a hero def and add
        /// it to the owned roster (not auto-fielded). Pure. Today progression calls this on
        /// stage-clear unlocks; a gacha roll later calls the same method. Throws on an
        /// unknown def or a duplicate <paramref name="heroId"/>.
        /// </summary>
        public static SaveState AcquireHero(SaveState save, string defId, GameConfig cfg, string heroId)
        {
            if (!cfg.Heroes.TryGetValue(defId, out var def))
                throw new InvalidOperationException($"AcquireHero: unknown hero def \"{defId}\"");
            if (save.Heroes.Exists(h => h.Id == heroId))
                throw new InvalidOperationException($"AcquireHero: hero id \"{heroId}\" already exists");

            var hero = new HeroInstance
            {
                Id = heroId,
                DefId = def.DefId,
                Level = 1,
                Xp = 0,
                Equipped = new Dictionary<EquipSlot, string>(),
            };
            var nextHeroes = new List<HeroInstance>(save.Heroes) { hero };

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

        /// <summary>
        /// Choose the party's formation leader — the hero the others fall in behind (Solo
        /// tactic). Must be a currently-fielded hero; pass null to revert to auto (the
        /// lowest-slot living hero leads). Pure. Throws if the hero isn't fielded.
        /// </summary>
        public static SaveState SetLeader(SaveState save, string? heroId)
        {
            if (heroId != null && Array.IndexOf(save.Party, heroId) < 0)
                throw new InvalidOperationException($"SetLeader: hero \"{heroId}\" is not fielded");
            if (save.LeaderHeroId == heroId) return save; // no-op, share the ref
            return WithParty(save, save.Party, heroId);
        }

        /// <summary>
        /// One-move bench/field swap: the owned-but-benched <paramref name="benchedHeroId"/>
        /// takes the EXACT party slot the currently-fielded <paramref name="fieldedHeroId"/>
        /// occupied (formation rank order is meaningful), and the outgoing hero leaves the
        /// party (still owned). If the outgoing hero was the leader, leadership reverts to
        /// auto (LeaderHeroId ⇒ null) — it does NOT transfer to the incoming hero. Pure.
        /// Any violation (unknown ids, benched hero already fielded, fielded hero not
        /// fielded, same id) is a no-op that returns the SAME save reference.
        /// </summary>
        public static SaveState SwapHero(SaveState save, string benchedHeroId, string fieldedHeroId)
        {
            if (benchedHeroId == null || fieldedHeroId == null || benchedHeroId == fieldedHeroId)
                return save; // no-op

            // benched must be OWNED and NOT currently fielded.
            if (!save.Heroes.Exists(h => h.Id == benchedHeroId)) return save;
            if (Array.IndexOf(save.Party, benchedHeroId) >= 0) return save;

            // fielded must be currently in the party.
            int slot = Array.IndexOf(save.Party, fieldedHeroId);
            if (slot < 0) return save;

            var nextParty = (string?[])save.Party.Clone();
            nextParty[slot] = benchedHeroId; // incoming takes the outgoing hero's exact rank

            // Leadership fixup: outgoing leader is benched ⇒ revert to auto (null); do NOT
            // hand leadership to the incoming hero. Any other leader is preserved.
            var nextLeader = save.LeaderHeroId == fieldedHeroId ? null : save.LeaderHeroId;
            return WithParty(save, nextParty, nextLeader);
        }

        /// <summary>Count of occupied (non-null) party slots.</summary>
        private static int CountFielded(string?[] party)
        {
            int n = 0;
            for (int i = 0; i < party.Length; i++)
                if (party[i] != null) n++;
            return n;
        }

        private static SaveState WithParty(SaveState save, string?[] nextParty)
            => WithParty(save, nextParty, save.LeaderHeroId);

        private static SaveState WithParty(SaveState save, string?[] nextParty, string? leaderHeroId) => new SaveState
        {
            Version = save.Version,
            RngSeed = save.RngSeed,
            RngCursor = save.RngCursor,
            Heroes = save.Heroes,
            Party = nextParty,
            LeaderHeroId = leaderHeroId,
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
