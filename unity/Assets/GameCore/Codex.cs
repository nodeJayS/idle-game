#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// Codex / collection (10.15, mobile arc MM3) — the retention layer BETWEEN power walls, a sibling
    /// of the Tower milestone buffs (<see cref="Tower.ApplyAccountBuffs"/>) and the crypt boons
    /// (<see cref="Crypt.ApplyBoons"/>). Three collection families:
    ///
    ///  - MONSTERS: lifetime kills per monster, banked from combat's transient
    ///    <see cref="CombatState.PendingKills"/> via <see cref="BankKills"/>. Each monster has
    ///    <see cref="BalanceConstants.CodexKillTiers"/> thresholds (bronze/silver/gold).
    ///  - SETS: which equip slots each gear set has ever been SEEN on, a bitmask stamped at the one
    ///    loot-commit path (<see cref="Inventory.AddLoot"/>). A set completes once seen on every slot
    ///    it can drop across (see <see cref="SetCompleteMask"/>).
    ///  - ZONES: entered / cleared, DERIVED purely from <see cref="ProgressState.HighestStage"/> — no
    ///    new persisted state.
    ///
    /// Every COMPLETED tier pays a small permanent account-wide stat drip
    /// (<see cref="BalanceConstants.CodexTierStatPct"/>), auto-DERIVED from state — nothing is
    /// claimable (the <see cref="Achievements"/> auto-pay philosophy; no new Goals.Claimables). Read
    /// models are pure projections over the save; <see cref="BankKills"/> is the only reducer and is
    /// pure (a new SaveState, input untouched; empty/no-op input shares the ref).
    /// </summary>
    public static class Codex
    {
        // ---- masks / thresholds --------------------------------------------

        /// <summary>The bitmask a gear set must reach to count COMPLETE: every equip slot a set piece
        /// can drop across. Set membership (<see cref="Loot.RollSetMembership"/>) is stamped on a drop
        /// regardless of its slot, so a set spans ALL active equip slots — the completion span is
        /// therefore <see cref="EquipSlots.Active"/> (derived from the sets' ACTUAL reachable slots,
        /// not a hardcoded count). <paramref name="cfg"/> is accepted for call-site symmetry and so a
        /// future slot-restricted set family can narrow this per config.</summary>
        public static int SetCompleteMask(GameConfig cfg)
        {
            int mask = 0;
            foreach (var slot in EquipSlots.Active) mask |= 1 << (int)slot;
            return mask;
        }

        /// <summary>How many kill tiers a lifetime count satisfies (0..CodexKillTiers.Length). Tiers
        /// are ascending, so this is just the count of thresholds at or below <paramref name="kills"/>.</summary>
        public static int KillTiersCompleted(long kills, GameConfig cfg)
        {
            int n = 0;
            foreach (var t in cfg.Balance.CodexKillTiers) if (kills >= t) n++;
            return n;
        }

        // ---- banking (the only reducer) ------------------------------------

        /// <summary>
        /// Bank a combat run's <see cref="CombatState.PendingKills"/> into the lifetime tally: add each
        /// monster's kills and report every kill TIER a monster's lifetime count crossed in THIS bank
        /// (so the client can toast "Slime — Silver"). Ascending thresholds, so a single big bank can
        /// cross several tiers for one monster (each reported once). Empty (or all-non-positive)
        /// <paramref name="pending"/> shares the input save ref. Pure — the input's Kills dict is never
        /// mutated (cloned), and SetSeen is carried forward by ref.
        /// </summary>
        public static (SaveState save, List<CodexTierCross> crossed) BankKills(
            SaveState save, IReadOnlyDictionary<string, int> pending, GameConfig cfg)
        {
            var crossed = new List<CodexTierCross>();
            if (pending == null || pending.Count == 0) return (save, crossed); // nothing to bank — share the ref

            var kills = new Dictionary<string, long>(save.Progress.Codex.Kills);
            var tiers = cfg.Balance.CodexKillTiers;
            bool changed = false;
            foreach (var kv in pending)
            {
                if (kv.Value <= 0) continue; // defensive: PendingKills only ever increments
                changed = true;
                long before = kills.TryGetValue(kv.Key, out var b) ? b : 0;
                long after = before + kv.Value;
                kills[kv.Key] = after;
                for (int t = 0; t < tiers.Length; t++)
                    if (before < tiers[t] && after >= tiers[t]) // threshold passed within (before, after]
                        crossed.Add(new CodexTierCross { MonsterId = kv.Key, TierIndex = t });
            }
            if (!changed) return (save, crossed); // every entry non-positive — share the ref

            return (WithCodex(save, new CodexState { Kills = kills, SetSeen = save.Progress.Codex.SetSeen }), crossed);
        }

        /// <summary>
        /// Retroactive set discovery for saves that predate the codex: gear ALREADY in the bag/equipped
        /// is proof those set pieces were seen, but only NEW drops stamp via <see cref="Inventory.AddLoot"/>
        /// — without this a pre-10.15 save's worn 4pc set would read "unseen" forever. Runs at load
        /// beside the other cfg-aware retro-grants (<see cref="Progression.SyncHeroUnlocks"/>,
        /// <see cref="Modifiers.SyncToStage"/> — it needs cfg to resolve slots through ItemBases, which
        /// Save.Migrate deliberately doesn't take). Bitmask OR = idempotent; when nothing new is seen
        /// the input ref is shared. Pure.
        /// </summary>
        public static SaveState SyncFromInventory(SaveState save, GameConfig cfg)
        {
            Dictionary<string, int>? seen = null; // cloned lazily, the AddLoot idiom
            foreach (var it in save.Inventory)
            {
                if (it.SetId == null || !cfg.ItemBases.TryGetValue(it.BaseId, out var itemBase)) continue;
                var cur = seen ?? save.Progress.Codex.SetSeen;
                int had = cur.TryGetValue(it.SetId, out var m) ? m : 0;
                int bit = 1 << (int)itemBase.Slot;
                if ((had & bit) == 0)
                {
                    seen ??= new Dictionary<string, int>(save.Progress.Codex.SetSeen);
                    seen[it.SetId] = had | bit;
                }
            }
            if (seen == null) return save; // nothing new — share the ref
            return WithCodex(save, new CodexState { Kills = save.Progress.Codex.Kills, SetSeen = seen });
        }

        // ---- derived read models -------------------------------------------

        /// <summary>One codex row per monster def in <paramref name="cfg"/> (shelved monsters absent
        /// from cfg are skipped — the SyncHeroUnlocks precedent). Order = cfg.Monsters iteration order.</summary>
        public static List<CodexMonsterEntry> MonsterEntries(SaveState save, GameConfig cfg)
        {
            var tiers = cfg.Balance.CodexKillTiers;
            var rows = new List<CodexMonsterEntry>(cfg.Monsters.Count);
            foreach (var kv in cfg.Monsters)
            {
                long kills = save.Progress.Codex.Kills.TryGetValue(kv.Key, out var k) ? k : 0;
                int done = KillTiersCompleted(kills, cfg);
                bool maxed = done >= tiers.Length;
                rows.Add(new CodexMonsterEntry
                {
                    Id = kv.Key,
                    Name = kv.Value.Name,
                    Kills = kills,
                    TiersCompleted = done,
                    NextThreshold = maxed ? 0 : tiers[done],
                    Maxed = maxed,
                });
            }
            return rows;
        }

        /// <summary>One codex row per gear set in <paramref name="cfg"/>: how many of its reachable
        /// slots have been seen, out of the required span, and whether it's complete.</summary>
        public static List<CodexSetEntry> SetEntries(SaveState save, GameConfig cfg)
        {
            int completeMask = SetCompleteMask(cfg);
            int required = EquipSlots.Active.Length;
            var rows = new List<CodexSetEntry>(cfg.Sets.Count);
            foreach (var kv in cfg.Sets)
            {
                int mask = save.Progress.Codex.SetSeen.TryGetValue(kv.Key, out var m) ? m : 0;
                int seen = 0;
                foreach (var slot in EquipSlots.Active) if ((mask & (1 << (int)slot)) != 0) seen++;
                rows.Add(new CodexSetEntry
                {
                    Id = kv.Key,
                    Name = kv.Value.Name,
                    SlotsSeen = seen,
                    SlotsRequired = required,
                    Complete = (mask & completeMask) == completeMask,
                });
            }
            return rows;
        }

        /// <summary>One codex row per zone: entered/cleared DERIVED from HighestStage alone (no new
        /// state). Zone t spans stages [t·StagesPerTier + 1, (t+1)·StagesPerTier] (the same tier bands
        /// <see cref="GameConfig.ZoneForStage"/> uses) — entered at its first stage, cleared at its
        /// last. Ranges come from cfg (StagesPerTier + the zone list), never literals.</summary>
        public static List<CodexZoneEntry> ZoneEntries(SaveState save, GameConfig cfg)
        {
            int per = Math.Max(1, cfg.Balance.StagesPerTier);
            int highest = save.Progress.HighestStage;
            var rows = new List<CodexZoneEntry>(cfg.Zones.Count);
            for (int t = 0; t < cfg.Zones.Count; t++)
            {
                int first = t * per + 1;
                int last = (t + 1) * per;
                rows.Add(new CodexZoneEntry
                {
                    Id = cfg.Zones[t].Id,
                    Name = cfg.Zones[t].Name,
                    Entered = highest >= first,
                    Cleared = highest >= last,
                });
            }
            return rows;
        }

        // ---- derived account buff ------------------------------------------

        /// <summary>Total COMPLETED tiers across all three families: per-monster kill tiers (cfg
        /// monsters only, so shelved-monster kills lingering in the dict don't inflate the ceiling)
        /// + complete sets + cleared zones. The one number the account drip scales on.</summary>
        public static int CompletedTiers(SaveState save, GameConfig cfg)
        {
            int total = 0;
            foreach (var kv in cfg.Monsters)
            {
                long kills = save.Progress.Codex.Kills.TryGetValue(kv.Key, out var k) ? k : 0;
                total += KillTiersCompleted(kills, cfg);
            }
            int completeMask = SetCompleteMask(cfg);
            foreach (var kv in cfg.Sets)
            {
                int mask = save.Progress.Codex.SetSeen.TryGetValue(kv.Key, out var m) ? m : 0;
                if ((mask & completeMask) == completeMask) total++;
            }
            int per = Math.Max(1, cfg.Balance.StagesPerTier);
            for (int t = 0; t < cfg.Zones.Count; t++)
                if (save.Progress.HighestStage >= (t + 1) * per) total++;
            return total;
        }

        /// <summary>The permanent account-wide stat bonus from completed codex tiers, as a fraction
        /// (0.05 = +5%): <see cref="CompletedTiers"/> × <see cref="BalanceConstants.CodexTierStatPct"/>.</summary>
        public static double AccountBuffPct(SaveState save, GameConfig cfg)
            => CompletedTiers(save, cfg) * cfg.Balance.CodexTierStatPct;

        /// <summary>Apply the derived codex drip to a computed stat block: scales core power stats
        /// (Hp/Atk/Def) by (1 + <see cref="AccountBuffPct"/>). Pure (a new block); a no-op share when
        /// nothing is collected. The <see cref="Tower.ApplyAccountBuffs"/> mirror — folded into the
        /// combat stat build right beside Tower's milestone buffs and the crypt boons.</summary>
        public static StatBlock ApplyAccountBuffs(StatBlock s, SaveState save, GameConfig cfg)
        {
            double pct = AccountBuffPct(save, cfg);
            if (pct <= 0) return s;
            var r = new StatBlock(s);
            r[StatKey.Hp] = r.Get(StatKey.Hp) * (1 + pct);
            r[StatKey.Atk] = r.Get(StatKey.Atk) * (1 + pct);
            r[StatKey.Def] = r.Get(StatKey.Def) * (1 + pct);
            return r;
        }

        // ---- copy helper (Codex lives under ProgressState) ------------------

        /// <summary>Clone the save with a new CodexState, nested under a cloned ProgressState (its
        /// siblings share refs). Everything else shares refs — the pure-reducer convention (mirrors
        /// <see cref="Achievements"/>.WithAchievements / <see cref="Tower"/>.WithFloor).</summary>
        private static SaveState WithCodex(SaveState save, CodexState codex) => new SaveState
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

    /// <summary>A monster's lifetime-kill tier newly crossed by a <see cref="Codex.BankKills"/> call,
    /// returned for the client feed / toast ("Slime — Silver").</summary>
    public struct CodexTierCross
    {
        public string MonsterId;
        public int TierIndex; // 0-based kill tier (into BalanceConstants.CodexKillTiers) just crossed
    }

    /// <summary>A monster codex row (<see cref="Codex.MonsterEntries"/>): lifetime kills, tiers earned,
    /// and the next threshold (0 when maxed).</summary>
    public struct CodexMonsterEntry
    {
        public string Id;
        public string Name;
        public long Kills;
        public int TiersCompleted;   // 0..CodexKillTiers.Length
        public long NextThreshold;   // 0 when Maxed
        public bool Maxed;
    }

    /// <summary>A gear-set codex row (<see cref="Codex.SetEntries"/>): reachable slots seen vs required.</summary>
    public struct CodexSetEntry
    {
        public string Id;
        public string Name;
        public int SlotsSeen;
        public int SlotsRequired;
        public bool Complete;
    }

    /// <summary>A zone codex row (<see cref="Codex.ZoneEntries"/>): entered/cleared, derived from
    /// HighestStage.</summary>
    public struct CodexZoneEntry
    {
        public string Id;
        public string Name;
        public bool Entered;
        public bool Cleared;
    }
}
