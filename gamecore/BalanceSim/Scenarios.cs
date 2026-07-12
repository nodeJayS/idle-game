#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;

namespace IdleGame.BalanceSim
{
    /// <summary>
    /// Pure scenario harness for the balance sim (design doc Phase B "Balance sim (tooling)"):
    /// builds a synthetic-but-realistic save at a given stage/level/gear point, then runs the
    /// REAL combat sim (InitBossChallenge / InitFarm + RefreshPartyStats + StepCombat) over it.
    /// No shortcuts around the game rules — the party is assembled through the same reducers the
    /// client uses (OnStageCleared unlocks, InvestSkill, EquipItem), so a sim verdict is a
    /// verdict about the shipped game. Deterministic: every roll flows from the cell seed.
    /// </summary>
    public static class Scenarios
    {
        /// <summary>Gear policies charted by the runner: bare heroes, then a full 5-slot set
        /// per fielded hero at each rarity, rolled at the stage's item level.</summary>
        public static readonly Rarity?[] GearPolicies =
            { null, Rarity.Normal, Rarity.Rare, Rarity.Unique, Rarity.Legendary, Rarity.Mythic };

        public static string GearName(Rarity? gear) => gear?.ToString().ToLowerInvariant() ?? "none";

        /// <summary>Endgame ACCOUNT STACKS the sim can layer onto a built save (10.1d) — the
        /// account-wide power the walls chart under-counts for live players. All three flow through
        /// the exact combat stat build the client uses (RefreshPartyStats → Tower.ApplyAccountBuffs +
        /// Crypt.ApplyBoons; ComputeHeroStats → item.Enhance), so a stacked verdict stays honest.</summary>
        public struct AccountStacks
        {
            public int TowerMilestones;  // Tower.HighestFloor = this × TowerMilestoneEvery (each = +TowerMilestoneStatPct Hp/Atk/Def)
            public int BoonRank;         // every crypt boon (vigor/ferocity/bulwark) set to this rank (+CryptBoonStatPct/rank)
            public int EnhanceLevel;     // every equipped item enhanced to this level (+EnhanceBasePctPerLevel/level on its base+affixes)
            public static readonly AccountStacks None = new AccountStacks();
            public bool Any => TowerMilestones > 0 || BoonRank > 0 || EnhanceLevel > 0;

            public string Label => !Any ? "none"
                : $"tower×{TowerMilestones}/boon r{BoonRank}/enh +{EnhanceLevel}";
        }

        /// <summary>Layer the account stacks onto a built save by writing the SAME save fields the
        /// client's progression writes (Tower.HighestFloor, Crypt.Boons, Item.Enhance) — the combat
        /// path then folds them in unchanged. Clamped to each system's cap. Mutates and returns the save.</summary>
        public static SaveState ApplyStacks(SaveState save, GameConfig cfg, AccountStacks st)
        {
            if (!st.Any) return save;
            if (st.TowerMilestones > 0)
                save.Progress.Tower.HighestFloor = Math.Max(save.Progress.Tower.HighestFloor,
                    st.TowerMilestones * Math.Max(1, cfg.Balance.TowerMilestoneEvery));
            if (st.BoonRank > 0)
            {
                int rank = Math.Min(st.BoonRank, cfg.Balance.CryptBoonMaxRank);
                foreach (var b in cfg.CryptBoons) save.Progress.Crypt.Boons[b.Id] = rank;
            }
            if (st.EnhanceLevel > 0)
            {
                int enh = Math.Min(st.EnhanceLevel, cfg.Balance.EnhanceMax);
                foreach (var item in save.Inventory) item.Enhance = enh;
            }
            return save;
        }

        /// <summary>Deterministic per-cell seed so any single result can be reproduced from
        /// (baseSeed, stage, level, gear, trial) alone. Plain xorshift-multiply mix.</summary>
        public static uint CellSeed(uint baseSeed, int stage, int level, int gearIndex, int trial)
        {
            unchecked
            {
                uint x = baseSeed
                    ^ (uint)stage * 0x9E3779B1u
                    ^ (uint)level * 0x85EBCA77u
                    ^ (uint)(gearIndex + 1) * 0xC2B2AE3Du
                    ^ (uint)trial * 0x27D4EB2Fu;
                x ^= x >> 16; x *= 0x7FEB352Du;
                x ^= x >> 15; x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>
        /// A save that looks like a player about to fight stage <paramref name="stage"/>:
        /// stages 1..stage-1 cleared (so the roster is whatever the unlock table grants by
        /// then), every hero at <paramref name="level"/>, skill points auto-invested
        /// round-robin across each hero's kit, and — if <paramref name="gear"/> is set — a
        /// full active-slot set of that rarity rolled at the stage's item level and equipped.
        /// </summary>
        public static SaveState BuildSave(GameConfig cfg, int stage, int level, Rarity? gear, uint seed,
                                          AccountStacks stacks = default)
        {
            var save = Save.NewGame(seed, cfg, 0);
            for (int st = 1; st < stage; st++)
                save = Progression.OnStageCleared(save, st, cfg);

            var heroes = new List<HeroInstance>(save.Heroes.Count);
            foreach (var h in save.Heroes)
                heroes.Add(new HeroInstance
                {
                    Id = h.Id,
                    DefId = h.DefId,
                    Level = Math.Clamp(level, 1, cfg.Balance.MaxLevel),
                    Xp = 0,
                    Equipped = new Dictionary<EquipSlot, string>(h.Equipped),
                    SkillRanks = new Dictionary<string, int>(h.SkillRanks),
                });
            save = WithHeroes(save, heroes);

            save = AutoInvestSkills(save, cfg);
            if (gear is Rarity rarity) save = EquipFullSet(save, cfg, stage, rarity, seed);
            save = ApplyStacks(save, cfg, stacks);
            return save;
        }

        /// <summary>Spend every earned skill point, one point per kit node per pass (spreads
        /// ranks evenly the way a reasonable player would). CanInvest carries all the real
        /// gating (unlock level, max rank, unspent points), so this can't over-invest.</summary>
        public static SaveState AutoInvestSkills(SaveState save, GameConfig cfg)
        {
            bool invested = true;
            while (invested)
            {
                invested = false;
                foreach (var heroId in save.Party)
                {
                    if (heroId == null) continue;
                    var hero = save.Heroes.Find(h => h.Id == heroId);
                    if (hero == null || !cfg.Heroes.TryGetValue(hero.DefId, out var def)) continue;
                    foreach (var skillId in def.Skills)
                        if (Skills.CanInvest(save, heroId, skillId, cfg))
                        {
                            save = Skills.InvestSkill(save, heroId, skillId, cfg);
                            invested = true;
                        }
                }
            }
            return save;
        }

        /// <summary>Roll and equip a full EquipSlots.Active set of <paramref name="rarity"/> on
        /// every fielded hero, at the stage's AffixItemLevel (MonsterLevel fallback). Bases are
        /// picked per slot by lowest BaseId so dictionary order can't leak in.</summary>
        public static SaveState EquipFullSet(SaveState save, GameConfig cfg, int stage, Rarity rarity, uint seed)
        {
            // StageFor, not a table Find: an endless stage must pin gear at ITS item level, not
            // fall back to stage 1 (that fallback made every endless walls row read unclearable).
            var rt = cfg.StageFor(stage) ?? cfg.Stages[0];
            int itemLevel = Math.Max(1, Math.Max(rt.AffixItemLevel, rt.MonsterLevel));
            var rng = new Rng(seed ^ 0x5F356495u); // gear rolls on their own stream

            var baseBySlot = new Dictionary<EquipSlot, string>();
            foreach (var b in cfg.ItemBases.Values.OrderBy(b => b.BaseId, StringComparer.Ordinal))
                if (!baseBySlot.ContainsKey(b.Slot)) baseBySlot[b.Slot] = b.BaseId;

            foreach (var heroId in save.Party)
            {
                if (heroId == null) continue;
                foreach (var slot in EquipSlots.Active)
                {
                    if (!baseBySlot.TryGetValue(slot, out var baseId)) continue;
                    var item = Loot.RollItem(rng, baseId, itemLevel, rarity, cfg);
                    save = Inventory.AddItems(save, new[] { item });
                    save = Inventory.EquipItem(save, heroId, item.Id, cfg);
                }
            }
            return save;
        }

        /// <summary>Fielded heroes in party-slot order — what the client hands every Init*.</summary>
        public static List<HeroInstance> FieldedParty(SaveState save)
        {
            var list = new List<HeroInstance>();
            foreach (var id in save.Party)
            {
                if (id == null) continue;
                var h = save.Heroes.Find(x => x.Id == id);
                if (h != null) list.Add(h);
            }
            return list;
        }

        public sealed class BossResult
        {
            public bool Won;
            public double Seconds;   // fight length (kill time on a win, timeout/wipe time on a loss)
            public bool Wiped;       // lost to a wipe rather than the timer
        }

        /// <summary>Run the stage's timed boss challenge (the gate that advances a stage).</summary>
        public static BossResult RunBoss(GameConfig cfg, SaveState save, int stage, uint seed)
        {
            var rng = new Rng(seed);
            var s = Combat.InitBossChallenge(FieldedParty(save), stage, cfg, rng);
            Combat.RefreshPartyStats(s, save, cfg); // folds in gear + account buffs, like the client
            Combat.RunToEnd(s, cfg, rng);
            return new BossResult
            {
                Won = s.Status == CombatStatus.Won,
                Seconds = s.TimeMs / 1000.0,
                Wiped = s.Status == CombatStatus.Lost && !s.Entities.Any(e => e.Team == Team.Party && e.Alive),
            };
        }

        public sealed class FarmResult
        {
            public double WindowSeconds;
            public int Kills;
            public int HeroDowns;
            public long HeroHits;    // hero→enemy hits landed (thorns reflections excluded by direction)
            public long Xp;
            public long Gold;
            public int Drops;
            public bool Wiped;

            public double KillsPerMinute => WindowSeconds <= 0 ? 0 : Kills * 60.0 / WindowSeconds;
            /// <summary>~1.0 means trash dies to a single hit — the out-leveled "one-shot" signal.</summary>
            public double HitsPerKill => Kills <= 0 ? double.NaN : (double)HeroHits / Kills;
            public double XpPerMinute => WindowSeconds <= 0 ? 0 : Xp * 60.0 / WindowSeconds;
            public double GoldPerMinute => WindowSeconds <= 0 ? 0 : Gold * 60.0 / WindowSeconds;
        }

        /// <summary>Farm the stage for a fixed window and measure throughput. Entity-id prefixes
        /// are the sim's own convention (heroes "P…", monsters "E…" — see AddParty/SpawnPack).</summary>
        public static FarmResult RunFarm(GameConfig cfg, SaveState save, int stage, double windowSeconds, uint seed)
        {
            var rng = new Rng(seed);
            var s = Combat.InitFarm(FieldedParty(save), stage, cfg, rng);
            Combat.RefreshPartyStats(s, save, cfg);

            var r = new FarmResult { WindowSeconds = windowSeconds };
            while (s.Status == CombatStatus.Running && s.TimeMs < windowSeconds * 1000.0)
            {
                foreach (var ev in Combat.StepCombat(s, Combat.DefaultStepMs, cfg, rng))
                {
                    switch (ev.Type)
                    {
                        case CombatEventType.Death when ev.EntityId != null:
                            if (ev.EntityId.StartsWith("E", StringComparison.Ordinal)) r.Kills++;
                            else r.HeroDowns++;
                            break;
                        case CombatEventType.Hit
                            when ev.SourceId != null && ev.SourceId.StartsWith("P", StringComparison.Ordinal)
                                 && ev.TargetId != null && ev.TargetId.StartsWith("E", StringComparison.Ordinal):
                            r.HeroHits++;
                            break;
                        case CombatEventType.LootDrop:
                            r.Drops++;
                            break;
                    }
                }
            }
            r.WindowSeconds = Math.Min(windowSeconds, s.TimeMs / 1000.0);
            r.Xp = s.PendingXp;
            r.Gold = s.PendingGold;
            r.Wiped = s.Status == CombatStatus.Lost;
            return r;
        }

        /// <summary>Majority-of-trials verdict for "a level-L party with this gear clears stage S".</summary>
        public static bool ClearsBoss(GameConfig cfg, int stage, int level, Rarity? gear, int trials, uint baseSeed,
                                      AccountStacks stacks = default)
        {
            int gearIndex = Array.IndexOf(GearPolicies, gear);
            int wins = 0;
            for (int t = 0; t < trials; t++)
            {
                uint seed = CellSeed(baseSeed, stage, level, gearIndex, t);
                var save = BuildSave(cfg, stage, level, gear, seed, stacks);
                if (RunBoss(cfg, save, stage, seed).Won) wins++;
                if (wins * 2 > trials) return true;                    // majority already decided
                if ((wins + (trials - 1 - t)) * 2 <= trials) return false;
            }
            return wins * 2 > trials;
        }

        /// <summary>
        /// The wall chart: lowest hero level that clears the stage's boss gate under a gear
        /// policy, or null if even the level cap fails. Binary search — power is monotone in
        /// level (base growth, skill points, and kit reveals all only ever increase).
        /// </summary>
        public static int? MinLevelToClear(GameConfig cfg, int stage, Rarity? gear, int trials, uint baseSeed,
                                           AccountStacks stacks = default)
        {
            int lo = 1, hi = cfg.Balance.MaxLevel;
            if (ClearsBoss(cfg, stage, lo, gear, trials, baseSeed, stacks)) return lo;
            if (!ClearsBoss(cfg, stage, hi, gear, trials, baseSeed, stacks)) return null;
            while (hi - lo > 1)
            {
                int mid = lo + (hi - lo) / 2;
                if (ClearsBoss(cfg, stage, mid, gear, trials, baseSeed, stacks)) hi = mid; else lo = mid;
            }
            return hi;
        }

        private static SaveState WithHeroes(SaveState save, List<HeroInstance> heroes) => new SaveState
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
