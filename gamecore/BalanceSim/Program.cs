#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IdleGame.GameCore;

namespace IdleGame.BalanceSim
{
    /// <summary>
    /// Balance-sim console runner (design doc Phase B tooling): charts difficulty vs hero
    /// power over pure GameCore and finds the walls. Progress goes to stderr; the report to
    /// stdout, so output stays pipeable. Everything is deterministic under --seed.
    ///
    ///   walls  [--stages 1-100] [--gear none,normal,rare,legendary | all] [--trials 3] [--seed 1] [--csv path]
    ///          Min hero level to clear each stage's boss gate, one column per gear policy.
    ///   sweep  [--stages 1-100:5] [--levels 5-100:5] [--gear rare] [--trials 1] [--seed 1] [--csv path]
    ///          Win/loss grid over stage × level at a fixed gear policy.
    ///   farm   --stage S --level L [--gear rare] [--window 120] [--seed 1]
    ///          Farm-throughput detail for one point: kills/min, hits-per-kill (≈1 = one-shot),
    ///          XP/gold rates, downs.
    ///   crypt  [--depths 1-60] [--stage 25] [--level 50] [--gear rare] [--trials 1] [--seed 1] [--csv path]
    ///          Crypt depth chart (§7.3): one floor per depth on a PINNED-power party, difficulty vs
    ///          reward as the depth ramp climbs — result, sweep seconds, rooms/chests, gold/dust/xp/
    ///          items/downs; then the crypt wall (first depth the party fails) and the reward-vs-ramp
    ///          trend. --stage/--level/--gear pin the party; depth is the only variable.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var cfg = GameConfig.Default();
            string mode = args.Length > 0 && !args[0].StartsWith("-") ? args[0] : "walls";
            var opts = ParseOptions(args.Skip(mode == args.ElementAtOrDefault(0) ? 1 : 0).ToArray());

            if (opts.ContainsKey("help") || mode == "help")
            {
                Console.WriteLine("modes: walls (default) | sweep | farm | crypt — see Program.cs header for options.");
                return 0;
            }

            uint seed = (uint)GetInt(opts, "seed", 1);
            switch (mode)
            {
                case "walls": return Walls(cfg, opts, seed);
                case "sweep": return Sweep(cfg, opts, seed);
                case "farm": return Farm(cfg, opts, seed);
                case "crypt": return CryptChart(cfg, opts, seed);
                default:
                    Console.Error.WriteLine($"unknown mode \"{mode}\" (walls | sweep | farm | crypt)");
                    return 2;
            }
        }

        // ---- walls -------------------------------------------------------------------

        private static int Walls(GameConfig cfg, Dictionary<string, string> opts, uint seed)
        {
            var (sFrom, sTo, sStep) = GetRange(opts, "stages", 1, cfg.Stages.Count, 1);
            int trials = GetInt(opts, "trials", 3);
            var gears = GetGears(opts, defaults: new Rarity?[] { null, Rarity.Normal, Rarity.Rare, Rarity.Legendary });

            var rows = new List<(int Stage, int?[] MinLevel)>();
            for (int stage = sFrom; stage <= sTo; stage += sStep)
            {
                var row = new int?[gears.Length];
                for (int g = 0; g < gears.Length; g++)
                    row[g] = Scenarios.MinLevelToClear(cfg, stage, gears[g], trials, seed);
                rows.Add((stage, row));
                Console.Error.Write($"\rwalls: stage {stage}/{sTo}   ");
            }
            Console.Error.WriteLine();

            var sb = new StringBuilder();
            sb.AppendLine($"Min hero level to clear the boss gate (trials={trials}, seed={seed}; \"--\" = fails even at the level cap)");
            sb.Append("stage".PadLeft(5));
            foreach (var g in gears) sb.Append(Scenarios.GearName(g).PadLeft(11));
            sb.AppendLine();
            foreach (var (stage, mins) in rows)
            {
                sb.Append(stage.ToString().PadLeft(5));
                foreach (var m in mins) sb.Append((m?.ToString() ?? "--").PadLeft(11));
                sb.AppendLine();
            }

            // The walls: stages where the required level jumps hard vs the previous stage.
            sb.AppendLine();
            for (int g = 0; g < gears.Length; g++)
            {
                var jumps = new List<string>();
                for (int i = 1; i < rows.Count; i++)
                {
                    int? prev = rows[i - 1].MinLevel[g], cur = rows[i].MinLevel[g];
                    if (prev.HasValue && !cur.HasValue) jumps.Add($"stage {rows[i].Stage} (unclearable)");
                    else if (prev.HasValue && cur.HasValue && cur.Value - prev.Value >= 5)
                        jumps.Add($"stage {rows[i].Stage} (+{cur.Value - prev.Value} → L{cur.Value})");
                }
                sb.AppendLine($"walls @ {Scenarios.GearName(gears[g])}: " + (jumps.Count > 0 ? string.Join(", ", jumps) : "none"));
            }
            Console.Write(sb.ToString());

            WriteCsv(opts, "stage,gear,min_level", w =>
            {
                foreach (var (stage, mins) in rows)
                    for (int g = 0; g < gears.Length; g++)
                        w.WriteLine($"{stage},{Scenarios.GearName(gears[g])},{mins[g]?.ToString() ?? ""}");
            });
            return 0;
        }

        // ---- sweep -------------------------------------------------------------------

        private static int Sweep(GameConfig cfg, Dictionary<string, string> opts, uint seed)
        {
            var (sFrom, sTo, sStep) = GetRange(opts, "stages", 1, cfg.Stages.Count, 5);
            var (lFrom, lTo, lStep) = GetRange(opts, "levels", 5, cfg.Balance.MaxLevel, 5);
            int trials = GetInt(opts, "trials", 1);
            var gear = GetGears(opts, defaults: new Rarity?[] { Rarity.Rare })[0];
            int gearIndex = Array.IndexOf(Scenarios.GearPolicies, gear);

            var levels = new List<int>();
            for (int l = lFrom; l <= lTo; l += lStep) levels.Add(l);

            var results = new List<(int Stage, int Level, bool Won, double Seconds)>();
            var sb = new StringBuilder();
            sb.AppendLine($"Boss-gate outcomes, gear={Scenarios.GearName(gear)} (rows=stage, cols=level; '#'=win '.'=loss; trials={trials}, seed={seed})");
            sb.Append("     ");
            foreach (var l in levels) sb.Append(l.ToString().PadLeft(4));
            sb.AppendLine();

            for (int stage = sFrom; stage <= sTo; stage += sStep)
            {
                sb.Append(stage.ToString().PadLeft(5));
                foreach (var level in levels)
                {
                    bool won = Scenarios.ClearsBoss(cfg, stage, level, gear, trials, seed);
                    uint cellSeed = Scenarios.CellSeed(seed, stage, level, gearIndex, 0);
                    var one = Scenarios.RunBoss(cfg, Scenarios.BuildSave(cfg, stage, level, gear, cellSeed), stage, cellSeed);
                    results.Add((stage, level, won, one.Seconds));
                    sb.Append((won ? "#" : ".").PadLeft(4));
                }
                sb.AppendLine();
                Console.Error.Write($"\rsweep: stage {stage}/{sTo}   ");
            }
            Console.Error.WriteLine();
            Console.Write(sb.ToString());

            WriteCsv(opts, "stage,level,gear,won,fight_seconds", w =>
            {
                foreach (var r in results)
                    w.WriteLine($"{r.Stage},{r.Level},{Scenarios.GearName(gear)},{(r.Won ? 1 : 0)},{r.Seconds.ToString("0.0", CultureInfo.InvariantCulture)}");
            });
            return 0;
        }

        // ---- farm --------------------------------------------------------------------

        private static int Farm(GameConfig cfg, Dictionary<string, string> opts, uint seed)
        {
            int stage = GetInt(opts, "stage", 1);
            int level = GetInt(opts, "level", 1);
            double window = GetInt(opts, "window", 120);
            var gear = GetGears(opts, defaults: new Rarity?[] { Rarity.Rare })[0];
            int gearIndex = Array.IndexOf(Scenarios.GearPolicies, gear);

            uint cellSeed = Scenarios.CellSeed(seed, stage, level, gearIndex, 0);
            var save = Scenarios.BuildSave(cfg, stage, level, gear, cellSeed);
            var r = Scenarios.RunFarm(cfg, save, stage, window, cellSeed);
            var boss = Scenarios.RunBoss(cfg, save, stage, cellSeed);

            double xpToNext = level < cfg.Balance.MaxLevel ? cfg.Balance.XpCurve(level) : 0;
            string minsToLevel = r.XpPerMinute > 0 && xpToNext > 0
                ? (xpToNext / r.XpPerMinute).ToString("0.0", CultureInfo.InvariantCulture)
                : "n/a";

            Console.WriteLine($"Farm stage {stage} — level {level}, gear {Scenarios.GearName(gear)}, window {r.WindowSeconds:0}s (seed {seed})");
            Console.WriteLine($"  party power    {Stats.ComputePartyPower(save, cfg):0}  ({Scenarios.FieldedParty(save).Count} heroes)");
            Console.WriteLine($"  kills          {r.Kills}  ({r.KillsPerMinute:0.0}/min)");
            Console.WriteLine($"  hits per kill  {(double.IsNaN(r.HitsPerKill) ? "n/a" : r.HitsPerKill.ToString("0.00", CultureInfo.InvariantCulture))}  (≈1 = one-shotting trash)");
            Console.WriteLine($"  hero downs     {r.HeroDowns}{(r.Wiped ? "  (WIPED before the window ended)" : "")}");
            Console.WriteLine($"  xp/min         {r.XpPerMinute:0}  (next level in ~{minsToLevel} min)");
            Console.WriteLine($"  gold/min       {r.GoldPerMinute:0}");
            Console.WriteLine($"  drops          {r.Drops}");
            Console.WriteLine($"  boss gate      {(boss.Won ? $"WIN in {boss.Seconds:0.0}s" : boss.Wiped ? "LOSS (wipe)" : "LOSS (timer)")}");
            return 0;
        }

        // ---- crypt -------------------------------------------------------------------

        private sealed class CryptRow
        {
            public int Depth;
            public double WinRate;    // fraction of trials cleared
            public double TimeoutRate; // fraction of trials that failed to the run-timer (party alive)
            public double Seconds, Rooms, Chests, Gold, Dust, Xp, Items, Downs, Keys;
            public bool Final;         // a run's final floor (reward vault + true boss)
            public double GoldPerMin => Seconds > 0 ? Gold * 60.0 / Seconds : 0;
            public double DustPerMin => Seconds > 0 ? Dust * 60.0 / Seconds : 0;
        }

        private static int CryptChart(GameConfig cfg, Dictionary<string, string> opts, uint seed)
        {
            var (dFrom, dTo, dStep) = GetRange(opts, "depths", 1, cfg.Balance.CryptMaxDepth, 1);
            int stage = GetInt(opts, "stage", 25);
            int level = GetInt(opts, "level", 50);
            var gear = GetGears(opts, defaults: new Rarity?[] { Rarity.Rare })[0];
            int gearIndex = Array.IndexOf(Scenarios.GearPolicies, gear);
            int trials = Math.Max(1, GetInt(opts, "trials", 1));

            // Party is PINNED across depths: build one save per trial (stage/level/gear fix the power;
            // only the depth ramps). The cell seed is trial-varied but depth-independent — GenSeed folds
            // the depth in for the layout, so each floor still differs.
            var saves = new SaveState[trials];
            var cellSeeds = new uint[trials];
            for (int t = 0; t < trials; t++)
            {
                cellSeeds[t] = Scenarios.CellSeed(seed, stage, level, gearIndex, t);
                saves[t] = Scenarios.BuildSave(cfg, stage, level, gear, cellSeeds[t]);
            }
            double partyPower = Stats.ComputePartyPower(saves[0], cfg);
            int heroCount = Scenarios.FieldedParty(saves[0]).Count;

            var rows = new List<CryptRow>();
            for (int d = dFrom; d <= dTo; d += dStep)
            {
                var row = new CryptRow { Depth = d, Final = d % Math.Max(1, cfg.Balance.CryptFloorsPerRun) == 0 };
                for (int t = 0; t < trials; t++)
                {
                    var r = CryptScenarios.RunFloor(cfg, saves[t], stage, d, cellSeeds[t]);
                    if (r.Won) row.WinRate++;
                    if (r.TimedOut) row.TimeoutRate++;
                    if (r.KeyHeld) row.Keys++;
                    row.Seconds += r.Seconds; row.Rooms += r.RoomsCleared; row.Chests += r.ChestsOpened;
                    row.Gold += r.Gold; row.Dust += r.Dust; row.Xp += r.Xp; row.Items += r.Items;
                    row.Downs += r.HeroDowns;
                }
                row.WinRate /= trials; row.TimeoutRate /= trials; row.Keys /= trials;
                row.Seconds /= trials; row.Rooms /= trials; row.Chests /= trials; row.Gold /= trials;
                row.Dust /= trials; row.Xp /= trials; row.Items /= trials; row.Downs /= trials;
                rows.Add(row);
                Console.Error.Write($"\rcrypt: depth {d}/{dTo}   ");
            }
            Console.Error.WriteLine();

            var ci = CultureInfo.InvariantCulture;
            string Result(CryptRow r)
            {
                if (trials == 1)
                    return r.WinRate >= 1 ? "WIN" : r.TimeoutRate >= 1 ? "LOSS*" : "LOSS";
                return (r.WinRate * trials).ToString("0", ci) + "/" + trials;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Crypt depth chart — party pinned at stage {stage}, level {level}, gear {Scenarios.GearName(gear)} "
                          + $"(power {partyPower:0}, {heroCount} heroes; trials={trials}, seed={seed})");
            sb.AppendLine("depth marks a run's FINAL floor with '*' (reward vault + true boss); others field the mini-boss guardian.");
            if (trials == 1) sb.AppendLine("result LOSS* = lost to the run-timer failsafe with the party still standing (a slog, not a wipe).");
            sb.AppendLine();
            sb.Append("depth".PadLeft(6));
            sb.Append("result".PadLeft(8));
            sb.Append("secs".PadLeft(8));
            sb.Append("rooms".PadLeft(7));
            sb.Append("chests".PadLeft(8));
            sb.Append("gold".PadLeft(9));
            sb.Append("dust".PadLeft(7));
            sb.Append("xp".PadLeft(11));
            sb.Append("items".PadLeft(7));
            sb.Append("downs".PadLeft(7));
            sb.AppendLine();
            foreach (var r in rows)
            {
                sb.Append(((r.Final ? "*" : "") + r.Depth).PadLeft(6));
                sb.Append(Result(r).PadLeft(8));
                sb.Append(r.Seconds.ToString("0.0", ci).PadLeft(8));
                sb.Append(r.Rooms.ToString("0.0", ci).PadLeft(7));
                sb.Append(r.Chests.ToString("0.0", ci).PadLeft(8));
                sb.Append(r.Gold.ToString("0", ci).PadLeft(9));
                sb.Append(r.Dust.ToString("0", ci).PadLeft(7));
                sb.Append(r.Xp.ToString("0", ci).PadLeft(11));
                sb.Append(r.Items.ToString("0.0", ci).PadLeft(7));
                sb.Append(r.Downs.ToString("0.0", ci).PadLeft(7));
                sb.AppendLine();
            }

            // ---- findings --------------------------------------------------------
            sb.AppendLine();

            // The wall: first depth whose win rate drops below a majority.
            CryptRow? wall = rows.FirstOrDefault(r => r.WinRate < 0.5);
            if (wall == null)
            {
                var last = rows[rows.Count - 1];
                sb.AppendLine($"crypt wall: none — the party clears every charted depth through {last.Depth} "
                              + $"(deepest sweep {last.Seconds:0.0}s, {last.Downs:0.0} downs).");
                bool trivialDeep = last.Downs < 0.5 && last.Seconds < cfg.Balance.DungeonMaxRunSeconds * 0.5;
                if (trivialDeep)
                    sb.AppendLine("  DEGENERATE: the deepest floor still clears with ~no downs well inside the timer — "
                                  + "the depth ramp is too shallow for this party (no meaningful ceiling in range).");
            }
            else
            {
                string how = wall.TimeoutRate >= 0.5 ? "run-timer failsafe (slog — party outlasts the clock, floor unfinished)"
                                                     : "party wipe";
                sb.AppendLine($"crypt wall: depth {(wall.Final ? "*" : "")}{wall.Depth} — first depth the party fails "
                              + $"({how}; sweep {wall.Seconds:0.0}s, {wall.Downs:0.0} downs).");
                var lastWin = rows.LastOrDefault(r => r.WinRate >= 0.5);
                if (lastWin != null && lastWin.Depth < wall.Depth)
                    sb.AppendLine($"  deepest reliable clear: depth {lastWin.Depth} ({lastWin.Seconds:0.0}s, {lastWin.Downs:0.0} downs).");
            }

            // Reward-vs-ramp trend: does gold/dust per minute keep pace as HP ramps and sweeps lengthen?
            var cleared = rows.Where(r => r.WinRate >= 0.5).ToList();
            if (cleared.Count >= 2)
            {
                var first = cleared[0];
                var lastC = cleared[cleared.Count - 1];
                double hpRamp = Crypt.FloorHpMult(lastC.Depth, cfg) / Crypt.FloorHpMult(first.Depth, cfg);
                sb.AppendLine();
                sb.AppendLine($"reward vs ramp (depth {first.Depth}→{lastC.Depth}, monster HP ×{hpRamp:0.0} over that span):");
                sb.AppendLine($"  sweep seconds {first.Seconds:0.0} → {lastC.Seconds:0.0}   "
                              + $"(×{(first.Seconds > 0 ? lastC.Seconds / first.Seconds : 0):0.00})");
                sb.AppendLine($"  gold/min      {first.GoldPerMin:0} → {lastC.GoldPerMin:0}   "
                              + $"(×{(first.GoldPerMin > 0 ? lastC.GoldPerMin / first.GoldPerMin : 0):0.00})");
                sb.AppendLine($"  dust/min      {first.DustPerMin:0} → {lastC.DustPerMin:0}   "
                              + $"(×{(first.DustPerMin > 0 ? lastC.DustPerMin / first.DustPerMin : 0):0.00})");
                double dustPace = first.DustPerMin > 0 ? lastC.DustPerMin / first.DustPerMin : 0;
                sb.AppendLine(dustPace >= 0.9
                    ? "  → dust rate holds up as floors lengthen — reward keeps pace with the HP ramp."
                    : "  → dust/min DECAYS as floors lengthen — reward falls behind the HP ramp (deeper = worse dust/hour).");
            }
            Console.Write(sb.ToString());

            WriteCsv(opts, "depth,final,win_rate,timeout_rate,seconds,rooms,chests,gold,dust,xp,items,downs,keys", w =>
            {
                foreach (var r in rows)
                    w.WriteLine(string.Join(",", new[]
                    {
                        r.Depth.ToString(ci), r.Final ? "1" : "0",
                        r.WinRate.ToString("0.###", ci), r.TimeoutRate.ToString("0.###", ci),
                        r.Seconds.ToString("0.0", ci), r.Rooms.ToString("0.0", ci), r.Chests.ToString("0.0", ci),
                        r.Gold.ToString("0", ci), r.Dust.ToString("0", ci), r.Xp.ToString("0", ci),
                        r.Items.ToString("0.0", ci), r.Downs.ToString("0.0", ci), r.Keys.ToString("0.0", ci),
                    }));
            });
            return 0;
        }

        // ---- option helpers ----------------------------------------------------------

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var opts = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                string key = args[i].Substring(2);
                string val = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
                opts[key] = val;
            }
            return opts;
        }

        private static int GetInt(Dictionary<string, string> opts, string key, int fallback)
            => opts.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

        /// <summary>"A-B" or "A-B:STEP" or a single "A".</summary>
        private static (int From, int To, int Step) GetRange(Dictionary<string, string> opts, string key,
                                                             int from, int to, int step)
        {
            if (!opts.TryGetValue(key, out var v) || v.Length == 0) return (from, to, step);
            var stepSplit = v.Split(':');
            if (stepSplit.Length > 1 && int.TryParse(stepSplit[1], out var st)) step = Math.Max(1, st);
            var range = stepSplit[0].Split('-');
            if (int.TryParse(range[0], out var a)) from = a;
            to = range.Length > 1 && int.TryParse(range[1], out var b) ? b : from;
            return (from, to, step);
        }

        private static Rarity?[] GetGears(Dictionary<string, string> opts, Rarity?[] defaults)
        {
            if (!opts.TryGetValue("gear", out var v) || v.Length == 0) return defaults;
            if (v == "all") return Scenarios.GearPolicies;
            var list = new List<Rarity?>();
            foreach (var name in v.Split(','))
            {
                if (name == "none") { list.Add(null); continue; }
                if (Enum.TryParse<Rarity>(name, ignoreCase: true, out var r)) { list.Add(r); continue; }
                Console.Error.WriteLine($"unknown gear \"{name}\" (none|normal|rare|unique|legendary|mythic|all)");
            }
            return list.Count > 0 ? list.ToArray() : defaults;
        }

        private static void WriteCsv(Dictionary<string, string> opts, string header, Action<TextWriter> body)
        {
            if (!opts.TryGetValue("csv", out var path) || path.Length == 0) return;
            using var w = new StreamWriter(path);
            w.WriteLine(header);
            body(w);
            Console.Error.WriteLine($"csv written: {Path.GetFullPath(path)}");
        }
    }
}
