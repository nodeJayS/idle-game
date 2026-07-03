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
    /// state. A toggled overlay (build on open, destroy on close) like the Tower/Modifiers panels, opened
    /// from the control bar. Read-only over the save except a roll, which routes through the view. There is
    /// NO live content until slice 3 seeds a banner, so the control-bar entry hides the panel until then.
    /// </summary>
    public sealed class GachaPanel : MonoBehaviour
    {
        private CombatView _view = null!;
        private GameConfig _cfg = null!;
        private Canvas? _canvas;
        private Image? _panel;   // the panel backdrop — the reveal overlay stretches to THIS, not the canvas
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
        private const float HeaderH = 64f;

        private void Build()
        {
            _canvas = UiKit.CreateCanvas("GachaCanvas", transform, sortOrder: 90);

            int banners = 0;
            foreach (var kv in _cfg.Banners) if (kv.Value.Pool.Count > 0) banners++;
            float h = Mathf.Min(HeaderH + Mathf.Max(1, banners) * RowH + 16f, 620f);

            var panel = UiKit.Panel(_canvas.transform, new Vector2(560f, h), new Color(0.09f, 0.10f, 0.13f, 0.98f));
            _panel = panel;
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, 50f); // above the bottom control bar

            var title = UiKit.Label(panel.transform, "Summon", 22, TextAnchor.UpperLeft, new Vector2(360f, 30f), Vector2.zero);
            title.color = new Color(1f, 0.82f, 0.32f);
            AnchorTL(title, new Vector2(22f, -14f));

            long gems = _view.Gems;
            var wallet = UiKit.Label(panel.transform, $"Gems: <color=#a6d8ff>{Num.CompactFloor(gems)}</color>", 15,
                TextAnchor.UpperLeft, new Vector2(300f, 24f), Vector2.zero);
            wallet.color = new Color(0.85f, 0.89f, 0.95f); wallet.supportRichText = true;
            AnchorTL(wallet, new Vector2(22f, -44f));

            var close = UiKit.TextButton(panel.transform, "Close", new Vector2(84f, 30f), Vector2.zero, Close, 15);
            AnchorTR((RectTransform)close.transform, new Vector2(-14f, -14f));

            if (banners == 0)
            {
                var empty = UiKit.Label(panel.transform, "No banners are live right now — check back soon.", 15,
                    TextAnchor.MiddleCenter, new Vector2(500f, 40f), Vector2.zero);
                empty.color = new Color(0.70f, 0.74f, 0.80f);
                AnchorTL(empty, new Vector2(30f, -HeaderH - 10f));
                return;
            }

            float y = -HeaderH;
            foreach (var kv in _cfg.Banners)
            {
                if (kv.Value.Pool.Count == 0) continue;
                BuildBannerRow(panel.transform, kv.Value, gems, y);
                y -= RowH;
            }
        }

        /// <summary>One banner block: name + featured hero, cost/gems, pity progress, and a Roll button
        /// (greyed when unaffordable). Multiple banners stack vertically (there's exactly one in practice).</summary>
        private void BuildBannerRow(Transform parent, GachaBannerDef banner, long gems, float y)
        {
            string featured = _view.HeroDefDisplayName(banner.FeaturedHeroDefId);

            var name = UiKit.Label(parent, banner.Name, 18, TextAnchor.UpperLeft, new Vector2(360f, 24f), Vector2.zero);
            name.color = new Color(1f, 0.88f, 0.55f);
            AnchorTL(name, new Vector2(22f, y - 6f));

            var feat = UiKit.Label(parent, $"Featured: <color=#ffd766>{featured}</color>", 14,
                TextAnchor.UpperLeft, new Vector2(360f, 22f), Vector2.zero);
            feat.color = new Color(0.85f, 0.89f, 0.95f); feat.supportRichText = true;
            AnchorTL(feat, new Vector2(22f, y - 32f));

            var cost = UiKit.Label(parent, $"Cost: <color=#a6d8ff>{Num.CompactCeil(banner.CostGems)} gems</color> per roll", 14,
                TextAnchor.UpperLeft, new Vector2(360f, 22f), Vector2.zero);
            cost.color = new Color(0.80f, 0.84f, 0.90f); cost.supportRichText = true;
            AnchorTL(cost, new Vector2(22f, y - 56f));

            // Pity progress: rolls done vs the guarantee. Omitted entirely when the banner has no pity.
            if (banner.PityCount > 0)
            {
                int pity = _view.GachaPityOf(banner.Id);
                var pit = UiKit.Label(parent, $"{pity} / {banner.PityCount} rolls to guarantee {featured}", 13,
                    TextAnchor.UpperLeft, new Vector2(360f, 20f), Vector2.zero);
                pit.color = new Color(0.70f, 0.74f, 0.82f);
                AnchorTL(pit, new Vector2(22f, y - 80f));
            }

            // Roll (greyed + non-interactable when unaffordable / no pool), rightmost.
            bool canRoll = _view.CanRollGacha(banner.Id);
            string bannerId = banner.Id;
            var roll = UiKit.TextButton(parent, "Roll", new Vector2(150f, 54f), Vector2.zero,
                canRoll ? () => Roll(bannerId) : (System.Action)(() => { }), 18);
            roll.interactable = canRoll;
            var img = roll.GetComponent<Image>();
            if (img != null) img.color = canRoll ? new Color(0.42f, 0.34f, 0.18f) : new Color(0.22f, 0.22f, 0.26f);
            AnchorTR((RectTransform)roll.transform, new Vector2(-16f, y - 40f));
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
            Color accent = r.IsFeatured ? new Color(1f, 0.82f, 0.30f) : new Color(0.55f, 0.62f, 0.95f);

            // Full-PANEL flash backdrop (FullScreen stretches to its parent, and the parent is the
            // panel — a canvas parent would gold-wash the entire game view every roll). Raycasts, so
            // a click anywhere on the panel dismisses once settled.
            var overlay = UiKit.FullScreen(_panel.transform, new Color(accent.r, accent.g, accent.b, 0f));
            var flash = overlay.rectTransform;

            var heroName = _view.HeroDefDisplayName(r.HeroDefId);
            var big = UiKit.Label(overlay.transform, heroName, 40, TextAnchor.MiddleCenter, new Vector2(520f, 80f), new Vector2(0f, 20f));
            big.color = Color.Lerp(accent, Color.white, 0.4f);
            big.fontStyle = FontStyle.Bold;
            big.raycastTarget = false;

            // Tags: NEW! (join) or a +XP(+scrap) dupe line, plus a Pity line when it was forced.
            string sub = r.IsNew ? "NEW!"
                : (r.DupeScrap != 0 ? $"Dupe   +{Num.CompactFloor(r.DupeXp)} XP   +{Num.CompactFloor(r.DupeScrap)} scrap"
                                    : $"Dupe   +{Num.CompactFloor(r.DupeXp)} XP");
            var tag = UiKit.Label(overlay.transform, sub, 22, TextAnchor.MiddleCenter, new Vector2(520f, 40f), new Vector2(0f, -40f));
            tag.color = r.IsNew ? new Color(1f, 0.92f, 0.5f) : new Color(0.82f, 0.86f, 0.94f);
            tag.fontStyle = FontStyle.Bold;
            tag.raycastTarget = false;

            if (r.PityTriggered)
            {
                var pity = UiKit.Label(overlay.transform, $"Pity! {heroName} is guaranteed.", 18,
                    TextAnchor.MiddleCenter, new Vector2(520f, 32f), new Vector2(0f, -78f));
                pity.color = new Color(1f, 0.85f, 0.4f);
                pity.raycastTarget = false;
            }

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
            var hint = UiKit.Label(overlay.transform, "click to continue", 14, TextAnchor.LowerCenter, new Vector2(520f, 24f), new Vector2(0f, 16f));
            hint.color = new Color(0.82f, 0.86f, 0.94f, 0.85f);
            hint.raycastTarget = false;

            var catcher = flash.gameObject.AddComponent<ClickCatcher>();
            catcher.OnClick = () => { _revealing = false; Rebuild(); };
        }

        // ---- anchor helpers (match TowerView / ModifierPanel) ----

        private static void AnchorTL(Component c, Vector2 pos)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
        }

        private static void AnchorTR(RectTransform rt, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = pos;
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
