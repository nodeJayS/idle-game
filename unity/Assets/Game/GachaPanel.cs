#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Hero summon screen (roadmap 3 — the gem SINK). Lists each configured banner: its name, gem cost per
    /// roll, the player's gem balance, the featured hero, pity progress ("X / PityCount to guarantee
    /// &lt;featured&gt;"), and a Roll button (greyed when <see cref="CombatView.CanRollGacha"/> is false). A
    /// roll fires the reveal beat — a colored flash + big hero name inside the panel (gold for the featured
    /// hero, cooler otherwise), NEW! / +XP+scrap / Pity lines — that settles into a click-anywhere-to-clear
    /// state. Built on <see cref="PanelKit"/>/<see cref="Theme"/>: a toggled overlay (build on open, destroy
    /// on close) like the Tower/Modifiers panels, opened from the control bar. Read-only over the save except
    /// a roll, which routes through the view. There is NO live content until slice 3 seeds a banner, so the
    /// control-bar entry hides the panel until then.
    /// </summary>
    public sealed class GachaPanel : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private Canvas? _canvas;
        private Image? _panel;   // the panel body — the reveal overlay stretches to THIS, not the canvas
        private bool _revealing; // a reveal overlay is up (blocks re-rolling until it's cleared)

        public bool IsOpen => _canvas != null;

        public void Bind(CombatView view, GameConfig cfg) { _view = view; _cfg = cfg; }

        public void Toggle() { if (IsOpen) Close(); else Build(); }

        public void Close()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _revealing = false;
        }

        private void Rebuild() { Close(); Build(); }

        // ---- build ----

        private const float RowH = 132f;
        private const float WalletH = 22f;
        private const float RollH = 54f;

        private void Build()
        {
            int banners = 0;
            foreach (var kv in _cfg.Banners) if (kv.Value.Pool.Count > 0) banners++;
            // Content-sized height: panel padding + header row + wallet line + the banner rows,
            // plus one Gap per layout seam (n rows + wallet + header + trailing Flex = n+2 seams).
            int n = Mathf.Max(1, banners);
            float h = Mathf.Min(Theme.PadL * 2f + Theme.RowH + WalletH + n * RowH + Theme.Gap * (n + 2), 620f);

            // Backdrop: Summon is a MANAGEMENT screen, not a transient toast, so it covers the view
            // exactly as PanelKit.Window does (10.21). It shipped backdrop-less, which left the world,
            // the Quest(83)/Chat(84)/NavBar(85) canvases and their buttons all visible AND clickable
            // behind it — a 560-wide dialog floating over the Quests panel reads as having no panel
            // at all. Theme.Backdrop is the same full-bleed cover every other window lays down.
            var canvasGo = PanelKit.Modal(transform, "GachaCanvas", 90, new Vector2(560f, h),
                                          out var body, backdrop: Theme.Backdrop, panelBg: Theme.GachaPanelBg);
            _canvas = canvasGo.GetComponent<Canvas>();
            _panel = body.GetComponent<Image>();

            // Header: title left, Close right. The row rides Theme.RowH (now the 44 touch floor, 10.13c),
            // so Close is 44 tall for free; widen it to 120 so it's a proper target, not a sliver.
            var header = PanelKit.Row(body, Theme.RowH);
            PanelKit.TextCell(header, "Summon", Theme.FsH1, Theme.GachaGold, TextAnchor.MiddleLeft, flex: 1f);
            PanelKit.ButtonCell(header, "Close", Close, width: 120f, fontSize: Theme.FsBody);

            long gems = _view.Gems;
            var wallet = PanelKit.Label(body, $"Gems: <color=#a6d8ff>{Num.CompactFloor(gems)}</color>",
                                        Theme.FsBody, Theme.TextBright, TextAnchor.MiddleLeft);
            wallet.supportRichText = true;
            PanelKit.Fixed(wallet.gameObject, height: WalletH); // pinned so the height formula above holds

            if (banners == 0)
            {
                PanelKit.Label(body, "No banners are live right now — check back soon.",
                               Theme.FsBody, Theme.TextMuted, TextAnchor.MiddleCenter);
                PanelKit.Flex(body);
                return;
            }

            foreach (var kv in _cfg.Banners)
            {
                if (kv.Value.Pool.Count == 0) continue;
                BuildBannerRow(body, kv.Value);
            }
            PanelKit.Flex(body); // keep rows top-aligned; any slack falls below them
        }

        /// <summary>One banner block: name + featured hero, cost/gems, pity progress, and a Roll button
        /// (greyed when unaffordable). Multiple banners stack vertically (there's exactly one in practice).</summary>
        private void BuildBannerRow(RectTransform body, GachaBannerDef banner)
        {
            string featured = _view.HeroDefDisplayName(banner.FeaturedHeroDefId);

            var row = PanelKit.Row(body, RowH);
            var col = PanelKit.VStack(row, Theme.GapXs);
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f; // text column takes the slack

            PanelKit.Label(col, banner.Name, Theme.FsH2, Theme.GachaBannerName, TextAnchor.MiddleLeft);

            var feat = PanelKit.Label(col, $"Featured: <color=#ffd766>{featured}</color>",
                                      Theme.FsLabel, Theme.TextBright, TextAnchor.MiddleLeft);
            feat.supportRichText = true;
            var cost = PanelKit.Label(col, $"Cost: <color=#a6d8ff>{Num.CompactCeil(banner.CostGems)} gems</color> per roll",
                                      Theme.FsLabel, Theme.TextBody, TextAnchor.MiddleLeft);
            cost.supportRichText = true;

            // Pity progress: rolls done vs the guarantee. Omitted entirely when the banner has no pity.
            if (banner.PityCount > 0)
            {
                int pity = _view.GachaPityOf(banner.Id);
                PanelKit.Label(col, $"{pity} / {banner.PityCount} rolls to guarantee {featured}",
                               Theme.FsSmall, Theme.TextMuted, TextAnchor.MiddleLeft);
            }

            // Roll (greyed + non-interactable when unaffordable / no pool). ButtonCell handles the
            // disabled color + click gate; override the enabled tint to warm gold.
            bool canRoll = _view.CanRollGacha(banner.Id);
            string bannerId = banner.Id;
            // The button sits in its own non-expanding slot: Row's childForceExpandHeight would
            // stretch a direct child to the full 132-unit banner row even over an explicit
            // flexibleHeight=0 (force-expand clamps child flex to ≥1), so a VStack — which does
            // NOT force-expand height — holds the button at its pre-kit 54 height, centered.
            var slot = PanelKit.VStack(row, 0f);
            slot.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var sle = slot.gameObject.AddComponent<LayoutElement>();
            sle.preferredWidth = 150f; sle.flexibleWidth = 0f;
            var roll = PanelKit.ButtonCell(slot, "Roll", () => Roll(bannerId),
                                           fontSize: Theme.FsH2, enabled: canRoll);
            if (canRoll) roll.GetComponent<Image>().color = Theme.BtnGold;
            PanelKit.Fixed(roll.gameObject, height: RollH);
        }

        // ---- roll + reveal beat ----

        /// <summary>Fire one roll through the view, then play the reveal beat before the panel settles. A
        /// reveal already up (or a no-op result) is a no-op — you can't spam-roll behind the overlay.</summary>
        private void Roll(string bannerId)
        {
            if (_revealing) return;
            var r = _view.RollGacha(bannerId);
            if (!r.Rolled) return; // couldn't afford / unknown banner — no reveal, no rebuild
            StartCoroutine(RevealThenRebuild(r));
        }

        /// <summary>The dopamine moment: a color flash (gold for the featured hero, cooler otherwise), the
        /// hero's name scaling up big, a NEW! tag / "+XP +scrap" dupe line / a Pity line, then it settles
        /// into a dismissable state — click anywhere in the panel to clear and rebuild with the new state.
        /// Colored uGUI flashes + a scale/fade coroutine in the CombatJuice spirit, no particle system.</summary>
        private IEnumerator RevealThenRebuild(Gacha.RollResult r)
        {
            _revealing = true;
            if (_canvas == null || _panel == null) yield break;

            // Accent: gold-ish for the featured hero, a cooler blue-violet otherwise.
            Color accent = r.IsFeatured ? Theme.GachaGold : Theme.RevealCommon;

            // Full-PANEL flash backdrop (FullScreen stretches to its parent, and the parent is the
            // panel — a canvas parent would gold-wash the entire game view every roll). ignoreLayout
            // keeps the panel's VerticalLayoutGroup from grabbing the overlay as a row. Raycasts, so a
            // click anywhere on the panel dismisses once settled.
            var overlay = UiKit.FullScreen(_panel.transform, new Color(accent.r, accent.g, accent.b, 0f));
            overlay.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var flash = overlay.rectTransform;

            // Center the reveal block between two flex slacks; a pinned bottom slot holds the settle hint.
            var vlg = overlay.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset((int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL, (int)Theme.PadL);
            vlg.spacing = Theme.GapS;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            PanelKit.Flex(flash); // top slack

            var heroName = _view.HeroDefDisplayName(r.HeroDefId);
            var big = PanelKit.Label(flash, heroName, Theme.FsRevealName,
                                     Color.Lerp(accent, Color.white, 0.4f), TextAnchor.MiddleCenter);
            big.fontStyle = FontStyle.Bold;
            big.raycastTarget = false;

            // Tags: NEW! (join) or a +shards dupe line (10.17 — dupes now fuel the hero's star track),
            // plus a Pity line when it was forced.
            string sub = r.IsNew ? "NEW!"
                                 : $"Dupe   +{Num.CompactFloor(r.DupeShards)} shards";
            var tag = PanelKit.Label(flash, sub, Theme.FsH1,
                                     r.IsNew ? Theme.RevealNewTag : Theme.RevealSubTag, TextAnchor.MiddleCenter);
            tag.fontStyle = FontStyle.Bold;
            tag.raycastTarget = false;

            if (r.PityTriggered)
            {
                var pity = PanelKit.Label(flash, $"Pity! {heroName} is guaranteed.", Theme.FsH2,
                                          Theme.AccentGold, TextAnchor.MiddleCenter);
                pity.raycastTarget = false;
            }

            PanelKit.Flex(flash); // bottom slack pushes the hint slot to the panel bottom

            // Bottom hint slot, pre-built empty (reserves the space) so the reveal doesn't reflow when the
            // "click to continue" prompt appears at settle.
            var hintSlot = PanelKit.VStack(flash, 0f);
            PanelKit.Fixed(hintSlot.gameObject, height: 24f);

            // A fitting existing SFX — the level-up jingle carries the "you got something" beat.
            SoundFx.Play("CH_Levelup", 0.55f);

            // Flash in fast, ease out; name pops from small to full over the same window.
            const float inDur = 0.14f, outDur = 0.5f;
            float t = 0f;
            var nameRt = (RectTransform)big.transform;
            while (t < inDur)
            {
                if (_canvas == null || overlay == null) yield break; // panel closed mid-reveal
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / inDur);
                overlay.color = new Color(accent.r, accent.g, accent.b, 0.55f * k);
                nameRt.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.12f, k);
                yield return null;
            }
            t = 0f;
            while (t < outDur)
            {
                if (_canvas == null || overlay == null) yield break; // panel closed mid-reveal
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / outDur);
                overlay.color = new Color(accent.r, accent.g, accent.b, Mathf.Lerp(0.55f, 0.14f, k));
                nameRt.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, k);
                yield return null;
            }
            if (_canvas == null || overlay == null) yield break;
            overlay.color = new Color(accent.r, accent.g, accent.b, 0.14f);

            // Settled: prompt + click-anywhere-to-clear. The backdrop stays raycastable so any click
            // (routed via the ClickCatcher) rebuilds the panel to the post-roll state.
            var hint = PanelKit.Label(hintSlot, "click to continue", Theme.FsLabel,
                new Color(Theme.RevealSubTag.r, Theme.RevealSubTag.g, Theme.RevealSubTag.b, 0.85f), TextAnchor.MiddleCenter);
            hint.raycastTarget = false;

            var catcher = flash.gameObject.AddComponent<ClickCatcher>();
            catcher.OnClick = () => { _revealing = false; Rebuild(); };
        }
    }

    /// <summary>Click relay for the reveal overlay: any pointer-down anywhere on the flash dismisses it
    /// (click-anywhere-in-the-panel to clear). Added to the raycastable backdrop image.</summary>
    public sealed class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public System.Action? OnClick;
        public void OnPointerClick(PointerEventData e) => OnClick?.Invoke();
    }
}
