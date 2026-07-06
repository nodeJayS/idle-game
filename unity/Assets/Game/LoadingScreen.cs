#nullable enable
using System;
using UnityEngine;

namespace IdleGame.Game
{
    /// <summary>
    /// The LOAD boundary between game modes (user call 2026-07-06): switching to/from the Crypt or
    /// the Tower must FEEL like loading into a different map, not the same world sliding over. A
    /// fullscreen black IMGUI overlay fades in, the world swap runs at full black (build/teardown +
    /// camera SNAP — nothing of the transition is ever visible), holds a beat with an animated
    /// ellipsis (the "dummy load"), and fades out onto the new map.
    ///
    /// While <see cref="Busy"/> is true, CombatView pauses the sim — so monsters on the destination
    /// map take their first step only when the player can see them (no converging under the shroud).
    /// The swap itself is synchronous and cheap (a dungeon generates in ~5ms); the dwell is pure
    /// presentation. IMGUI because it must paint over EVERYTHING (HUD + uGUI panels), and OnGUI at a
    /// strongly negative GUI.depth does exactly that with zero canvas plumbing.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        private const float FadeInSec = 0.25f;
        private const float HoldSec = 0.9f;
        private const float FadeOutSec = 0.35f;

        private static LoadingScreen? _inst;

        /// <summary>True from Run() until the fade-out completes — the sim-pause window.</summary>
        public static bool Busy => _inst != null && _inst._phase != Phase.Idle;

        private enum Phase { Idle, FadeIn, Hold, FadeOut }
        private Phase _phase = Phase.Idle;
        private float _t;
        private string _title = "";
        private Action? _atBlack;

        private GUIStyle? _titleStyle;
        private Texture2D? _black;

        /// <summary>Fade to black, run <paramref name="atBlack"/> (the world swap + camera snap) at
        /// full cover, hold the dummy-load beat, fade back out. One transition at a time: if a load
        /// is already in flight the call is ignored (the UI disables its buttons via Busy anyway).</summary>
        public static void Run(string title, Action atBlack)
        {
            if (Busy) return;
            if (_inst == null)
            {
                var go = new GameObject("LoadingScreen");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<LoadingScreen>();
            }
            _inst._title = title;
            _inst._atBlack = atBlack;
            _inst._phase = Phase.FadeIn;
            _inst._t = 0f;
        }

        private void Update()
        {
            if (_phase == Phase.Idle) return;
            _t += Time.unscaledDeltaTime;

            switch (_phase)
            {
                case Phase.FadeIn when _t >= FadeInSec:
                    // Full black: do the actual swap now — nothing of it is visible.
                    try { _atBlack?.Invoke(); }
                    finally { _atBlack = null; }
                    _phase = Phase.Hold;
                    _t = 0f;
                    break;
                case Phase.Hold when _t >= HoldSec:
                    _phase = Phase.FadeOut;
                    _t = 0f;
                    break;
                case Phase.FadeOut when _t >= FadeOutSec:
                    _phase = Phase.Idle;
                    break;
            }
        }

        private void OnGUI()
        {
            if (_phase == Phase.Idle) return;
            GUI.depth = -1000; // in front of every other OnGUI (CombatView HUD draws at default 0)

            float a = _phase switch
            {
                Phase.FadeIn => Mathf.Clamp01(_t / FadeInSec),
                Phase.Hold => 1f,
                _ => 1f - Mathf.Clamp01(_t / FadeOutSec),
            };

            if (_black == null)
            {
                _black = new Texture2D(1, 1);
                _black.SetPixel(0, 0, Color.white);
                _black.Apply();
            }
            var prev = GUI.color;
            GUI.color = new Color(0.02f, 0.02f, 0.035f, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _black);
            GUI.color = prev;

            // Title + animated ellipsis, only while meaningfully covered.
            if (a > 0.6f)
            {
                _titleStyle ??= new GUIStyle { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _titleStyle.font = UiKit.Font;
                _titleStyle.normal.textColor = new Color(0.88f, 0.85f, 1f, Mathf.InverseLerp(0.6f, 1f, a));
                int dots = 1 + (int)(Time.unscaledTime * 2.5f) % 3;
                GUI.Label(new Rect(0, Screen.height / 2f - 30, Screen.width, 60),
                          _title + new string('.', dots), _titleStyle);
            }
        }
    }
}
