using UnityEngine;
using IdleGame.GameCore;

namespace IdleGame.Game
{
    /// <summary>
    /// Persists the live save periodically and on app pause/quit. Each write stamps
    /// the save's heartbeat via <see cref="Save.Touch"/> (LastClaimAt = now) so that
    /// time spent playing online isn't later counted as offline by idle accrual — the
    /// offline gap on next launch ≈ the time the app was actually closed.
    /// Read-only w.r.t. game rules: it only reads <see cref="CombatView.CurrentSave"/>
    /// and calls the pure GameCore reducer + the SaveStore host.
    /// </summary>
    public sealed class Autosave : MonoBehaviour
    {
        private const float IntervalSec = 30f;

        private CombatView _view = null!;
        private float _timer;

        public void Bind(CombatView view) => _view = view;

        private static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void Update()
        {
            if (_view == null) return;
            _timer += Time.deltaTime;
            if (_timer >= IntervalSec)
            {
                _timer = 0f;
                Flush();
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() => Flush();

        private void Flush()
        {
            if (_view == null || _view.CurrentSave == null) return;
            SaveStore.Save(Save.Touch(_view.CurrentSave, NowMs()));
        }
    }
}
