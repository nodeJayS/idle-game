using System.Collections.Generic;
using System.Linq;
using IdleGame.GameCore;
using Xunit;

namespace IdleGame.GameCore.Tests
{
    // Codex / collection (10.15, mobile arc MM3): three collection families (monster kills, gear-set
    // discovery, zone clears), each COMPLETED tier paying a small permanent account-wide stat drip
    // (auto-derived — nothing claimable). PURE C# sim only. Mirrors the Achievements/Tower/Crypt idioms
    // its state and buff sit beside.
    public class CodexTests
    {
        private static readonly GameConfig Cfg = GameConfig.Default();
        private const long Now = 1_780_000_000_000L;

        private static SaveState Fresh() => Save.NewGame(1, Cfg, Now);
        private static string AnyMonster() => Cfg.Monsters.Keys.First();
        private static string BaseForSlot(EquipSlot slot) => Cfg.ItemBases.First(kv => kv.Value.Slot == slot).Key;
        private static string AnySetId() => Cfg.Sets.Keys.First();

        private static Item SetPiece(EquipSlot slot, string setId, Rarity rarity = Rarity.Rare) =>
            new Item { Id = "it_" + slot + "_" + setId, BaseId = BaseForSlot(slot), Rarity = rarity, ItemLevel = 20, SetId = setId };

        private static int SlotsSeen(SaveState save, string setId) =>
            Codex.SetEntries(save, Cfg).First(e => e.Id == setId).SlotsSeen;

        // ============================ new-game / defaults ============================

        [Fact]
        public void NewGameStartsWithAnEmptyCodex()
        {
            var save = Fresh();
            Assert.Empty(save.Progress.Codex.Kills);
            Assert.Empty(save.Progress.Codex.SetSeen);
            Assert.Equal(0, Codex.CompletedTiers(save, Cfg));
            Assert.Equal(0.0, Codex.AccountBuffPct(save, Cfg), 10);
        }

        [Fact]
        public void SetCompleteMaskSpansEveryActiveEquipSlot()
        {
            int mask = Codex.SetCompleteMask(Cfg);
            foreach (var slot in EquipSlots.Active) Assert.True((mask & (1 << (int)slot)) != 0, $"{slot} missing from mask");
            // Exactly the active slots — no legacy slot bit leaks in.
            int expected = 0;
            foreach (var slot in EquipSlots.Active) expected |= 1 << (int)slot;
            Assert.Equal(expected, mask);
        }

        // ============================ BankKills ============================

        [Fact]
        public void BankKillsAddsAndDetectsTheFirstTierCrossing()
        {
            var save = Fresh();
            string m = AnyMonster();
            long tier0 = Cfg.Balance.CodexKillTiers[0];

            var (a, crossed) = Codex.BankKills(save, new Dictionary<string, int> { [m] = (int)tier0 }, Cfg);

            Assert.Equal(tier0, a.Progress.Codex.Kills[m]);
            Assert.Single(crossed);
            Assert.Equal(m, crossed[0].MonsterId);
            Assert.Equal(0, crossed[0].TierIndex);
            Assert.Empty(save.Progress.Codex.Kills); // input untouched (pure)
        }

        [Fact]
        public void BankKillsReportsEachTierExactlyOnceAcrossBanks()
        {
            var save = Fresh();
            string m = AnyMonster();
            long tier0 = Cfg.Balance.CodexKillTiers[0];
            long tier1 = Cfg.Balance.CodexKillTiers[1];

            var (a, c0) = Codex.BankKills(save, new Dictionary<string, int> { [m] = (int)tier0 }, Cfg);
            Assert.Single(c0); // tier 0 only

            // Bank more, staying below tier 1: no new crossing.
            var (b, c1) = Codex.BankKills(a, new Dictionary<string, int> { [m] = 5 }, Cfg);
            Assert.Empty(c1);

            // Now cross tier 1: reported once, and NOT tier 0 again.
            var (d, c2) = Codex.BankKills(b, new Dictionary<string, int> { [m] = (int)tier1 }, Cfg);
            Assert.Single(c2);
            Assert.Equal(1, c2[0].TierIndex);
        }

        [Fact]
        public void BankKillsSpanningTwoThresholdsReportsBoth()
        {
            var save = Fresh();
            string m = AnyMonster();
            long tier1 = Cfg.Balance.CodexKillTiers[1]; // jump from 0 straight past tier 0 AND tier 1

            var (_, crossed) = Codex.BankKills(save, new Dictionary<string, int> { [m] = (int)tier1 }, Cfg);

            Assert.Equal(2, crossed.Count);
            Assert.Equal(new[] { 0, 1 }, crossed.Select(x => x.TierIndex));
        }

        [Fact]
        public void BankKillsEmptyOrNonPositivePendingSharesTheRef()
        {
            var save = Fresh();
            var (a, ca) = Codex.BankKills(save, new Dictionary<string, int>(), Cfg);
            Assert.Same(save, a);
            Assert.Empty(ca);

            var (b, cb) = Codex.BankKills(save, new Dictionary<string, int> { ["x"] = 0 }, Cfg);
            Assert.Same(save, b); // nothing positive banked
            Assert.Empty(cb);
        }

        // ============================ AddLoot set stamping ============================

        [Fact]
        public void AddLootStampsTheSetSlotBit()
        {
            var save = Fresh();
            string set = AnySetId();
            var r = Inventory.AddLoot(save, new[] { SetPiece(EquipSlot.Weapon, set) }, Cfg, allowOverflow: true);

            int bit = 1 << (int)EquipSlot.Weapon;
            Assert.Equal(bit, r.Save.Progress.Codex.SetSeen[set]);
            Assert.Equal(1, SlotsSeen(r.Save, set));
            Assert.Empty(save.Progress.Codex.SetSeen); // input untouched (pure)
        }

        [Fact]
        public void AddLootStampsEvenWhenTheFilterAutoSalvagesThePiece()
        {
            // Arm the filter to auto-salvage Rare weapons, then drop a Rare set weapon: it's scrapped,
            // but "seen is seen" — the slot bit is still stamped.
            var save = Inventory.SetSalvageFloor(Fresh(), EquipSlot.Weapon, Rarity.Rare);
            string set = AnySetId();
            var r = Inventory.AddLoot(save, new[] { SetPiece(EquipSlot.Weapon, set) }, Cfg, allowOverflow: true);

            Assert.Single(r.Salvaged);   // the filter ate it
            Assert.Empty(r.Stored);
            Assert.Equal(1 << (int)EquipSlot.Weapon, r.Save.Progress.Codex.SetSeen[set]); // ...but it was seen
        }

        [Fact]
        public void AddLootDoesNotStampNonSetItems()
        {
            var save = Fresh();
            var plain = new Item { Id = "p1", BaseId = BaseForSlot(EquipSlot.Weapon), Rarity = Rarity.Rare, ItemLevel = 20 };
            var r = Inventory.AddLoot(save, new[] { plain }, Cfg, allowOverflow: true);

            Assert.Empty(r.Save.Progress.Codex.SetSeen);
            Assert.Same(save.Progress.Codex, r.Save.Progress.Codex); // no new bit => Codex ref shared
        }

        [Fact]
        public void AddLootSetsCompletedFiresExactlyWhenTheLastSlotLands()
        {
            var save = Fresh();
            string set = AnySetId();

            // Drop the first four active slots: never complete along the way.
            var slots = EquipSlots.Active;
            for (int i = 0; i < slots.Length - 1; i++)
            {
                var r = Inventory.AddLoot(save, new[] { SetPiece(slots[i], set) }, Cfg, allowOverflow: true);
                Assert.Empty(r.SetsCompleted);
                save = r.Save;
            }

            // The final slot completes the set — reported exactly once.
            var last = Inventory.AddLoot(save, new[] { SetPiece(slots[slots.Length - 1], set) }, Cfg, allowOverflow: true);
            Assert.Single(last.SetsCompleted);
            Assert.Equal(set, last.SetsCompleted[0]);
            Assert.True(Codex.SetEntries(last.Save, Cfg).First(e => e.Id == set).Complete);

            // Re-dropping a slot already seen does NOT re-fire completion.
            var again = Inventory.AddLoot(last.Save, new[] { SetPiece(slots[0], set) }, Cfg, allowOverflow: true);
            Assert.Empty(again.SetsCompleted);
        }

        [Fact]
        public void AddItemsDeliberatelyDoesNotStampSets()
        {
            // The raw test/admin primitive bypasses the loot-commit path — no set discovery.
            var save = Inventory.AddItems(Fresh(), new[] { SetPiece(EquipSlot.Weapon, AnySetId()) });
            Assert.Empty(save.Progress.Codex.SetSeen);
        }

        // ============================ zone derivation ============================

        [Fact]
        public void ZoneEntriesDeriveEnteredAndClearedFromHighestStage()
        {
            int per = Cfg.Balance.StagesPerTier;

            var none = Fresh(); // HighestStage 0
            var z0 = Codex.ZoneEntries(none, Cfg);
            Assert.False(z0[0].Entered);
            Assert.False(z0[0].Cleared);

            var entered = Fresh();
            entered.Progress.HighestStage = 1; // first stage of zone 0
            var z1 = Codex.ZoneEntries(entered, Cfg);
            Assert.True(z1[0].Entered);
            Assert.False(z1[0].Cleared);

            var cleared0 = Fresh();
            cleared0.Progress.HighestStage = per; // last stage of zone 0
            var z2 = Codex.ZoneEntries(cleared0, Cfg);
            Assert.True(z2[0].Cleared);
            Assert.False(z2[1].Entered); // zone 1 not yet entered

            var allDone = Fresh();
            allDone.Progress.HighestStage = Cfg.Stages.Count; // clears every zone
            Assert.All(Codex.ZoneEntries(allDone, Cfg), z => Assert.True(z.Cleared));
        }

        // ============================ monster read model ============================

        [Fact]
        public void MonsterEntriesReportTiersAndNextThreshold()
        {
            var save = Fresh();
            string m = AnyMonster();
            save.Progress.Codex.Kills[m] = Cfg.Balance.CodexKillTiers[0]; // exactly tier 0

            var row = Codex.MonsterEntries(save, Cfg).First(e => e.Id == m);
            Assert.Equal(Cfg.Balance.CodexKillTiers[0], row.Kills);
            Assert.Equal(1, row.TiersCompleted);
            Assert.Equal(Cfg.Balance.CodexKillTiers[1], row.NextThreshold); // next unmet threshold
            Assert.False(row.Maxed);

            // One row per cfg monster (shelved defs absent from cfg are simply not present).
            Assert.Equal(Cfg.Monsters.Count, Codex.MonsterEntries(save, Cfg).Count);
        }

        [Fact]
        public void MonsterEntriesMarkMaxedAtTheTopTier()
        {
            var save = Fresh();
            string m = AnyMonster();
            save.Progress.Codex.Kills[m] = Cfg.Balance.CodexKillTiers.Last();
            var row = Codex.MonsterEntries(save, Cfg).First(e => e.Id == m);
            Assert.True(row.Maxed);
            Assert.Equal(Cfg.Balance.CodexKillTiers.Length, row.TiersCompleted);
            Assert.Equal(0, row.NextThreshold);
        }

        // ============================ buff math ============================

        [Fact]
        public void CompletedTiersCountsAllThreeFamilies()
        {
            var save = Fresh();
            // 1 monster at gold (3 tiers) + 1 complete set (1) + zones 0..1 cleared (2) = 6.
            save.Progress.Codex.Kills[AnyMonster()] = Cfg.Balance.CodexKillTiers.Last();
            save.Progress.Codex.SetSeen[AnySetId()] = Codex.SetCompleteMask(Cfg);
            save.Progress.HighestStage = 2 * Cfg.Balance.StagesPerTier; // zones 0 and 1 cleared

            int expected = Cfg.Balance.CodexKillTiers.Length + 1 + 2;
            Assert.Equal(expected, Codex.CompletedTiers(save, Cfg));
            Assert.Equal(expected * Cfg.Balance.CodexTierStatPct, Codex.AccountBuffPct(save, Cfg), 10);
        }

        [Fact]
        public void ApplyAccountBuffsScalesCoreStatsAndNoOpsAtZero()
        {
            var save = Fresh();
            save.Progress.HighestStage = Cfg.Stages.Count; // 10 zone tiers => a nonzero buff
            double pct = Codex.AccountBuffPct(save, Cfg);
            Assert.True(pct > 0);

            var s = new StatBlock { [StatKey.Hp] = 100, [StatKey.Atk] = 10, [StatKey.Def] = 5, [StatKey.MoveSpd] = 3 };
            var buffed = Codex.ApplyAccountBuffs(s, save, Cfg);
            Assert.Equal(100 * (1 + pct), buffed.Get(StatKey.Hp), 6);
            Assert.Equal(10 * (1 + pct), buffed.Get(StatKey.Atk), 6);
            Assert.Equal(5 * (1 + pct), buffed.Get(StatKey.Def), 6);
            Assert.Equal(3, buffed.Get(StatKey.MoveSpd), 6); // non-core untouched

            Assert.Same(s, Codex.ApplyAccountBuffs(s, Fresh(), Cfg)); // no completion => same ref
        }

        [Fact]
        public void CodexBuffReachesPartyCombatStats()
        {
            // Mirrors TowerCombatTests.MilestoneBuffRaisesPartyCombatStats: the derived drip actually
            // lands on the party's combat stats via the Combat stat-build fold.
            var baseSave = Leveled();
            var buffed = Leveled();
            buffed.Progress.HighestStage = Cfg.Stages.Count; // clears all zones => nonzero codex buff

            double hpBase = HeroMaxHp(baseSave);
            double hpBuffed = HeroMaxHp(buffed);

            double pct = Codex.AccountBuffPct(buffed, Cfg);
            Assert.True(pct > 0);
            Assert.Equal(hpBase * (1 + pct), hpBuffed, 3);
        }

        // ============================ balance gate ============================

        [Fact]
        public void MaxAttainableBuffStaysUnderTheCeiling()
        {
            // Every monster maxed, every set complete, every zone cleared — the codex ceiling. Keeps
            // future content additions honest: if this trips, raise CodexTierStatPct CONSCIOUSLY, not
            // by accidentally adding monsters/sets/zones.
            var full = FullCodex(Fresh());

            int expectedTiers = Cfg.Monsters.Count * Cfg.Balance.CodexKillTiers.Length
                                + Cfg.Sets.Count + Cfg.Zones.Count;
            Assert.Equal(expectedTiers, Codex.CompletedTiers(full, Cfg));

            double max = Codex.AccountBuffPct(full, Cfg);
            Assert.True(max <= 0.25, $"codex ceiling {max:F3} exceeds 0.25 — raise CodexTierStatPct consciously");
        }

        // ============================ migration ============================

        [Fact]
        public void MigrateBackfillsCodexStateOnOlderSaves()
        {
            var save = Fresh();
            save.Progress.Codex = null!; // simulate a pre-10.15 payload
            var migrated = Save.Migrate(save);
            Assert.NotNull(migrated.Progress.Codex);
            Assert.NotNull(migrated.Progress.Codex.Kills);
            Assert.NotNull(migrated.Progress.Codex.SetSeen);
        }

        [Fact]
        public void SyncFromInventoryStampsPreCodexSetPiecesRetroactively()
        {
            // Retroactive discovery: a pre-codex save whose bag already holds set pieces must not
            // read "unseen" forever. SyncFromInventory (the cfg-aware retro-grant, run at load beside
            // SyncHeroUnlocks) stamps their slot bits; a second run shares the ref (idempotent).
            var save = Fresh();
            string set = AnySetId();
            save.Inventory.Add(SetPiece(EquipSlot.Helm, set));
            save.Inventory.Add(SetPiece(EquipSlot.Weapon, set));
            Assert.Empty(save.Progress.Codex.SetSeen); // pre-codex: nothing stamped yet

            var synced = Codex.SyncFromInventory(save, Cfg);
            Assert.Equal((1 << (int)EquipSlot.Helm) | (1 << (int)EquipSlot.Weapon),
                         synced.Progress.Codex.SetSeen[set]);
            Assert.Empty(save.Progress.Codex.SetSeen); // input untouched (pure)

            Assert.Same(synced, Codex.SyncFromInventory(synced, Cfg)); // nothing new — ref-share
        }

        [Fact]
        public void SerializedSaveWithoutCodexLoadsEmpty()
        {
            // A save whose JSON predates the Codex field (Progress present, no Codex key) must load
            // with an empty CodexState — the AchievementState-absence precedent.
            string json = $"{{\"Version\":{Save.SaveVersion},\"Party\":[\"h1\"],\"Progress\":{{\"HighestStage\":5}}}}";
            var save = Persistence.Deserialize(json); // Deserialize runs Migrate
            Assert.NotNull(save.Progress.Codex);
            Assert.Empty(save.Progress.Codex.Kills);
            Assert.Empty(save.Progress.Codex.SetSeen);
        }

        [Fact]
        public void CodexRoundTripsThroughSerialization()
        {
            var save = Fresh();
            save.Progress.Codex.Kills[AnyMonster()] = 137;
            save.Progress.Codex.SetSeen[AnySetId()] = Codex.SetCompleteMask(Cfg);
            var json = Persistence.Serialize(save);
            var again = Persistence.Serialize(Persistence.Deserialize(json));
            Assert.Equal(json, again);
        }

        // ============================ HandleDeath counting ============================

        [Fact]
        public void CombatKillsAccrueIntoPendingKills()
        {
            // Drive a real farm fight and count enemy monster deaths — every one must land in
            // PendingKills, keyed by a valid monster id (the counting hook next to XP/gold banking).
            var save = Leveled();
            var heroes = PartyHeroes(save);
            var s = Combat.InitFarm(heroes, 1, Cfg, new Rng(7));

            // Party entity ids are stable; any Death event NOT for a party entity is a monster death.
            var partyIds = new HashSet<string>();
            foreach (var e in s.Entities) if (e.Team == Team.Party) partyIds.Add(e.Id);

            int monsterDeaths = 0;
            for (int i = 0; i < 800; i++)
                foreach (var ev in Combat.StepCombat(s, Combat.DefaultStepMs, Cfg, new Rng(7)))
                    if (ev.Type == CombatEventType.Death && ev.EntityId != null && !partyIds.Contains(ev.EntityId))
                        monsterDeaths++;

            Assert.True(monsterDeaths > 0, "no monsters died in 800 steps — strengthen the fixture");

            long banked = 0;
            foreach (var v in s.PendingKills.Values) banked += v;
            Assert.Equal(monsterDeaths, banked);
            foreach (var id in s.PendingKills.Keys) Assert.True(Cfg.Monsters.ContainsKey(id), $"unknown monster id {id}");
        }

        // ============================ threading sweep ============================

        [Fact]
        public void CodexSurvivesEveryReducerThatCopiesProgress()
        {
            // Mirrors EndlessTests' EndlessBest sweep: a dropped Codex copy site would substitute a
            // fresh (empty) CodexState, so a distinctive marker kill must survive every ProgressState
            // rebuilder. VALUE survival, not ref identity — Tower.RecordClear rolls boss loot through
            // AddLoot, which may legitimately re-wrap the Codex to stamp a set discovery, always
            // carrying Kills forward.
            var s = Fresh();
            s.Progress.Codex.Kills["marker"] = 5;
            long Marker(SaveState x) => x.Progress.Codex.Kills.TryGetValue("marker", out var v) ? v : 0;

            Assert.Equal(5, Marker(Progression.SetStage(s, 1, Cfg)));
            Assert.Equal(5, Marker(Progression.OnStageCleared(s, 3, Cfg)));
            Assert.Equal(5, Marker(Tower.RecordClear(s, 1, Cfg)));
            Assert.Equal(5, Marker(Crypt.RecordFloorClear(s, 1, Cfg)));
            Assert.Equal(5, Marker(Inventory.SetImprintGuard(s, true)));
            Assert.Equal(5, Marker(Inventory.SetSalvageFloor(s, EquipSlot.Weapon, Rarity.Rare)));
            Assert.Equal(5, Marker(Achievements.Record(s, AchievementMetric.MonstersKilled, 5, Cfg).save));
            Assert.Equal(5, Marker(DailyLogin.Claim(s, Cfg, Now).save));

            // AddLoot commits loot: a non-set item shares the Codex ref, a set piece re-wraps it —
            // either way the marker kill carries through.
            var plain = new Item { Id = "z1", BaseId = BaseForSlot(EquipSlot.Helm), Rarity = Rarity.Normal, ItemLevel = 1 };
            var afterLoot = Inventory.AddLoot(s, new[] { plain }, Cfg, allowOverflow: true).Save;
            Assert.Equal(5, Marker(afterLoot));
            Assert.Same(s.Progress.Codex, afterLoot.Progress.Codex); // no set piece => shares the ref (no clone)

            // Intro pays its first beat once lifetime kills clear SlayTarget — drive it through WithIntro.
            var slain = Achievements.Record(s, AchievementMetric.MonstersKilled, 100, Cfg).save;
            Assert.Equal(5, Marker(IntroQuests.Sync(slain, Cfg).save));
        }

        // ------------------------------------ helpers ------------------------------------

        private static SaveState Leveled() => Progression.GrantPartyXp(Fresh(), 200_000, Cfg);

        private static List<HeroInstance> PartyHeroes(SaveState save)
        {
            var list = new List<HeroInstance>();
            foreach (var id in save.Party)
                if (id != null) { var h = save.Heroes.Find(x => x.Id == id); if (h != null) list.Add(h); }
            return list;
        }

        private static double HeroMaxHp(SaveState save)
        {
            var s = Combat.InitFarm(PartyHeroes(save), 1, Cfg, new Rng(1));
            Combat.RefreshPartyStats(s, save, Cfg); // applies gear + account buffs, as the client does
            return s.Entities.First(e => e.RefKind == "hero").MaxHp;
        }

        private static SaveState FullCodex(SaveState save)
        {
            long top = Cfg.Balance.CodexKillTiers.Last();
            foreach (var kv in Cfg.Monsters) save.Progress.Codex.Kills[kv.Key] = top;
            int mask = Codex.SetCompleteMask(Cfg);
            foreach (var kv in Cfg.Sets) save.Progress.Codex.SetSeen[kv.Key] = mask;
            save.Progress.HighestStage = Cfg.Stages.Count; // clears every zone
            return save;
        }
    }
}
