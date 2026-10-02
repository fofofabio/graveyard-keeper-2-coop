using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Coop
{
    /// <summary>
    /// The mod's own status window while joining: two keeper heads in the game's portrait frames,
    /// looking at each other, with the step under way between them.
    ///
    /// Connecting pulses three dots; copying the host's world fills a bar; when the session is up
    /// the heads lean in and the dots light up, then the window closes by itself. When joining
    /// fails the heads turn away from each other and the reason stays until OK is pressed.
    /// Everything is the game's own art: the window frame, the notification portrait frame and
    /// the keeper's portrait (<c>portrait_icon_hero</c>).
    /// </summary>
    internal static class CoopProgressWindow
    {
        private enum Mode
        {
            Hidden,
            Working,
            Done,
            Failed
        }

        private const float Width = 236f;
        private const float DoneSeconds = 3f;

        private static readonly Color Waiting = new Color(1f, 0.741f, 0f, 1f);
        private static readonly Color Joined = new Color(0.55f, 0.95f, 0.45f, 1f);
        private static readonly Color Broken = new Color(0.95f, 0.3f, 0.25f, 1f);

        private static RectTransform window;
        private static Image shade;
        private static TextMeshProUGUI title;
        private static TextMeshProUGUI detail;
        private static RectTransform leftHead;
        private static RectTransform rightHead;
        private static readonly List<Image> dots = new List<Image>();
        private static RectTransform bar;
        private static RectTransform barFill;
        private static Button ok;
        private static TextMeshProUGUI okText;
        private static RectTransform okRect;

        private static Mode mode = Mode.Hidden;
        private static bool joining;
        private static float doneUntil;
        private static string dismissed;
        private static bool okClicked;

        /// <summary>Tests: what the window shows.</summary>
        internal static string Describe()
        {
            if (window == null || !window.gameObject.activeSelf)
            {
                return "hidden";
            }
            return mode + " [" + title.text + "] " + detail.text.Replace("\n", " ") + (bar.gameObject.activeSelf ? " bar=" + barFill.anchorMax.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        }

        /// <summary>Tests: show a state without a real connection ("working", "copy|0.4", "done", "failed|text", "hide").</summary>
        internal static void ShowForTest(string state, string argument)
        {
            switch (state)
            {
                case "working":
                    CoopStatus.Set(CoopPhase.Connecting, L.F("Connecting to {0}…", "127.0.0.1:8889"));
                    CoopStatus.SetStep(JoinStep.Connecting);
                    break;
                case "copy":
                    CoopStatus.Set(CoopPhase.Connecting, L.F("Copying the host's world ({0}%)…", Mathf.RoundToInt(float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture) * 100f)));
                    CoopStatus.SetStep(JoinStep.Copying, float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "done":
                    joining = true;
                    CoopStatus.Set(CoopPhase.Connected, L.F("You joined {0}'s game.", "Host"));
                    break;
                case "failed":
                    CoopStatus.Set(CoopPhase.Failed, argument);
                    break;
                case "failed-key":
                    // A real failure message, in the current language.
                    CoopStatus.Set(CoopPhase.Failed, L.F("Could not reach the host at {0}. Make sure they are hosting and already in their world. Over the internet, their router has to forward port {1}.", "10.0.0.9:8889", 8889));
                    break;
                case "ok":
                    if (ok != null && ok.gameObject.activeInHierarchy)
                    {
                        GameUi.ClickForTest(ok);
                    }
                    break;
                default:
                    CoopStatus.Set(CoopPhase.Idle, string.Empty);
                    break;
            }
        }

        /// <summary>From the plugin's LateUpdate.</summary>
        internal static void Update()
        {
            if (!GameUi.Ready)
            {
                return;
            }
            Mode wanted = Decide();
            if (wanted == Mode.Hidden)
            {
                if (window != null && window.gameObject.activeSelf)
                {
                    window.gameObject.SetActive(false);
                }
                if (shade != null && shade.gameObject.activeSelf)
                {
                    shade.gameObject.SetActive(false);
                }
                mode = Mode.Hidden;
                return;
            }
            if (window == null || shade == null)
            {
                Build();
            }
            if (!window.gameObject.activeSelf)
            {
                window.gameObject.SetActive(true);
                shade.transform.SetAsLastSibling();
                window.SetAsLastSibling();
            }
            mode = wanted;
            // Like the game's dialogs: the screen behind dims and takes no clicks while joining,
            // so nothing else can be started halfway. The short "together" moment does not block.
            bool blocking = mode != Mode.Done;
            if (shade.gameObject.activeSelf != blocking)
            {
                shade.gameObject.SetActive(blocking);
            }
            Draw();
        }

        private static Mode Decide()
        {
            Unity.Netcode.NetworkManager netcode = Unity.Netcode.NetworkManager.Singleton;
            bool host = netcode != null && netcode.IsListening && netcode.IsHost;
            switch (CoopStatus.Phase)
            {
                case CoopPhase.Connecting:
                    if (host)
                    {
                        return Mode.Hidden;
                    }
                    joining = true;
                    doneUntil = 0f;
                    dismissed = null;
                    return Mode.Working;
                case CoopPhase.Connected:
                    if (!joining || host)
                    {
                        return Mode.Hidden;
                    }
                    if (doneUntil <= 0f)
                    {
                        doneUntil = Time.unscaledTime + DoneSeconds;
                    }
                    if (Time.unscaledTime < doneUntil)
                    {
                        return Mode.Done;
                    }
                    joining = false;
                    doneUntil = 0f;
                    return Mode.Hidden;
                case CoopPhase.Failed:
                    if (okClicked)
                    {
                        okClicked = false;
                        dismissed = CoopStatus.Detail;
                        CoopHostLeft.Dismissed();
                    }
                    joining = false;
                    return CoopStatus.Detail == dismissed ? Mode.Hidden : Mode.Failed;
                default:
                    joining = false;
                    doneUntil = 0f;
                    return Mode.Hidden;
            }
        }

        private static void Draw()
        {
            float now = Time.unscaledTime;
            string heading;
            switch (mode)
            {
                case Mode.Done:
                    heading = L.T("Together");
                    break;
                case Mode.Failed:
                    heading = CoopHostLeft.Pending ? L.T("The session has ended") : L.T("Something went wrong");
                    break;
                default:
                    heading = CoopStatus.Step == JoinStep.Copying ? L.T("Copying the world")
                        : CoopStatus.Step == JoinStep.Loading ? L.T("Loading the world")
                        : L.T("Connecting");
                    break;
            }
            if (title.text != heading) title.text = heading;
            string text = CoopStatus.Detail ?? string.Empty;
            if (detail.text != text) detail.text = text;
            GameUi.SetKind(detail, GameUi.TextKind.Body);

            // The heads: facing each other while working, closer when joined, turned away on failure.
            float gap = mode == Mode.Done ? 26f : 38f;
            float lean = mode == Mode.Done ? Mathf.Sin(now * 6f) * 0.8f : 0f;
            leftHead.anchoredPosition = new Vector2(-gap - lean, 0f);
            rightHead.anchoredPosition = new Vector2(gap + lean, 0f);
            // The keeper faces the viewer, so the heads tilt: towards each other while connecting,
            // further when joined, away from each other when it failed.
            float tilt = mode == Mode.Failed ? -12f : mode == Mode.Done ? 14f : 7f + Mathf.Sin(now * 2.2f) * 2f;
            leftHead.localRotation = Quaternion.Euler(0f, 0f, -tilt);
            rightHead.localRotation = Quaternion.Euler(0f, 0f, tilt);

            for (int i = 0; i < dots.Count; i++)
            {
                Color colour;
                if (mode == Mode.Done) colour = Joined;
                else if (mode == Mode.Failed) colour = Broken;
                else
                {
                    // A wave running from one head to the other.
                    float wave = Mathf.Sin(now * 5f - i * 1.1f) * 0.5f + 0.5f;
                    colour = new Color(Waiting.r, Waiting.g, Waiting.b, 0.25f + wave * 0.75f);
                }
                dots[i].color = colour;
                dots[i].rectTransform.localScale = Vector3.one * (mode == Mode.Working ? 0.8f + 0.4f * dots[i].color.a : 1f);
            }

            bool showBar = mode == Mode.Working && CoopStatus.Step == JoinStep.Copying && CoopStatus.Progress >= 0f;
            if (bar.gameObject.activeSelf != showBar) bar.gameObject.SetActive(showBar);
            if (showBar)
            {
                barFill.anchorMax = new Vector2(Mathf.Clamp01(CoopStatus.Progress), 1f);
            }
            bool showOk = mode == Mode.Failed;
            string okLabel = (CoopInput.PadActive ? CoopInput.Icon(LazyBearTechnology.GameKey.Select) ?? string.Empty : string.Empty) + L.T("OK");
            if (okText != null && okText.text != okLabel) okText.text = okLabel;
            if (showOk && (CoopInput.PadDown(LazyBearTechnology.GamepadButton.A) || CoopInput.PadDown(LazyBearTechnology.GamepadButton.B)))
            {
                okClicked = true;
            }
            if (okRect.gameObject.activeSelf != showOk) okRect.gameObject.SetActive(showOk);

            // Heads and bar take the top 114 units; the text follows, then OK when there is one.
            float textHeight = Mathf.Max(14f, detail.GetPreferredValues(text, Width - 40f, 400f).y);
            float bottom = showOk ? 50f : 18f;
            detail.rectTransform.offsetMin = new Vector2(20f, bottom);
            window.sizeDelta = new Vector2(Width, Mathf.Round(114f + textHeight + bottom));
        }

        private static void Build()
        {
            RectTransform layer = GameUi.Layer;
            RectTransform shadeRect = GameUi.NewRect(layer, "GK2Coop.StatusShade");
            GameUi.Stretch(shadeRect, 0f, 0f, 0f, 0f);
            shade = shadeRect.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.4f);
            shade.raycastTarget = true;
            Button close;
            window = GameUi.NewFrame(layer, out title, out close);
            window.name = "GK2Coop.Status";
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.anchoredPosition = new Vector2(0f, 30f);
            window.sizeDelta = new Vector2(Width, 170f);
            if (close != null)
            {
                // Joining cannot be cancelled from here; the cross is not offered.
                close.gameObject.SetActive(false);
            }

            RectTransform stage = GameUi.NewRect(window, "Heads");
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 1f);
            stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = new Vector2(0f, -70f);
            stage.sizeDelta = new Vector2(200f, 50f);
            leftHead = NewHead(stage, "Left");
            rightHead = NewHead(stage, "Right");
            dots.Clear();
            for (int i = 0; i < 3; i++)
            {
                RectTransform dot = GameUi.NewRect(stage, "Dot" + i);
                dot.sizeDelta = new Vector2(4f, 4f);
                dot.anchoredPosition = new Vector2((i - 1) * 8f, 0f);
                var image = dot.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                dots.Add(image);
            }

            bar = GameUi.NewPlate(window, "Bar").rectTransform;
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -100f);
            bar.sizeDelta = new Vector2(150f, 8f);
            barFill = GameUi.NewRect(bar, "Fill");
            barFill.anchorMin = Vector2.zero;
            barFill.anchorMax = new Vector2(0f, 1f);
            barFill.offsetMin = new Vector2(2f, 2f);
            barFill.offsetMax = new Vector2(-2f, -2f);
            var fill = barFill.gameObject.AddComponent<Image>();
            fill.color = Waiting;
            fill.raycastTarget = false;

            detail = GameUi.NewText(window, GameUi.TextKind.Body, string.Empty);
            detail.alignment = TextAlignmentOptions.Top;
            RectTransform detailRect = detail.rectTransform;
            detailRect.anchorMin = new Vector2(0f, 0f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.pivot = new Vector2(0.5f, 1f);
            detailRect.offsetMin = new Vector2(20f, 14f);
            detailRect.offsetMax = new Vector2(-20f, -114f);

            TextMeshProUGUI okLabel;
            ok = GameUi.NewButton(window, L.T("OK"), out okLabel);
            okText = okLabel;
            ok.onClick.AddListener(() => okClicked = true);
            okRect = (RectTransform)ok.transform;
            okRect.anchorMin = okRect.anchorMax = new Vector2(0.5f, 0f);
            okRect.pivot = new Vector2(0.5f, 0f);
            okRect.anchoredPosition = new Vector2(0f, 14f);
            okRect.sizeDelta = new Vector2(70f, 26f);
            var okLayout = ok.gameObject.GetComponent<LayoutElement>() ?? ok.gameObject.AddComponent<LayoutElement>();
            okLayout.ignoreLayout = true;
        }

        /// <summary>A keeper head in the game's round notification portrait frame.</summary>
        internal static RectTransform NewHead(RectTransform parent, string name)
        {
            RectTransform head = GameUi.NewRect(parent, name);
            head.sizeDelta = new Vector2(36f, 36f);
            Sprite frame = GameUi.FindSprite("notification-portrait-mask");
            Sprite portrait = GameUi.FindSprite("portrait_icon_hero");
            var back = head.gameObject.AddComponent<Image>();
            back.raycastTarget = false;
            back.color = new Color(0.16f, 0.14f, 0.15f, 1f);
            if (frame != null)
            {
                back.sprite = frame;
                head.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            }
            RectTransform face = GameUi.NewRect(head, "Face");
            var faceImage = face.gameObject.AddComponent<Image>();
            faceImage.raycastTarget = false;
            if (portrait != null)
            {
                faceImage.sprite = portrait;
                faceImage.preserveAspect = true;
                Material icon = GameUi.IconMaterial();
                if (icon != null)
                {
                    faceImage.material = icon;
                }
                // The portrait is the whole keeper (32x47) in the middle of a 96x96 sprite. At 1.6x
                // his head, the top 22 of those 47 pixels, fills the circle.
                const float zoom = 1.6f;
                face.sizeDelta = new Vector2(portrait.rect.width * zoom, portrait.rect.height * zoom);
                face.anchoredPosition = new Vector2(0f, -12.5f * zoom);
            }
            else
            {
                faceImage.color = new Color(0f, 0f, 0f, 0f);
            }
            // A pale ring around the portrait, as the game's portrait frames have: the same circle,
            // a little larger, behind the head.
            RectTransform ring = GameUi.NewRect(parent, name + "Ring");
            ring.SetSiblingIndex(head.GetSiblingIndex());
            ring.sizeDelta = new Vector2(40f, 40f);
            var ringImage = ring.gameObject.AddComponent<Image>();
            ringImage.raycastTarget = false;
            ringImage.sprite = frame;
            ringImage.color = new Color(0.62f, 0.6f, 0.68f, 1f);
            ring.gameObject.AddComponent<FollowTarget>().Target = head;
            return head;
        }

        /// <summary>Keeps a frame on its head as the head moves and turns.</summary>
        private sealed class FollowTarget : MonoBehaviour
        {
            internal RectTransform Target;

            private void LateUpdate()
            {
                if (Target == null)
                {
                    return;
                }
                var rect = (RectTransform)transform;
                rect.anchoredPosition = Target.anchoredPosition;
                rect.localRotation = Target.localRotation;
                rect.localScale = Target.localScale;
            }
        }
    }
}
