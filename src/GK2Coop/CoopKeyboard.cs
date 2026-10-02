using System;
using System.Text;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Typing with a controller where Steam offers no keyboard of its own (outside Big Picture and
    /// the Steam Deck): letters, digits and a few signs on the game's buttons, chosen with the pad
    /// and A. B takes back a character, and closes when there is none left. Enough for a name, an
    /// address or a short chat line in Latin letters.
    /// </summary>
    internal static class CoopKeyboard
    {
        private static readonly string[] Rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM.:" };
        private static readonly GameUiPanel panel = new GameUiPanel("GK2Coop.Keyboard") { PadNavigation = true, Modal = true };

        private static bool open;
        private static bool lower = true;
        private static string title = string.Empty;
        private static readonly StringBuilder text = new StringBuilder();
        private static int limit;
        private static Action<string> done;

        internal static bool IsOpen => open;

        internal static void Open(string heading, string initial, int maxLength, Action<string> onDone)
        {
            title = heading ?? string.Empty;
            text.Length = 0;
            text.Append(initial ?? string.Empty);
            limit = maxLength > 0 ? maxLength : 64;
            done = onDone;
            open = true;
        }

        /// <summary>From the plugin's Update, after the other windows (it is on top).</summary>
        internal static void Update()
        {
            if (!open)
            {
                panel.Hide();
                return;
            }
            if (!GameUi.Ready || !panel.Begin(string.IsNullOrEmpty(title) ? L.T("Type") : title, Vector2.zero, 330f))
            {
                return;
            }
            RectTransform window = panel.Window;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = new Vector2(0f, -20f);
            window.SetAsLastSibling();
            panel.Label(text + "_", GameUi.TextKind.Notice);
            foreach (string row in Rows)
            {
                panel.BeginRow();
                foreach (char key in row)
                {
                    string shown = lower ? char.ToLowerInvariant(key).ToString() : key.ToString();
                    if (panel.Button(shown, 44f))
                    {
                        Type(shown);
                    }
                }
                panel.EndRow();
            }
            panel.BeginRow();
            if (panel.Button(lower ? "Aa" : "aA", 60f))
            {
                lower = !lower;
            }
            if (panel.Button("-", 44f)) Type("-");
            if (panel.Button(L.T("Space"), 110f)) Type(" ");
            if (panel.Button(L.T("Delete"), 110f)) Delete();
            panel.EndRow();
            panel.BeginRow();
            if (panel.Button(L.T("OK"), 120f))
            {
                Finish(true);
            }
            if (panel.Button(L.T("Close"), 120f))
            {
                Finish(false);
            }
            panel.EndRow();
            panel.End();
            if (panel.CloseClicked())
            {
                Finish(false);
            }
            if (panel.BackPressed())
            {
                if (text.Length > 0) Delete();
                else Finish(false);
            }
        }

        private static void Type(string what)
        {
            if (text.Length + what.Length <= limit)
            {
                text.Append(what);
            }
        }

        private static void Delete()
        {
            if (text.Length > 0)
            {
                text.Length -= 1;
            }
        }

        private static void Finish(bool accept)
        {
            open = false;
            panel.Hide();
            Action<string> callback = done;
            done = null;
            if (accept)
            {
                callback?.Invoke(text.ToString());
            }
        }

        // ---------------------------------------------------------------- tests

        internal static string Describe()
        {
            return "open=" + open + " text=" + text + " pad=" + panel.DescribePad();
        }

        internal static string PressForTest(string what)
        {
            if (what == "clear") text.Length = 0;
            else if (what == "ok") Finish(true);
            else if (what == "close") Finish(false);
            return Describe();
        }
    }
}
