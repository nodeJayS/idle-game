#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The mode-select window (10.13e) that replaced CombatView's IMGUI DrawModesPanel family — one row
    /// per ALT mode (Tower, Crypt) with its active marker + entry verb, plus the crypt boon shop. No
    /// Campaign row (user call 2026-07-09: redundant — leaving a mode is the top-centre Exit button's
    /// job). A toggled window in the <see cref="TowerView"/> mold: built per-open, canvas destroyed on
    /// close. Read-only over the save except entering a mode / buying a boon / ticking keys, all routed
    /// through CombatView's Nav* seam so the reducer plumbing stays beside its siblings.
    ///
    /// Built on <see cref="PanelKit.Modal"/> with the violet border (migrated verbatim from the IMGUI
    /// frame): NO backdrop, so — exactly like the old menu — clicks outside the panel fall through to
    /// the game rather than being swallowed. Modal is silent on open (the old menu had no open sound
    /// either); its Close / verb ButtonCells click through the one UI sound family for free.
    /// </summary>
    public sealed class ModesWindow : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private Canvas? _canvas;

        // The violet frame — the exact color the IMGUI DrawModesPanel drew its border with.
        private static readonly Color Violet = new(0.55f, 0.48f, 0.75f, 0.95f);
        private static readonly Color RowBg = new(0.13f, 0.13f, 0.18f, 0.95f);     // mode-row + boon-box inset
        private static readonly Color TitleCol = new(0.92f, 0.9f, 1f);
        private static readonly Color DescCol = new(0.72f, 0.74f, 0.82f);
        private static readonly Color ActiveGreen = new(0.55f, 0.9f, 0.6f);
        private static readonly Color BoonGold = new(1f, 0.85f, 0.4f);
        private static readonly Color BoonText = new(0.85f, 0.87f, 0.92f);
        private static readonly Color BoonDim = new(0.55f, 0.57f, 0.62f);

        public bool IsOpen => _canvas != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle() { if (IsOpen) Close(); else Build(); }

        public void Close()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
        }

        /// <summary>Tear down + rebuild on the current save (post-boon-buy or a key-recharge tick — rank,
        /// cost, dust and the key countdown all move). Mirrors TowerView's per-open rebuild.</summary>
        private void Rebuild()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            Build();
        }

        private void Build()
        {
            // Modal (not Window): no header/Close row (the IMGUI put Close at the BOTTOM — kept there),
            // no backdrop (clicks outside fall through), violet border. NOTE: unlike PanelKit.Window,
            // Modal's returned body ALREADY carries a VerticalLayoutGroup (see PanelKit.Modal), so it is
            // a ready stack — do NOT call PanelKit.Stack on it (that would double-add the group). This
            // matches MainMenu, the proven Modal consumer.
            var canvasGo = PanelKit.Modal(transform, "ModesCanvas", sortOrder: 95, new Vector2(640f, 560f),
                                          out var body, backdrop: null, border: Violet);
            _canvas = canvasGo.GetComponent<Canvas>();
            var save = _view.CurrentSave;

            // Title — Modal has no header, so this is the first body row.
            var title = PanelKit.Label(body, Loc.T("modes.title"), Theme.FsTitle, TitleCol, TextAnchor.MiddleCenter);
            PanelKit.Fixed(title.gameObject, height: 36f);

            var kind = _view.CurrentKind;
            bool inDungeon = kind == EncounterKind.Dungeon;
            bool inTower = kind == EncounterKind.Tower;
            bool inCampaign = !inDungeon && !inTower;

            // --- Tower row ---
            ModeRow(body,
                Loc.F("modes.tower-name", Tower.HighestFloor(save)),
                inTower ? Loc.F("modes.tower-active-desc", _view.CurrentTowerFloor)
                        : Loc.T("modes.tower-desc"),
                active: inTower,
                buttonLabel: inCampaign ? Loc.T("modes.choose-floor") : null,
                onClick: () => { Close(); _view.NavOpenTower(); });

            // --- Crypt row (the full label/desc state machine, verbatim) ---
            int keys = Crypt.Keys(save), bank = _cfg.Balance.CryptKeyBank;
            string cryptDesc;
            if (inDungeon)
            {
                int left = _view.CryptRunFloorsLeft;
                cryptDesc = Loc.F("modes.crypt-active-desc", Crypt.NextFloor(save), left, left == 1 ? "" : "s");
            }
            else
            {
                cryptDesc = Loc.F("modes.crypt-keys", keys, bank);
                if (keys < bank)
                {
                    // Countdown to the next daily key — display-CEIL (a countdown never under-promises).
                    long hrs = (Crypt.NextKeyAtMs(save) - _view.NowMillis + 3_599_999) / 3_600_000;
                    cryptDesc += Loc.F("modes.crypt-next-key", hrs);
                }
                cryptDesc += Loc.F("modes.crypt-run-info", _cfg.Balance.CryptFloorsPerRun);
            }
            ModeRow(body,
                Loc.F("modes.crypt-name", Crypt.DepthRecord(save)),
                cryptDesc,
                active: inDungeon,
                buttonLabel: !inCampaign ? null
                           : Crypt.IsComplete(save, _cfg) ? Loc.T("modes.crypt-cleared")
                           : keys > 0 ? Loc.T("modes.crypt-enter") : Loc.T("modes.crypt-no-keys"),
                onClick: _view.NavEnterDungeonRun); // closes this window itself past its key/complete guard

            // --- Boon shop ---
            BuildBoons(body, save);

            PanelKit.Flex(body); // push Close to the panel bottom (the IMGUI pinned it there)

            var closeRow = PanelKit.Row(body, Theme.BtnH); // 48, over the 44 floor
            PanelKit.FlexSpacer(closeRow);
            PanelKit.ButtonCell(closeRow, Loc.T("common.close"), Close, width: 200f, fontSize: Theme.FsH1);
            PanelKit.FlexSpacer(closeRow);
        }

        /// <summary>One mode row: an inset box with the name + one-line desc stacked left, and either a
        /// green "● Active" tag or the switch button on the right (buttonLabel null + inactive = the
        /// current mode with no verb). Mirrors the IMGUI DrawModeRow.</summary>
        private void ModeRow(RectTransform body, string name, string desc, bool active,
                             string? buttonLabel, Action onClick)
        {
            var box = PanelKit.Section(body, null);
            var boxImg = box.GetComponent<Image>();
            if (boxImg != null) boxImg.color = RowBg;

            var row = PanelKit.Row(box, 62f);
            var col = PanelKit.VStack(row, Theme.GapXs);
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            PanelKit.Label(col, name, 20, Color.white, TextAnchor.UpperLeft);
            PanelKit.Label(col, desc, Theme.FsLabel, DescCol, TextAnchor.UpperLeft);

            if (active)
                PanelKit.TextCell(row, Loc.T("modes.active"), Theme.FsH2, ActiveGreen, TextAnchor.MiddleRight, width: 130f);
            else if (buttonLabel != null)
                PanelKit.ButtonCell(row, buttonLabel, onClick, width: 200f, fontSize: Theme.FsBody);
        }

        /// <summary>The crypt Boon shop: header (Grave Dust total) + one row per boon track — rank/effect
        /// and a Buy button priced off the geometric curve (MAX / unaffordable-dust labels otherwise). A
        /// buy routes through NavBuyBoon (persist + live-party refresh + feed) and rebuilds the window so
        /// rank/cost/dust re-read. Mirrors the IMGUI DrawCryptBoons.</summary>
        private void BuildBoons(RectTransform body, SaveState save)
        {
            var box = PanelKit.Section(body, null);
            var boxImg = box.GetComponent<Image>();
            if (boxImg != null) boxImg.color = RowBg;

            PanelKit.Label(box, Loc.F("modes.boons-header", Num.CompactFloor(Crypt.Dust(save, _cfg))),
                Theme.FsSubTab, BoonGold, TextAnchor.MiddleLeft);

            foreach (var boon in _cfg.CryptBoons)
            {
                string id = boon.Id; // captured per-iteration for the Buy closure
                int rank = Crypt.BoonRank(save, id);
                bool maxed = rank >= _cfg.Balance.CryptBoonMaxRank;
                double pct = rank * _cfg.Balance.CryptBoonStatPct * 100;

                var row = PanelKit.Row(box, Theme.TouchMin); // 44 — the Buy button clears the touch floor
                PanelKit.TextCell(row, Loc.F("modes.boon-row", boon.Name, pct, boon.Stat, rank, _cfg.Balance.CryptBoonMaxRank),
                    Theme.FsBody, BoonText, TextAnchor.MiddleLeft, flex: 1f);

                if (maxed)
                {
                    PanelKit.TextCell(row, Loc.T("common.max"), Theme.FsBody, BoonDim, TextAnchor.MiddleRight, width: 180f);
                    continue;
                }

                long cost = Crypt.BoonCost(rank, _cfg);
                if (Crypt.Dust(save, _cfg) >= cost)
                    PanelKit.ButtonCell(row, Loc.F("modes.boon-buy", Num.CompactCeil(cost)),
                        () => { if (_view.NavBuyBoon(id)) Rebuild(); }, width: 180f, fontSize: Theme.FsLabel);
                else
                    PanelKit.TextCell(row, Loc.F("modes.boon-cost", Num.CompactCeil(cost)), Theme.FsBody, BoonDim,
                        TextAnchor.MiddleRight, width: 180f);
            }
        }

        // While open: yield to any full-screen management panel (close self, as the IMGUI bailed on
        // AnyPanelOpen), and tick the crypt key recharge — on a real tick, rebuild so the countdown +
        // key line re-read. Change-only: NavTickCryptKeys returns false (no rebuild) on the no-op frames.
        private void Update()
        {
            if (_canvas == null) return;
            if (_view.AnyManagementPanelOpen) { Close(); return; }
            if (_view.NavTickCryptKeys()) Rebuild();
        }
    }
}
