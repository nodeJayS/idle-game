#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace IdleGame.Game
{
    /// <summary>
    /// Title screen (uGUI, built in code): Continue / New Game. Read-only w.r.t. game
    /// rules — it just invokes the callbacks <see cref="Bootstrap"/> wires in, which
    /// own the load / new-game decisions. Continue is disabled when no save exists;
    /// New Game over an existing save asks to confirm before overwriting.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        public Action OnContinue = () => { };
        public Action OnNewGame = () => { };
        public bool HasSave;

        public void Open()
        {
            var canvas = UiKit.CreateCanvas("MainMenuCanvas", transform, sortOrder: 100);
            UiKit.FullScreen(canvas.transform, new Color(0.05f, 0.04f, 0.07f, 1f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(440, 440), new Color(0.10f, 0.10f, 0.14f, 1f));
            UiKit.Label(panel.transform, "IDLE ARPG", 36, TextAnchor.MiddleCenter, new Vector2(400, 60), new Vector2(0, 160));

            var cont = UiKit.TextButton(panel.transform, "Continue", new Vector2(300, 54), new Vector2(0, 70),
                () => { CloseAnd(OnContinue); });
            cont.interactable = HasSave;

            UiKit.TextButton(panel.transform, "New Game", new Vector2(300, 54), new Vector2(0, 4), NewGameClicked);

            UiKit.TextButton(panel.transform, "Exit Game", new Vector2(300, 54), new Vector2(0, -62), Quit);

            UiKit.Label(panel.transform, HasSave ? "" : "No save yet — start a new game",
                        14, TextAnchor.MiddleCenter, new Vector2(400, 24), new Vector2(0, -170));
        }

        private void NewGameClicked()
        {
            if (HasSave) ShowOverwriteConfirm();
            else CloseAnd(OnNewGame);
        }

        private void ShowOverwriteConfirm()
        {
            var canvas = UiKit.CreateCanvas("ConfirmCanvas", transform, sortOrder: 110);
            UiKit.FullScreen(canvas.transform, new Color(0f, 0f, 0f, 0.6f));

            var panel = UiKit.Panel(canvas.transform, new Vector2(420, 200), new Color(0.12f, 0.10f, 0.10f, 1f));
            UiKit.Label(panel.transform, "Overwrite your existing save?", 20, TextAnchor.MiddleCenter,
                        new Vector2(380, 40), new Vector2(0, 50));

            UiKit.TextButton(panel.transform, "Overwrite", new Vector2(160, 48), new Vector2(-90, -40),
                () => CloseAnd(OnNewGame));
            UiKit.TextButton(panel.transform, "Cancel", new Vector2(160, 48), new Vector2(90, -40),
                () => Destroy(canvas.gameObject));
        }

        private void Quit()
        {
            Debug.Log("[MainMenu] Exit requested.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false; // Application.Quit is a no-op in the editor
#else
            Application.Quit();
#endif
        }

        private void CloseAnd(Action next)
        {
            Destroy(gameObject); // tears down all menu canvases (children of this GO)
            next();
        }
    }
}
