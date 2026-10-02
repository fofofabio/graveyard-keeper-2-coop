using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// The player-facing multiplayer UI: a small session status indicator and optional name tags
    /// above characters. Deliberately separate from the F8 diagnostics window, which stays a
    /// developer tool. Drawn with the game's own plate and fonts (<see cref="UpdateGameUi"/>), or with
    /// IMGUI when the game's parts cannot be found.
    /// </summary>
    internal static class CoopHud
    {
        private static ManualLogSource log;
        private static GUIStyle statusStyle;
        private static GUIStyle tagStyle;
        private static GUIStyle tagShadowStyle;
        private static Texture2D statusBackground;
        private static GUIStyle toastStyle;
        private static GUIStyle toastProblemStyle;

        private static float nextRttSample;
        private static string rttText = "--";
        private static ulong pendingDisconnectClient;
        private static string pendingDisconnectReason;
        private static float pendingDisconnectAt = -1f;

        internal static bool ShowStatus { get; set; }
        internal static bool ShowNameTags { get; set; }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        /// <summary>
        /// A rejected client is disconnected a moment after the refusal is sent, so it has time to
        /// receive and display the reason rather than seeing an unexplained drop.
        /// </summary>
        internal static void NotePendingDisconnect(ulong clientId, string reason)
        {
            pendingDisconnectClient = clientId;
            pendingDisconnectReason = reason;
            pendingDisconnectAt = Time.unscaledTime + 1.5f;
        }

        private sealed class TagTarget
        {
            public Transform Anchor;
            // The head as drawn: the tag is centred over it (the bubble point sits beside the head,
            // and in the side view the drawing is off the body's centre).
            public Renderer Head;
            public Transform Center;
            public string Label;
            public bool IsLocal;
        }

        // How far above the drawn head's centre a name sits, in world units: where the game's own
        // speech-bubble point stands while the keeper stands still.
        private const float HeadLift = 1.0f;

        /// <summary>
        /// Where the tag goes, in the world camera's viewport: over the head as drawn, in both
        /// directions. (Up to 0.63 the height came from the speech-bubble point, which does not move
        /// with the drawn head: while walking the name drifted 40 to 75 pixels above it and back.)
        /// </summary>
        private static bool TagPoint(Camera camera, TagTarget target, out Vector3 viewport)
        {
            if (target.Head != null && target.Head.enabled && target.Head.gameObject.activeInHierarchy)
            {
                // The head sprite's box is much taller than the head drawn in it (half-height 1.6
                // world units): its top put the names ~55 px too high (0.64.0, seen in play). The
                // speech-bubble point stands 1.0 above the head's centre: that height, from the head.
                viewport = camera.WorldToViewportPoint(target.Head.bounds.center + new Vector3(0f, HeadLift, 0f));
                return true;
            }
            if (target.Anchor == null)
            {
                viewport = Vector3.zero;
                return false;
            }
            viewport = camera.WorldToViewportPoint(target.Anchor.position);
            if (target.Center != null)
            {
                viewport.x = camera.WorldToViewportPoint(target.Center.position).x;
            }
            return true;
        }

        private static readonly List<TagTarget> tagTargets = new List<TagTarget>();
        private static float nextTagRefresh;

        internal static void Tick()
        {
            if (pendingDisconnectAt > 0f && Time.unscaledTime >= pendingDisconnectAt)
            {
                pendingDisconnectAt = -1f;
                try
                {
                    if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                    {
                        NetworkManager.Singleton.DisconnectClient(pendingDisconnectClient);
                        log.LogWarning("Disconnected client " + pendingDisconnectClient + ": " + pendingDisconnectReason);
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning("Could not disconnect client " + pendingDisconnectClient + ": " + ex.Message);
                }
            }

            if (ShowNameTags && Time.unscaledTime >= nextTagRefresh)
            {
                nextTagRefresh = Time.unscaledTime + 0.5f;
                try
                {
                    RefreshTagTargets();
                }
                catch (Exception ex)
                {
                    nextTagRefresh = Time.unscaledTime + 10f;
                    log.LogWarning("Name tag refresh failed: " + ex.Message);
                }
            }
        }

        // ---------------------------------------------------------------- game-styled drawing

        private static bool gameUiActive;
        private static RectTransform hudRoot;
        private static RectTransform toastColumn;
        private static RectTransform statusPlate;
        private static TMPro.TextMeshProUGUI statusView;
        private static string statusCache = string.Empty;
        private static float nextStatusText;
        private static readonly List<KeyValuePair<RectTransform, TMPro.TextMeshProUGUI>> toastViews = new List<KeyValuePair<RectTransform, TMPro.TextMeshProUGUI>>();
        private static readonly List<TMPro.TextMeshProUGUI> tagViews = new List<TMPro.TextMeshProUGUI>();

        /// <summary>Tests: what the game-styled HUD shows.</summary>
        internal static string DescribeGameUi()
        {
            if (!gameUiActive || hudRoot == null)
            {
                return "gameUi=False";
            }
            string[] tags = tagViews.Where(t => t.gameObject.activeSelf)
                .Select(t => t.text + "@" + t.rectTransform.anchoredPosition.x.ToString("F0") + "," + t.rectTransform.anchoredPosition.y.ToString("F0")).ToArray();
            string[] toasts = toastViews.Where(t => t.Key.gameObject.activeSelf).Select(t => t.Value.text).ToArray();
            string status = statusPlate.gameObject.activeSelf ? "[" + statusView.text.Replace("\n", " / ") + "]" : "hidden";
            return "gameUi=True status=" + status + " toasts=[" + string.Join(" ; ", toasts) + "] tags=[" + string.Join(" ; ", tags) + "]";
        }

        /// <summary>
        /// From the plugin's LateUpdate, after the camera has moved: the status plate, the
        /// announcements under it and the name tags, drawn with the game's own fonts and plate.
        /// The column sits under the location plate the game shows at the top right.
        /// </summary>
        internal static void UpdateGameUi()
        {
            gameUiActive = GameUi.Ready;
            if (!gameUiActive)
            {
                return;
            }
            if (hudRoot == null)
            {
                hudRoot = GameUi.NewRect(GameUi.HudLayer, "GK2Coop.Hud");
                GameUi.Stretch(hudRoot, 0f, 0f, 0f, 0f);
                toastColumn = GameUi.NewRect(hudRoot, "Column");
                toastColumn.anchorMin = toastColumn.anchorMax = new Vector2(1f, 1f);
                toastColumn.pivot = new Vector2(1f, 1f);
                var group = toastColumn.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                group.spacing = 3f;
                group.childAlignment = TextAnchor.UpperRight;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;
                var fitter = toastColumn.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                statusView = GameUi.NewPlateText(toastColumn, "Status", GameUi.TextKind.Body, out statusPlate);
                tagViews.Clear();
                toastViews.Clear();
            }
            toastColumn.anchoredPosition = new Vector2(-6f, -40f);

            if (ShowStatus && Time.unscaledTime >= nextStatusText)
            {
                nextStatusText = Time.unscaledTime + 0.25f;
                NetworkManager session = NetworkManager.Singleton;
                bool inSession = session != null && session.IsListening && !CoopSession.Rejected;
                statusCache = inSession ? BuildStatusText() : null;
            }
            bool statusShown = ShowStatus && !string.IsNullOrEmpty(statusCache);
            if (statusPlate.gameObject.activeSelf != statusShown)
            {
                statusPlate.gameObject.SetActive(statusShown);
            }
            if (statusShown)
            {
                GameUi.SetKind(statusView, GameUi.TextKind.Body);
                GameUi.SetPlateText(statusView, statusCache, 230f);
            }

            int shown = 0;
            if (ShowStatus)
            {
                foreach (KeyValuePair<string, bool> toast in CoopStatus.ActiveToasts())
                {
                    if (shown == toastViews.Count)
                    {
                        RectTransform plate;
                        TMPro.TextMeshProUGUI text = GameUi.NewPlateText(toastColumn, "Toast", GameUi.TextKind.Body, out plate);
                        toastViews.Add(new KeyValuePair<RectTransform, TMPro.TextMeshProUGUI>(plate, text));
                    }
                    KeyValuePair<RectTransform, TMPro.TextMeshProUGUI> view = toastViews[shown++];
                    if (!view.Key.gameObject.activeSelf) view.Key.gameObject.SetActive(true);
                    GameUi.SetKind(view.Value, toast.Value ? GameUi.TextKind.Notice : GameUi.TextKind.Body);
                    GameUi.SetPlateText(view.Value, toast.Key, 230f);
                }
            }
            for (int i = shown; i < toastViews.Count; i++)
            {
                if (toastViews[i].Key.gameObject.activeSelf) toastViews[i].Key.gameObject.SetActive(false);
            }

            if (lateTags == null && hudRoot != null)
            {
                // Name tags last in the frame, after the camera has followed the player.
                lateTags = hudRoot.gameObject.AddComponent<LateTags>();
            }
        }

        private static LateTags lateTags;

        [DefaultExecutionOrder(32000)]
        private sealed class LateTags : MonoBehaviour
        {
            private void LateUpdate()
            {
                if (gameUiActive)
                {
                    UpdateTagViews();
                    TraceLate();
                }
            }
        }

        // ---------------------------------------------------------------- tests: a frame-by-frame trace

        private static int traceFramesLeft;
        private static readonly List<string> traceRows = new List<string>();
        private static bool tracePreRenderHooked;
        private static Transform traceAnchor;
        private static Transform traceView;
        private static TMPro.TextMeshProUGUI traceTag;

        /// <summary>Tests: record the local player's tag for the next frames: where it was put, and where the camera drew from.</summary>
        internal static string TraceTagsForTest(int frames)
        {
            traceRows.Clear();
            traceFramesLeft = frames;
            if (!tracePreRenderHooked)
            {
                tracePreRenderHooked = true;
                Camera.onPreRender += TracePreRender;
            }
            return "tracing " + frames + " frames";
        }

        /// <summary>Tests: for each shown name tag, how far its text sits from the centre of its box.</summary>
        internal static string TagCentresForTest()
        {
            var parts = new List<string>();
            Camera world = ResolveWorldCamera();
            Canvas canvas = hudRoot == null ? null : hudRoot.GetComponentInParent<Canvas>();
            Camera ui = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            int index = 0;
            foreach (TagTarget target in tagTargets)
            {
                if (target.Anchor == null) continue;
                if (index >= tagViews.Count) break;
                TMPro.TextMeshProUGUI tag = tagViews[index++];
                if (tag == null || !tag.gameObject.activeInHierarchy) continue;
                tag.ForceMeshUpdate();
                float inBox = tag.textBounds.center.x - tag.rectTransform.rect.center.x;
                string vsHead = "?";
                if (world != null && target.Head != null)
                {
                    // Where the text's middle and the head's middle are on the screen, in pixels.
                    Vector2 text = RectTransformUtility.WorldToScreenPoint(ui, tag.rectTransform.TransformPoint(tag.textBounds.center));
                    Vector3 head = world.WorldToScreenPoint(target.Head.bounds.center);
                    float scale = world.pixelWidth > 0 ? (float)Screen.width / world.pixelWidth : 1f;
                    vsHead = (text.x - head.x * scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    // And how far the bottom of the text is above the top of the drawn head.
                    Vector2 textBottom = RectTransformUtility.WorldToScreenPoint(ui, tag.rectTransform.TransformPoint(new Vector3(tag.textBounds.center.x, tag.textBounds.min.y, 0f)));
                    Vector3 headTop = world.WorldToScreenPoint(target.Head.bounds.center + new Vector3(0f, target.Head.bounds.extents.y, 0f));
                    vsHead += " aboveHead=" + (textBottom.y - headTop.y * scale).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    // World units: the bubble point above the head's centre, and the head's half-height.
                    vsHead += " bubbleLift=" + (target.Anchor.position.y - target.Head.bounds.center.y).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                              " headExtent=" + target.Head.bounds.extents.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                }
                parts.Add(tag.text + " inbox=" + inBox.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " vsHead=" + vsHead + " margin=" + tag.margin);
            }
            return parts.Count == 0 ? "no tags" : string.Join("; ", parts.ToArray());
        }

        internal static string TraceResultForTest()
        {
            return "rows=" + traceRows.Count + " skipped=" + skippedTagFrames + (traceFramesLeft > 0 ? " (running)" : string.Empty) + "\n" + string.Join("\n", traceRows.ToArray());
        }

        private static string V(Vector3 v)
        {
            return v.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + v.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," + v.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void TraceLate()
        {
            if (traceFramesLeft <= 0)
            {
                return;
            }
            Camera camera = ResolveWorldCamera();
            traceAnchor = null;
            traceTag = null;
            int index = 0;
            foreach (TagTarget target in tagTargets)
            {
                if (target.Anchor == null) continue;
                if (target.IsLocal && index < tagViews.Count)
                {
                    traceAnchor = target.Anchor;
                    traceTag = tagViews[index];
                    Transform view = traceAnchor;
                    while (view != null && view.GetComponent("PlayerView") == null) view = view.parent;
                    traceView = view;
                }
                index++;
            }
            if (camera == null || traceAnchor == null)
            {
                return;
            }
            traceRows.Add("L f=" + Time.frameCount + " cam=" + V(camera.transform.position) + " off=" + V(Shader.GetGlobalVector("_CameraOffset")) +
                          " anchor=" + V(traceAnchor.position) + " view=" + (traceView == null ? "-" : V(traceView.position)) +
                          " tag=" + traceTag.rectTransform.anchoredPosition.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "," + traceTag.rectTransform.anchoredPosition.y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void TracePreRender(Camera rendering)
        {
            if (traceFramesLeft <= 0 || traceAnchor == null)
            {
                return;
            }
            Camera camera = ResolveWorldCamera();
            if (rendering != camera)
            {
                return;
            }
            traceFramesLeft--;
            Vector3 screen = camera.WorldToScreenPoint(traceAnchor.position);
            traceRows.Add("R f=" + Time.frameCount + " cam=" + V(camera.transform.position) + " off=" + V(Shader.GetGlobalVector("_CameraOffset")) +
                          " anchor=" + V(traceAnchor.position) + " view=" + (traceView == null ? "-" : V(traceView.position)) +
                          " px=" + screen.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," + screen.y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                          " camPx=" + camera.pixelWidth + "x" + camera.pixelHeight);
        }

        private static int steadyCameraWidth;
        private static float steadyCameraAt = -10f;
        private static int skippedTagFrames;

        private static void UpdateTagViews()
        {
            int shown = 0;
            Camera camera = ShowNameTags ? ResolveWorldCamera() : null;
            if (camera != null)
            {
                // In single frames (walking off, the camera coming to rest) the world camera, Screen
                // and the HUD report a low-resolution buffer's size (640x360) instead of the
                // window's; a tag placed in such a frame jumped by dozens of pixels and back, the
                // shaking seen after walking. Those frames leave the tags where they were.
                int width = camera.pixelWidth;
                float now = Time.unscaledTime;
                if (width >= steadyCameraWidth || now - steadyCameraAt > 2f)
                {
                    steadyCameraWidth = width;
                    steadyCameraAt = now;
                }
                else if (width < steadyCameraWidth * 3 / 4)
                {
                    skippedTagFrames++;
                    return;
                }
                if (width == steadyCameraWidth)
                {
                    steadyCameraAt = now;
                }
                Rect area = hudRoot.rect;
                foreach (TagTarget target in tagTargets)
                {
                    if (!TagPoint(camera, target, out Vector3 viewport))
                    {
                        continue;
                    }
                    if (!camera.orthographic && viewport.z <= 0f)
                    {
                        continue;
                    }
                    var local = new Vector2((viewport.x - 0.5f) * area.width, (viewport.y - 0.5f) * area.height);
                    if (shown == tagViews.Count)
                    {
                        TMPro.TextMeshProUGUI created = GameUi.NewText(hudRoot, GameUi.TextKind.Header, string.Empty);
                        created.name = "NameTag";
                        created.alignment = TMPro.TextAlignmentOptions.Bottom;
                        RectTransform rect = created.rectTransform;
                        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                        rect.pivot = new Vector2(0.5f, 0f);
                        rect.sizeDelta = new Vector2(160f, 20f);
                        // The game's header text comes with inner margins (for its dialog header):
                        // they pushed the name to one side of the character.
                        created.margin = Vector4.zero;
                        tagViews.Add(created);
                    }
                    TMPro.TextMeshProUGUI tag = tagViews[shown++];
                    if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
                    if (tag.alignment != TMPro.TextAlignmentOptions.Bottom) tag.alignment = TMPro.TextAlignmentOptions.Bottom;
                    if (tag.margin != Vector4.zero) tag.margin = Vector4.zero;
                    if (tag.text != target.Label)
                    {
                        tag.text = target.Label;
                        GameUi.EnsureGlyphs(tag);
                    }
                    Color colour = target.IsLocal ? new Color(0.75f, 0.95f, 1f) : new Color(1f, 0.87f, 0.6f);
                    if (tag.color != colour) tag.color = colour;
                    tag.rectTransform.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y) + 2f);
                }
            }
            for (int i = shown; i < tagViews.Count; i++)
            {
                if (tagViews[i].gameObject.activeSelf) tagViews[i].gameObject.SetActive(false);
            }
            // Players standing together would print their names over each other: stack them.
            for (int i = 0; i < shown; i++)
            {
                for (int j = 0; j < shown; j++)
                {
                    if (i == j) continue;
                    RectTransform a = tagViews[i].rectTransform;
                    RectTransform b = tagViews[j].rectTransform;
                    Vector2 pa = a.anchoredPosition;
                    Vector2 pb = b.anchoredPosition;
                    float halfWidths = (tagViews[i].preferredWidth + tagViews[j].preferredWidth) * 0.5f;
                    if (Mathf.Abs(pa.x - pb.x) < halfWidths && pa.y >= pb.y && pa.y - pb.y < 11f && (pa.y != pb.y || i > j))
                    {
                        a.anchoredPosition = new Vector2(pa.x, pb.y + 11f);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- drawing

        internal static void Draw()
        {
            if (gameUiActive || (Event.current != null && Event.current.type != EventType.Repaint))
            {
                return;
            }
            EnsureStyles();
            if (ShowNameTags)
            {
                DrawNameTags();
            }
            if (ShowStatus)
            {
                DrawStatus();
            }
        }

        private static void EnsureStyles()
        {
            if (statusStyle != null)
            {
                return;
            }
            statusBackground = new Texture2D(1, 1);
            statusBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f));
            statusBackground.Apply();

            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                padding = new RectOffset(8, 8, 6, 6)
            };
            statusStyle.normal.textColor = Color.white;
            statusStyle.normal.background = statusBackground;

            tagStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            tagShadowStyle = new GUIStyle(tagStyle);
            tagShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);

            toastStyle = new GUIStyle(statusStyle) { wordWrap = true, fontSize = 12 };
            toastStyle.normal.textColor = new Color(0.86f, 0.94f, 1f);
            toastProblemStyle = new GUIStyle(toastStyle);
            toastProblemStyle.normal.textColor = new Color(1f, 0.78f, 0.55f);
        }

        private static void DrawStatus()
        {
            // Same 1080p-based scaling as the co-op menu, so the panel stays readable on large screens.
            float scale = CoopMenu.UiScale;
            float screenWidth = Screen.width / scale;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                string text = BuildStatusText();
                if (!string.IsNullOrEmpty(text))
                {
                    text = string.Join("\n", text.Split('\n').Select(line => Wrap(line, 58)).ToArray());
                }
                if (string.IsNullOrEmpty(text))
                {
                    DrawToasts(12f, screenWidth);
                    return;
                }
                Vector2 size = statusStyle.CalcSize(new GUIContent(text));
                var area = new Rect(screenWidth - size.x - 12f, 12f, size.x, size.y);
                GUI.Label(area, text, statusStyle);
                DrawToasts(area.yMax + 6f, screenWidth);
            }
            finally
            {
                GUI.matrix = saved;
            }
        }

        /// <summary>
        /// Short-lived messages under the status panel: someone joined or left, a retry is under
        /// way, a connection failed. Problems are tinted so they read differently at a glance
        /// from the ordinary comings and goings.
        /// </summary>
        private static void DrawToasts(float top, float screenWidth)
        {
            float y = top;
            foreach (KeyValuePair<string, bool> toast in CoopStatus.ActiveToasts())
            {
                GUIStyle style = toast.Value ? toastProblemStyle : toastStyle;
                var content = new GUIContent(toast.Key);
                float width = Mathf.Min(420f, style.CalcSize(content).x);
                float height = style.CalcHeight(content, width);
                GUI.Label(new Rect(screenWidth - width - 12f, y, width, height), content, style);
                y += height + 4f;
            }
        }

        /// <summary>Hard-wraps a sentence so a long failure message stays inside the panel.</summary>
        private static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width)
            {
                return text ?? string.Empty;
            }
            var builder = new System.Text.StringBuilder(text.Length + 8);
            int lineLength = 0;
            foreach (string word in text.Split(' '))
            {
                if (lineLength > 0 && lineLength + word.Length + 1 > width)
                {
                    builder.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    builder.Append(' ');
                    lineLength++;
                }
                builder.Append(word);
                lineLength += word.Length;
            }
            return builder.ToString();
        }

        private static string BuildStatusText()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening)
            {
                // A failure before any session exists is exactly the case that used to be
                // invisible, so the phase is reported rather than a flat "offline".
                switch (CoopStatus.Phase)
                {
                    case CoopPhase.Connecting:
                        return L.T("Connecting…") + "\n" + CoopStatus.Detail;
                    case CoopPhase.Failed:
                        return L.T("Could not connect") + "\n" + CoopStatus.Detail;
                    default:
                        return null;
                }
            }
            if (CoopSession.Rejected)
            {
                return L.T("Connection refused") + "\n" + CoopStatus.Detail;
            }

            SampleRtt();

            if (netcode.IsHost)
            {
                var peers = netcode.ConnectedClientsIds
                    .Where(id => id != netcode.LocalClientId)
                    .ToArray();
                if (peers.Length == 0)
                {
                    return L.T("Hosting") + "\n" + L.T("Waiting for players…");
                }
                string names = string.Join(", ", peers.Select(id => CoopSession.NameFor(id) + Elsewhere(id)).ToArray());
                return L.T("Hosting") + "\n" + L.F("With {0}", names) + "\n" + L.F("Ping {0} ms", rttText);
            }

            if (!netcode.IsConnectedClient)
            {
                return L.T("Connecting…");
            }
            return L.F("In {0}'s world", CoopSession.NameFor(NetworkManager.ServerClientId) + Elsewhere(NetworkManager.ServerClientId)) + "\n" +
                   L.F("Ping {0} ms", rttText);
        }

        /// <summary>" (in Prison)" for a player in another scene, whom the scene sync hides.</summary>
        private static string Elsewhere(ulong clientId)
        {
            return CoopSceneSync.IsElsewhere(clientId) ? " " + L.F("(in {0})", L.Place(CoopSceneSync.SceneOf(clientId))) : string.Empty;
        }

        private static void SampleRtt()
        {
            if (Time.unscaledTime < nextRttSample)
            {
                return;
            }
            nextRttSample = Time.unscaledTime + 1f;
            try
            {
                NetworkManager netcode = NetworkManager.Singleton;
                object transport = netcode.NetworkConfig == null ? null : netcode.NetworkConfig.NetworkTransport;
                if (transport == null)
                {
                    rttText = "--";
                    return;
                }
                MethodInfo getRtt = transport.GetType().GetMethod("GetCurrentRtt", BindingFlags.Instance | BindingFlags.Public);
                if (getRtt == null)
                {
                    rttText = "--";
                    return;
                }
                ulong target = netcode.IsHost
                    ? netcode.ConnectedClientsIds.FirstOrDefault(id => id != netcode.LocalClientId)
                    : NetworkManager.ServerClientId;
                object value = getRtt.Invoke(transport, new object[] { target });
                rttText = Convert.ToInt64(value).ToString();
            }
            catch
            {
                rttText = "--";
            }
        }

        /// <summary>
        /// Rebuilds the tag target list. Called from the one-second tick, never from OnGUI, which
        /// Unity invokes several times per frame — a scene-wide object search there would be paid
        /// two or more times every frame.
        /// </summary>
        private static void RefreshTagTargets()
        {
            int previousCount = tagTargets.Count;
            tagTargets.Clear();
            Type bodyType = Plugin.FindGameType("PlayerPhysicalBody");
            if (bodyType == null)
            {
                return;
            }

            object localPlayerData = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            foreach (Component body in Resources.FindObjectsOfTypeAll(bodyType)
                         .OfType<Component>()
                         .Where(candidate => candidate.gameObject.scene.IsValid() && candidate.gameObject.activeInHierarchy))
            {
                object bodyData = CoopDiagnostics.GetMember(body, "playerData");
                if (bodyData == null)
                {
                    continue;
                }
                bool isLocal = ReferenceEquals(bodyData, localPlayerData);
                if (!isLocal && TryResolveClientId(bodyData, out ulong tagClient) && CoopSceneSync.IsElsewhere(tagClient))
                {
                    continue;
                }
                string label = ResolveLabel(bodyData, isLocal);
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }
                tagTargets.Add(new TagTarget
                {
                    Anchor = ResolveAnchor(body),
                    Center = body.transform,
                    Head = ResolveHead(body),
                    Label = label,
                    IsLocal = isLocal
                });
            }
            if (tagTargets.Count != previousCount)
            {
                log.LogInfo("Name tag targets: " + string.Join(", ",
                    tagTargets.Select(t => t.Label + (t.IsLocal ? " (local)" : " (remote)")).ToArray()));
            }
        }

        /// <summary>
        /// Tags are anchored to <c>PlayerView.BubblePoint</c>, the same transform the game uses for
        /// speech bubbles, so they sit where overhead UI is expected to appear. IMGUI has no depth
        /// test, so a tag stays visible through geometry: that is intentional for finding your
        /// partner, and the whole feature can be turned off.
        /// </summary>
        private static void DrawNameTags()
        {
            Camera camera = ResolveWorldCamera();
            if (camera == null)
            {
                return;
            }
            // The world camera renders into a low-resolution RenderTexture that is blitted up to
            // the window, so WorldToScreenPoint returns render-texture pixels, not screen pixels.
            // Normalise through the camera's own pixel rect before converting to GUI space.
            float cameraWidth = camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width;
            float cameraHeight = camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height;

            // BlitCameraRT does Graphics.Blit(renderTexture, destination): a stretched full-screen
            // blit with no letterboxing, so a straight proportional mapping is the correct one.
            bool reportGeometry = Time.unscaledTime >= nextTagGeometryReport;
            var geometry = new List<string>();
            if (reportGeometry)
            {
                nextTagGeometryReport = Time.unscaledTime + 4f;
            }

            for (int index = 0; index < tagTargets.Count; index++)
            {
                TagTarget target = tagTargets[index];
                if (target.Anchor == null)
                {
                    continue;
                }
                Vector3 screen = camera.WorldToScreenPoint(target.Anchor.position);

                // GUI space has its origin at the top left; screen space at the bottom left.
                float x = screen.x / cameraWidth * Screen.width;
                float y = (1f - screen.y / cameraHeight) * Screen.height;

                // Only a perspective camera puts points behind the lens at z <= 0. For an
                // orthographic camera z is just distance along the view axis and can legitimately
                // be zero or negative for a visible object, so the same test hides every tag.
                bool behindCamera = !camera.orthographic && screen.z <= 0f;

                if (reportGeometry)
                {
                    bool onScreen = x > -200f && x < Screen.width + 200f &&
                                    y > -200f && y < Screen.height + 200f;
                    geometry.Add(target.Label +
                                 " cam=(" + screen.x.ToString("F0") + "," + screen.y.ToString("F0") + "," + screen.z.ToString("F1") + ")" +
                                 " gui=(" + x.ToString("F0") + "," + y.ToString("F0") + ")" +
                                 (behindCamera ? " SKIPPED-behind" : onScreen ? " on-screen" : " OFF-SCREEN"));
                }
                if (behindCamera)
                {
                    continue;
                }

                var content = new GUIContent(target.Label);
                Vector2 size = tagStyle.CalcSize(content);
                var rect = new Rect(x - size.x * 0.5f, y - size.y, size.x, size.y);

                tagStyle.normal.textColor = target.IsLocal ? new Color(0.75f, 0.95f, 1f) : new Color(1f, 0.87f, 0.6f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), content, tagShadowStyle);
                GUI.Label(rect, content, tagStyle);

            }

            if (reportGeometry)
            {
                log.LogInfo("Tag geometry: screen=" + Screen.width + "x" + Screen.height +
                            " cam=" + cameraWidth + "x" + cameraHeight +
                            " ortho=" + camera.orthographic +
                            " targets=" + tagTargets.Count + " :: " +
                            (geometry.Count == 0 ? "nothing drawn" : string.Join(" | ", geometry.ToArray())));
            }
        }

        private static string ResolveLabel(object bodyData, bool isLocal)
        {
            if (isLocal)
            {
                return CoopSession.LocalName;
            }
            ulong clientId;
            return TryResolveClientId(bodyData, out clientId) ? CoopSession.NameFor(clientId) : null;
        }

        /// <summary>Maps a body's player data back to the network player that owns it.</summary>
        /// <summary>The point above another player's character where the game puts speech bubbles.</summary>
        internal static Transform BubblePointOf(ulong clientId)
        {
            Type bodyType = Plugin.FindGameType("PlayerPhysicalBody");
            if (bodyType == null)
            {
                return null;
            }
            foreach (Component body in Resources.FindObjectsOfTypeAll(bodyType).OfType<Component>())
            {
                if (!body.gameObject.scene.IsValid() || !body.gameObject.activeInHierarchy)
                {
                    continue;
                }
                object bodyData = CoopDiagnostics.GetMember(body, "playerData");
                if (bodyData != null && TryResolveClientId(bodyData, out ulong owner) && owner == clientId)
                {
                    return ResolveAnchor(body);
                }
            }
            return null;
        }

        internal static bool TryResolveClientId(object bodyData, out ulong clientId)
        {
            clientId = 0;
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            if (save == null)
            {
                return false;
            }

            object hostPlayer = CoopDiagnostics.GetMember(save, "hostPlayer");
            if (hostPlayer != null && ReferenceEquals(CoopDiagnostics.GetMember(hostPlayer, "playerData"), bodyData))
            {
                clientId = Convert.ToUInt64(CoopDiagnostics.GetMember(hostPlayer, "clientId"));
                return true;
            }

            var clients = CoopDiagnostics.GetMember(save, "clientPlayers") as System.Collections.IEnumerable;
            if (clients == null)
            {
                return false;
            }
            foreach (object client in clients)
            {
                if (ReferenceEquals(CoopDiagnostics.GetMember(client, "playerData"), bodyData))
                {
                    clientId = Convert.ToUInt64(CoopDiagnostics.GetMember(client, "clientId"));
                    return true;
                }
            }
            return false;
        }

        private static string lastCameraReport;
        private static float nextCameraReport;
        private static float nextTagGeometryReport;

        /// <summary>
        /// Prefers the game's own world camera over <c>Camera.main</c>, which depends on a tag that
        /// need not be set and may resolve differently under another render mode.
        /// </summary>
        private static Camera ResolveWorldCamera()
        {
            object cameraSystem = CoopDiagnostics.GetStatic(Plugin.FindGameType("CameraSystem"), "Instance");
            var camera = cameraSystem == null ? null : CoopDiagnostics.GetMember(cameraSystem, "WorldCamera") as Camera;
            if (camera == null)
            {
                camera = Camera.main;
            }
            if (camera != null)
            {
                // Re-report when the render target changes: the world camera only gains its
                // RenderTexture once gameplay starts, so a single startup line is misleading.
                string report = "Name tag camera: " + camera.name +
                            "; cameraPixels=" + camera.pixelWidth + "x" + camera.pixelHeight +
                            "; screen=" + Screen.width + "x" + Screen.height +
                            "; targetTexture=" + (camera.targetTexture == null
                                ? "none"
                                : camera.targetTexture.width + "x" + camera.targetTexture.height) +
                            "; source=" + (cameraSystem == null ? "Camera.main" : "CameraSystem.WorldCamera");
                if (!string.Equals(report, lastCameraReport, StringComparison.Ordinal) &&
                    Time.unscaledTime >= nextCameraReport)
                {
                    lastCameraReport = report;
                    nextCameraReport = Time.unscaledTime + 3f;
                    log.LogInfo(report);
                }
            }
            return camera;
        }

        /// <summary>The head sprite of a body ("hed", as the skin changer names it), if drawn.</summary>
        private static Renderer ResolveHead(Component body)
        {
            foreach (SpriteRenderer sprite in body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite != null && sprite.name == "hed")
                {
                    return sprite;
                }
            }
            return null;
        }

        private static Transform ResolveAnchor(Component body)
        {
            object view = CoopDiagnostics.GetMember(body, "playerView");
            var bubblePoint = view == null ? null : CoopDiagnostics.GetMember(view, "BubblePoint") as Transform;
            return bubblePoint != null ? bubblePoint : body.transform;
        }
    }
}
