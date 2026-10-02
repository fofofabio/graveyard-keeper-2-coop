using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Coop
{
    /// <summary>
    /// A "Co-op" entry in the game's pause menu, for what the keyboard does with F6, F7, F10 and
    /// F11: a small window with invite friends, change your look, and the status and name tags on
    /// or off. It is how a controller reaches those; the entry is a copy
    /// of the pause menu's own Settings button, wired to the game's controller navigation as the
    /// pause menu wires its own.
    /// </summary>
    internal static class CoopPauseEntry
    {
        private static ManualLogSource log;
        private static GameObject entry;
        private static Component pauseWindow;
        private static bool entryClicked;
        private static bool open;
        private static bool quickPage;
        private static bool testPage;
        private static float nextLook;
        private static readonly GameUiPanel panel = new GameUiPanel("GK2Coop.InGame") { PadNavigation = true };

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static bool IsOpen => open;

        /// <summary>From the plugin's Update.</summary>
        internal static void Update(bool inSession)
        {
            if (!GameUi.Ready)
            {
                return;
            }
            if (entry == null && Time.unscaledTime >= nextLook)
            {
                nextLook = Time.unscaledTime + 2f;
                TryAddEntry();
            }
            if (entry != null && entry.activeSelf != inSession)
            {
                // Only in a co-op session.
                entry.SetActive(inSession);
            }
            if (entryClicked)
            {
                entryClicked = false;
                open = true;
                try
                {
                    (pauseWindow as UIGamePauseWindow)?.Close();
                }
                catch (Exception ex)
                {
                    log.LogWarning("Pause menu: could not close it: " + ex.Message);
                }
            }
            if (!inSession)
            {
                open = false;
            }
            if (open)
            {
                Draw();
            }
            else
            {
                panel.Hide();
            }
        }

        private static void TryAddEntry()
        {
            try
            {
                pauseWindow = null;
                foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(typeof(UIGamePauseWindow)))
                {
                    var component = found as Component;
                    if (component != null && component.gameObject.scene.IsValid())
                    {
                        pauseWindow = component;
                        break;
                    }
                }
                var settings = pauseWindow == null ? null : AccessTools.Field(typeof(UIGamePauseWindow), "settingsBtn").GetValue(pauseWindow) as Component;
                if (settings == null)
                {
                    return;
                }
                entry = UnityEngine.Object.Instantiate(settings.gameObject, settings.transform.parent, false);
                entry.name = "GK2CoopPauseButton";
                entry.transform.SetSiblingIndex(settings.transform.GetSiblingIndex() + 1);
                foreach (MonoBehaviour behaviour in entry.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    string typeName = behaviour == null ? string.Empty : behaviour.GetType().Name;
                    if (typeName.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("Translat", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }
                var button = entry.GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => entryClicked = true);
                (button as LazyButton)?.SetCallbacksIntoGamepadNavigationItem();
                TextMeshProUGUI label = entry.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.text = L.T("Co-op");
                }
                GameUi.RefreshTextTransitions(button);
                log.LogInfo("Co-op entry added to the pause menu.");
            }
            catch (Exception ex)
            {
                entry = null;
                log.LogWarning("Pause menu: no co-op entry: " + ex.Message);
                nextLook = Time.unscaledTime + 30f;
            }
        }

        /// <summary>Keeps the label in the current language (the game restyles on a switch).</summary>
        internal static void RefreshLabel()
        {
            TextMeshProUGUI label = entry == null ? null : entry.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && label.text != L.T("Co-op"))
            {
                label.text = L.T("Co-op");
            }
        }

        private static void Draw()
        {
            string title = testPage ? CoopTestTools.Title : quickPage ? L.T("Quick message") : L.T("Co-op");
            if (!panel.Begin(title, new Vector2(0f, 0f), 250f))
            {
                return;
            }
            RectTransform window = panel.Window;
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = Vector2.zero;
            RefreshLabel();
            if (testPage)
            {
                CoopTestTools.Draw(panel, out bool closeForTest, out bool backFromTest);
                panel.End();
                if (closeForTest || panel.CloseClicked())
                {
                    testPage = false;
                    open = false;
                }
                if (backFromTest || panel.BackPressed())
                {
                    testPage = false;
                }
                return;
            }
            if (quickPage)
            {
                // Lines to send without typing, for a controller.
                for (int i = 0; i < CoopChat.QuickLines.Length; i++)
                {
                    if (panel.Button(L.T(CoopChat.QuickLines[i])))
                    {
                        CoopChat.SendQuick(i);
                        quickPage = false;
                        open = false;
                    }
                }
                if (panel.Button(L.T("Back")))
                {
                    quickPage = false;
                }
                panel.End();
                if (panel.CloseClicked())
                {
                    quickPage = false;
                    open = false;
                }
                if (panel.BackPressed())
                {
                    quickPage = false;
                }
                return;
            }
            if (CoopSceneShare.CanWatchLate(out string sceneOf) && panel.Button(L.F("Watch {0}'s scene", sceneOf)))
            {
                open = false;
                CoopSceneShare.WatchLate();
            }
            if (panel.Button(L.T("Quick message")))
            {
                quickPage = true;
            }
            if (panel.Button(L.T("Write a message")))
            {
                open = false;
                CoopChat.WriteWithController();
            }
            if (CoopSteamLobby.CanInvite && panel.Button(L.T("Invite friends")))
            {
                CoopSteamLobby.OpenInviteDialog("Pause > Co-op");
            }
            if (panel.Button(L.T("Change your look")))
            {
                open = false;
                CoopAppearanceSync.OpenCustomizationWindow();
            }
            if (panel.Button(CoopHud.ShowNameTags ? L.T("Hide name tags") : L.T("Show name tags")))
            {
                CoopHud.ShowNameTags = !CoopHud.ShowNameTags;
            }
            if (panel.Button(CoopHud.ShowStatus ? L.T("Hide the status") : L.T("Show the status")))
            {
                CoopHud.ShowStatus = !CoopHud.ShowStatus;
            }
            if (CoopTestTools.Enabled && panel.Button("Test tools"))
            {
                CoopTestTools.Reset();
                testPage = true;
            }
            if (panel.Button(L.T("Close")))
            {
                open = false;
            }
            panel.End();
            if (panel.CloseClicked() || panel.BackPressed())
            {
                open = false;
            }
        }

        // ---------------------------------------------------------------- tests

        internal static string Describe()
        {
            return "entry=" + (entry == null ? "none" : entry.activeSelf + ":" + entry.GetComponentInChildren<TextMeshProUGUI>(true)?.text) +
                   " open=" + open + " panel=" + (panel.IsBuilt && panel.Window.gameObject.activeSelf ? panel.Describe() : "hidden") +
                   " pad=" + panel.DescribePad();
        }

        /// <summary>Tests: "click" the pause menu's Co-op entry, or "choose" an entry of the co-op window.</summary>
        internal static string ClickForTest(string what, string text)
        {
            if (what == "click")
            {
                entryClicked = true;
                return "entry clicked";
            }
            Button button = panel.FindButton(text);
            if (button == null)
            {
                return "no " + text;
            }
            GameUi.ClickForTest(button);
            return "chose " + text;
        }

        internal static string OpenPauseForTest()
        {
            if (pauseWindow == null)
            {
                return "no pause window";
            }
            (pauseWindow as UIGamePauseWindow)?.Open(null);
            return "pause opened";
        }
    }
}
