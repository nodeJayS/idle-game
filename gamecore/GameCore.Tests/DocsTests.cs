using System;
using System.IO;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    /// <summary>
    /// Mechanical enforcement of the doc contract (user call 2026-07-07: "enforce the
    /// rule"). docs/ROADMAP.md is THE priority list and it decayed once already — 514
    /// lines of shipped-receipt prose before the 13ce5fa restructure, because its
    /// "one ledger line when shipped, details in git" rule had no teeth. Every session
    /// runs this suite on every slice, so a bloating roadmap now turns the build red at
    /// the moment of the violation instead of three weeks later.
    /// </summary>
    public class DocsTests
    {
        /// <summary>Generous headroom over the restructured size (~210 lines): the
        /// Your-calls list and backlog briefs may grow legitimately, but +40 lines of
        /// slack is nowhere near enough to hold receipt prose. If this fires, PRUNE —
        /// never raise the budget without the user's say-so.</summary>
        private const int RoadmapLineBudget = 250;

        private static string RoadmapPath()
        {
            // Walk up from the test bin dir until the repo root (the dir holding docs/).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "ROADMAP.md");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("docs/ROADMAP.md not found above the test bin dir");
        }

        [Fact]
        public void RoadmapStaysWithinItsLineBudget()
        {
            string path = RoadmapPath();
            int lines = File.ReadAllLines(path).Length;
            Assert.True(lines <= RoadmapLineBudget,
                $"docs/ROADMAP.md is {lines} lines (budget {RoadmapLineBudget}). " +
                "The ledger rule: a shipped item gets ONE dated line in '## Shipped " +
                "ledger' — full receipts belong in the git commit message, not here. " +
                "Prune the doc; do NOT raise this budget without the user's say-so.");
        }

        [Fact]
        public void RoadmapKeepsItsContractSections()
        {
            string text = File.ReadAllText(RoadmapPath());
            foreach (var heading in new[]
                     {
                         "## Where the game stands",
                         "## Your calls",
                         "## Backlog",
                         "## Shipped ledger",
                         "## Standing rules",
                     })
                Assert.True(text.Contains(heading),
                    $"docs/ROADMAP.md lost its '{heading}' section — the four-doc " +
                    "contract expects the restructured shape (status / user calls / " +
                    "backlog / ledger / rules). Restore the section rather than " +
                    "deleting this test.");
        }
    }
}
