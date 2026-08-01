#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The persistent bottom-corner navigation (10.13b) that replaced CombatView's IMGUI control bar.
    /// Two thumb-reach clusters at the LANDSCAPE bottom corners: an everyday pair bottom-left
    /// (Inventory, Heroes) and the system panels bottom-right (Modifiers, Modes, Summon, Goals — Goals
    /// OUTERMOST at the corner so its claim pip sits at maximum glanceability). Unlike TowerView this
    /// is built ONCE at Bind and always present (no toggle); it only opens the windows CombatView owns.
    ///
    /// Its content lives under <see cref="UiKit.SafeRoot"/> — the FIRST real consumer of slice 10.13a's
    /// safe-area plumbing, so the clusters inset from device notches / rounded corners (desktop no-op).
    /// Buttons are <see cref="UiKit.TextButton"/>s, so the 10.9d UI-click sound family and the shared
    /// font come for free — sound/typography parity with every other button, no extra wiring.
    ///
    /// Read-only over the save: it polls CombatView's internal seams change-only in Update (visibility,
    /// bag collapse, FTUE reveal, live labels/tint, claim pip) and routes each verb through a Nav* seam.
    /// The save reference changes identity on every reducer, so it is always read fresh through the view.
    /// </summary>
    public sealed class NavBar : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        // Button geometry at the 1280x720 CanvasScaler reference. Height sits inside the same bottom
        // band the IMGUI bar occupied (Theme.HudBarH) while clearing the 44pt touch-target rule
        // (ROADMAP 10.13): >=48 tall / >=64 wide. Per-button widths echo the old bar's label widths.
        private const float BtnH = 52f;
        private const int NavFont = 22;
        private const float PipInterval = 0.5f; // claim-pip poll cadence (Goals.Claimables allocates)
        private const float ManagePipInterval = 1f; // Manage pip: Session.Preview plans the whole gear sweep (heavier)

        // Modes label tint while an alt-mode run is live — the violet migrated verbatim from the old
        // bar's ModesActiveStyle (0.8, 0.68, 1) so the way home reads the same.
        private static readonly Color ModesViolet = new(0.8f, 0.68f, 1f);

        private GameObject _root = null!;    // full-stretch container toggled for whole-nav visibility
        private GameObject _heroesBtn = null!;
        private GameObject _manageBtn = null!;
        private GameObject _modifiersBtn = null!;
        private GameObject _modesBtn = null!;
        private GameObject _summonBtn = null!;
        private GameObject _goalsBtn = null!;
        private Text _invText = null!;
        private Text _modText = null!;
        private Text _modesText = null!;
        private GameObject _pipDot = null!;
        private GameObject _managePipDot = null!;

        // Change-only poll state (sentinels force a first apply on the first visible frame).
        private bool? _lastVisible;
        private bool _lastBag;
        private int _lastFtue = -1;
        private string _invShown = "";
        private int _lastMods = -1;
        private int _lastAlt = -1;
        private bool _lastPip;
        private float _pipTimer;
        private bool _lastManagePip;
        private float _managePipTimer;

        public void Bind(CombatView view, GameConfig cfg)
        {
            _view = view;
            _cfg = cfg;
            Build();
        }

        private void Build()
        {
            // Under the full-screen windows' canvases (Heroes/Goals sortOrder 90+) so they cover the
            // nav; the ControlBarVisible poll hides it under them anyway. Matches the TopBar/Chat HUD band.
            var canvas = UiKit.CreateCanvas("NavBarCanvas", transform, sortOrder: 85);
            var safe = UiKit.SafeRoot(canvas);

            _root = new GameObject("Nav", typeof(RectTransform));
            _root.transform.SetParent(safe, false);
            var rrt = (RectTransform)_root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;

            // Bottom-LEFT: the everyday cluster — [Inventory][Heroes][Manage] (10.14b added Manage, the
            // mid-session chore sweep, after Heroes so the two loadout verbs sit together on the left).
            var left = Cluster(rrt, new Vector2(0f, 0f));
            var invBtn = NavButton(left, Loc.T("nav.inventory"), 160f, _view.NavToggleInventory);
            _invText = invBtn.GetComponentInChildren<Text>();
            _heroesBtn = NavButton(left, Loc.T("nav.heroes"), 120f, _view.NavToggleHeroes).gameObject;
            _manageBtn = NavButton(left, Loc.T("nav.manage"), 120f, OpenManage).gameObject;
            // Manage pip: the same gold dot as Goals', lit when Session.Preview finds pending chores.
            _managePipDot = AddPip(_manageBtn);

            // Bottom-RIGHT: system panels, Goals last so it lands at the corner (rightmost) with the pip.
            var right = Cluster(rrt, new Vector2(1f, 0f));
            var modBtn = NavButton(right, Loc.T("nav.modifiers"), 160f, _view.NavToggleModifiers);
            _modText = modBtn.GetComponentInChildren<Text>();
            _modifiersBtn = modBtn.gameObject;
            var modesBtn = NavButton(right, Loc.T("nav.modes"), 120f, _view.NavToggleModes);
            _modesText = modesBtn.GetComponentInChildren<Text>();
            _modesBtn = modesBtn.gameObject;
            _summonBtn = NavButton(right, Loc.T("nav.summon"), 130f, _view.NavToggleSummon).gameObject;
            _goalsBtn = NavButton(right, Loc.T("nav.goals"), 120f, _view.NavToggleGoals).gameObject;

            // Claim pip on the Goals button (10.14b factored it into AddPip, now shared with Manage).
            _pipDot = AddPip(_goalsBtn);
        }

        // A small gold dot overlaid at a button's top-right corner, toggled by SetActive, starting off.
        // Gold matches the old control bar's (1, 0.85, 0.4). Not a layout child of the cluster (it hangs
        // off the button), so it never perturbs the reflow. Non-raycasting so it never eats a tap.
        private static GameObject AddPip(GameObject btn)
        {
            var pip = UiKit.Label(btn.transform, "●", 18, TextAnchor.MiddleCenter,
                                  new Vector2(22f, 22f), Vector2.zero);
            pip.color = new Color(1f, 0.85f, 0.4f);
            pip.raycastTarget = false;
            var prt = (RectTransform)pip.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-2f, -2f);
            pip.gameObject.SetActive(false);
            return pip.gameObject;
        }

        // Open the Manage confirm card (a transient PanelKit.Modal, like the boot arrival card — not a
        // bound panel, so the NavBar owns spawning it rather than routing through a CombatView toggle).
        // Its dimming backdrop covers the nav, blocking a second tap while open, so no re-open guard.
        private void OpenManage()
        {
            var go = new GameObject("ManageModal");
            go.transform.SetParent(transform, false);
            go.AddComponent<ManageModal>().Show(_view, _cfg);
        }

        // A corner-anchored horizontal cluster that reflows as buttons hide: a HorizontalLayoutGroup
        // (non-force-expand, explicit LayoutElement preferred sizes per the 10.3 layout-kit lessons)
        // wrapped in a ContentSizeFitter so hiding a button shrinks the cluster toward its corner.
        private static RectTransform Cluster(RectTransform parent, Vector2 corner)
        {
            var go = new GameObject("Cluster", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = new Vector2(corner.x == 0f ? Theme.HudPad : -Theme.HudPad, Theme.HudPad);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.HudGap;
            hlg.childAlignment = corner.x == 0f ? TextAnchor.LowerLeft : TextAnchor.LowerRight;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        private static Button NavButton(RectTransform cluster, string label, float w, Action onClick)
        {
            var btn = UiKit.TextButton(cluster, label, new Vector2(w, BtnH), Vector2.zero, onClick, NavFont);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = BtnH;
            return btn;
        }

        // Change-only HUD poll (the TopBar.Update idiom): re-interpolate a label/visibility ONLY when its
        // source moves, so the steady-state nav allocates ~nothing. The unlock set only ever grows, so a
        // cheap bitmask compare per frame suffices; the claim pip runs on a coarse interval because
        // Goals.Claimables allocates a list.
        private void Update()
        {
            // The old IMGUI bar drew behind OnGUI's `_combat == null` early-return; this Update has no
            // such umbrella, so guard the boot frames where the view exists but its save doesn't yet.
            if (_view == null || _view.CurrentSave == null) return;

            bool visible = _view.ControlBarVisible;
            if (_lastVisible != visible) { _lastVisible = visible; _root.SetActive(visible); }
            if (!visible) return;

            var save = _view.CurrentSave;
            bool bag = _view.BagOpen;

            int ftue = 0;
            if (Progression.FeatureUnlocked(Feature.Modifiers, save)) ftue |= 1;
            if (Progression.FeatureUnlocked(Feature.Modes, save)) ftue |= 2;
            if (_view.AnyLiveBanner && Progression.FeatureUnlocked(Feature.Gacha, save)) ftue |= 4;
            if (Progression.FeatureUnlocked(Feature.DailyLogin, save)
                || Progression.FeatureUnlocked(Feature.Achievements, save)) ftue |= 8;

            // Close-Bag collapse: while the bag is open every other button hides and Inventory reads
            // "Close Bag" (the old invOpen early-return); otherwise the FTUE bitmask drives each button.
            if (bag != _lastBag || ftue != _lastFtue)
            {
                _lastBag = bag; _lastFtue = ftue;
                _heroesBtn.SetActive(!bag);
                // Manage shares the Goals FTUE gate (bit 8 = DailyLogin || Achievements): by that reveal
                // the player has retention systems worth sweeping (claims, upgrades, salvage-worthy loot).
                _manageBtn.SetActive(!bag && (ftue & 8) != 0);
                _modifiersBtn.SetActive(!bag && (ftue & 1) != 0);
                _modesBtn.SetActive(!bag && (ftue & 2) != 0);
                _summonBtn.SetActive(!bag && (ftue & 4) != 0);
                _goalsBtn.SetActive(!bag && (ftue & 8) != 0);
            }

            string inv = bag ? Loc.T("nav.close-bag") : Loc.T("nav.inventory");
            if (inv != _invShown) { _invShown = inv; _invText.text = inv; }

            int mods = _view.ActiveModifierCount;
            if (mods != _lastMods) { _lastMods = mods; _modText.text = mods > 0 ? Loc.F("nav.modifiers-n", mods) : Loc.T("nav.modifiers"); }

            int alt = _view.AltModeLive ? 1 : 0;
            if (alt != _lastAlt) { _lastAlt = alt; _modesText.color = alt == 1 ? ModesViolet : Color.white; }

            _pipTimer -= Time.unscaledDeltaTime;
            if (_pipTimer <= 0f)
            {
                _pipTimer = PipInterval;
                bool pip = Goals.Claimables(save, _cfg, _view.NowMillis).Count > 0;
                if (pip != _lastPip) { _lastPip = pip; _pipDot.SetActive(pip); }
            }

            // Manage pip: Session.Preview plans the whole gear sweep (heavier than Claimables), so poll on
            // a coarser 1s cadence, and only while the Manage button is revealed (bit 8) to skip the alloc.
            if ((ftue & 8) != 0)
            {
                _managePipTimer -= Time.unscaledDeltaTime;
                if (_managePipTimer <= 0f)
                {
                    _managePipTimer = ManagePipInterval;
                    bool mpip = !Session.Preview(save, _cfg, _view.NowMillis).IsEmpty;
                    if (mpip != _lastManagePip) { _lastManagePip = mpip; _managePipDot.SetActive(mpip); }
                }
            }
        }
    }
}
