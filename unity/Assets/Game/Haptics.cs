#nullable enable

namespace IdleGame.Game
{
    /// <summary>
    /// The haptic-feedback seam (10.20a). Three intensity verbs — <see cref="Tick"/> (light),
    /// <see cref="Impact"/> (medium), <see cref="Heavy"/> — each a no-op until the toggle is on.
    /// No call sites yet: the point of landing the seam now is that every FUTURE call is born gated
    /// behind <see cref="Settings.Haptics"/>, so nothing ever buzzes with the setting off.
    ///
    /// The bodies stay empty deliberately. The 10.22 handheld feel pass supplies the real
    /// per-platform vocabulary (the crit tick, the boss thump, the gacha reveal) over iOS
    /// UIFeedbackGenerator / Android Vibrator — mapping these three verbs to actual device motors is
    /// that slice's work, not this one.
    /// </summary>
    public static class Haptics
    {
        /// <summary>A light tap — the smallest confirm (button press, crit tick).</summary>
        public static void Tick()
        {
            if (!Settings.Haptics) return;
        }

        /// <summary>A medium bump — a landed hit, a claim.</summary>
        public static void Impact()
        {
            if (!Settings.Haptics) return;
        }

        /// <summary>A heavy thump — a boss down, a gacha reveal.</summary>
        public static void Heavy()
        {
            if (!Settings.Haptics) return;
        }
    }
}
