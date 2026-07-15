#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// The persistent top-centre verb strip (10.13e) that replaced CombatView's IMGUI DrawTopControls —
    /// the LAST interactive IMGUI to migrate. Same mold as the <see cref="NavBar"/>: a canvas-owning
    /// MonoBehaviour built ONCE at Bind, with content under <see cref="UiKit.SafeRoot"/>, that polls
    /// CombatView's internal seams change-only in Update and routes each verb through a Nav* method.
    ///
    /// Three top-centre clusters, each at its OWN fixed top offset (not a reflowing column): the verb
    /// row must stay put when the stage-nav row above it hides — otherwise the alt-mode Exit / boss
    /// Flee would slide up under the IMGUI boss timer / context line still drawn by DrawHud. Within a
    /// cluster the horizontal layout re-centres as buttons show/hide (the NavBar idiom).
    ///
    /// - FARM: [◀] [Stage N / Endless K] [▶ | Push beyond…]  then  [Challenge …]
    /// - BOSS-CHALLENGE: [Flee]
    /// - DUNGEON / TOWER: [Exit Crypt | Exit Tower — Floor N] [2x/1x]  (the EXIT lives where Challenge
    ///   lives — user call 2026-07-06: the way home is always the same spot)
    /// - Auto-advance: shelved OFF (AutoAdvanceAvailable), but the gate + green-when-armed tint stay so
    ///   re-enabling it just works.
    ///
    /// Buttons ride the 44pt touch floor (ROADMAP 10.13): 48 tall, the arrows &gt;= 56 wide (the IMGUI's
    /// 46x38 were under it). The whole strip hides while a panel is open or the fight isn't Running —
    /// see <see cref="CombatView.TopControlsVisible"/>. Read-only over the save (never caches SaveState;
    /// GoToStage swaps the save each call, so state is read fresh through the view every frame).
    /// </summary>
    public sealed class TopControls : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;

        // Fixed top offsets (1280x720 CanvasScaler reference) — each cluster anchors top-centre at its
        // own y so hiding the stage-nav row never shifts the verb row beneath it.
        private const float NavTop = 46f;   // stage nav (farm)
        private const float VerbTop = 104f; // primary verb (Challenge / Flee / Exit + speed) — clears DrawHud's boss timer
        private const float AutoTop = 162f; // auto-advance (shelved)
        private const float BtnH = 48f;     // over the 44pt floor

        private static readonly Color AutoArmed = new(0.55f, 0.9f, 0.6f); // green when auto-advance is on (verbatim tint)

        private GameObject _root = null!;

        // Stage-nav cluster.
        private GameObject _navCluster = null!, _backBtn = null!, _fwdBtn = null!, _pushBtn = null!;
        private Text _stageLabel = null!;

        // Primary-verb cluster (always shown while the strip is visible — its children are the toggled ones).
        private GameObject _challengeBtn = null!, _fleeBtn = null!, _exitBtn = null!, _speedBtn = null!;
        private Text _challengeText = null!, _exitText = null!, _speedText = null!;

        // Auto-advance cluster.
        private GameObject _autoCluster = null!, _autoBtn = null!;
        private Text _autoText = null!;

        // Change-only poll state. Active-flags start false (widgets are built inactive) so the first
        // visible frame applies every SetActive; the label sentinels force a first re-interpolation.
        private bool? _lastVisible;
        private bool _aNav, _aBack, _aFwd, _aPush;
        private bool _aChal, _aFlee, _aExit, _aSpeed, _aAuto;
        private int _lastStage = -1;
        private int _lastMajor = -1;    // 0/1
        private int _lastExitKind = -999;
        private int _lastExitFloor = int.MinValue;
        private int _lastSpeed = -1;    // 0/1
        private int _lastArmed = -1;    // 0/1

        public void Bind(CombatView view, GameConfig cfg)
        {
            _view = view;
            _cfg = cfg;
            Build();
        }

        private void Build()
        {
            // sortOrder 85 (== NavBar): the top strip and bottom nav never overlap spatially, and the
            // TopControlsVisible poll hides the whole strip under any full-screen window regardless.
            var canvas = UiKit.CreateCanvas("TopControlsCanvas", transform, sortOrder: 85);
            var safe = UiKit.SafeRoot(canvas);

            _root = new GameObject("TopControls", typeof(RectTransform));
            _root.transform.SetParent(safe, false);
            var rrt = (RectTransform)_root.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;

            // --- stage-nav cluster (farm) ---
            var nav = Cluster(rrt, NavTop);
            _navCluster = nav.gameObject;
            _backBtn = MakeButton(nav, "◀", 56f, () => _view.NavGoToStage(_view.CurrentSave.Progress.CurrentStage - 1), 24).gameObject;
            _stageLabel = MakeLabel(nav, "Stage 1", 180f, 20);
            _fwdBtn = MakeButton(nav, "▶", 56f, () => _view.NavGoToStage(_view.CurrentSave.Progress.CurrentStage + 1), 24).gameObject;
            _pushBtn = MakeButton(nav, "Push beyond…", 200f, () => _view.NavGoToStage(_view.CurrentSave.Progress.CurrentStage + 1), 18).gameObject;

            // --- primary-verb cluster (stays active; every running mode shows one of its children) ---
            var verb = Cluster(rrt, VerbTop);
            var chal = MakeButton(verb, "Challenge Miniboss", 370f, _view.NavChallengeBoss, 18);
            _challengeBtn = chal.gameObject; _challengeText = chal.GetComponentInChildren<Text>();
            _fleeBtn = MakeButton(verb, "Flee", 180f, _view.NavFlee, 18).gameObject;
            var exit = MakeButton(verb, "Exit Crypt", 370f, OnExit, 18);
            _exitBtn = exit.gameObject; _exitText = exit.GetComponentInChildren<Text>();
            var speed = MakeButton(verb, "1x", 72f, _view.NavToggleAltSpeed, 18);
            _speedBtn = speed.gameObject; _speedText = speed.GetComponentInChildren<Text>();

            // --- auto-advance cluster (shelved) ---
            var auto = Cluster(rrt, AutoTop);
            _autoCluster = auto.gameObject;
            var autoB = MakeButton(auto, "▶ Auto-Advance", 260f, _view.NavToggleAutoAdvance, 18);
            _autoBtn = autoB.gameObject; _autoText = autoB.GetComponentInChildren<Text>();

            // Start hidden; the change-only poll lights up exactly what the current mode needs. The
            // verb cluster stays active (every running mode shows one verb — its children are the ones
            // toggled); the stage label + auto button stay active inside their conditionally-shown
            // clusters (hiding the cluster hides them), so neither is toggled individually.
            foreach (var go in new[] { _navCluster, _backBtn, _fwdBtn, _pushBtn,
                                       _challengeBtn, _fleeBtn, _exitBtn, _speedBtn, _autoCluster })
                go.SetActive(false);
            _root.SetActive(false);
        }

        /// <summary>The Dungeon/Tower Exit verb — routes to the mode-appropriate abandon (the way home is
        /// always the same spot, so ONE button; the label + target switch on the live kind).</summary>
        private void OnExit()
        {
            if (_view.CurrentKind == EncounterKind.Tower) _view.NavExitTower();
            else _view.NavExitCrypt();
        }

        // A top-centre horizontal cluster at a fixed top offset: HorizontalLayoutGroup (non-expand,
        // explicit LayoutElement sizes per the 10.3 layout-kit lessons) + a ContentSizeFitter, so it
        // re-centres as buttons hide (the NavBar cluster idiom, mirrored top instead of corner).
        private static RectTransform Cluster(RectTransform parent, float topOffset)
        {
            var go = new GameObject("Cluster", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topOffset);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.HudGap;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        // A button pinned to a fixed size (the HLG sizes children by LayoutElement, as in NavBar).
        private static Button MakeButton(RectTransform cluster, string label, float w, Action onClick, int font)
        {
            var btn = UiKit.TextButton(cluster, label, new Vector2(w, BtnH), Vector2.zero, onClick, font);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = BtnH;
            le.flexibleWidth = le.flexibleHeight = 0f;
            return btn;
        }

        // The stage readout (a Text, not a button) sized to a fixed width so ◀/▶ sit at stable spots.
        private static Text MakeLabel(RectTransform cluster, string text, float w, int font)
        {
            var lbl = UiKit.Label(cluster, text, font, TextAnchor.MiddleCenter, new Vector2(w, BtnH), Vector2.zero);
            var le = lbl.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = BtnH;
            le.flexibleWidth = le.flexibleHeight = 0f;
            return lbl;
        }

        private static void SetA(GameObject go, ref bool last, bool want)
        {
            if (last == want) return;
            last = want;
            go.SetActive(want);
        }

        // Change-only HUD poll (the NavBar.Update idiom): re-interpolate a label / flip a SetActive ONLY
        // when its source moves, so the steady-state strip allocates ~nothing.
        private void Update()
        {
            // Guard the boot frames where the view exists but its save doesn't (the IMGUI drew behind
            // OnGUI's _combat==null early-return; this Update has no such umbrella).
            if (_view == null || _view.CurrentSave == null) return;

            bool vis = _view.TopControlsVisible;
            if (_lastVisible != vis) { _lastVisible = vis; _root.SetActive(vis); }
            if (!vis) return; // vis implies combat is non-null + Running, so CurrentKind below is safe

            var save = _view.CurrentSave;
            var kind = _view.CurrentKind;
            bool farm = kind == EncounterKind.Farm;
            bool alt = kind == EncounterKind.Dungeon || kind == EncounterKind.Tower;

            int cur = save.Progress.CurrentStage;
            int max = Progression.MaxSelectableStage(save, _cfg); // the ONE selection rule (endless-aware)
            bool frontier = cur == _cfg.Stages.Count;

            // Stage-nav cluster (farm only). ◀ only past stage 1; ▶ only below the max; at the campaign
            // frontier the ▶ becomes the wide "Push beyond…" affordance (same GoToStage underneath).
            SetA(_navCluster, ref _aNav, farm);
            SetA(_backBtn, ref _aBack, farm && cur > 1);
            SetA(_fwdBtn, ref _aFwd, farm && cur < max && !frontier);
            SetA(_pushBtn, ref _aPush, farm && cur < max && frontier);
            if (farm && _lastStage != cur)
            {
                _lastStage = cur;
                _stageLabel.text = cur > _cfg.Stages.Count ? $"Endless {cur - _cfg.Stages.Count}" : $"Stage {cur}";
            }

            // Primary-verb cluster: Challenge (farm) / Flee (boss) / Exit + speed (alt).
            SetA(_challengeBtn, ref _aChal, farm);
            SetA(_fleeBtn, ref _aFlee, kind == EncounterKind.BossChallenge);
            SetA(_exitBtn, ref _aExit, alt);
            SetA(_speedBtn, ref _aSpeed, alt);
            if (farm)
            {
                int major = _cfg.StageFor(cur)?.IsMajorBoss == true ? 1 : 0;
                if (_lastMajor != major)
                {
                    _lastMajor = major;
                    _challengeText.text = major == 1 ? "Challenge ★ Major Boss" : "Challenge Miniboss";
                }
            }
            if (alt)
            {
                int floor = kind == EncounterKind.Tower ? _view.CurrentTowerFloor : -1;
                if (_lastExitKind != (int)kind || _lastExitFloor != floor)
                {
                    _lastExitKind = (int)kind; _lastExitFloor = floor;
                    _exitText.text = kind == EncounterKind.Dungeon ? "Exit Crypt" : $"Exit Tower — Floor {floor}";
                }
                int sp = _view.AltSpeed2x ? 1 : 0;
                if (_lastSpeed != sp) { _lastSpeed = sp; _speedText.text = sp == 1 ? "2x" : "1x"; }
            }

            // Auto-advance (shelved OFF — the gate keeps it hidden today; the armed tint is preserved).
            // Only the cluster toggles — its single button stays active inside it.
            bool autoAvail = _view.AutoAdvanceAvailable;
            SetA(_autoCluster, ref _aAuto, autoAvail);
            if (autoAvail)
            {
                int armed = _view.AutoAdvanceArmed ? 1 : 0;
                if (_lastArmed != armed)
                {
                    _lastArmed = armed;
                    _autoText.text = armed == 1 ? "■ Stop Auto-Advance" : "▶ Auto-Advance";
                    _autoText.color = armed == 1 ? AutoArmed : Color.white;
                }
            }
        }
    }
}
