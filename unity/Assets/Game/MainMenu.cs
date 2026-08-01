#nullable enable
using System;
using UnityEngine;
using IdleGame.GameCore;

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
            // PanelKit.Modal (not Window): the boot menu has no Close verb and the caller owns
            // dismissal (CloseAnd tears down the whole MainMenu GO), and — unlike Window — Modal
            // stays SILENT, so the game doesn't play a popup-open chime the instant it boots. The
            // opaque backdrop is the historical boot-screen colour (kept literal, pixel-faithful).
            PanelKit.Modal(transform, "MainMenuCanvas", sortOrder: 100, new Vector2(520f, 540f),
                           out var body, backdrop: new Color(0.05f, 0.04f, 0.07f, 1f));

            var title = PanelKit.Label(body, Loc.T("menu.title"), 40, Theme.TextBright, TextAnchor.MiddleCenter);
            PanelKit.Fixed(title.gameObject, height: 64f);

            PanelKit.Flex(body); // slack above the button block — centres it between title and hint

            // The front door: generous 76-tall buttons (well over the 48 primary-verb floor), FsH1 labels.
            // Continue is interactable only with a save on disk (identical to the old gate) and greys out
            // otherwise via ButtonCell's disabled treatment.
            PanelKit.ButtonCell(PanelKit.Row(body, 76f), Loc.T("menu.continue"),
                () => CloseAnd(OnContinue), fontSize: Theme.FsH1, enabled: HasSave);
            PanelKit.ButtonCell(PanelKit.Row(body, 76f), Loc.T("menu.new-game"), NewGameClicked, fontSize: Theme.FsH1);
            PanelKit.ButtonCell(PanelKit.Row(body, 76f), Loc.T("common.exit-game"), Quit, fontSize: Theme.FsH1);

            PanelKit.Flex(body); // slack below the block — the hint pins to the panel bottom

            var hint = PanelKit.Label(body, HasSave ? "" : Loc.T("menu.no-save"),
                                      Theme.FsBody, Theme.TextMuted, TextAnchor.MiddleCenter);
            PanelKit.Fixed(hint.gameObject, height: 26f);
        }

        private void NewGameClicked()
        {
            if (HasSave) ShowOverwriteConfirm();
            else CloseAnd(OnNewGame);
        }

        private void ShowOverwriteConfirm()
        {
            // A second Modal above the menu (sortOrder 110). Cancel destroys just this dialog's canvas
            // (the return value); Overwrite runs CloseAnd(OnNewGame), tearing down the whole menu.
            var canvas = PanelKit.Modal(transform, "ConfirmCanvas", sortOrder: 110, new Vector2(520f, 260f),
                                        out var body, backdrop: Theme.BackdropDim);

            var prompt = PanelKit.Label(body, Loc.T("menu.overwrite-prompt"), Theme.FsH1,
                                        Theme.TextBright, TextAnchor.MiddleCenter);
            PanelKit.Fixed(prompt.gameObject, height: 40f);

            PanelKit.Flex(body);

            var row = PanelKit.Row(body, 70f);
            PanelKit.ButtonCell(row, Loc.T("menu.overwrite"), () => CloseAnd(OnNewGame), fontSize: Theme.FsH1);
            PanelKit.ButtonCell(row, Loc.T("common.cancel"), () => Destroy(canvas), fontSize: Theme.FsH1);
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
