#nullable enable
using System;
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// The guided FTUE intro (§7.4): five one-shot quests that sit AHEAD of the rolling
    /// <see cref="Quests"/> board on a fresh (armed) game and walk a new player through the core loop —
    /// slay a pack → collect a drop → equip it → beat the stage-1 boss → reach the first reveal.
    ///
    /// WHY a separate sim-side track (not the rolling board): the board's <see cref="QuestKind"/> quests
    /// are rolling (complete → reroll), deduped by kind, and <see cref="Quests.EnsureBoard"/> actively
    /// DELETES any quest whose kind isn't in its rotation. Intro quests are one-shot, ordered, and
    /// complete from a PURE PREDICATE over the save rather than a fed counter — so they can't live on the
    /// board without contorting it. Instead each beat retro-completes the moment its deed already
    /// happened (the <see cref="Progression.SyncHeroUnlocks"/> pattern): <see cref="Sync"/> pays every
    /// earned-but-unpaid beat. That gives retro-completion for free and needs ZERO new client counter
    /// hooks — the client just calls <see cref="Sync"/> on load / after loot / on stage clear.
    ///
    /// Pure (returns a new SaveState, input untouched), mirroring Quests/Achievements.
    /// </summary>
    public static class IntroQuests
    {
        /// <summary>Kills that count as "a pack" for beat 1 — evidenced by the lifetime MonstersKilled
        /// achievement counter (the only persisted kill tally, already fed by normal play).</summary>
        public const int SlayTarget = 10;

        /// <summary>The five beats, in reveal order. Static content (like <see cref="Quests"/>'s cycle);
        /// per-save state is only the claimed flags in <see cref="IntroState.IntroClaimed"/>.</summary>
        public static readonly IReadOnlyList<IntroQuest> All = new List<IntroQuest>
        {
            new IntroQuest { Id = "intro_slay",  Title = "Slay your first pack",    Desc = "Defeat a handful of monsters.",       RewardGold = 100, RewardScrap = 5,  RewardXp = 50 },
            new IntroQuest { Id = "intro_loot",  Title = "Collect your first drop", Desc = "Pick up any piece of loot.",          RewardGold = 150, RewardScrap = 5,  RewardXp = 50 },
            new IntroQuest { Id = "intro_equip", Title = "Equip your first find",   Desc = "Gear up a hero from your bag.",        RewardGold = 150, RewardScrap = 10, RewardXp = 75 },
            new IntroQuest { Id = "intro_boss",  Title = "Defeat the Stage 1 boss", Desc = "Win the boss challenge to advance.",   RewardGold = 250, RewardScrap = 10, RewardXp = 100 },
            new IntroQuest { Id = "intro_reach", Title = "Reach Stage 2",           Desc = "Push onward to the next stage.",       RewardGold = 300, RewardScrap = 15, RewardXp = 150 },
        };

        /// <summary>Has this beat's deed happened yet? A PURE predicate over the save — no fed counters,
        /// so it retro-completes and can never wedge. Evidence per beat:
        /// slay = lifetime kills ≥ <see cref="SlayTarget"/>; loot = any drop's fate (bagged, equipped,
        /// salvaged, or a rare+ find); equip = any hero wearing anything; boss = stage-1 boss cleared
        /// (HighestStage ≥ 1); reach = at stage 2 (HighestStage ≥ 2, the first staged reveal).</summary>
        public static bool IsComplete(string id, SaveState save) => id switch
        {
            "intro_slay"  => Achievements.Counter(save, AchievementMetric.MonstersKilled) >= SlayTarget,
            "intro_loot"  => HasAnyLoot(save),
            "intro_equip" => AnyEquipped(save),
            "intro_boss"  => save.Progress.HighestStage >= 1,
            "intro_reach" => save.Progress.HighestStage >= 2,
            _ => false,
        };

        /// <summary>Has this beat's reward already been paid (so it never mints twice)?</summary>
        public static bool IsClaimed(string id, SaveState save) =>
            save.Progress.Intro.IntroClaimed.TryGetValue(id, out var c) && c;

        /// <summary>Should the client show the intro strip at all? Only on an armed (fresh) game that
        /// hasn't finished all five beats — once every beat is claimed the intro retires and the rolling
        /// board takes over fully. Unarmed (existing) saves never see it.</summary>
        public static bool Active(SaveState save) => save.Progress.Intro.Armed && !AllClaimed(save);

        /// <summary>Every beat paid out (or the save is unarmed and has no intro).</summary>
        public static bool AllClaimed(SaveState save)
        {
            foreach (var q in All)
                if (!IsClaimed(q.Id, save)) return false;
            return true;
        }

        /// <summary>The intro strip as the client renders it: each beat plus its complete/claimed state.
        /// Pure read; order = reveal order.</summary>
        public static List<IntroQuestStatus> Board(SaveState save)
        {
            var rows = new List<IntroQuestStatus>(All.Count);
            foreach (var q in All)
                rows.Add(new IntroQuestStatus { Quest = q, Complete = IsComplete(q.Id, save), Claimed = IsClaimed(q.Id, save) });
            return rows;
        }

        /// <summary>
        /// Retro-complete the intro: pay (gold + scrap + party XP) every beat whose deed has already
        /// happened but hasn't been claimed — strictly in beat order (an earned-but-out-of-order beat
        /// waits for its predecessors) — marking each claimed so it never mints twice. Called by the
        /// client on load / after loot / on stage clear (idempotent, cheap). No-op (shares the ref) on an
        /// unarmed save or when nothing new completed. Returns the updated save plus the beats paid this
        /// call (for the client feed / juice). Pure — mirrors <see cref="Achievements.Record"/>.
        /// </summary>
        public static (SaveState save, List<IntroQuest> completed) Sync(SaveState save, GameConfig cfg)
        {
            var completed = new List<IntroQuest>();
            if (!save.Progress.Intro.Armed) return (save, completed); // unarmed saves have no intro track

            var result = save;
            Dictionary<string, bool>? claimed = null; // cloned lazily on first payout
            foreach (var q in All)
            {
                if (IsClaimed(q.Id, result)) continue; // paid = satisfied forever, even if its predicate
                                                       // later regresses (unequip) — a claimed beat never blocks
                // Guided order: a beat pays only once every earlier beat has paid. An out-of-order deed
                // (a drop before the tenth kill) waits, then retro-pays in the same Sync that clears the
                // earlier beat — so the feed and strip always advance top-to-bottom and can still never wedge.
                if (!IsComplete(q.Id, result)) break;

                // Pay through the existing reward reducers (they carry Intro forward by ref).
                result = Progression.GrantGold(result, q.RewardGold);
                result = Progression.GrantScrap(result, q.RewardScrap);
                result = Progression.GrantPartyXp(result, q.RewardXp, cfg);

                claimed ??= new Dictionary<string, bool>(result.Progress.Intro.IntroClaimed);
                claimed[q.Id] = true;
                completed.Add(q);
            }

            if (claimed == null) return (save, completed); // nothing newly earned — share the ref
            return (WithIntro(result, new IntroState { Armed = result.Progress.Intro.Armed, IntroClaimed = claimed }), completed);
        }

        private static bool AnyEquipped(SaveState save)
        {
            foreach (var h in save.Heroes)
                if (h.Equipped != null && h.Equipped.Count > 0) return true;
            return false;
        }

        // A drop's fate is one of: sitting in the bag, worn by a hero, auto-salvaged, or logged as a
        // rare+ find. Any of those is proof the player collected loot. Inventory/Equipped are written by
        // the loot/equip reducers directly (never gated), so this holds even with no achievement hooks.
        private static bool HasAnyLoot(SaveState save) =>
            (save.Inventory != null && save.Inventory.Count > 0)
            || AnyEquipped(save)
            || Achievements.Counter(save, AchievementMetric.ItemsSalvaged) > 0
            || Achievements.Counter(save, AchievementMetric.RarePlusFound) > 0;

        // Clone the save with a new IntroState, nested under a cloned ProgressState (its siblings share
        // refs). Everything else shares refs — the pure-reducer convention (mirrors WithAchievements).
        private static SaveState WithIntro(SaveState save, IntroState intro) => new SaveState
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
                Intro = intro,
                Loot = save.Progress.Loot,
                Codex = save.Progress.Codex,
            },
            Quests = save.Quests,
            Modifiers = save.Modifiers,
            GachaPity = save.GachaPity,
            LastClaimAt = save.LastClaimAt,
        };
    }

    /// <summary>One guided-intro beat (§7.4): imperative title + flavor, and a one-time reward paid by
    /// <see cref="IntroQuests.Sync"/>. Completion is a predicate over the save, not a stored progress
    /// value — see <see cref="IntroQuests.IsComplete"/>.</summary>
    public struct IntroQuest
    {
        public string Id;
        public string Title;
        public string Desc;
        public long RewardGold;
        public long RewardScrap;
        public int RewardXp;
    }

    /// <summary>An intro beat plus its live state, for the client strip (<see cref="IntroQuests.Board"/>):
    /// <see cref="Complete"/> = deed done, <see cref="Claimed"/> = reward paid.</summary>
    public struct IntroQuestStatus
    {
        public IntroQuest Quest;
        public bool Complete;
        public bool Claimed;
    }
}
