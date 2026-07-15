#nullable enable
using System.Collections.Generic;

namespace IdleGame.GameCore
{
    /// <summary>
    /// The "30-second session" super-verbs (mobile arc MM2): the session design says a returning
    /// player gets ONE arrival payoff and ONE mid-session "do the obvious chores" verb — where the
    /// hand-built client today boots into two modals (idle + daily) and offers three separate manual
    /// sweeps (claim-all, salvage-all, auto-equip). This composes those into two atomic reducers so a
    /// single tap does the obvious thing. Pure like every reducer: same inputs ⇒ same outputs, and a
    /// total no-op returns the INPUT ref (the codebase's shared-ref contract). Nothing new is decided
    /// here — each verb routes through the EXISTING reducer it aggregates (<see cref="Idle"/>,
    /// <see cref="Goals"/>, <see cref="Upgrades"/>, <see cref="Inventory"/>), so the super-verb can
    /// never disagree with the buttons it replaces.
    /// </summary>
    public static class Session
    {
        // ============================ Arrival (the boot sweep) ============================

        /// <summary>What the one boot payoff yielded: the raw idle accrual (may be empty) plus every
        /// goal claim, with the convenience totals the client's arrival card prints. Amounts are read
        /// straight off the composed reducers' own reports (<see cref="IdleReport"/>, <see cref="GoalClaim"/>)
        /// — never recomputed — so the card can't diverge from the grant. Transient; never saved.</summary>
        public sealed class ArrivalReport
        {
            public IdleReport Idle = new IdleReport();        // gold/xp/items/scrap from the offline accrual
            public List<GoalClaim> GoalsClaimed = new List<GoalClaim>(); // daily login etc. (gems)

            // --- convenience totals the card prints (all sourced from the two reports above) ---
            public long Gold => Idle.Gold;
            public long Xp => Idle.Xp;
            public long ScrapGained => Idle.ScrapGained;       // scrap from auto-salvaged offline drops
            public List<Item> Items => Idle.Items;             // items the idle claim rolled into the bag
            public long Gems                                   // premium currency from the goal claims
            {
                get { long g = 0; foreach (var c in GoalsClaimed) g += c.Gems; return g; }
            }

            /// <summary>Nothing accrued and nothing was claimable — the card is suppressed and the
            /// returned save is the input ref.</summary>
            public bool IsEmpty => Idle.IsEmpty && GoalsClaimed.Count == 0;
        }

        /// <summary>
        /// ONE payoff moment at boot (the 30-second-session contract): apply the offline accrual THEN
        /// every pending goal claim, as one atomic arrival. Order is idle-first because idle advances
        /// <see cref="SaveState.LastClaimAt"/> while the goal claims are date-derived (they key off the
        /// UTC calendar day via <see cref="DailyLogin"/>, not LastClaimAt) and so are independent of it —
        /// either order grants the same thing; idle-first just matches the narrative "here's what you
        /// earned, and here's your login bonus." Determinism rides on <see cref="Idle.Claim"/>'s ephemeral
        /// rng (seeded from RngSeed^LastClaimAt), so same save+now ⇒ identical item rolls without touching
        /// the persisted cursor. If BOTH halves no-op the returned save is the INPUT ref: Idle.Claim shares
        /// the ref on clock-skew/no-elapsed, Goals.ClaimAll shares it when nothing is claimable, so a
        /// no-op ∘ no-op leaves the reference untouched. Pure.
        /// </summary>
        public static (SaveState next, ArrivalReport report) Arrive(SaveState save, GameConfig cfg, long nowMs)
        {
            var report = new ArrivalReport();

            var (afterIdle, idle) = Idle.Claim(save, cfg, nowMs);
            report.Idle = idle;

            var (afterGoals, claimed) = Goals.ClaimAll(afterIdle, cfg, nowMs);
            report.GoalsClaimed = claimed;

            // afterGoals === afterIdle === save whenever both halves no-op'd (each shares its ref), so a
            // total no-op hands the caller its own reference back.
            return (afterGoals, report);
        }

        // ======================= Manage (the mid-session chore sweep) =======================

        /// <summary>One planned/applied auto-equip: the item, the hero it lands on, and its blended
        /// power delta — the exact fields <see cref="Upgrades.ItemEval"/> exposes, so the preview line
        /// and the report line are the same numbers by construction.</summary>
        public struct SessionEquip
        {
            public Item Item;
            public string HeroId;
            public double DeltaPercent; // (after − before)/before; > 0 for every reported equip (upgrades only)
        }

        /// <summary>What the confirm card shows BEFORE <see cref="Apply"/> runs — a pure read model that
        /// PREDICTS exactly what Apply will do (a test locks Preview.counts == Apply.report counts). The
        /// gear plan is computed AS Apply computes it (equip sweep first, then salvage the remainder), so
        /// the shown salvage count already excludes anything the sweep will rescue by equipping it.</summary>
        public sealed class ManagePreview
        {
            public List<GoalClaim> Claims = new List<GoalClaim>();  // Goals.Claimables (gems)
            public List<SessionEquip> Equips = new List<SessionEquip>(); // items the sweep would auto-equip
            public int SalvageCount;                                 // loose items the sweep would scrap
            public long SalvageScrap;                                // scrap they would yield (ScrapValue sum)

            public int EquipCount => Equips.Count;
            public bool IsEmpty => Claims.Count == 0 && Equips.Count == 0 && SalvageCount == 0;
        }

        /// <summary>What <see cref="Apply"/> actually did — mirrors <see cref="ManagePreview"/> field for
        /// field so a client can diff intent vs. outcome. Empty on a no-op (nothing in any bucket).</summary>
        public sealed class ManageReport
        {
            public List<GoalClaim> Claimed = new List<GoalClaim>();
            public List<SessionEquip> Equipped = new List<SessionEquip>();
            public int SalvagedCount;
            public long ScrapGained;

            public bool IsEmpty => Claimed.Count == 0 && Equipped.Count == 0 && SalvagedCount == 0;
        }

        /// <summary>
        /// Predict the mid-session chore sweep without mutating anything. Claims come from
        /// <see cref="Goals.Claimables"/> (so they match <see cref="Goals.ClaimAll"/>); the gear plan is
        /// run exactly as <see cref="Apply"/> runs it (see <see cref="PlanGear"/> for the equip-before-salvage
        /// ordering and why). Pure.
        /// </summary>
        public static ManagePreview Preview(SaveState save, GameConfig cfg, long nowMs)
        {
            var (equips, salvageIds, salvageScrap, _) = PlanGear(save, cfg);
            return new ManagePreview
            {
                Claims = Goals.Claimables(save, cfg, nowMs),
                Equips = equips,
                SalvageCount = salvageIds.Count,
                SalvageScrap = salvageScrap,
            };
        }

        /// <summary>
        /// Do the obvious chores in one atomic verb: claim-all, THEN the auto-equip sweep, THEN salvage
        /// the remaining loose junk. Ordering is load-bearing — the aggregated salvage is the "salvage
        /// all" verb, which eats EVERY loose, unlocked, non-guarded item regardless of rarity (verified
        /// against <see cref="Inventory.SalvageAll"/>), so a bag item can be BOTH a genuine upgrade AND
        /// salvage-eligible. Running the equip sweep FIRST equips those upgrades (equipped gear is
        /// salvage-immune), so the salvage step only ever scraps what the sweep left behind — an upgrade
        /// is never destroyed. Claim is order-free (it only touches gems + the daily stamp, never the
        /// inventory/heroes the gear steps read). No-op (nothing in any bucket) returns the INPUT ref:
        /// each aggregated reducer shares its ref on no-op, so a triple no-op threads the same reference
        /// through untouched. IDEMPOTENT: after one Apply the bag holds no loose junk (everything was
        /// equipped or scrapped) and the day's claim is spent, so a second Apply reports nothing and
        /// shares the ref. Pure.
        /// </summary>
        public static (SaveState next, ManageReport report) Apply(SaveState save, GameConfig cfg, long nowMs)
        {
            var report = new ManageReport();

            // 1) Claims — independent of the gear steps (gems + daily stamp only).
            var (afterClaim, claimed) = Goals.ClaimAll(save, cfg, nowMs);
            report.Claimed = claimed;

            // 2) Equip sweep — plan + apply on the post-claim save (claiming leaves inventory/heroes
            // untouched, so the plan is identical to Preview's, which plans on the pre-claim save).
            var (equips, salvageIds, _, afterEquip) = PlanGear(afterClaim, cfg);
            report.Equipped = equips;

            // 3) Salvage the remainder — SalvageMany over the exact ids the plan flagged (every one is
            // eligible by construction, so its returned count/scrap equal the preview's). Ineligible ids
            // can't appear here; the sweep-contract skip lives inside the plan's eligibility filter.
            var (next, count, scrap) = Inventory.SalvageMany(afterEquip, salvageIds, cfg);
            report.SalvagedCount = count;
            report.ScrapGained = scrap;

            // next === save whenever all three halves no-op'd (ClaimAll shares ref, the equip sweep leaves
            // the save untouched when nothing equips, SalvageMany shares ref on an empty/no-match list).
            return (next, report);
        }

        // ------------------------------------ internals ------------------------------------

        /// <summary>
        /// The shared gear plan both <see cref="Preview"/> and <see cref="Apply"/> run, so their counts
        /// are identical by construction. Iterates the bag's LOOSE items in inventory-list order (the
        /// codebase's canonical order — Inventory.Sort persists it) and runs
        /// <see cref="Upgrades.AutoEquipIfBetter"/> over them, scoped to the FIELDED party and the current
        /// stage exactly like the live client sweep (CombatView). Each successful equip REPLACES the
        /// working save, so the next item is judged against the updated party — the deterministic order is
        /// the loose snapshot taken up front. After the sweep, the salvage set is every remaining item the
        /// "salvage all" predicate accepts (unequipped in the post-equip save + unlocked + not
        /// imprint-guarded), matching <see cref="Inventory.SalvageAll"/>; guarded/locked/equipped items are
        /// skipped SILENTLY (the sweep contract), appearing in neither the equip nor the salvage bucket.
        /// Returns the plan plus the post-equip save Apply hands to the salvage step. Pure — the input
        /// save is never mutated (AutoEquipIfBetter/EquipItem clone).
        /// </summary>
        private static (List<SessionEquip> equips, List<string> salvageIds, long salvageScrap, SaveState afterEquip)
            PlanGear(SaveState save, GameConfig cfg)
        {
            var equips = new List<SessionEquip>();

            // Fielded party = the non-empty party slots (mirrors CombatView's auto-equip sweep). No
            // fielded heroes ⇒ nothing to equip into; the sweep degenerates to salvage-only.
            var fielded = new List<string>();
            foreach (var id in save.Party) if (id != null) fielded.Add(id);
            int stage = save.Progress.CurrentStage;

            // Loose snapshot in inventory order: items not equipped by anyone right now. Locked items are
            // INCLUDED here (locking blocks salvage, not equip) — auto-equipping a locked upgrade is fine.
            var equipped = EquippedIds(save);
            var loose = new List<Item>();
            foreach (var it in save.Inventory)
                if (!equipped.Contains(it.Id)) loose.Add(it);

            var working = save;
            if (fielded.Count > 0)
                foreach (var it in loose)
                {
                    var (nextSave, eval) = Upgrades.AutoEquipIfBetter(working, it, cfg, stage, fielded);
                    if (eval == null) continue; // not an upgrade for any fielded hero — leave it for salvage
                    working = nextSave;
                    equips.Add(new SessionEquip { Item = it, HeroId = eval.HeroId, DeltaPercent = eval.DeltaPercent });
                }

            // Salvage set = SalvageAll's eligibility on the POST-equip save (equipped upgrades are now
            // immune). Walk the inventory (not the loose snapshot) so the just-equipped items are excluded
            // via the fresh equipped set, and any item a swap displaced back into the pool is included —
            // exactly what SalvageAll would take.
            var equippedAfter = EquippedIds(working);
            bool guard = working.Progress.Loot.NeverSalvageImprinted;
            var salvageIds = new List<string>();
            long salvageScrap = 0;
            foreach (var it in working.Inventory)
            {
                if (it.Locked) continue;                                   // locked: refused by every salvage path
                if (equippedAfter.Contains(it.Id)) continue;              // worn gear is never scrapped
                if (guard && Inventory.IsImprinted(it, cfg)) continue;    // guarded imprint prize: spared silently
                salvageIds.Add(it.Id);
                salvageScrap += cfg.Balance.ScrapValue(it.Rarity, it.ItemLevel);
            }

            return (equips, salvageIds, salvageScrap, working);
        }

        /// <summary>Ids equipped by any hero right now (a swept item is immune while worn). Mirrors
        /// Inventory's private EquippedIds — the same set both salvage verbs guard against.</summary>
        private static HashSet<string> EquippedIds(SaveState save)
        {
            var set = new HashSet<string>();
            foreach (var h in save.Heroes)
                foreach (var id in h.Equipped.Values) set.Add(id);
            return set;
        }
    }
}
