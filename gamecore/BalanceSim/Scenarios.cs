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
                    Stars = h.Stars,
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

        // ================================================================================
        // Endgame SINK horizon (10.17 / mobile arc MM5) — the ~6-month spend-horizon model.
        // Coarse EXPECTED-VALUE economics (NOT a per-roll simulation): a daily endgame player's
        // weekly income of each currency vs the total cost to saturate each endgame SINK family,
        // giving weeks-to-saturation per family and the content HORIZON (the last sink to empty).
        // The deliverable is the horizon ESTIMATE + a re-tunable harness, not a faithful sim — so
        // the modeling assumptions live in SinkModelParams, all documented and overridable.
        // ================================================================================

        /// <summary>Tunable modeling assumptions for <see cref="ComputeSinkHorizon"/> — the harness knobs
        /// (NOT game constants). Retune these consciously; the game constants (star costs, enhance curve,
        /// shard rates) are read straight from <see cref="GameConfig"/>.</summary>
        public struct SinkModelParams
        {
            public int FarmStage;              // endgame farm depth the income is measured at (default: last campaign stage)
            public int Level;                  // party level (default: MaxLevel)
            public Rarity Gear;                // gear policy for the income party (default: Mythic — endgame)
            public double ActiveMinutesPerDay; // active farming minutes/day for a daily player (idle accrual omitted = conservative)
            public double FarmWindowSeconds;   // farm-throughput measurement window
            public int ReforgesPerSlotLifetime;// affix-chase reforges per worn slot over the whole content lifetime
            public double DupeFraction;        // fraction of gacha rolls that are DUPES at endgame (owned roster ⇒ ~all)
            public double EndlessMilestonesPerWeek; // NEW endless milestones the endgame push still clears per week (tapering)
            // THE horizon lever. At maxed endgame the farm economy floods gold+scrap (enhance/reforge
            // saturate in days), so the horizon is entirely GEM-gated ascension. A daily player does NOT
            // funnel 100% of premium gems into one ascension banner — gems also pull NEW heroes, bank for
            // future banners, and fund other spends; only a fraction farms dupes for ascension shards.
            // This is what stretches the whole-roster ascension chase to the ~6-month horizon WITHOUT
            // touching the user's per-hero star costs ({10,20,30,50,80} ≈ 19 dupes/hero stays intact).
            public double GemFractionToAscension;

            public static SinkModelParams Default(GameConfig cfg) => new SinkModelParams
            {
                FarmStage = cfg.Stages.Count,
                Level = cfg.Balance.MaxLevel,
                Gear = Rarity.Mythic,
                ActiveMinutesPerDay = 90,   // ~1.5h of active farming a day; idle accrual would add gold on top
                FarmWindowSeconds = 120,
                ReforgesPerSlotLifetime = 40,
                DupeFraction = 1.0,          // at endgame the whole pool is owned, so every roll dupes
                EndlessMilestonesPerWeek = 1,
                GemFractionToAscension = 0.33, // ~a third of premium income funds ascension dupes (see above)
            };
        }

        /// <summary>The computed horizon: per-currency weekly income, per-family total sink cost, and the
        /// weeks-to-saturation each family takes — plus the overall content HORIZON (max across families,
        /// i.e. the last sink a daily player exhausts).</summary>
        public sealed class SinkHorizon
        {
            public int PartyHeroes;
            public int FarmStage, ItemLevel;
            public double GoldPerMin, ScrapPerMin, DropsPerMin;
            // weekly income by currency
            public double GoldPerWeek, ScrapPerWeek, GemsPerWeek, ShardsPerWeek;
            // total lifetime cost of each sink family (to fully saturate it)
            public double EnhanceScrapCost, StarShardCost, ReforgeGoldCost, ReforgeScrapCost;
            // weeks to saturate each family (cost / the weekly income of its currency)
            public double EnhanceWeeks, StarWeeks, ReforgeWeeks;
            /// <summary>The content horizon: the LAST sink family a daily player saturates.</summary>
            public double HorizonWeeks;
        }

        /// <summary>
        /// Model the ~6-month endgame spend horizon (the 10.17 acceptance). Measures a daily endgame
        /// player's weekly income of each currency (from a real farm window at the endgame depth) against
        /// the total cost to fully saturate each sink family — gear enhancement to +EnhanceMax across the
        /// worn party, hero ascension to max stars, and an affix-reforge cadence — and returns the weeks
        /// each takes plus the overall horizon. Deterministic under <paramref name="seed"/>.
        /// </summary>
        public static SinkHorizon ComputeSinkHorizon(GameConfig cfg, uint seed, SinkModelParams? paramsOpt = null)
        {
            var p = paramsOpt ?? SinkModelParams.Default(cfg);
            var b = cfg.Balance;

            // ---- income: one real farm window at the endgame depth, pinned at max power ----
            int gearIndex = Array.IndexOf(GearPolicies, (Rarity?)p.Gear);
            uint cellSeed = CellSeed(seed, p.FarmStage, p.Level, gearIndex, 0);
            var incomeSave = BuildSave(cfg, p.FarmStage, p.Level, p.Gear,
                cellSeed, AccountStacks.None); // bare account: income shouldn't presume the very stacks it funds
            var farm = RunFarm(cfg, incomeSave, p.FarmStage, p.FarmWindowSeconds, cellSeed);

            var rt = cfg.StageFor(p.FarmStage) ?? cfg.Stages[0];
            int itemLevel = Math.Max(1, Math.Max(rt.AffixItemLevel, rt.MonsterLevel));
            int partyHeroes = FieldedParty(incomeSave).Count;

            // Scrap income = salvaged drops. Coarse: value each drop at a representative salvage — a
            // Rare-tier piece at the farm's item level (drops skew low-rarity, so Rare is a mid estimate).
            double avgScrapPerDrop = cfg.Balance.ScrapValue(Rarity.Rare, itemLevel);
            double scrapPerMin = farm.KillsPerMinute > 0 ? (farm.Drops * 60.0 / Math.Max(1e-9, farm.WindowSeconds)) * avgScrapPerDrop : 0;
            double goldPerMin = farm.GoldPerMinute;
            double dropsPerMin = farm.Drops * 60.0 / Math.Max(1e-9, farm.WindowSeconds);

            double activeMinPerWeek = p.ActiveMinutesPerDay * 7.0;
            double goldPerWeek = goldPerMin * activeMinPerWeek;
            double scrapPerWeek = scrapPerMin * activeMinPerWeek;

            // Gems/week: daily-login base × 7 + endless-milestone gems. Shards/week: universal from the
            // same endless milestones + hero shards from spending that week's gems on dupes.
            double gemsPerWeek = b.DailyLoginBaseGems * 7.0
                                 + b.EndlessGemsPerMilestone * p.EndlessMilestonesPerWeek;
            long bannerCost = FirstBannerCost(cfg);
            double rollsPerWeek = bannerCost > 0 ? (gemsPerWeek * p.GemFractionToAscension) / bannerCost : 0;
            double dupeShardsPerWeek = rollsPerWeek * p.DupeFraction * b.AscensionShardsPerDupe;
            double universalShardsPerWeek = b.AscensionShardsPerEndlessMilestone * p.EndlessMilestonesPerWeek;
            double shardsPerWeek = dupeShardsPerWeek + universalShardsPerWeek;

            // ---- sink costs ----
            int slots = EquipSlots.Active.Length;
            var repItem = new Item { Rarity = p.Gear, ItemLevel = itemLevel }; // a representative worn piece
            double enhanceScrapPerItem = ExpectedScrapToMaxEnhance(repItem, cfg);
            double enhanceScrapCost = enhanceScrapPerItem * partyHeroes * slots;

            // Ascension is a permanent PER-HERO investment the whole COLLECTION wants (not just the 3
            // fielded slots) — a roster-builder ascends every owned hero, and gacha keeps minting more.
            int ascendHeroes = incomeSave.Heroes.Count;
            double starPerHero = 0;
            foreach (var c in b.AscensionStarCosts) starPerHero += c;
            double starShardCost = starPerHero * ascendHeroes;

            var (reforgeGold, reforgeScrap) = Inventory.ReforgeCost(repItem, cfg);
            double reforgeGoldCost = (double)reforgeGold * p.ReforgesPerSlotLifetime * partyHeroes * slots;
            double reforgeScrapCost = (double)reforgeScrap * p.ReforgesPerSlotLifetime * partyHeroes * slots;

            // ---- weeks-to-saturation per family (cost / that currency's weekly income) ----
            double W(double cost, double perWeek) => perWeek > 0 ? cost / perWeek : double.PositiveInfinity;
            double enhanceWeeks = W(enhanceScrapCost, scrapPerWeek);
            double starWeeks = W(starShardCost, shardsPerWeek);
            // reforge draws BOTH gold and scrap; the binding constraint is the slower currency, but it
            // also competes with enhance for scrap — charge reforge scrap on top of enhance scrap.
            double reforgeWeeks = Math.Max(W(reforgeGoldCost, goldPerWeek),
                                           W(enhanceScrapCost + reforgeScrapCost, scrapPerWeek));

            double horizon = Max3(enhanceWeeks, starWeeks, reforgeWeeks);

            return new SinkHorizon
            {
                PartyHeroes = partyHeroes,
                FarmStage = p.FarmStage,
                ItemLevel = itemLevel,
                GoldPerMin = goldPerMin,
                ScrapPerMin = scrapPerMin,
                DropsPerMin = dropsPerMin,
                GoldPerWeek = goldPerWeek,
                ScrapPerWeek = scrapPerWeek,
                GemsPerWeek = gemsPerWeek,
                ShardsPerWeek = shardsPerWeek,
                EnhanceScrapCost = enhanceScrapCost,
                StarShardCost = starShardCost,
                ReforgeGoldCost = reforgeGoldCost,
                ReforgeScrapCost = reforgeScrapCost,
                EnhanceWeeks = enhanceWeeks,
                StarWeeks = starWeeks,
                ReforgeWeeks = reforgeWeeks,
                HorizonWeeks = horizon,
            };
        }

        /// <summary>Expected scrap to enhance one item from +0 to +EnhanceMax, summing per-level
        /// EnhanceCost × expected attempts (1/successChance). A LOWER bound: it ignores the extra re-climb
        /// cost when a fail at/above EnhanceDropFrom knocks the item down a level — so the real sink (and
        /// horizon) is a touch LONGER than this reports.</summary>
        public static double ExpectedScrapToMaxEnhance(Item item, GameConfig cfg)
        {
            var b = cfg.Balance;
            double total = 0;
            var probe = new Item { Rarity = item.Rarity, ItemLevel = item.ItemLevel };
            for (int level = 1; level <= b.EnhanceMax; level++)
            {
                double p = b.EnhanceSuccess[level - 1];
                double attempts = p > 0 ? 1.0 / p : 0;
                probe.Enhance = level - 1;                 // cost is charged at the CURRENT level before the attempt
                total += b.EnhanceCost(probe) * attempts;
            }
            return total;
        }

        /// <summary>The cheapest banner's gem cost (the roll price shard income is priced against), or 0 if
        /// no banner ships.</summary>
        private static long FirstBannerCost(GameConfig cfg)
        {
            long best = 0;
            foreach (var kv in cfg.Banners)
                if (best == 0 || kv.Value.CostGems < best) best = kv.Value.CostGems;
            return best;
        }

        private static double Max3(double a, double b, double c)
        {
            double m = a;
            if (b > m) m = b;
            if (c > m) m = c;
            return m;
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
