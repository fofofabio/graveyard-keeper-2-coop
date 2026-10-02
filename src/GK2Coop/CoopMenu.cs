using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// The co-op setup panel on the main menu, so nobody has to edit a config file to play
    /// together. Drawn with the game's own window, buttons and fonts (<see cref="GameUi"/>), from a
    /// "Co-op" button added to the main menu's list; IMGUI remains as the fallback when the game's
    /// parts cannot be found, so a changed game never leaves the player without the menu.
    ///
    /// It deliberately does not start the game itself. The player picks host or join, and then
    /// presses New Game or Continue exactly as they always would — the normal menu and intro flow
    /// stays intact, which has been a requirement of this project from the start.
    ///
    /// Choices are written straight to the plugin's config, so they persist to the next session
    /// and remain editable by hand for anyone who prefers that.
    /// </summary>
    internal static class CoopMenu
    {
        /// <summary>What a page draws with: the game's own widgets, or IMGUI as a fallback.</summary>
        internal interface IMenuUi
        {
            /// <summary>True when the window has its own close cross, so pages need no Close button.</summary>
            bool HasOwnClose { get; }
            void Title(string text);
            void Label(string text, GameUi.TextKind kind = GameUi.TextKind.Body, float width = 0f);
            bool Button(string text, float width = 0f, bool selected = true);
            string Input(string value, int characterLimit);
            void Space(float pixels);
            void BeginRow();
            void EndRow();
        }

        private sealed class ImguiUi : IMenuUi
        {
            public bool HasOwnClose => false;
            public void Title(string text) { GUILayout.Label(L.T(text), titleStyle); }

            public void Label(string text, GameUi.TextKind kind = GameUi.TextKind.Body, float width = 0f)
            {
                GUIStyle style = kind == GameUi.TextKind.Hint ? hintStyle : kind == GameUi.TextKind.Notice ? noticeStyle : bodyStyle;
                text = L.T(text);
                if (width > 0f) GUILayout.Label(text, style, GUILayout.Width(width + 10f));
                else GUILayout.Label(text, style);
            }

            public bool Button(string text, float width = 0f, bool selected = true)
            {
                text = L.T(text);
                if (width > 0f)
                {
                    return GUILayout.Toggle(selected, text, "Button", GUILayout.Width(width + 4f)) != selected;
                }
                return GUILayout.Button(text, GUILayout.Height(30f));
            }

            public string Input(string value, int characterLimit) { return GUILayout.TextField(value ?? string.Empty, characterLimit); }
            public void Space(float pixels) { GUILayout.Space(pixels); }
            public void BeginRow() { GUILayout.BeginHorizontal(); }
            public void EndRow() { GUILayout.EndHorizontal(); }
        }

        private sealed class GameStyleUi : IMenuUi
        {
            internal GameUiPanel Panel;
            internal string Heading = string.Empty;
            public bool HasOwnClose => true;
            public void Title(string text) { Heading = L.T(text); }
            public void Label(string text, GameUi.TextKind kind = GameUi.TextKind.Body, float width = 0f) { Panel.Label(L.T(text), kind, width); }
            public bool Button(string text, float width = 0f, bool selected = true) { return Panel.Button(L.T(text), width, selected); }
            public string Input(string value, int characterLimit) { return Panel.Input(value, characterLimit); }
            // Page spacing is written in 1080p pixels; the game's UI units are twice that size.
            public void Space(float pixels) { Panel.Space(pixels * 0.5f); }
            public void BeginRow() { Panel.BeginRow(); }
            public void EndRow() { Panel.EndRow(); }
        }

        private static readonly ImguiUi imgui = new ImguiUi();
        private static readonly GameStyleUi gameUi = new GameStyleUi { Panel = new GameUiPanel("GK2Coop.Menu") { PadNavigation = true } };
        private static readonly GameUiPanel noticePanel = new GameUiPanel("GK2Coop.MenuNotice") { PadNavigation = true };

        /// <summary>Tests: what the controller has focused in the co-op window.</summary>
        internal static string DescribePadForTest()
        {
            return gameUi.Panel.DescribePad() + " owned=" + GameUiPanel.PadOwned;
        }
        private static bool usingGameUi;
        private static GameObject entryButton;
        private static UnityEngine.UI.Button entryClick;
        private static TMPro.TextMeshProUGUI entryLabel;
        private static string entryLanguage;
        private static bool entryClicked;
        private static bool reportedGameUi;
        private static Page shownPage = Page.Closed;

        /// <summary>
        /// From the plugin's Update: the menu in the game's own style — a "Co-op" button in the
        /// main menu's own list, and the co-op window framed like the game's dialogs. When the
        /// game's parts cannot be found the IMGUI panel in <see cref="Draw"/> takes over.
        /// </summary>
        internal static void Tick(Func<bool> onMainMenu)
        {
            if (!Enabled || startupMode == null)
            {
                return;
            }
            usingGameUi = GameUi.Ready;
            if (!usingGameUi)
            {
                return;
            }
            if (!reportedGameUi)
            {
                reportedGameUi = true;
                log.LogInfo("Co-op menu drawn in the game's own style.");
            }
            if (!onMainMenu())
            {
                page = Page.Closed;
                gameUi.Panel.Hide();
                noticePanel.Hide();
                return;
            }
            bool entryShown = EnsureEntryButton();
            if (entryLabel != null)
            {
                string wanted = CurrentModeLabel();
                if (entryLabel.text != wanted) entryLabel.text = wanted;
                // Japanese, Chinese and Korean need other fonts. The game's text styles pick them per
                // language; reapply the button's styles when the language changes, and if the font
                // still lacks a character, use a loaded font that has them all.
                string language = L.Language;
                if (language != entryLanguage)
                {
                    entryLanguage = language;
                    GameUi.RefreshTextTransitions(entryClick);
                }
                GameUi.EnsureGlyphs(entryLabel);
            }
            if (entryClicked)
            {
                entryClicked = false;
                page = page == Page.Closed ? Page.Root : Page.Closed;
                confirmation = string.Empty;
            }

            if (page == Page.Closed)
            {
                gameUi.Panel.Hide();
                bool noticeWanted = !string.IsNullOrEmpty(CoopWorkshopUpdater.Notice);
                if (!entryShown || noticeWanted)
                {
                    // Without the main menu's list (a changed menu), this small window opens it.
                    if (noticePanel.Begin(L.T("Co-op"), new Vector2(12f, 32f), 220f))
                    {
                        if (noticeWanted) noticePanel.Label(CoopWorkshopUpdater.Notice, GameUi.TextKind.Notice);
                        if (!entryShown && noticePanel.Button(CurrentModeLabel()))
                        {
                            page = Page.Root;
                            confirmation = string.Empty;
                        }
                        noticePanel.End();
                        noticePanel.CloseClicked();
                    }
                }
                else
                {
                    noticePanel.Hide();
                }
                return;
            }

            noticePanel.Hide();
            if (!gameUi.Panel.Begin(gameUi.Heading, new Vector2(12f, 32f), 270f))
            {
                return;
            }
            if (page != shownPage)
            {
                shownPage = page;
                gameUi.Panel.ScrollToTop();
            }
            switch (page)
            {
                case Page.Root: DrawRoot(gameUi); break;
                case Page.Host: DrawHost(gameUi); break;
                case Page.Join: DrawJoin(gameUi); break;
            }
            gameUi.Panel.SetTitle(gameUi.Heading);
            gameUi.Panel.End();
            if (gameUi.Panel.CloseClicked())
            {
                page = Page.Closed;
            }
            if (gameUi.Panel.BackPressed())
            {
                // B on a controller: a section back to the start, the start closes the window.
                page = page == Page.Root ? Page.Closed : Page.Root;
            }
        }

        /// <summary>
        /// A copy of the main menu's own "Load" button, placed under it, that opens the co-op
        /// window. The copy loses the original's click handlers and any translation component, so
        /// it keeps the text given here.
        /// </summary>
        private static bool EnsureEntryButton()
        {
            if (entryButton != null)
            {
                return true;
            }
            try
            {
                Type menuType = Plugin.FindGameType("UIMainMenuWindow");
                Component menu = null;
                foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(menuType))
                {
                    var component = found as Component;
                    if (component != null && component.gameObject.scene.IsValid())
                    {
                        menu = component;
                        break;
                    }
                }
                var load = menu == null ? null : menuType.GetField("loadGameButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(menu) as Component;
                if (load == null)
                {
                    return false;
                }
                entryButton = UnityEngine.Object.Instantiate(load.gameObject, load.transform.parent, false);
                entryButton.name = "GK2CoopButton";
                entryButton.transform.SetSiblingIndex(load.transform.GetSiblingIndex() + 1);
                foreach (MonoBehaviour behaviour in entryButton.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    string typeName = behaviour == null ? string.Empty : behaviour.GetType().Name;
                    if (typeName.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("Translat", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        UnityEngine.Object.DestroyImmediate(behaviour);
                    }
                }
                entryClick = entryButton.GetComponent<UnityEngine.UI.Button>();
                entryClick.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                entryClick.onClick.AddListener(() => entryClicked = true);
                // A controller's A reaches the button through its navigation item, which the copy
                // has but which is wired only for the original (as UIMainMenuWindow.Init does).
                (entryClick as LazyBearTechnology.LazyButton)?.SetCallbacksIntoGamepadNavigationItem();
                entryLabel = entryButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (entryLabel != null)
                {
                    entryLabel.text = CurrentModeLabel();
                }
                GameUi.RefreshTextTransitions(entryClick);
                log.LogInfo("Co-op button added to the main menu under " + load.name + ".");
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not add the Co-op button to the main menu: " + ex.Message);
                entryButton = null;
                return false;
            }
        }

        /// <summary>Tests: open a page, describe the window, or press a button in it.</summary>
        internal static string TestUi(string action, string argument)
        {
            switch (action)
            {
                case "open":
                    page = (Page)Enum.Parse(typeof(Page), argument, true);
                    return "page=" + page;
                case "click":
                    if (argument == "entry")
                    {
                        if (entryClick == null) return "no entry button";
                        GameUi.ClickForTest(entryClick);
                        return "clicked entry";
                    }
                    UnityEngine.UI.Button button = gameUi.Panel.FindButton(L.T(argument));
                    if (button == null) return "no button '" + argument + "'";
                    GameUi.ClickForTest(button);
                    return "clicked " + argument;
                default:
                    return "gameUi=" + usingGameUi + " page=" + page +
                           " entry=" + (entryButton == null ? "none" : entryButton.activeInHierarchy + ":" + (entryLabel == null ? "?" : entryLabel.text)) +
                           " window=" + (gameUi.Panel.IsBuilt && gameUi.Panel.Window.gameObject.activeSelf ? gameUi.Panel.Describe() : "hidden");
            }
        }


        private enum Page
        {
            Closed,
            Root,
            Host,
            Join
        }

        private const float PanelWidth = 430f;

        private static ManualLogSource log;
        private static ConfigEntry<string> startupMode;
        private static ConfigEntry<string> address;
        private static ConfigEntry<int> port;
        private static ConfigEntry<string> playerName;
        private static Action armStartupAction;

        private static Page page = Page.Closed;
        private static string addressField = string.Empty;
        private static string portField = string.Empty;
        private static string nameField = string.Empty;
        private static string confirmation = string.Empty;
        private static List<string> localAddresses;
        private static Vector2 scroll;
        private static bool reportedFirstDraw;

        private static GUIStyle panelStyle;
        private static GUIStyle titleStyle;
        private static GUIStyle bodyStyle;
        private static GUIStyle hintStyle;
        private static GUIStyle noticeStyle;
        private static bool followBootstrap;

        // Below the game's own top-left menu icons, which the button used to sit underneath.
        private const float PanelLeft = 24f;
        private const float PanelTop = 64f;
        private static Texture2D panelBackground;

        internal static bool Enabled { get; set; }

        /// <summary>[Network] Transport and MaxPlayers, set by the plugin.</summary>
        internal static ConfigEntry<string> TransportSetting { get; set; }
        internal static ConfigEntry<int> MaxPlayersSetting { get; set; }

        /// <summary>Network.NightPasses: "Everyone" or "Host" (<see cref="CoopSleepSync.HostNightRule"/>).</summary>
        internal static ConfigEntry<string> NightSetting { get; set; }

        private static void DrawNightSetting(IMenuUi ui)
        {
            if (NightSetting == null)
            {
                return;
            }
            bool hostSleep = string.Equals(NightSetting.Value, "Host", StringComparison.OrdinalIgnoreCase);
            ui.Space(6f);
            ui.Label("The night passes when", GameUi.TextKind.Body);
            ui.BeginRow();
            if (ui.Button("Everyone sleeps", 180f, !hostSleep) && hostSleep)
            {
                NightSetting.Value = "Everyone";
                hostSleep = false;
            }
            if (ui.Button("I sleep", 180f, hostSleep) && !hostSleep)
            {
                NightSetting.Value = "Host";
                hostSleep = true;
            }
            ui.EndRow();
            // The default says itself; the other choice needs a word on what the others see.
            if (hostSleep)
            {
                ui.Label("Your sleep passes the night for everyone. The others' clock jumps ahead with yours.", GameUi.TextKind.Hint);
            }
        }

        private static List<KeyValuePair<string, Steamworks.CSteamID>> friendsHosting = new List<KeyValuePair<string, Steamworks.CSteamID>>();
        private static float nextFriendsRefresh;

        private static void UseTransport(string mode)
        {
            if (TransportSetting != null)
            {
                TransportSetting.Value = mode;
            }
            CoopSteamTransportSwitch.UseSteam = mode == "Steam";
        }

        internal static void Init(ManualLogSource source, ConfigEntry<string> mode, ConfigEntry<string> hostAddress,
            ConfigEntry<int> hostPort, ConfigEntry<string> name, Action armStartup)
        {
            log = source;
            startupMode = mode;
            address = hostAddress;
            port = hostPort;
            playerName = name;
            armStartupAction = armStartup;
            addressField = hostAddress.Value;
            portField = hostPort.Value.ToString();
            nameField = name.Value;
        }

        /// <summary>
        /// Drawn only on the main menu. In game the status panel and toasts carry the session, and
        /// a setup form there would only invite changing settings that are already in use.
        /// </summary>
        internal static void Draw(bool onMainMenu)
        {
            if (!Enabled || startupMode == null)
            {
                return;
            }
            if (!onMainMenu)
            {
                page = Page.Closed;
                return;
            }
            if (usingGameUi)
            {
                // Drawn by Tick in the game's own style.
                return;
            }
            if (!reportedFirstDraw)
            {
                reportedFirstDraw = true;
                log.LogInfo("Co-op menu is on screen; current mode is " + startupMode.Value + ".");
            }
            EnsureStyles();

            // IMGUI sizes are in pixels, so on a 1440p or 4K screen the panel shrank to a corner.
            // Scale everything from a 1080p layout instead.
            float scale = UiScale;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                DrawScaled(Screen.height / scale);
            }
            finally
            {
                GUI.matrix = saved;
            }
        }

        /// <summary>1 at 1080p and below, growing with the screen height.</summary>
        internal static float UiScale => Mathf.Clamp(Screen.height / 1080f, 1f, 3f);

        private static void DrawScaled(float screenHeight)
        {
            if (page == Page.Closed)
            {
                if (GUI.Button(new Rect(PanelLeft, PanelTop, 170f, 34f), CurrentModeLabel()))
                {
                    page = Page.Root;
                    confirmation = string.Empty;
                }
                if (!string.IsNullOrEmpty(CoopWorkshopUpdater.Notice))
                {
                    GUI.Label(new Rect(PanelLeft, PanelTop + 40f, PanelWidth, 60f), CoopWorkshopUpdater.Notice, noticeStyle);
                }
                return;
            }

            var area = new Rect(PanelLeft, PanelTop, PanelWidth, Mathf.Min(620f, screenHeight - PanelTop - 24f));
            GUI.Box(area, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 12f, area.width - 28f, area.height - 24f));
            // Scrollbars appear only when the page is taller than the panel.
            scroll = GUILayout.BeginScrollView(scroll, false, false);

            switch (page)
            {
                case Page.Root: DrawRoot(imgui); break;
                case Page.Host: DrawHost(imgui); break;
                case Page.Join: DrawJoin(imgui); break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// The main menu button says only "Co-op": the saved mode is the last choice, not something
        /// happening now, and the window's first page says what the next game will do.
        /// </summary>
        private static string CurrentModeLabel()
        {
            return L.T("Co-op");
        }

        /// <summary>The invite key's name, for the text that mentions it (set by the plugin).</summary>
        internal static string InviteKeyName { get; set; } = "F10";

        // Sections a player opens on purpose: hosting or joining without Steam, and this PC's
        // addresses, which stay hidden until asked for (they are easy to leak on a stream).
        private static bool showAddressHosting;
        private static bool showAddressJoin;
        private static bool showAddresses;

        private static bool Steam => CoopSteamTransportSwitch.SteamAvailable();

        private static void DrawRoot(IMenuUi ui)
        {
            ui.Title("Co-op");
            ui.Label("Play in one world together. One player hosts, the others join.");
            ui.Space(10f);
            if (ui.Button("Host a game"))
            {
                page = Page.Host;
                localAddresses = null;
                showAddresses = false;
            }
            ui.Space(4f);
            if (ui.Button("Join a game"))
            {
                page = Page.Join;
            }

            string plan = PlannedSession();
            if (plan != null)
            {
                ui.Space(10f);
                ui.Label(plan);
                if (ui.Button("Cancel"))
                {
                    startupMode.Value = "None";
                    confirmation = L.T("Co-op is off. Your next game is single player.");
                }
            }
            if (!string.IsNullOrEmpty(confirmation))
            {
                ui.Space(6f);
                ui.Label(confirmation, GameUi.TextKind.Notice);
            }
            ui.Space(10f);
            ui.Label(L.F("Version {0}", Plugin.Version), GameUi.TextKind.Hint);
            if (!ui.HasOwnClose)
            {
                ui.Space(8f);
                if (ui.Button("Close"))
                {
                    page = Page.Closed;
                }
            }
        }

        /// <summary>What the next game will do, in a sentence, or null when co-op is off.</summary>
        private static string PlannedSession()
        {
            bool overSteam = CoopSteamTransportSwitch.UseSteam;
            if (string.Equals(startupMode.Value, "Host", StringComparison.OrdinalIgnoreCase))
            {
                return overSteam ? L.T("Your next game is hosted on Steam.") : L.F("Your next game is hosted on port {0}.", port.Value);
            }
            if (string.Equals(startupMode.Value, "Connect", StringComparison.OrdinalIgnoreCase))
            {
                return overSteam ? L.T("Your next game joins a Steam friend.") : L.F("Your next game joins {0}.", address.Value);
            }
            return null;
        }

        private static void DrawHost(IMenuUi ui)
        {
            ui.Title("Host a game");
            DrawNameField(ui);
            DrawNightSetting(ui);

            if (Steam)
            {
                ui.Space(6f);
                int players = MaxPlayersSetting != null ? Mathf.Clamp(MaxPlayersSetting.Value, 2, 4) : 4;
                ui.BeginRow();
                ui.Label("Players", GameUi.TextKind.Body, 120f);
                for (int option = 2; option <= 4; option++)
                {
                    if (ui.Button(option.ToString(), 40f, players == option) && players != option)
                    {
                        players = option;
                        if (MaxPlayersSetting != null) MaxPlayersSetting.Value = option;
                        CoopSteamLobby.MaxPlayers = option;
                    }
                }
                ui.EndRow();
                ui.Space(4f);
                if (ui.Button("Host on Steam"))
                {
                    UseTransport("Steam");
                    CoopSteamLobby.MaxPlayers = players;
                    Apply("Host", "0.0.0.0");
                    confirmation = L.F("Done. Load a game and your friends can join. Press {0} in game to invite them.", InviteKeyName);
                }
                ui.Label("Friends join from their Steam friends list or through an invite.", GameUi.TextKind.Hint);
                ui.Space(8f);
                if (ui.Button("Host without Steam", 0f, !showAddressHosting))
                {
                    showAddressHosting = !showAddressHosting;
                }
            }

            if (!Steam || showAddressHosting)
            {
                ui.Space(6f);
                ui.Label("Others connect to this PC. That works on the same network, over a VPN such as Tailscale, or over the internet if your router forwards the port.", GameUi.TextKind.Hint);
                ui.BeginRow();
                ui.Label("Port", GameUi.TextKind.Body, 120f);
                portField = ui.Input(portField, 5);
                ui.EndRow();
                if (ui.Button(showAddresses ? L.T("Hide my addresses") : L.T("Show my addresses")))
                {
                    showAddresses = !showAddresses;
                    localAddresses = null;
                }
                if (showAddresses)
                {
                    if (localAddresses == null)
                    {
                        localAddresses = CollectLocalAddresses();
                    }
                    foreach (string entry in localAddresses)
                    {
                        int split = entry.LastIndexOf(' ');
                        ui.BeginRow();
                        ui.Label(entry);
                        if (split > 0 && ui.Button("Copy", 56f))
                        {
                            GUIUtility.systemCopyBuffer = entry.Substring(split + 1).Trim();
                            confirmation = L.T("Address copied.");
                        }
                        ui.EndRow();
                    }
                }
                ui.Space(4f);
                if (ui.Button("Host"))
                {
                    UseTransport("IP");
                    Apply("Host", "0.0.0.0");
                    confirmation = L.T("Done. Load a game, then give the others your address.");
                }
            }
            DrawFooter(ui);
        }

        /// <summary>Addresses joined before ("host:port", newest first), kept in the config.</summary>
        internal static BepInEx.Configuration.ConfigEntry<string> RecentAddresses { get; set; }

        private static List<string> Recent()
        {
            var list = new List<string>();
            foreach (string part in (RecentAddresses?.Value ?? string.Empty).Split(','))
            {
                string entry = part.Trim();
                if (entry.Length > 0 && !list.Contains(entry)) list.Add(entry);
            }
            return list;
        }

        private static void Remember(string address, int portNumber)
        {
            if (RecentAddresses == null)
            {
                return;
            }
            string entry = address + ":" + portNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            List<string> list = Recent();
            list.Remove(entry);
            list.Insert(0, entry);
            if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
            RecentAddresses.Value = string.Join(",", list.ToArray());
        }

        private static void DrawJoin(IMenuUi ui)
        {
            ui.Title("Join a game");
            DrawNameField(ui);

            if (Steam)
            {
                if (Time.unscaledTime >= nextFriendsRefresh)
                {
                    nextFriendsRefresh = Time.unscaledTime + 3f;
                    try { friendsHosting = CoopSteamLobby.FriendsHosting(); } catch (Exception ex) { log.LogWarning("Could not read the Steam friends list: " + ex.Message); }
                }
                ui.Space(6f);
                ui.Label("Friends hosting now");
                if (friendsHosting.Count == 0)
                {
                    ui.Label("None right now. You can also accept an invite in Steam.", GameUi.TextKind.Hint);
                }
                foreach (KeyValuePair<string, Steamworks.CSteamID> friend in friendsHosting)
                {
                    ui.BeginRow();
                    ui.Label(friend.Key);
                    if (ui.Button("Join", 56f))
                    {
                        CoopSteamLobby.RequestJoin(friend.Value, "the co-op menu");
                        followBootstrap = true;
                    }
                    ui.EndRow();
                }
                ui.Space(8f);
                if (ui.Button("Join by address", 0f, !showAddressJoin))
                {
                    showAddressJoin = !showAddressJoin;
                }
            }

            if (!Steam || showAddressJoin)
            {
                ui.Space(6f);
                ui.Label("Address");
                addressField = ui.Input(addressField ?? string.Empty, 64);
                List<string> recent = Recent();
                if (recent.Count > 0)
                {
                    // Joined before: one press fills the address and port.
                    ui.Label("Recent", GameUi.TextKind.Hint);
                    foreach (string entry in recent)
                    {
                        int colon = entry.LastIndexOf(':');
                        string host = colon > 0 ? entry.Substring(0, colon) : entry;
                        string portText = colon > 0 ? entry.Substring(colon + 1) : portField;
                        if (ui.Button(entry))
                        {
                            addressField = host;
                            portField = portText;
                        }
                    }
                }
                ui.BeginRow();
                ui.Label("Port", GameUi.TextKind.Body, 120f);
                portField = ui.Input(portField, 5);
                ui.EndRow();
                ui.Space(4f);
                if (ui.Button("Join"))
                {
                    string wanted = (addressField ?? string.Empty).Trim();
                    if (wanted.Length == 0)
                    {
                        confirmation = L.T("Enter the host's address first.");
                    }
                    else
                    {
                        UseTransport(wanted.StartsWith(CoopSteamTransportSwitch.SteamAddressPrefix, StringComparison.OrdinalIgnoreCase) ? "Steam" : "IP");
                        Apply("Connect", wanted);
                        try
                        {
                            CoopSaveBootstrap.Begin(wanted, (ushort)port.Value);
                            confirmation = string.Empty;
                            followBootstrap = true;
                            Remember(wanted, port.Value);
                        }
                        catch (Exception ex)
                        {
                            confirmation = ex.GetBaseException().Message;
                            log.LogWarning("Could not start save bootstrap: " + confirmation);
                        }
                    }
                }
                ui.Label("You play in a copy of the host's world. Your own saves are not touched.", GameUi.TextKind.Hint);
                ui.Space(6f);
                // The old path, for two PCs that already have the same save; kept small.
                if (ui.Button("Join without copying"))
                {
                    string wanted = (addressField ?? string.Empty).Trim();
                    if (wanted.Length == 0)
                    {
                        confirmation = L.T("Enter the host's address first.");
                    }
                    else
                    {
                        Apply("Connect", wanted);
                        followBootstrap = false;
                        confirmation = L.F("Done. Load the same save as the host to join {0}.", wanted);
                    }
                }
                ui.Label("Only when you both have the same save.", GameUi.TextKind.Hint);
            }
            DrawFooter(ui);
        }

        private static void DrawNameField(IMenuUi ui)
        {
            if (string.IsNullOrEmpty(nameField) && Steam)
            {
                try { nameField = Steamworks.SteamFriends.GetPersonaName(); } catch (Exception) { }
            }
            ui.BeginRow();
            ui.Label("Your name", GameUi.TextKind.Body, 120f);
            nameField = ui.Input(nameField ?? string.Empty, 24);
            ui.EndRow();
        }

        private static void DrawFooter(IMenuUi ui)
        {
            if (followBootstrap && page == Page.Join)
            {
                // The transfer's own status, live, so the panel never claims it is still copying
                // after it has failed.
                ui.Space(8f);
                bool failed = CoopSaveBootstrap.HasFailed;
                string text = string.IsNullOrEmpty(CoopStatus.Detail) ? L.T("Copying the host's world…") : CoopStatus.Detail;
                ui.Label((failed ? L.T("Could not copy the host's world.") + " " : string.Empty) + text, failed ? GameUi.TextKind.Notice : GameUi.TextKind.Body);
            }
            else if (!string.IsNullOrEmpty(confirmation))
            {
                ui.Space(8f);
                ui.Label(confirmation, GameUi.TextKind.Notice);
            }
            ui.Space(8f);
            ui.BeginRow();
            if (ui.Button("Back"))
            {
                page = Page.Root;
                confirmation = string.Empty;
                followBootstrap = false;
            }
            if (!ui.HasOwnClose && ui.Button("Close"))
            {
                page = Page.Closed;
            }
            ui.EndRow();
        }

        /// <summary>
        /// "Copy host world and join" for a given address, as the button does — used by Steam
        /// invites and the friends list. Throws with a player-facing message when it cannot start.
        /// </summary>
        internal static void CopyAndJoin(string wanted)
        {
            addressField = wanted;
            Apply("Connect", wanted);
            CoopSaveBootstrap.Begin(wanted, (ushort)port.Value);
            confirmation = string.Empty;
            followBootstrap = true;
        }

        private static void Apply(string mode, string useAddress)
        {
            ushort parsedPort;
            if (ushort.TryParse(portField, out parsedPort) && parsedPort > 0)
            {
                port.Value = parsedPort;
            }
            else
            {
                portField = port.Value.ToString();
            }
            address.Value = useAddress;
            playerName.Value = (nameField ?? string.Empty).Trim();
            CoopSession.SetLocalName(playerName.Value);
            startupMode.Value = mode;

            // Lets the startup driver act on the new choice without restarting the game.
            if (armStartupAction != null)
            {
                armStartupAction();
            }
            log.LogInfo("Co-op menu set " + mode + " with address " + useAddress + ":" + port.Value + ".");
        }

        /// <summary>
        /// Every address this machine can be reached on, labelled so a player can tell which is
        /// which. Read from the local adapters only — nothing is sent anywhere, and the public
        /// address is deliberately not fetched, because that would mean contacting a third party
        /// without being asked.
        /// </summary>
        private static List<string> CollectLocalAddresses()
        {
            var found = new List<string>();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }
                        string text = ip.Address.ToString();
                        if (text.StartsWith("169.254", StringComparison.Ordinal))
                        {
                            // A self-assigned address means that adapter has no working network.
                            continue;
                        }
                        string label = Describe(adapter, text);
                        if (label != null)
                        {
                            found.Add(L.T(label) + "  " + text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read this machine's addresses: " + ex.Message);
            }
            if (found.Count == 0)
            {
                found.Add(L.T("No usable address found."));
            }
            return found;
        }

        /// <summary>A plain label for an address another player can use, or null for one they cannot.</summary>
        private static string Describe(NetworkInterface adapter, string ip)
        {
            string name = adapter.Name ?? string.Empty;
            string description = adapter.Description ?? string.Empty;
            if (ip.StartsWith("100.", StringComparison.Ordinal) ||
                name.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) >= 0 ||
                description.IndexOf("Tailscale", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Tailscale";
            }
            foreach (string hidden in new[] { "VPN", "WireGuard", "Wintun", "TAP-Windows", "NordLynx", "OpenVPN", "Hyper-V", "vEthernet", "VirtualBox", "VMware" })
            {
                // A privacy VPN or a virtual machine's adapter cannot be reached by other players.
                if (name.IndexOf(hidden, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    description.IndexOf(hidden, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return null;
                }
            }
            return "Home network";
        }

        private static void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }
            panelBackground = new Texture2D(1, 1);
            panelBackground.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            panelBackground.Apply();

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelBackground;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            titleStyle.normal.textColor = Color.white;

            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            bodyStyle.normal.textColor = new Color(0.92f, 0.92f, 0.94f);

            hintStyle = new GUIStyle(bodyStyle) { fontSize = 11, fontStyle = FontStyle.Italic };
            hintStyle.normal.textColor = new Color(0.72f, 0.74f, 0.78f);

            // Readable over any menu background: the closed menu has no panel behind it.
            noticeStyle = new GUIStyle(bodyStyle) { padding = new RectOffset(8, 8, 6, 6) };
            noticeStyle.normal.background = panelStyle.normal.background;
            noticeStyle.normal.textColor = new Color(1f, 0.86f, 0.55f);
        }
    }
}
