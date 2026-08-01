using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// The string-table contract (10.20c). The debt-stopper is <see cref="EveryReferencedKeyExists"/>:
    /// it regex-scans every client + GameCore source file for <c>Loc.T("…")</c> / <c>Loc.F("…")</c>
    /// and fails the suite on any key missing from <see cref="Loc.En"/> — so a future hardcoded key
    /// that never got tabled turns the build red at the moment of the violation, not when a player
    /// sees raw key soup. The rest pin table hygiene: no empty values, kebab-case keys, and every
    /// format entry actually string.Formats (malformed braces throw at RENDER time otherwise —
    /// in the middle of a feed line, the worst place to learn about a typo).
    /// </summary>
    public class LocTests
    {
        /// <summary>Repo root: walk up from the test bin dir (the DocsTests pattern, ≤8 dirs).</summary>
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "unity", "Assets", "Game"))) return dir.FullName;
            }
            throw new DirectoryNotFoundException("unity/Assets/Game not found above the test bin dir");
        }

        // A literal key is the string that starts Loc.T( / Loc.F( and runs to its closing quote
        // followed by `,` or `)` — the trailing delimiter deliberately EXCLUDES composed keys
        // ("rarity." + name), which can't be verified statically; those are pinned by the
        // dedicated ComposedRarityKeysExist fact below instead.
        private static readonly Regex KeyRef = new(@"Loc\.(?:T|F)\(\s*""([^""]+)""\s*[,)]", RegexOptions.Compiled);

        private static IEnumerable<string> SourceFiles()
        {
            string root = RepoRoot();
            foreach (var sub in new[] { Path.Combine("unity", "Assets", "Game"), Path.Combine("unity", "Assets", "GameCore") })
                foreach (var f in Directory.EnumerateFiles(Path.Combine(root, sub), "*.cs", SearchOption.AllDirectories))
                    yield return f;
        }

        [Fact]
        public void EveryReferencedKeyExists()
        {
            var missing = new List<string>();
            foreach (var file in SourceFiles())
            {
                string text = File.ReadAllText(file);
                foreach (Match m in KeyRef.Matches(text))
                {
                    string key = m.Groups[1].Value;
                    if (!Loc.En.ContainsKey(key))
                        missing.Add($"{Path.GetFileName(file)}: \"{key}\"");
                }
            }
            Assert.True(missing.Count == 0,
                "Loc key(s) referenced in code but missing from Loc.En — add the entry, don't ship key soup:\n  "
                + string.Join("\n  ", missing));
        }

        [Fact]
        public void NoEmptyValuesAndKeysAreKebabCase()
        {
            var keyStyle = new Regex("^[a-z0-9.-]+$");
            foreach (var kv in Loc.En)
            {
                Assert.True(keyStyle.IsMatch(kv.Key), $"Loc key '{kv.Key}' breaks the dot-namespaced lowercase-kebab style.");
                Assert.False(string.IsNullOrEmpty(kv.Value), $"Loc key '{kv.Key}' has an empty value.");
            }
        }

        [Fact]
        public void FormatEntriesFormat()
        {
            // Every {0}-bearing value must survive string.Format with dummy args — this catches a
            // malformed brace ({0)... / {{0}) at TEST time instead of at render time. Numeric dummies
            // because several entries carry numeric format specifiers ({1:0.#}).
            var indexRe = new Regex(@"\{(\d+)");
            foreach (var kv in Loc.En)
            {
                if (!kv.Value.Contains("{0}") && !indexRe.IsMatch(kv.Value)) continue;
                int max = indexRe.Matches(kv.Value).Cast<Match>().Max(m => int.Parse(m.Groups[1].Value));
                object[] args = Enumerable.Repeat((object)1, max + 1).ToArray();
                var ex = Record.Exception(() => Loc.F(kv.Key, args));
                Assert.True(ex == null, $"Loc key '{kv.Key}' fails string.Format: {ex?.Message}");
            }
        }

        [Fact]
        public void ComposedRarityKeysExist()
        {
            // StatDisplay.RarityName builds its key by concatenation ("rarity." + name), which the
            // static scan above cannot see — pin every enum member's key here so a renamed/added
            // Rarity can't silently render as its raw key.
            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
                Assert.True(Loc.En.ContainsKey("rarity." + r.ToString().ToLowerInvariant()),
                    $"Loc.En is missing 'rarity.{r.ToString().ToLowerInvariant()}' (composed by StatDisplay.RarityName).");
        }
    }
}
