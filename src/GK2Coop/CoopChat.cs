using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Coop
{
    /// <summary>
    /// In-game chat.
    ///
    /// The chat key (T by default) opens a line at the bottom left; Enter sends, Escape closes.
    /// While it is open the game's own input is switched off (<c>LazyInput.SetInputActivity</c>), so
    /// typing does not walk, swing tools or open windows. Messages go to the host, which passes them
    /// on with the sender's name; each player sees the last few lines, fading after a while, and the
    /// full recent history while the line is open. Messages are plain text, at most 160 characters.
    /// </summary>
    internal static class CoopChat
    {
        internal const string ChatMessage = "GK2Coop.Chat.v1";

        private const int MaxLength = 160;
        private const int MaxHistory = 30;
        private const int VisibleLines = 6;
        private const float LineSeconds = 12f;
        private const string FieldName = "GK2CoopChatInput";

        private struct Line
        {
            internal string Name;
            internal string Text;
            internal float At;
            internal bool Own;
        }

        private static ManualLogSource log;
        private static readonly List<Line> history = new List<Line>();
        private static bool open;
        private static bool focusPending;
        private static string draft = string.Empty;
        private static GUIStyle lineStyle;
        private static GUIStyle shadowStyle;
        private static GUIStyle fieldStyle;
        private static Texture2D backdrop;

        internal static bool Enabled { get; set; } = true;
        internal static KeyCode Key { get; set; } = KeyCode.T;
        internal static bool IsOpen => open;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        private static bool InSession()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            return netcode != null && netcode.IsListening && (netcode.IsHost ? netcode.ConnectedClientsIds.Count > 1 : CoopSession.Welcomed);
        }

        /// <summary>
        /// The chat key is also one of the game's keys (T opens the tech tree). On the frame the
        /// chat opens, the game must not see it, whichever reads the key first.
        /// </summary>
        internal static bool Swallows(LazyBearTechnology.GameKey key)
        {
            if (!Enabled || open || (object)key == null || !Input.GetKeyDown(Key) || !InSession())
            {
                return false;
            }
            try
            {
                foreach (LazyBearTechnology.KeyBinding binding in LazyBearTechnology.LazyInput.GameBindings.keyBindings)
                {
                    if ((object)binding.gameKey != null && binding.gameKey.value == key.value && (binding.keyCode == Key || (binding.additionalKeyCodes != null && Array.IndexOf(binding.additionalKeyCodes, Key) >= 0)))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>From the plugin's Update: the key that opens the chat line.</summary>
        internal static void Update(Func<bool> onMainMenu)
        {
            if (!Enabled)
            {
                return;
            }
            if (open && (!InSession() || onMainMenu()))
            {
                Close();
                return;
            }
            if (!open && Input.GetKeyDown(Key) && InSession() && !onMainMenu())
            {
                open = true;
                focusPending = true;
                draft = string.Empty;
                SetGameInput(false);
                return;
            }
            if (open && gameUiActive)
            {
                HandleGameUiKeys();
            }
        }

        private static void Close()
        {
            open = false;
            draft = string.Empty;
            SetGameInput(true);
        }

        private static void SetGameInput(bool active)
        {
            try
            {
                Type input = Plugin.FindGameType("LazyBearTechnology.LazyInput");
                input?.GetMethod("SetInputActivity", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, new object[] { active });
            }
            catch (Exception ex)
            {
                log.LogWarning("Chat: could not " + (active ? "restore" : "pause") + " the game's input: " + ex.Message);
            }
        }

        /// <summary>From the plugin's OnGUI.</summary>
        internal static void Draw()
        {
            if (!Enabled || gameUiActive)
            {
                return;
            }
            EnsureStyles();
            float scale = CoopMenu.UiScale;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                DrawScaled(Screen.width / scale, Screen.height / scale);
            }
            finally
            {
                GUI.matrix = saved;
            }
        }

        private static void DrawScaled(float width, float height)
        {
            const float left = 16f;
            const float lineHeight = 22f;
            float bottom = height - 140f;
            float now = Time.unscaledTime;

            var shown = new List<Line>();
            for (int i = history.Count - 1; i >= 0 && shown.Count < (open ? 12 : VisibleLines); i--)
            {
                if (open || now - history[i].At < LineSeconds)
                {
                    shown.Insert(0, history[i]);
                }
            }
            float y = bottom - shown.Count * lineHeight;
            if (open && shown.Count > 0)
            {
                GUI.DrawTexture(new Rect(left - 6f, y - 4f, 520f, shown.Count * lineHeight + 8f), backdrop);
            }
            foreach (Line line in shown)
            {
                float fade = open ? 1f : Mathf.Clamp01((LineSeconds - (now - line.At)) / 2f);
                string text = line.Name + ": " + line.Text;
                Color colour = line.Own ? new Color(0.75f, 0.95f, 1f, fade) : new Color(1f, 0.92f, 0.7f, fade);
                shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f * fade);
                lineStyle.normal.textColor = colour;
                GUI.Label(new Rect(left + 1f, y + 1f, 700f, lineHeight), text, shadowStyle);
                GUI.Label(new Rect(left, y, 700f, lineHeight), text, lineStyle);
                y += lineHeight;
            }

            if (!open)
            {
                return;
            }
            Event current = Event.current;
            if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            {
                string text = (draft ?? string.Empty).Trim();
                if (text.Length > 0)
                {
                    Send(text);
                }
                current.Use();
                Close();
                return;
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                current.Use();
                Close();
                return;
            }
            GUI.DrawTexture(new Rect(left - 6f, bottom + 6f, 520f, 30f), backdrop);
            GUI.Label(new Rect(left, bottom + 10f, 60f, 24f), L.T("Say:"), lineStyle);
            GUI.SetNextControlName(FieldName);
            draft = GUI.TextField(new Rect(left + 44f, bottom + 9f, 460f, 24f), draft ?? string.Empty, MaxLength, fieldStyle);
            if (focusPending)
            {
                GUI.FocusControl(FieldName);
                focusPending = false;
            }
        }

        // ---------------------------------------------------------------- game-styled drawing

        private static bool gameUiActive;
        private static RectTransform chatRoot;
        private static readonly List<TMPro.TextMeshProUGUI> lineViews = new List<TMPro.TextMeshProUGUI>();
        private static RectTransform inputPlate;
        private static TMPro.TextMeshProUGUI sayLabel;
        private static TMPro.TMP_InputField inputField;

        /// <summary>
        /// From the plugin's LateUpdate: the chat lines in the game's font at the bottom left, and
        /// while typing, the game's own text field on a dark plate. Enter sends, Escape closes.
        /// </summary>
        internal static void UpdateGameUi()
        {
            gameUiActive = Enabled && GameUi.Ready;
            if (!gameUiActive)
            {
                return;
            }
            if (chatRoot == null)
            {
                chatRoot = GameUi.NewRect(GameUi.HudLayer, "GK2Coop.Chat");
                chatRoot.anchorMin = chatRoot.anchorMax = new Vector2(0f, 0f);
                chatRoot.pivot = new Vector2(0f, 0f);
                chatRoot.sizeDelta = new Vector2(300f, 200f);
                lineViews.Clear();
                inputPlate = null;
                inputField = null;
            }
            chatRoot.anchoredPosition = new Vector2(8f, 64f);

            const float lineHeight = 11f;
            float now = Time.unscaledTime;
            var shown = new List<Line>();
            for (int i = history.Count - 1; i >= 0 && shown.Count < (open ? 12 : VisibleLines); i--)
            {
                if (open || now - history[i].At < LineSeconds)
                {
                    shown.Insert(0, history[i]);
                }
            }
            float y = open ? 22f : 0f;
            for (int i = 0; i < Math.Max(shown.Count, lineViews.Count); i++)
            {
                if (i >= shown.Count)
                {
                    if (lineViews[i].gameObject.activeSelf) lineViews[i].gameObject.SetActive(false);
                    continue;
                }
                if (i == lineViews.Count)
                {
                    TMPro.TextMeshProUGUI created = GameUi.NewText(chatRoot, GameUi.TextKind.Body, string.Empty);
                    created.name = "ChatLine";
                    created.richText = false;
                    created.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                    RectTransform rect = created.rectTransform;
                    rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                    rect.pivot = new Vector2(0f, 0f);
                    rect.sizeDelta = new Vector2(300f, lineHeight);
                    lineViews.Add(created);
                }
                Line line = shown[i];
                TMPro.TextMeshProUGUI view = lineViews[i];
                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                // Reapplying the game's style after a language change resets the alignment.
                if (view.alignment != TMPro.TextAlignmentOptions.BottomLeft) view.alignment = TMPro.TextAlignmentOptions.BottomLeft;
                string text = line.Name + ": " + line.Text;
                if (view.text != text)
                {
                    view.text = text;
                    // Players type in any language, whatever the game is set to.
                    GameUi.EnsureGlyphs(view);
                }
                float fade = open ? 1f : Mathf.Clamp01((LineSeconds - (now - line.At)) / 2f);
                Color colour = line.Own ? new Color(0.75f, 0.95f, 1f, fade) : new Color(1f, 0.92f, 0.7f, fade);
                if (view.color != colour) view.color = colour;
                // Newest at the bottom, just above the text field while it is open.
                view.rectTransform.anchoredPosition = new Vector2(0f, y + (shown.Count - 1 - i) * lineHeight);
            }

            if (!open)
            {
                if (inputPlate != null && inputPlate.gameObject.activeSelf)
                {
                    if (inputField != null) inputField.DeactivateInputField();
                    inputPlate.gameObject.SetActive(false);
                }
                return;
            }
            if (inputPlate == null)
            {
                Image plate = GameUi.NewPlate(chatRoot, "ChatInput");
                inputPlate = plate.rectTransform;
                inputPlate.anchorMin = inputPlate.anchorMax = new Vector2(0f, 0f);
                inputPlate.pivot = new Vector2(0f, 0f);
                inputPlate.anchoredPosition = Vector2.zero;
                inputPlate.sizeDelta = new Vector2(260f, 20f);
                TMPro.TextMeshProUGUI say = GameUi.NewText(inputPlate, GameUi.TextKind.Hint, L.T("Say:"));
                sayLabel = say;
                say.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                float sayWidth = Mathf.Ceil(say.GetPreferredValues(say.text).x) + 2f;
                say.rectTransform.anchoredPosition = new Vector2(6f, -3f);
                say.rectTransform.sizeDelta = new Vector2(sayWidth, 14f);
                inputField = GameUi.NewInput(inputPlate, MaxLength);
                if (inputField != null)
                {
                    var rect = (RectTransform)inputField.transform;
                    rect.anchorMin = new Vector2(0f, 0f);
                    rect.anchorMax = new Vector2(1f, 1f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.offsetMin = new Vector2(sayWidth + 10f, 2f);
                    rect.offsetMax = new Vector2(-3f, -2f);
                }
            }
            if (!inputPlate.gameObject.activeSelf)
            {
                inputPlate.gameObject.SetActive(true);
            }
            if (sayLabel != null)
            {
                GameUi.SetKind(sayLabel, GameUi.TextKind.Hint);
            }
            if (sayLabel != null && sayLabel.text != L.T("Say:"))
            {
                // The language changed since the line was built: relabel and make room.
                sayLabel.text = L.T("Say:");
                GameUi.EnsureGlyphs(sayLabel);
                float width = Mathf.Ceil(sayLabel.GetPreferredValues(sayLabel.text).x) + 2f;
                sayLabel.rectTransform.sizeDelta = new Vector2(width, 14f);
                if (inputField != null) ((RectTransform)inputField.transform).offsetMin = new Vector2(width + 10f, 2f);
            }
            if (inputField == null)
            {
                return;
            }
            if (focusPending)
            {
                focusPending = false;
                inputField.SetTextWithoutNotify(string.Empty);
                inputField.ActivateInputField();
                inputField.Select();
            }
            draft = inputField.text;
        }

        /// <summary>Enter and Escape while the game-styled line is open (from Update).</summary>
        private static void HandleGameUiKeys()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string text = ((inputField != null ? inputField.text : draft) ?? string.Empty).Trim();
                if (text.Length > MaxLength)
                {
                    text = text.Substring(0, MaxLength);
                }
                if (text.Length > 0)
                {
                    Send(text);
                }
                if (inputField != null) inputField.SetTextWithoutNotify(string.Empty);
                Close();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (inputField != null) inputField.SetTextWithoutNotify(string.Empty);
                Close();
            }
        }

        /// <summary>Tests: whether the line is open and drawn in the game's style.</summary>
        internal static string DescribeGameUi()
        {
            int lines = 0;
            foreach (TMPro.TextMeshProUGUI view in lineViews)
            {
                if (view != null && view.gameObject.activeSelf) lines++;
            }
            return "gameUi=" + gameUiActive + " open=" + open + " lines=" + lines +
                   " input=" + (inputPlate != null && inputPlate.gameObject.activeSelf) + " focused=" + (inputField != null && inputField.isFocused);
        }

        /// <summary>Tests: open the line as the key does.</summary>
        internal static void OpenForTest()
        {
            if (!open)
            {
                open = true;
                focusPending = true;
                draft = string.Empty;
                SetGameInput(false);
            }
        }

        /// <summary>
        /// With a controller: Steam's keyboard, and the line is sent when it closes. False when
        /// Steam's keyboard is not available (then the chat line opens as with T).
        /// </summary>
        internal static bool WriteWithController()
        {
            if (!Enabled || !InSession())
            {
                return false;
            }
            if (CoopInput.TypeText(L.T("Say:"), 120, text =>
            {
                if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0) Send(text.Trim());
            }))
            {
                return true;
            }
            if (CoopInput.PadActive)
            {
                CoopKeyboard.Open(L.T("Say:"), string.Empty, 120, text =>
                {
                    if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0) Send(text.Trim());
                });
                return false;
            }
            open = true;
            focusPending = true;
            draft = string.Empty;
            SetGameInput(false);
            return false;
        }

        private static void Send(string text)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening)
            {
                return;
            }
            string name = CoopSession.LocalName;
            Add(name, text, true);
            if (netcode.IsHost)
            {
                Relay(netcode, null, name, text);
            }
            else
            {
                using (var writer = new FastBufferWriter(512, Allocator.Temp))
                {
                    writer.WriteValueSafe(new FixedString64Bytes(name ?? string.Empty));
                    writer.WriteValueSafe(new FixedString512Bytes(text));
                    netcode.CustomMessagingManager.SendNamedMessage(ChatMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
            log.LogInfo("Chat: " + name + ": " + text);
        }

        private static void Relay(NetworkManager netcode, ulong? except, string name, string text)
        {
            using (var writer = new FastBufferWriter(512, Allocator.Temp))
            {
                writer.WriteValueSafe(new FixedString64Bytes(name ?? string.Empty));
                writer.WriteValueSafe(new FixedString512Bytes(text));
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId && clientId != except)
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(ChatMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
            }
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out FixedString64Bytes nameText);
                reader.ReadValueSafe(out FixedString512Bytes messageText);
                string text = messageText.ToString();
                if (text.Length > MaxLength)
                {
                    text = text.Substring(0, MaxLength);
                }
                // The host names joiners itself, so nobody can speak as someone else.
                string name = netcode.IsHost ? CoopSession.NameFor(sender) : nameText.ToString();
                if (text.Trim().Length == 0)
                {
                    return;
                }
                Add(name, text, false);
                if (netcode.IsHost)
                {
                    Relay(netcode, sender, name, text);
                }
                log.LogInfo("Chat: " + name + ": " + text);
            }
            catch (Exception ex)
            {
                log.LogWarning("Chat: could not read a message from " + sender + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Lines a controller player can send without typing. They travel as their number, so
        /// every player reads them in their own language.
        /// </summary>
        internal static readonly string[] QuickLines =
        {
            L.Key("Come here!"), L.Key("Look at this!"), L.Key("Wait for me!"), L.Key("Thanks!"), L.Key("Yes."), L.Key("No."), L.Key("I'm going to sleep."),
        };

        private const string QuickMark = "\u0001q";

        internal static void SendQuick(int index)
        {
            if (index >= 0 && index < QuickLines.Length && InSession())
            {
                Send(QuickMark + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static void Add(string name, string text, bool own)
        {
            if (text != null && text.StartsWith(QuickMark, StringComparison.Ordinal) &&
                int.TryParse(text.Substring(QuickMark.Length), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int quick) &&
                quick >= 0 && quick < QuickLines.Length)
            {
                text = L.T(QuickLines[quick]);
            }
            history.Add(new Line { Name = string.IsNullOrEmpty(name) ? "?" : name, Text = text, At = Time.unscaledTime, Own = own });
            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }
        }

        /// <summary>Tests: the last line received or sent, as "name: text".</summary>
        internal static string LastLine()
        {
            return history.Count == 0 ? string.Empty : history[history.Count - 1].Name + ": " + history[history.Count - 1].Text;
        }

        /// <summary>Tests: send a line as if typed.</summary>
        internal static void SendForTest(string text)
        {
            Send(text);
        }

        private static void EnsureStyles()
        {
            if (lineStyle != null)
            {
                return;
            }
            lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = false, wordWrap = false };
            shadowStyle = new GUIStyle(lineStyle);
            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 15 };
            backdrop = new Texture2D(1, 1);
            backdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.45f));
            backdrop.Apply();
        }
    }
}
