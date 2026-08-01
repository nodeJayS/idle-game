#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace IdleGame.GameCore
{
    /// <summary>
    /// The string table (10.20c, mobile arc MM8) — the l10n foundation. Every player-facing UI
    /// string the client renders routes through here; the ENGLISH table below is the shipping
    /// copy, byte-identical to the literals it replaced (the extraction was behavior-preserving
    /// by contract). Pure C# and hosted in GameCore deliberately: display-strings-as-data sits
    /// beside the display-adjacent English GameCore already carries (config names, quest titles),
    /// and living here makes the table dotnet-testable — LocTests regex-scans the client for
    /// every referenced key and fails the suite on a missing entry, so hardcoded-string debt
    /// can't quietly re-accumulate.
    ///
    /// Key style: dot-namespaced lowercase kebab (<c>nav.inventory</c>, <c>feed.floor-bundle</c>).
    /// Format entries carry {0}/{1} placeholders (plus format specifiers where the old
    /// interpolation had them) and go through <see cref="F"/>. A missing key renders AS the key —
    /// loud on screen and greppable, never a silent empty string.
    ///
    /// The 10.20d language-pack seam is <see cref="SetTable"/>: a downloaded/loaded pack is
    /// consulted FIRST with English as the per-key fallback, so a partial pack degrades to
    /// English rather than to key soup. Nothing calls it yet.
    /// </summary>
    public static class Loc
    {
        private static IReadOnlyDictionary<string, string>? _overrides;

        /// <summary>Table lookup: the active language pack first, then English, else the KEY
        /// ITSELF — a missing entry must be loud (visible in play, greppable in a report), not
        /// an invisible empty label.</summary>
        public static string T(string key)
        {
            if (_overrides != null && _overrides.TryGetValue(key, out var o)) return o;
            return En.TryGetValue(key, out var v) ? v : key;
        }

        /// <summary>Format entry: <see cref="T"/> + <see cref="string.Format(System.IFormatProvider,string,object[])"/>
        /// under the INVARIANT culture — numeric renderings must not drift with the device's
        /// region setting (the sim is deterministic; its display should be too). Values that
        /// need locale-aware number formatting go through Num.* BEFORE being passed as args.</summary>
        public static string F(string key, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, T(key), args);

        /// <summary>DATA-name lookup, for GameCore-config display names (zones/heroes/items/
        /// modifiers) keyed by their stable id: a language pack overrides by key, otherwise the
        /// English CONFIG name passes through as the canonical fallback. This keeps config
        /// English out of the En table (one source of truth — the config) while still giving a
        /// pack a hook to translate it. No call sites yet; the phase-2 sweep adopts it.</summary>
        public static string Content(string key, string fallback)
        {
            if (_overrides != null && _overrides.TryGetValue(key, out var o)) return o;
            return En.TryGetValue(key, out var v) ? v : fallback;
        }

        /// <summary>Install (non-null) or clear (null) a language pack. Overrides are consulted
        /// before English per key — see the class summary for the partial-pack rationale.</summary>
        public static void SetTable(IReadOnlyDictionary<string, string>? overrides) => _overrides = overrides;

        // The English table. Grouped by namespace; entries are the exact bytes the old inline
        // literals rendered (spacing — including multi-space alignment runs — is deliberate).
        internal static readonly Dictionary<string, string> En = new()
        {
            // ---- shared verbs/chips (defined once, used across files) ----
            ["common.close"] = "Close",
            ["common.cancel"] = "Cancel",
            ["common.confirm"] = "Confirm",
            ["common.ok"] = "OK",
            ["common.on"] = "On",
            ["common.off"] = "Off",
            ["common.locked"] = "Locked",
            ["common.free"] = "Free",
            ["common.max"] = "MAX",
            ["common.exit-game"] = "Exit Game",

            // ---- bottom nav (NavBar) ----
            ["nav.inventory"] = "Inventory",
            ["nav.close-bag"] = "Close Bag",
            ["nav.heroes"] = "Heroes",
            ["nav.manage"] = "Manage",
            ["nav.modifiers"] = "Modifiers",
            ["nav.modifiers-n"] = "Modifiers ({0})",
            ["nav.modes"] = "Modes",
            ["nav.summon"] = "Summon",
            ["nav.goals"] = "Goals",

            // ---- top-centre strip (TopControls) ----
            ["top.stage"] = "Stage {0}",
            ["top.endless"] = "Endless {0}",
            ["top.push-beyond"] = "Push beyond…",
            ["top.challenge-miniboss"] = "Challenge Miniboss",
            ["top.challenge-major-boss"] = "Challenge ★ Major Boss",
            ["top.flee"] = "Flee",
            ["top.exit-crypt"] = "Exit Crypt",
            ["top.exit-tower"] = "Exit Tower — Floor {0}",
            ["top.speed-1x"] = "1x",
            ["top.speed-2x"] = "2x",
            ["top.auto-advance"] = "▶ Auto-Advance",
            ["top.auto-advance-stop"] = "■ Stop Auto-Advance",

            // ---- Settings window (TopBar) ----
            ["settings.title"] = "Settings",
            ["settings.name"] = "Name",
            ["settings.master-volume"] = "Master Volume",
            ["settings.sfx-volume"] = "SFX Volume",
            ["settings.ambience-volume"] = "Ambience Volume",
            ["settings.damage-numbers"] = "Damage Numbers",
            ["settings.screen-shake"] = "Screen Shake",
            ["settings.loot-feed"] = "Loot Feed",
            ["settings.projectiles"] = "Projectiles",
            ["settings.spawn-animations"] = "Spawn Animations",
            ["settings.text-size"] = "Text Size",
            ["settings.reduced-motion"] = "Reduced Motion",
            ["settings.haptics"] = "Haptics",
            ["settings.render-scale"] = "Render Scale",
            ["settings.shadows"] = "Shadows",
            ["settings.post-fx"] = "Post FX",
            ["settings.main-menu"] = "Main Menu",

            // ---- title screen (MainMenu) ----
            ["menu.title"] = "IDLE ARPG",
            ["menu.continue"] = "Continue",
            ["menu.new-game"] = "New Game",
            ["menu.no-save"] = "No save yet — start a new game",
            ["menu.overwrite-prompt"] = "Overwrite your existing save?",
            ["menu.overwrite"] = "Overwrite",

            // ---- loading-screen titles (CombatView → LoadingScreen.Run) ----
            ["loading.depth"] = "Depth {0} — {1}",
            ["loading.descending"] = "Descending — Depth {0}",
            ["loading.ascending"] = "Ascending — Floor {0}",
            ["loading.return-camp"] = "Returning to camp",

            // ---- mode select (ModesWindow) ----
            ["modes.title"] = "Game Modes",
            ["modes.tower-name"] = "Tower of Ascension  (F{0})",
            ["modes.tower-active-desc"] = "Climbing floor {0} — clear it or exit up top.",
            ["modes.tower-desc"] = "One-clear floors on a brutal curve; milestones pay permanent buffs.",
            ["modes.choose-floor"] = "Choose Floor",
            ["modes.crypt-name"] = "Crypt  (Depth {0})",
            ["modes.crypt-active-desc"] = "Depth {0} — clear every monster, {1} floor{2} beyond this one.",
            ["modes.crypt-keys"] = "Keys {0}/{1}",
            ["modes.crypt-next-key"] = " (next in {0}h)",
            ["modes.crypt-run-info"] = "  ·  {0}-floor runs  ·  wipe = no chest",
            ["modes.crypt-cleared"] = "Cleared!",
            ["modes.crypt-enter"] = "Enter  (1 Key)",
            ["modes.crypt-no-keys"] = "No Keys",
            ["modes.active"] = "● Active",
            ["modes.boons-header"] = "Crypt Boons — Grave Dust: {0}",
            ["modes.boon-row"] = "{0}  (+{1:0}% {2})   rank {3}/{4}",
            ["modes.boon-buy"] = "Buy  ({0} dust)",
            ["modes.boon-cost"] = "{0} dust",

            // ---- Manage confirm card (ManageModal) ----
            ["manage.title"] = "Manage",
            ["manage.nothing"] = "Nothing needs doing.",
            ["manage.claim"] = "Claim {0} reward{1}  (+{2} gems)",
            ["manage.equip"] = "Equip {0} upgrade{1}",
            ["manage.salvage"] = "Salvage {0} item{1}  (+{2} scrap)",

            // ---- boot arrival card (IdleClaimModal) ----
            ["arrival.idle-title"] = "Idle Rewards",
            ["arrival.daily-title"] = "Daily Reward",
            ["arrival.time"] = "Time: {0}{1}",
            ["arrival.capped"] = "  (capped)",
            ["arrival.hours"] = "{0}h {1}m",
            ["arrival.minutes"] = "{0}m {1}s",
            ["arrival.bonus"] = "Bonus",
            ["arrival.claim-line"] = "{0}   +{1} gems",
            ["arrival.collect"] = "Collect",
            ["arrival.gold"] = "Gold:  {0}",
            ["arrival.xp"] = "XP:    {0}",
            ["arrival.items"] = "Items: {0}",

            // ---- chat window chrome (ChatPanel) ----
            ["chat.title"] = "Chat",
            ["chat.coming-soon"] = "Coming soon — chat arrives with the online update.",

            // ---- quest board (QuestPanel) ----
            ["quest.title"] = "Quests",
            ["quest.getting-started"] = "Getting started",
            ["quest.slay-monsters"] = "Slay {0} monsters",
            ["quest.salvage-items"] = "Salvage {0} items",
            ["quest.earn-gold"] = "Earn {0} gold",
            ["quest.clear-stages"] = "Clear {0} stages",
            ["quest.find-rare-plus"] = "Find {0} Rare+ items",
            ["quest.goal"] = "Goal",

            // ---- rarity names (StatDisplay.RarityName; RarityTag rides it) ----
            ["rarity.normal"] = "Normal",
            ["rarity.rare"] = "Rare",
            ["rarity.unique"] = "Unique",
            ["rarity.legendary"] = "Legendary",
            ["rarity.mythic"] = "Mythic",

            // ---- codex kill tiers (CombatView feed) ----
            ["codex.tier-bronze"] = "Bronze",
            ["codex.tier-silver"] = "Silver",
            ["codex.tier-gold"] = "Gold",

            // ---- live-ops events (composed CLIENT-side off EventInfo.Id — the 10.20c leak fix) ----
            ["event.weekend-boost"] = "{0} Weekend Boost",
            ["event.mutated-crypt"] = "Mutated Crypt",
            ["event.ends-in"] = "{0} — ends in {1}h",

            // ---- IMGUI HUD (CombatView wallet / context lines / party chips) ----
            ["hud.major-boss"] = "★ MAJOR BOSS — Stage {0}",
            ["hud.miniboss"] = "Miniboss — Stage {0}",
            ["hud.boss-modifier"] = "  ·  {0}",
            ["hud.wallet-gold"] = "Gold   {0}",
            ["hud.wallet-scrap"] = "Scrap  {0}",
            ["hud.wallet-gems"] = "Gems   {0}",
            ["hud.party-empty"] = "— empty —",
            ["hud.party-chip"] = "{0}  Lv {1}",
            ["hud.endless-depth"] = "Endless depth {0}",
            ["hud.hint-next"] = "Next: {0}",
            ["hud.hint-idle"] = "Idle rewards ready to claim",
            ["hud.hint-skill-point"] = "A hero has an unspent skill point",

            // ---- end-of-encounter card (CombatView.BuildOutcomeUi) ----
            ["outcome.crypt-complete"] = "Crypt run complete!",
            ["outcome.depth-cleared"] = "Depth {0} cleared!",
            ["outcome.tower-cleared"] = "Tower floor {0} cleared!",
            ["outcome.stage-cleared"] = "Stage {0} cleared!",
            ["outcome.descending"] = "Descending deeper…",
            ["outcome.returning"] = "Returning to camp…",
            ["outcome.advancing"] = "Advancing to the next stage…",
            ["outcome.boss-failed"] = "BOSS FAILED",
            ["outcome.floor-failed"] = "FLOOR {0} FAILED",
            ["outcome.crypt-failed"] = "CRYPT FAILED",
            ["outcome.party-wiped"] = "PARTY WIPED",

            // ---- activity feed templates (CombatView + Bootstrap) ----
            ["feed.ascends"] = "{0} ascends — ★{1}!",
            ["feed.auto-equipped"] = "Auto-equipped {0} → {1} ({2})",
            ["feed.salvaged-items"] = "Salvaged {0} item{1}  (+{2} scrap)",
            ["feed.claimed-gems"] = "{0}  +{1} gems",
            ["feed.boon-bought"] = "Boon bought: {0} rank {1}.",
            ["feed.joins-roster"] = "{0} joins the roster!",
            ["feed.summon-dupe"] = "Summon: {0} (dupe)  (+{1} shards)",
            ["feed.pity"] = "Pity! {0} is guaranteed.",
            ["feed.modifier-cant-afford"] = "Not enough gold + scrap to upgrade that modifier.",
            ["feed.modifier-rolled"] = "{0} tuning rolled {1}{2:0.#}% → now +{3:0}%",
            ["feed.modifier-at-base"] = "{0} is already at base tuning.",
            ["feed.modifier-reset"] = "{0} tuning reset to base (+0%).",
            ["feed.reforge-cant-afford"] = "Not enough gold + scrap to reforge that item.",
            ["feed.reforged"] = "Reforged {0} — its affixes re-rolled.",
            ["feed.enhanced"] = "⚒ Enhanced: {0}",
            ["feed.enhance-dropped"] = "⚒ Enhance failed — dropped to +{0}",
            ["feed.enhance-kept"] = "⚒ Enhance failed (+{0} kept)",
            ["feed.salvage-none"] = "No loose, unlocked items to salvage.",
            ["feed.salvaged-selected"] = "Salvaged {0} selected  (+{1} scrap)",
            ["feed.loadout-saved"] = "{0}'s loadout saved.",
            ["feed.loadout-applied"] = "{0} wears their loadout — {1} equipped{2}.",
            ["feed.loadout-skipped"] = " ({0} piece{1} unavailable)",
            ["feed.goal-complete"] = "Goal complete: {0}  (+{1} gold)",
            ["feed.season-gems"] = "Season tier {0}: +{1} gems ★",
            ["feed.season-gold"] = "Season tier {0}: +{1} gold",
            ["feed.achievement"] = "★ Achievement: {0} {1}!{2}",
            ["feed.reward-gold"] = "{0} gold",
            ["feed.reward-scrap"] = "{0} scrap",
            ["feed.reward-xp"] = "{0} XP",
            ["feed.reward-wrap"] = "  (+{0})",
            ["feed.intro-beat"] = "✔ {0} — +{1} gold",
            ["feed.daily"] = "Daily reward — day {0} streak!  +{1} gems",
            ["feed.set-collected"] = "Set collected: {0}!",
            ["feed.bag-full"] = "Bag full — new loot left behind. Salvage or enable auto-salvage.",
            ["feed.level-up"] = "Level up!",
            ["feed.codex"] = "Codex: {0} — {1}!",
            ["feed.endless-cleared"] = "Endless {0} cleared!",
            ["feed.stage-cleared"] = "Stage {0} cleared!",
            ["feed.endless-record"] = "New endless record — depth {0}!",
            ["feed.endless-gems"] = "+{0} gems — endless milestone!",
            ["feed.endless-shards"] = "+{0} hero shards — endless milestone!",
            ["feed.modifier-unlocked"] = "Modifier unlocked: {0} (str {1})",
            ["feed.modifiers-upgraded"] = "Modifiers upgraded → strength {0}",
            ["feed.reveal-idle"] = "Idle rewards unlocked — progress banks while you're away.",
            ["feed.reveal-daily"] = "Daily login unlocked — check in each day for gems.",
            ["feed.reveal-achievements"] = "Achievements unlocked — see Goals for lifetime milestones.",
            ["feed.reveal-modifiers"] = "Modifiers unlocked — risk for reward.",
            ["feed.reveal-modes"] = "The Tower and the Crypt have opened — Modes menu.",
            ["feed.reveal-gacha"] = "Summoning unlocked — spend gems on new heroes.",
            ["feed.first-boss"] = "Your first boss falls — the road ahead opens!",
            ["feed.joins-party"] = "{0} joins your party!",
            ["feed.auto-advance-stopped"] = "Auto-advance stopped — failed Stage {0}'s boss.",
            ["feed.tower-cleared"] = "Tower floor {0} cleared!",
            ["feed.floor-bundle"] = "Floor bundle: +{0} gold · {1} item{2}",
            ["feed.loot-item"] = "{0}{1} (i{2})",
            ["feed.loot-upgrade-tag"] = "  ▲ {0} {1}",
            ["feed.auto-salvaged"] = "Auto-salvaged {0} → +{1} scrap.",
            ["feed.ascension-buff"] = "Ascension buff! +{0:0}% account power (Hp/Atk/Def).",
            ["feed.new-modifier"] = "New modifier unlocked: {0}! Slot it in the Modifiers panel.",
            ["feed.tower-failed"] = "Tower floor {0} failed — train up and try again.",
            ["feed.depth-cleared"] = "Depth {0} cleared!  +{1} gems",
            ["feed.crypt-complete"] = "Crypt run complete!  {0}",
            ["feed.crypt-lost"] = "The crypt claims this run.  {0}",
            ["feed.run-summary"] = "{0} floor{1} cleared  ·  +{2} gems  ·  +{3} dust  ·  +{4} gold",
            ["feed.entering-zone"] = "Now entering {0}.",
            ["feed.streak"] = "{0} {1} kills in a blink",
            ["feed.streak-massacre"] = "MASSACRE!",
            ["feed.streak-slaughter"] = "Slaughter!",
            ["feed.streak-rampage"] = "Rampage!",
            ["feed.speed-2x"] = "Speed: 2x (crypt & tower)",
            ["feed.speed-1x"] = "Speed: 1x",
            ["feed.crypt-silent"] = "The crypt lies silent — every depth is cleared.",
            ["feed.crypt-no-keys"] = "No crypt keys — the next one arrives at the daily reset.",
            ["feed.crypt-entering"] = "Depth {0} — entering {1}…",
            ["feed.crypt-resume"] = "Resuming your crypt run — Depth {0}.",
            ["feed.crypt-descending"] = "Descending… Depth {0} — {1}",
            ["feed.crypt-abandoned"] = "Crypt run abandoned — the chest is forfeit.",
            ["feed.tower-abandoned"] = "Tower floor {0} abandoned.",
            ["feed.auto-advance-on"] = "Auto-advance on — pushing stages until a boss run fails.",
            ["feed.auto-advance-off"] = "Auto-advance off.",
            ["feed.room-clear-gold"] = "Room clear! +{0} gold",
            ["feed.room-clear"] = "Room clear!",
            ["feed.wave"] = "Another wave rises!",
            ["feed.boss-key"] = "The Boss Key clatters free — the boss door will open!",
            ["feed.chest-gold"] = "The chest creaks open: +{0} gold",
            ["feed.chest"] = "The chest creaks open…",
            ["feed.mimic"] = "That chest has TEETH!",
            ["feed.imprinted"] = "✦ Imprinted! {0} rolled {1} — equip it to cleave harder.",
        };
    }
}
