using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Coop
{
    /// <summary>
    /// What a player sees of a shared story scene (CoopSceneShare), drawn with the game's parts.
    ///
    /// The question is one of the game's notifications at the top right: the other player's head
    /// in the portrait ring, "Host is watching a scene.", the answers as key hints and an
    /// hourglass counting down. Answering plays out on the strip itself, as the joining window
    /// does: watching brings a second head in and the two lean together on green; keeping on
    /// playing turns the head away on red, and the strip fades.
    ///
    /// While watching, a small window shows both heads with the game's eye between them and the
    /// way out.
    ///
    /// Keys and buttons: the keyboard has its own keys (default Y and N, free in the game); on a
    /// controller the answers are Y and B, and stopping is holding B. For the ten seconds the
    /// question is open the game does not see those buttons, so Y does not also open the
    /// inventory. Hints show the key cap, or the game's own button icon when a controller is in
    /// use. Everything can also be clicked.
    /// </summary>
    internal static class CoopScenePrompt
    {
        internal enum Phase
        {
            Hidden,
            Asking,
            Accepted,
            Declined
        }

        private const float AnswerSeconds = 1.4f;
        private const float HoldSeconds = 0.6f;

        internal static KeyCode WatchKey { get; set; } = KeyCode.Y;
        internal static KeyCode SkipKey { get; set; } = KeyCode.N;

        private static BepInEx.Logging.ManualLogSource log;
        private static Phase phase = Phase.Hidden;
        private static float phaseSince;
        private static string askingName = string.Empty;
        private static float secondsLeft;
        private static int lastAskedFrame = -10;

        // The notification
        private static RectTransform note;
        private static CanvasGroup noteGroup;
        private static Image strip;
        private static RectTransform portrait;
        private static RectTransform headThem;
        private static RectTransform headYou;
        private static TextMeshProUGUI line;
        private static Hint watchHint;
        private static Hint skipHint;
        private static RectTransform glass;
        private static TextMeshProUGUI seconds;
        private static bool watchClicked;
        private static bool skipClicked;

        // The watching panel
        private static RectTransform panel;
        private static TextMeshProUGUI panelTitle;
        private static Hint stopHint;
        private static Image holdFill;
        private static bool stopClicked;
        private static float holdSince = -1f;

        // The controller's Y and B are hidden from the game while asking
        private static bool blocking;

        private sealed class Hint
        {
            internal RectTransform Root;
            internal Image Cap;
            internal TextMeshProUGUI Letter;
            internal TextMeshProUGUI Label;
            internal Button Button;
            internal bool IsButton;
        }

        internal static void Init(BepInEx.Logging.ManualLogSource source)
        {
            log = source;
        }

        internal static void Install()
        {
            // While the question is open the game does not see the controller's Y and B; and it
            // never sees the chat key on the frame the chat opens.
            CoopInput.Hide(key => CoopChat.Swallows(key));
            CoopInput.Hide(key => blocking && phase == Phase.Asking && CoopInput.PadActive &&
                                  (CoopInput.KeysOf(GamepadButton.Y).Contains(key.value) || CoopInput.KeysOf(GamepadButton.B).Contains(key.value)));
        }

        internal static Phase Current => phase;

        // ---------------------------------------------------------------- the question

        /// <summary>Each frame while the question is open.</summary>
        internal static void Ask(string name, float left)
        {
            if (phase != Phase.Asking)
            {
                phase = Phase.Asking;
                phaseSince = Time.unscaledTime;
                watchClicked = skipClicked = false;
            }
            askingName = name;
            secondsLeft = left;
            lastAskedFrame = Time.frameCount;
            blocking = true;
            if (GameUi.Ready)
            {
                DrawNote();
            }
        }

        /// <summary>The answer, by key, button or click, since the last frame.</summary>
        internal static bool TakeWatch()
        {
            bool pressed = watchClicked || KeyDown(WatchKey) || CoopInput.PadDown(GamepadButton.Y);
            watchClicked = false;
            return pressed && phase == Phase.Asking;
        }

        internal static bool TakeSkip()
        {
            bool pressed = skipClicked || KeyDown(SkipKey) || CoopInput.PadDown(GamepadButton.B);
            skipClicked = false;
            return pressed && phase == Phase.Asking;
        }

        internal static void SetClickedForTest(bool watch)
        {
            if (watch) watchClicked = true; else skipClicked = true;
        }

        /// <summary>The question was answered (or ran out): play it out on the strip.</summary>
        internal static void Answered(bool watch)
        {
            phase = watch ? Phase.Accepted : Phase.Declined;
            phaseSince = Time.unscaledTime;
            blocking = false;
        }

        /// <summary>The question went away without an answer shown (the scene ended first).</summary>
        internal static void Withdraw()
        {
            phase = Phase.Hidden;
            blocking = false;
            if (note != null) note.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- watching

        /// <summary>Each frame while watching.</summary>
        internal static void Watching(string name)
        {
            if (GameUi.Ready)
            {
                DrawPanel();
            }
        }

        internal static void HidePanel()
        {
            if (panel != null && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
            holdSince = -1f;
        }

        /// <summary>"Stop watching": clicked, the key, or B held on a controller.</summary>
        internal static bool TakeStop()
        {
            bool stop = stopClicked || KeyDown(SkipKey);
            stopClicked = false;
            if (CoopInput.PadActive && CoopInput.PadHeld(GamepadButton.B))
            {
                if (holdSince < 0f) holdSince = Time.unscaledTime;
                if (Time.unscaledTime - holdSince >= HoldSeconds)
                {
                    holdSince = -1f;
                    stop = true;
                }
            }
            else
            {
                holdSince = -1f;
            }
            return stop;
        }

        internal static void SetStopForTest()
        {
            stopClicked = true;
        }

        // ---------------------------------------------------------------- each frame

        /// <summary>From the plugin's Update: the answer playing out, then gone.</summary>
        internal static void Update()
        {
            if (phase == Phase.Asking)
            {
                // Ask() is called every frame while asking; when it stops, so does the question.
                // Counted in frames: a slow frame (a hitch, a loading scene) is not the question ending.
                if (Time.frameCount - lastAskedFrame > 2)
                {
                    Withdraw();
                }
                return;
            }
            if (phase == Phase.Accepted || phase == Phase.Declined)
            {
                float t = Time.unscaledTime - phaseSince;
                if (t >= AnswerSeconds)
                {
                    phase = Phase.Hidden;
                    if (note != null) note.gameObject.SetActive(false);
                    return;
                }
                if (GameUi.Ready && note != null)
                {
                    AnimateAnswer(t);
                }
            }
        }

        // ---------------------------------------------------------------- drawing: notification

        private static void DrawNote()
        {
            if (note == null)
            {
                BuildNote();
            }
            if (!note.gameObject.activeSelf)
            {
                note.gameObject.SetActive(true);
            }
            noteGroup.alpha = 1f;
            SetStrip("notification-violet-el");
            headThem.localRotation = Quaternion.Euler(0f, 0f, -8f);
            headThem.anchoredPosition = Vector2.zero;
            headYou.gameObject.SetActive(false);
            SetText(line, L.F("{0} is watching a scene.", askingName));
            GameUi.SetKind(line, GameUi.TextKind.Body);
            watchHint.Root.gameObject.SetActive(true);
            skipHint.Root.gameObject.SetActive(true);
            glass.gameObject.SetActive(true);
            seconds.gameObject.SetActive(true);
            SetHint(watchHint, WatchKey, GameKey.Inventory, L.T("Watch"));
            SetHint(skipHint, SkipKey, GameKey.Back, L.T("Keep playing"));
            SetText(seconds, Mathf.CeilToInt(Mathf.Max(0f, secondsLeft)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            LayoutNote();
        }

        private static void AnimateAnswer(float t)
        {
            bool yes = phase == Phase.Accepted;
            watchHint.Root.gameObject.SetActive(false);
            skipHint.Root.gameObject.SetActive(false);
            glass.gameObject.SetActive(false);
            seconds.gameObject.SetActive(false);
            SetStrip(yes ? "notification-green-el" : "notification-red-el");
            SetText(line, yes ? L.T("Watching together.") : L.T("You keep playing."));
            // On the green or red strip the game's white reads better than its body grey.
            line.color = Color.white;
            float ease = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f));
            if (yes)
            {
                // The viewer's head comes in beside the other and they lean together.
                headYou.gameObject.SetActive(true);
                headThem.anchoredPosition = new Vector2(-7f * ease, 0f);
                headYou.anchoredPosition = new Vector2(Mathf.Lerp(34f, 9f, ease), 0f);
                headThem.localRotation = Quaternion.Euler(0f, 0f, -14f * ease);
                headYou.localRotation = Quaternion.Euler(0f, 0f, 14f * ease);
            }
            else
            {
                headYou.gameObject.SetActive(false);
                headThem.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, 14f, ease));
            }
            // The last part fades out.
            noteGroup.alpha = 1f - Mathf.Clamp01((t - (AnswerSeconds - 0.45f)) / 0.45f);
            LayoutNote();
        }

        private static void BuildNote()
        {
            RectTransform layer = GameUi.Layer;
            note = GameUi.NewRect(layer, "GK2Coop.ScenePrompt");
            note.anchorMin = note.anchorMax = new Vector2(1f, 1f);
            note.pivot = new Vector2(1f, 1f);
            note.anchoredPosition = new Vector2(-12f, -118f);
            noteGroup = note.gameObject.AddComponent<CanvasGroup>();

            strip = Picture(note, "Strip", "notification-violet-el", true);
            RectTransform stripRect = strip.rectTransform;
            stripRect.anchorMin = new Vector2(0f, 0f);
            stripRect.anchorMax = new Vector2(1f, 1f);
            stripRect.offsetMin = new Vector2(60f, 0f);
            stripRect.offsetMax = Vector2.zero;

            Image portraitImage = Picture(note, "Portrait", "notification-portrait", false);
            portrait = portraitImage.rectTransform;
            portrait.anchorMin = portrait.anchorMax = new Vector2(0f, 0.5f);
            portrait.pivot = new Vector2(0f, 0.5f);
            portrait.sizeDelta = new Vector2(68f, 48f);
            portrait.anchoredPosition = Vector2.zero;
            RectTransform heads = GameUi.NewRect(portrait, "Heads");
            heads.sizeDelta = new Vector2(48f, 48f);
            heads.anchoredPosition = Vector2.zero;
            headYou = CoopProgressWindow.NewHead(heads, "You");
            headThem = CoopProgressWindow.NewHead(heads, "Them");
            headThem.localScale = headYou.localScale = Vector3.one * 0.8f;

            line = Text(note, GameUi.TextKind.Body);
            watchHint = NewHint(note, () => watchClicked = true);
            skipHint = NewHint(note, () => skipClicked = true);
            glass = Picture(note, "Hourglass", "hourglass", false).rectTransform;
            glass.sizeDelta = new Vector2(10f, 10f);
            seconds = Text(note, GameUi.TextKind.Hint);
        }

        private static void LayoutNote()
        {
            const float left = 70f;
            float lineWidth = line.GetPreferredValues(line.text, 600f, 20f).x;
            float watchWidth = HintWidth(watchHint);
            float skipWidth = HintWidth(skipHint);
            bool hints = watchHint.Root.gameObject.activeSelf;
            float content = Mathf.Max(lineWidth + (hints ? 34f : 0f), hints ? watchWidth + 10f + skipWidth : 0f);
            float width = Mathf.Round(left + content + 12f);
            note.sizeDelta = new Vector2(width, 48f);
            Place(line.rectTransform, new Vector2(left, hints ? -8f : -16f), new Vector2(lineWidth + 4f, 16f));
            Place(watchHint.Root, new Vector2(left, -27f), new Vector2(watchWidth, 16f));
            Place(skipHint.Root, new Vector2(left + watchWidth + 10f, -27f), new Vector2(skipWidth, 16f));
            Place(glass, new Vector2(width - 38f, -11f), glass.sizeDelta);
            Place(seconds.rectTransform, new Vector2(width - 26f, -8f), new Vector2(22f, 16f));
        }

        // ---------------------------------------------------------------- drawing: watching panel

        private static void DrawPanel()
        {
            if (panel == null)
            {
                BuildPanel();
            }
            if (!panel.gameObject.activeSelf)
            {
                panel.gameObject.SetActive(true);
            }
            if (panelTitle != null) SetText(panelTitle, L.T("Watching a scene"));
            SetHint(stopHint, SkipKey, GameKey.Back, L.T("Stop watching"));
            // Holding B fills the button from the left.
            float held = holdSince < 0f ? 0f : Mathf.Clamp01((Time.unscaledTime - holdSince) / HoldSeconds);
            holdFill.rectTransform.anchorMax = new Vector2(held, 1f);
            holdFill.gameObject.SetActive(held > 0f);
            // As wide as the heading needs (the frame's corners take about 90) and the button.
            float heading = panelTitle == null ? 0f : panelTitle.GetPreferredValues(panelTitle.text, 800f, 30f).x;
            float width = Mathf.Round(Mathf.Max(200f, Mathf.Max(HintWidth(stopHint) + 60f, heading + 96f)));
            panel.sizeDelta = new Vector2(width, 116f);
        }

        private static void BuildPanel()
        {
            Button close;
            panel = GameUi.NewFrame(GameUi.Layer, out panelTitle, out close);
            panel.name = "GK2Coop.SceneWatching";
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-12f, -12f);
            if (close != null)
            {
                close.onClick.AddListener(() => stopClicked = true);
            }
            RectTransform stage = GameUi.NewRect(panel, "Heads");
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 1f);
            stage.anchoredPosition = new Vector2(0f, -60f);
            stage.sizeDelta = new Vector2(160f, 40f);
            RectTransform left = CoopProgressWindow.NewHead(stage, "Them");
            RectTransform right = CoopProgressWindow.NewHead(stage, "You");
            left.localScale = right.localScale = Vector3.one * 0.75f;
            left.anchoredPosition = new Vector2(-34f, 0f);
            right.anchoredPosition = new Vector2(34f, 0f);
            left.localRotation = Quaternion.Euler(0f, 0f, -12f);
            right.localRotation = Quaternion.Euler(0f, 0f, 12f);
            Image eye = Picture(stage, "Eye", "i_eye", false);
            eye.rectTransform.sizeDelta = new Vector2(30f, 30f);

            TextMeshProUGUI label;
            Button button = GameUi.NewButton(panel, string.Empty, out label);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(18f, 12f);
            rect.offsetMax = new Vector2(-18f, 38f);
            var layout = button.gameObject.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            button.onClick.AddListener(() => stopClicked = true);
            RectTransform fill = GameUi.NewRect(rect, "Hold");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            holdFill = fill.gameObject.AddComponent<Image>();
            holdFill.raycastTarget = false;
            holdFill.color = new Color(1f, 0.85f, 0.5f, 0.35f);
            stopHint = new Hint { Root = rect, Label = label, Button = button, IsButton = true };
            AddCap(stopHint, rect, 7f);
        }

        // ---------------------------------------------------------------- hints

        private static Hint NewHint(RectTransform parent, Action onClick)
        {
            RectTransform root = GameUi.NewRect(parent, "Hint");
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 0.5f);
            // An invisible graphic makes the whole hint clickable.
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
            var hint = new Hint { Root = root, Button = button, Label = Text(root, GameUi.TextKind.Body) };
            AddCap(hint, root, 0f);
            return hint;
        }

        private static void AddCap(Hint hint, RectTransform parent, float x)
        {
            Image cap = Picture(parent, "Key", "widget_items_cell-world_item_cell_1-key_icon", true);
            cap.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            RectTransform capRect = cap.rectTransform;
            capRect.anchorMin = capRect.anchorMax = new Vector2(0f, 0.5f);
            capRect.pivot = new Vector2(0f, 0.5f);
            capRect.sizeDelta = new Vector2(14f, 14f);
            capRect.anchoredPosition = new Vector2(x, 0f);
            TextMeshProUGUI letter = Text(capRect, GameUi.TextKind.Body);
            letter.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            letter.color = Color.white;
            letter.alignment = TextAlignmentOptions.Center;
            RectTransform letterRect = letter.rectTransform;
            letterRect.anchorMin = letterRect.anchorMax = new Vector2(0.5f, 0.5f);
            letterRect.pivot = new Vector2(0.5f, 0.5f);
            letterRect.sizeDelta = new Vector2(14f, 14f);
            letterRect.anchoredPosition = new Vector2(0f, 1f);
            hint.Cap = cap;
            hint.Letter = letter;
        }

        /// <summary>
        /// The key cap with the keyboard key, or, with a controller in use, the game's own icon for
        /// the button (the same one its hints show, for Xbox, PlayStation or Switch).
        /// </summary>
        private static void SetHint(Hint hint, KeyCode key, GameKey padKey, string text)
        {
            bool pad = CoopInput.PadActive;
            string icon = pad ? CoopInput.Icon(padKey) : null;
            bool useCap = string.IsNullOrEmpty(icon);
            hint.Cap.gameObject.SetActive(useCap);
            if (useCap)
            {
                SetText(hint.Letter, KeyName(key));
            }
            string shown = useCap ? text : icon + text;
            SetText(hint.Label, shown);
            if (!hint.IsButton)
            {
                RectTransform labelRect = hint.Label.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 0.5f);
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(useCap ? 18f : 0f, 0f);
                labelRect.sizeDelta = new Vector2(hint.Label.GetPreferredValues(shown, 600f, 20f).x + 4f, 16f);
            }
            else
            {
                // A button: room for the key cap on the left.
                hint.Label.margin = new Vector4(useCap ? 16f : 0f, 0f, 0f, 0f);
            }
        }

        private static float HintWidth(Hint hint)
        {
            float text = hint.Label.GetPreferredValues(hint.Label.text, 600f, 20f).x;
            return text + (hint.Cap.gameObject.activeSelf ? 18f : 0f) + 4f;
        }

        private static string KeyName(KeyCode key)
        {
            string name = key.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal)) return name.Substring(5);
            return name.Length <= 3 ? name : name.Substring(0, 3);
        }

        // ---------------------------------------------------------------- input

        private static bool KeyDown(KeyCode key)
        {
            return key != KeyCode.None && !CoopChat.IsOpen && Input.GetKeyDown(key);
        }

        // ---------------------------------------------------------------- the plain look

        /// <summary>From the plugin's OnGUI when the game's parts are not used: a plain window.</summary>
        internal static void DrawPlain(bool asking, bool watching, float left)
        {
            if (GameUi.Ready)
            {
                return;
            }
            if (asking && phase == Phase.Asking)
            {
                var area = new Rect(Screen.width - 330f, 90f, 310f, 110f);
                GUILayout.BeginArea(area, L.T("A scene is starting"), GUI.skin.window);
                GUILayout.Label(L.F("{0} is watching a scene.", askingName));
                GUILayout.Label(L.F("Closes in {0} s.", Mathf.CeilToInt(Mathf.Max(0f, left))));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("[" + KeyName(WatchKey) + "] " + L.T("Watch"))) watchClicked = true;
                if (GUILayout.Button("[" + KeyName(SkipKey) + "] " + L.T("Keep playing"))) skipClicked = true;
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
            if (watching)
            {
                var area = new Rect(Screen.width - 250f, 10f, 230f, 60f);
                GUILayout.BeginArea(area, L.T("Watching a scene"), GUI.skin.window);
                if (GUILayout.Button("[" + KeyName(SkipKey) + "] " + L.T("Stop watching"))) stopClicked = true;
                GUILayout.EndArea();
            }
        }

        // ---------------------------------------------------------------- tests

        internal static string Describe()
        {
            if (note == null || !note.gameObject.activeSelf)
            {
                return "hidden";
            }
            string text = "[" + line.text + "]";
            if (watchHint.Root.gameObject.activeSelf)
            {
                text += " | <" + HintText(watchHint) + "> | <" + HintText(skipHint) + "> | " + seconds.text + "s";
            }
            return text + " (" + phase + ")";
        }

        internal static string DescribePanel()
        {
            if (panel == null || !panel.gameObject.activeSelf)
            {
                return "hidden";
            }
            return "[" + (panelTitle != null ? panelTitle.text : string.Empty) + "] | <" + HintText(stopHint) + ">";
        }

        private static string HintText(Hint hint)
        {
            return (hint.Cap.gameObject.activeSelf ? hint.Letter.text + " " : string.Empty) + hint.Label.text;
        }

        // ---------------------------------------------------------------- parts

        private static void SetStrip(string sprite)
        {
            Sprite found = GameUi.FindSprite(sprite);
            if (found != null && strip.sprite != found)
            {
                strip.sprite = found;
            }
        }

        private static void SetText(TextMeshProUGUI label, string text)
        {
            if (label.text != text)
            {
                label.text = text;
            }
        }

        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        private static TextMeshProUGUI Text(RectTransform parent, GameUi.TextKind kind)
        {
            TextMeshProUGUI label = GameUi.NewText(parent, kind, string.Empty);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Left;
            return label;
        }

        private static Image Picture(RectTransform parent, string name, string sprite, bool sliced)
        {
            RectTransform rect = GameUi.NewRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            Sprite found = GameUi.FindSprite(sprite);
            if (found != null)
            {
                image.sprite = found;
                image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                if (sprite == "i_eye" || sprite == "hourglass")
                {
                    Material icon = GameUi.IconMaterial();
                    if (icon != null) image.material = icon;
                }
            }
            else
            {
                image.color = new Color(0.2f, 0.18f, 0.25f, 0.9f);
            }
            return image;
        }
    }
}
