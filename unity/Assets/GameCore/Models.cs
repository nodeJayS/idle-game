#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    // ------------------------------------------------------------------------
    // GameCore data model.
    //
    // THE RULE: this assembly is pure logic. No UnityEngine references anywhere
    // in GameCore. Unity references GameCore as the client; a .NET server can
    // reuse it for authority.
    //
    // Three kinds of state, never mixed:
    //   - SaveState   : persisted
    //   - GameConfig  : static content, injected (see GameConfig.cs)
    //   - combat state: transient
    // ------------------------------------------------------------------------

    // Ascending value — order matters: AffixDef.RarityFloor and the rarity-roll
    // bias both rely on (int)Rarity as the rank (Normal = 0 … Mythic = 4).
    // 2026-07 refactor dropped Magic and added Mythic on top; persisted (int)Rarity
    // shifted, so old saves reseed at New Game (deliberate — no migration).
    public enum Rarity { Normal, Rare, Unique, Legendary, Mythic }

    // Appended (not reordered) so persisted slot values stay stable. MapleStory-style
    // gear sheet: armor pieces + a weapon/offhand pair, plus two accessory slots.
    // Trimmed to 5 ACTIVE slots (2026-07-02): fewer, chunkier gear decisions. The
    // retired members (Ring/Amulet/Offhand/Cape) MUST remain declared — saves persist
    // Equipped dictionary keys by enum NAME, so deleting them makes old saves fail to
    // deserialize entirely (load falls back to New Game!). They are load-compat only:
    // no item base targets them and Inventory.PruneUnknownGear dissolves their gear
    // into scrap at load. Use EquipSlots.Active everywhere instead of Enum.GetValues.
    public enum EquipSlot
    {
        Weapon = 0, Helm = 1, Chest = 2, Boots = 3, Gloves = 7,
        Ring = 4, Amulet = 5, Offhand = 6, Cape = 8, // LEGACY — deserialization only
    }

    public static class EquipSlots
    {
        /// <summary>The real slot set. Iterate THIS, never Enum.GetValues (which
        /// still contains the retired load-compat members).</summary>
        public static readonly EquipSlot[] Active =
            { EquipSlot.Weapon, EquipSlot.Helm, EquipSlot.Chest, EquipSlot.Boots, EquipSlot.Gloves };
    }

    // Appended (not reordered) so persisted StatKey values stay stable across saves.
    // MoveSpd = movement (tiles/sec); AtkSpd = action rate (attacks, spells, heals — and
    // client animation speed).
    // Values 9 and 10 are RETIRED (formerly MaxMana / ManaRegen — mana was removed; skills
    // are cooldown-only now). They stay reserved as an explicit gap so every later member keeps
    // its persisted integer value; Save.Migrate strips any old affix that still carries 9/10.
    // Lifesteal/ThornsReflect appended last (stable persisted values). They are EXCLUSIVE imprint
    // stats — in no item base's AllowedAffixes, so they only ever reach gear via Loot.ImprintDrop
    // (the rare suffix mods of Leeching / of Thorns). Read in Combat.ApplyHit alongside the same-named
    // monster-behavior fields, so a hero with the stat leeches/reflects exactly like a modded monster.
    // HpRegenPct = fraction of MaxHp healed per second — only ever granted by heal-over-time
    // BUFFS (priest Sanctify), never rolled on gear; flat HpRegen stays the hp/sec stat.
    public enum StatKey { Hp = 0, Atk = 1, Def = 2, MoveSpd = 3, CritChance = 4, CritDmg = 5, HpRegen = 6, AttackRange = 7, SplashRadius = 8, AtkSpd = 11, Lifesteal = 12, ThornsReflect = 13, ChainCount = 14, HpRegenPct = 15 }

    /// <summary>A bag of stat values. Partial blocks simply omit keys.</summary>
    public sealed class StatBlock : Dictionary<StatKey, double>
    {
        public StatBlock() { }
        public StatBlock(IDictionary<StatKey, double> src) : base(src) { }

        public double Get(StatKey k) => TryGetValue(k, out var v) ? v : 0.0;
    }

    public sealed class HeroInstance
    {
        public string Id = "";
        public string DefId = "";
        public int Level = 1;
        public long Xp = 0; // remainder toward the next level; long so a months-long curve can't overflow
        public Dictionary<EquipSlot, string> Equipped = new Dictionary<EquipSlot, string>();
        // 2+2 hero template (design §7.2): the kit is fixed (2 actives + 2 passives from
        // HeroDef.Skills, revealed by SkillDef.UnlockLevel) — there is no loadout choice.
        // skillId -> points invested (rank). Absent = rank 0 (= base). Points are EARNED from
        // hero level (1 per SkillPointsEveryLevels) and SPENT here; unspent is derived, never
        // persisted (see Skills.UnspentPoints). Threaded through every HeroInstance copy site,
        // or an equip/level edit silently resets it.
        public Dictionary<string, int> SkillRanks = new Dictionary<string, int>();
    }

    public sealed class Affix
    {
        public StatKey Stat;
        public double Value;
    }

    public sealed class Item
    {
        public string Id = "";
        public string BaseId = "";
        public Rarity Rarity;
        public int ItemLevel;
        public List<Affix> Affixes = new List<Affix>();
        // Enhancement level (+N): multiplies the item BASE's stats only (+5%/level via
        // Balance.EnhanceBasePctPerLevel) — affix rolls stay as rolled (rolls are
        // Reforge's domain, the base is Enhance's). Absent in old saves => 0.
        public int Enhance;
        // Player lock: a locked item can NEVER be salvaged — single-item, mass "Salvage all",
        // and the auto-salvage threshold path all skip it. Toggle via Inventory.ToggleLock;
        // works on bag AND equipped gear (an equipped item stays locked when unequipped).
        // Absent in old saves => false (System.Text.Json defaults it), so no SaveVersion bump.
        public bool Locked;
        // Gear-set membership (§6.2): a SetDef id, rolled once at drop time on the drop's own
        // rng (never re-rolled — Reforge/Enhance copy it through). Absent in old saves => null
        // = no set, so no SaveVersion bump (the Item.Locked precedent).
        public string? SetId;
    }

    public sealed class ProgressState
    {
        public int HighestStage = 0;
        public int CurrentStage = 1;
        public int AccountLevel = 1;
        // Tower of Ascension (alt mode) progress. Nested here so it rides the existing
        // `Progress` reference-threading — only the handful of `new ProgressState{}` reducers
        // (7 as of 10.5a; grep before adding a field) must carry each sub-state, and the
        // ~20 SaveState copy sites get it free.
        public TowerState Tower = new TowerState();
        // Lifetime achievement ladder (Lever 4). Nested here for the same reason as Tower — the
        // ProgressState constructors carry it forward, so the ~20 other save-copy sites get it free.
        public AchievementState Achievements = new AchievementState();
        // Daily-login streak (Lever 4 — premium currency). Nested here for the same threading reason.
        public DailyLoginState Daily = new DailyLoginState();
        // Crypt (roguelite dungeon) meta. Nested here for the same threading reason.
        public CryptState Crypt = new CryptState();
        // FTUE staged-reveal arming + guided-intro track (§7.4). Nested here for the same
        // threading reason as Tower/Achievements/Daily/Crypt. Absent on older saves =>
        // deserializes to a fresh IntroState with Armed=false, so Progression.FeatureUnlocked
        // treats every existing save as fully unlocked, forever (fresh games only gate).
        public IntroState Intro = new IntroState();
        // Loot filter (10.5a): per-slot auto-salvage floors + the imprint guard. Nested here for
        // the same threading reason as its siblings.
        public LootFilterState Loot = new LootFilterState();
    }

    /// <summary>10.5a loot filter: per-slot auto-salvage floors + the imprint guard.
    /// Additive (no version bump) — backfilled by <see cref="Save.Migrate"/> like Crypt/Daily.
    /// Read through <see cref="Inventory.WouldAutoSalvage"/> — the ONE auto-salvage predicate.</summary>
    public sealed class LootFilterState
    {
        // slot -> highest rarity that still auto-salvages ("X & below"); absent slot = off.
        public Dictionary<EquipSlot, Rarity> SalvageMaxBySlot = new Dictionary<EquipSlot, Rarity>();
        // Imprinted gear (any affix outside cfg.AffixPool — the Tower-gated prize) refuses ALL
        // salvage paths while on, exactly like Item.Locked. Default ON, incl. for old saves
        // (field initializer survives deserialization of absent properties).
        public bool NeverSalvageImprinted = true;
    }

    /// <summary>Which HUD feature/panel the staged reveal gates (§7.4). A player meets only the
    /// intro's surface in minute one; each of these is HIDDEN until the schedule reveals it, keyed
    /// off the save's highest CLEARED stage via <see cref="Progression.FeatureUnlocked"/>. Names are
    /// client-obvious: AutoAdvance = the auto-push-to-next-stage toggle; IdleClaim = the offline-accrual
    /// claim + its launch popup; DailyLogin = the login-streak claim + its popup; Achievements = the
    /// lifetime ladder panel; Modifiers = the risk/reward modifier shop+loadout; Modes = the Tower+Crypt
    /// menu; Gacha = the banner/summon screen.</summary>
    public enum Feature { AutoAdvance, IdleClaim, DailyLogin, Achievements, Modifiers, Modes, Gacha }

    /// <summary>
    /// FTUE state (§7.4): the staged-reveal arm plus the guided-intro quest track. <see cref="Armed"/>
    /// is set ONLY by <see cref="Save.NewGame"/> — existing saves deserialize it false and see every
    /// feature/panel unlocked forever (the fresh-games-only rule; additive field, NO SaveVersion bump,
    /// same precedent as GachaPity / Modifiers.Tuning / CryptState.ActiveRun). Read via
    /// <see cref="Progression.FeatureUnlocked"/>. <see cref="IntroClaimed"/> records which of the five
    /// one-shot intro quests have paid out (id -> true), so a reward mints exactly once; completion
    /// itself is a pure predicate over the save (see <see cref="IntroQuests"/>), so the intro
    /// retro-completes and can never wedge. Nested under <see cref="ProgressState"/> so it rides the
    /// existing Progress reference-threading.
    /// </summary>
    public sealed class IntroState
    {
        public bool Armed; // set true at New Game only; false on every existing save (unarmed = all unlocked)
        public Dictionary<string, bool> IntroClaimed = new Dictionary<string, bool>(); // intro quest id -> reward paid
    }

    /// <summary>Tower of Ascension progress (a separate one-clear-per-floor track, distinct from the
    /// farmable stage ladder). <see cref="HighestFloor"/> is the deepest floor cleared (0 = none);
    /// the next attemptable floor is HighestFloor + 1. Permanent account-wide buffs are *derived*
    /// from this (every N floors → a milestone), so nothing else needs persisting.</summary>
    public sealed class TowerState
    {
        public int HighestFloor = 0;
    }

    /// <summary>What a goal tracks. Each maps to a game event the client feeds into
    /// <see cref="Quests"/>.Advance (kills, stage clears, salvages, gold earned, rare drops).</summary>
    public enum QuestKind { KillMonsters, ClearStages, SalvageItems, EarnGold, FindRarePlus }

    /// <summary>One active goal on the board: accrue <see cref="Progress"/> up to
    /// <see cref="Target"/>, then it pays out and a fresh goal rolls in.</summary>
    public sealed class Quest
    {
        public QuestKind Kind;
        public long Target;
        public long Progress;
        public long RewardGold;
        public int RewardXp;
    }

    /// <summary>The rolling goal board (always a few near-term carrots). <see cref="RollCount"/>
    /// is a monotonic cursor so replacements cycle goal kinds deterministically.</summary>
    public sealed class QuestBoard
    {
        public List<Quest> Active = new List<Quest>();
        public int RollCount;
    }

    /// <summary>A lifetime stat an achievement tracks (Lever 4). ADD-metrics accumulate forever
    /// (kills, gold earned, salvages, rare+ finds, bosses); MAX-metrics keep a peak (deepest stage /
    /// tower floor, highest hero level). The client feeds these from the SAME game events it already
    /// feeds the goal board. Appended, not reordered — persisted as dictionary keys.</summary>
    public enum AchievementMetric
    {
        MonstersKilled, BossesKilled, ItemsSalvaged, GoldEarned, RarePlusFound,
        HighestStage, HighestTowerFloor, HeroLevel,
    }

    /// <summary>
    /// Lifetime achievement progress (Lever 4 — the permanent milestone ladder, distinct from the
    /// rolling <see cref="QuestBoard"/> that resets on completion). <see cref="Counters"/> hold the
    /// lifetime value per metric; <see cref="Claimed"/> records how many tiers of each achievement
    /// have already paid out, so a reward mints exactly once. Nested under <see cref="ProgressState"/>
    /// (like <see cref="TowerState"/>) so it rides the existing Progress reference-threading — only
    /// the ProgressState constructors carry it, not every SaveState copy site.
    /// </summary>
    public sealed class AchievementState
    {
        public Dictionary<AchievementMetric, long> Counters = new Dictionary<AchievementMetric, long>(); // metric -> lifetime value
        public Dictionary<string, int> Claimed = new Dictionary<string, int>();                          // achId -> tiers paid out
    }

    /// <summary>
    /// Daily-login streak state (Lever 4 — the premium-currency hook). Tracks the day of the last claim
    /// (a UTC day-index = epoch-ms / 86_400_000), the consecutive-day <see cref="Streak"/>, and lifetime
    /// <see cref="TotalClaims"/>. A claim grants GEMS — the PREMIUM currency, held in
    /// <see cref="SaveState.Currencies"/> and (for now) earnable no other way — the seed of the future
    /// gacha/microtransaction economy. Nested under <see cref="ProgressState"/> (like
    /// <see cref="TowerState"/> / <see cref="AchievementState"/>) so it rides the Progress threading.
    /// </summary>
    public sealed class DailyLoginState
    {
        public long LastClaimDay; // UTC day-index of the last claim; 0 = never claimed
        public int Streak;        // consecutive-day streak (1 on the first claim or after a break)
        public int TotalClaims;   // lifetime daily claims
    }

    /// <summary>
    /// Crypt (roguelite dungeon) meta progress. Entry is gated by KEYS that recharge one per UTC day
    /// (banked up to <see cref="BalanceConstants.CryptKeyBank"/>; <see cref="CryptState.LastKeyDay"/> is
    /// the DailyLogin-style day-index stamp). A run descends CryptFloorsPerRun floors back-to-back
    /// starting at <see cref="DepthRecord"/>+1 — the record is the deepest floor ever CLEARED (the
    /// difficulty ladder, Tower-style: first clears pay gems, re-runs repeat the frontier). Completing
    /// a whole run grants the end-of-run chest (grave dust, a crypt-only currency in
    /// <see cref="SaveState.Currencies"/>); a wipe forfeits the chest but keeps everything dropped.
    /// Dust buys permanent account-wide BOONS (<see cref="Boons"/>: boonId → rank), the crypt's own
    /// progression lane. Nested under <see cref="ProgressState"/> like Tower/Daily.
    /// </summary>
    public sealed class CryptState
    {
        public int Keys;        // banked entry keys (cap = Balance.CryptKeyBank)
        public long LastKeyDay; // UTC day-index of the last key grant; 0 = never ticked (first tick grants)
        public int DepthRecord; // deepest floor cleared (0 = none); the next run starts at DepthRecord+1
        public Dictionary<string, int> Boons = new Dictionary<string, int>(); // boonId -> rank purchased
        // §7.3 mid-run persistence: the run in progress, or null when idle. Set at run start / on
        // each descend, cleared when the run completes, is abandoned, or wipes. Purely additive —
        // absent in older saves deserializes to null ("no run"), so NO SaveVersion bump (the same
        // precedent as GachaPity / Modifiers.Tuning). The persisted SEED lets a resume reproduce the
        // current floor's exact layout without the session-local DungeonMode counter.
        public CryptRunState? ActiveRun;
    }

    /// <summary>A crypt RUN suspended across sessions (§7.3 persistence). Quitting mid-run keeps this
    /// (and the already-spent key), so a resume drops back into the SAME floor — regenerated from
    /// <see cref="Seed"/> — rather than forfeiting the key. Floors already descended stay banked in
    /// <see cref="CryptState.DepthRecord"/>; this only tracks the floor currently in progress.</summary>
    public sealed class CryptRunState
    {
        public int Floor;       // the floor being attempted right now
        public int FloorsLeft;  // descends remaining AFTER this floor (0 ⇒ this is the final floor)
        public int Seed;        // exact DungeonGen seed for this floor's layout (resume reproduces it)
        public bool FinalFloor; // this floor grows the end-of-run reward vault
    }

    /// <summary>Which reward channel a monster modifier boosts (thematic per type — see
    /// <see cref="Modifiers"/> / <see cref="GameConfig"/>). Gold/XP scale the per-kill payout;
    /// DropRate biases loot rarity upward.</summary>
    public enum ModifierReward { Gold, Xp, DropRate }

    /// <summary>One channel of a modifier's reward split. A mod can pay several at once (a "hybrid"
    /// reward, e.g. half gold + half drop rate); a plain mod is just a single part.</summary>
    public struct RewardPart
    {
        public ModifierReward Channel;
        public double PerStrength;
    }

    /// <summary>An optional per-hit combat behavior a modifier grants the monster, beyond stat
    /// multipliers. Vampiric = heals a fraction of damage it deals; Thorns = reflects a fraction
    /// of damage taken back to the attacker; Splash = its attacks hit the whole party in a radius
    /// (a real combat mechanic — the basis of the first loot-imprint modifier). None = stat-only.</summary>
    public enum ModifierBehavior { None, Vampiric, Thorns, Splash, Chain }

    /// <summary>Which imprint slot a rare (mechanical) modifier occupies — PoE-style. An item can hold
    /// at most ONE prefix imprint and ONE suffix imprint. Prefix imprints title the gear with a leading
    /// word ("Volatile Sword"); suffix imprints add a trailing phrase ("Sword of Leeching").</summary>
    public enum ImprintSlot { Prefix, Suffix }

    /// <summary>
    /// Banked monster modifiers (the risk/reward knob). <see cref="Owned"/> maps a modifier
    /// typeId to the best strength banked (= highest stage of that type's boss you've cleared);
    /// <see cref="Active"/> are the types currently toggled ON, which apply to farm trash —
    /// harder mobs for a thematic reward bonus. Stack freely. Persisted; threaded like
    /// <see cref="SaveState.Quests"/> through every reducer copy site.
    /// </summary>
    public sealed class MonsterModifiers
    {
        public Dictionary<string, int> Owned = new Dictionary<string, int>(); // typeId -> best strength
        public List<string> Active = new List<string>();                      // typeIds applied to farm trash
        // Modifier-shop tuning: typeId -> a multiplier (≥1.0, absent = 1.0) on BOTH the mod's monster
        // danger AND its reward, gambled up/down (floored at 1.0) by spending gold+scrap. Persisted.
        public Dictionary<string, double> Tuning = new Dictionary<string, double>();
    }

    public sealed class SaveState
    {
        public int Version;
        public uint RngSeed;
        public int RngCursor;
        public List<HeroInstance> Heroes = new List<HeroInstance>();
        public string?[] Party = new string?[Save.PartySize]; // null = empty slot
        // Chosen formation leader (a fielded hero id). null = auto: the lowest-slot living
        // hero leads. The combat sim reads this via CombatState.LeaderRefId.
        public string? LeaderHeroId;
        public List<Item> Inventory = new List<Item>();
        public Dictionary<string, long> Currencies = new Dictionary<string, long>();
        public ProgressState Progress = new ProgressState();
        public QuestBoard Quests = new QuestBoard();
        public MonsterModifiers Modifiers = new MonsterModifiers();
        // Gacha pity counters (roadmap 3): bannerId -> rolls made on that banner WITHOUT drawing its
        // featured hero. Hitting GachaBannerDef.PityCount forces the featured hero and resets to 0;
        // drawing the featured hero naturally resets it too. Additive field — absent/null on older
        // saves, backfilled by Save.Migrate (no SaveVersion bump), same as Item.Locked / Modifiers.Tuning.
        public Dictionary<string, int> GachaPity = new Dictionary<string, int>();
        public long LastClaimAt; // epoch ms (server-validated later)
    }
}
