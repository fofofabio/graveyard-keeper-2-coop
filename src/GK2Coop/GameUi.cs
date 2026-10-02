using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GK2Coop
{
    /// <summary>
    /// The game's own look for the mod's screens.
    ///
    /// Graveyard Keeper 2 builds its windows with uGUI and TextMeshPro pixel fonts, which IMGUI
    /// cannot draw. The game's own Steam Workshop window solves the same problem for UI made in
    /// code: it borrows its parts from the standard dialog window (<c>UIDialogWindow</c>). This kit
    /// does the same — the window frame and header bar, the red button with its hover and pressed
    /// text styles, the header and body text, and an input field (from the item count window) are
    /// copied from the live game, so the mod's windows look and react like the game's.
    ///
    /// Everything sits in one layer under the game's own UI root, so it shares the game's canvas
    /// scale, pixel snapping and input. When the parts cannot be found (a game update renamed
    /// them), <see cref="Ready"/> stays false and callers draw their IMGUI fallback instead.
    /// </summary>
    internal static class GameUi
    {
        internal enum TextKind
        {
            Body,
            Hint,
            Header,
            Notice
        }

        private static ManualLogSource log;
        private static float nextResolve;
        private static bool reportedMissing;

        private static Transform uiRoot;
        private static GameObject frameTemplate;
        private static string frameHeaderPath;
        private static string frameClosePath;
        private static GameObject buttonTemplate;
        private static GameObject headerTemplate;
        private static GameObject bodyTemplate;
        private static GameObject inputTemplate;
        private static Sprite plateSprite;
        private static RectTransform layer;
        private static RectTransform hudLayer;

        internal static readonly Color HintColour = new Color(0.588f, 0.553f, 0.533f, 1f);
        internal static readonly Color BodyColour = new Color(0.86f, 0.82f, 0.78f, 1f);
        internal static readonly Color NoticeColour = new Color(1f, 0.741f, 0f, 1f);

        internal static bool Enabled { get; set; } = true;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        /// <summary>True when the game's parts are found and the mod's layer exists.</summary>
        internal static bool Ready
        {
            get
            {
                if (!Enabled)
                {
                    return false;
                }
                if (layer != null && hudLayer != null && uiRoot != null && buttonTemplate != null && frameTemplate != null)
                {
                    return true;
                }
                if (Time.unscaledTime < nextResolve)
                {
                    return false;
                }
                nextResolve = Time.unscaledTime + 2f;
                try
                {
                    return Resolve();
                }
                catch (Exception ex)
                {
                    if (!reportedMissing)
                    {
                        reportedMissing = true;
                        log.LogWarning("Game-styled UI unavailable, using the plain look: " + ex.Message);
                    }
                    return false;
                }
            }
        }

        /// <summary>The layer every mod window lives in, stretched over the game's UI root.</summary>
        internal static RectTransform Layer => Ready ? layer : null;

        private static string styledLanguage;

        /// <summary>
        /// After the game's language changes, reapplies the game's text styles to every text of
        /// the mod. The styles pick the font per language (Japanese, Chinese and Korean need their
        /// own); the game applies them when a text is shown, so texts that stay on screen, such as
        /// an announcement or the chat line, would keep the old language's font.
        /// </summary>
        internal static void RestyleOnLanguageChange()
        {
            string language = L.Language;
            if (language == styledLanguage || layer == null || hudLayer == null)
            {
                return;
            }
            bool first = styledLanguage == null;
            styledLanguage = language;
            if (first)
            {
                return;
            }
            int restyled = 0;
            foreach (RectTransform root in new[] { layer, hudLayer })
            {
                foreach (LazyBearTechnology.TextStyleComponent style in root.GetComponentsInChildren<LazyBearTechnology.TextStyleComponent>(true))
                {
                    MethodInfo apply = style.GetType().GetMethod("ApplyStyle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (apply != null)
                    {
                        apply.Invoke(style, null);
                        EnsureGlyphs(style.GetComponent<TextMeshProUGUI>());
                        restyled++;
                    }
                }
            }
            log.LogInfo("Language is now " + language + "; restyled " + restyled + " texts of the mod.");
        }

        /// <summary>
        /// In the world the HUD sits under the game's windows (an open inventory covers it); on
        /// the main menu, whose background fills the screen, it sits just under the mod's menus.
        /// </summary>
        internal static void PlaceHud(bool onMainMenu)
        {
            if (hudLayer == null || layer == null || uiRoot == null)
            {
                return;
            }
            // During a story scene the mod's status, names and chat step aside as the game's HUD does.
            bool show = !InCinematic;
            if (hudLayer.gameObject.activeSelf != show)
            {
                hudLayer.gameObject.SetActive(show);
            }
            int wanted = onMainMenu ? uiRoot.childCount - 2 : 0;
            if (onMainMenu && layer.GetSiblingIndex() != uiRoot.childCount - 1)
            {
                layer.SetAsLastSibling();
            }
            if (hudLayer.GetSiblingIndex() != wanted)
            {
                hudLayer.SetSiblingIndex(wanted);
            }
        }

        /// <summary>The layer under the game's own windows, for the HUD and chat.</summary>
        internal static RectTransform HudLayer => Ready ? hudLayer : null;

        /// <summary>Screen pixels to the layer's own units, for things placed over the world.</summary>
        internal static bool ScreenToLayer(Vector2 screen, out Vector2 local)
        {
            local = Vector2.zero;
            if (layer == null)
            {
                return false;
            }
            Canvas canvas = layer.GetComponentInParent<Canvas>();
            Camera camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, camera, out local);
        }

        private static bool Resolve()
        {
            Component dialog = null;
            Type dialogType = Plugin.FindGameType("UIDialogWindow");
            if (dialogType != null)
            {
                foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(dialogType))
                {
                    var component = found as Component;
                    if (component != null && component.gameObject.scene.IsValid())
                    {
                        dialog = component;
                        break;
                    }
                }
            }
            if (dialog == null)
            {
                return false;
            }

            var header = Field(dialog, "header") as TextMeshProUGUI;
            var body = Field(dialog, "information") as TextMeshProUGUI;
            var buttonHolder = Field(dialog, "buttonPrefab") as Component;
            object layout = Field(dialog, "genericWindowLayout");
            Transform frame = layout is Component layoutComponent ? layoutComponent.transform.Find("Frame") : null;
            if (header == null || body == null || buttonHolder == null || frame == null)
            {
                throw new InvalidOperationException("the game's dialog window has changed (header=" + (header != null) + ", body=" + (body != null) +
                                                    ", button=" + (buttonHolder != null) + ", frame=" + (frame != null) + ")");
            }

            uiRoot = dialog.transform.parent;
            headerTemplate = header.gameObject;
            bodyTemplate = body.gameObject;
            buttonTemplate = buttonHolder.gameObject;
            frameTemplate = frame.gameObject;
            frameHeaderPath = header.transform.IsChildOf(frame) ? RelativePath(frame, header.transform) : null;
            Button close = null;
            foreach (Button candidate in frame.GetComponentsInChildren<Button>(true))
            {
                if (candidate.name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    close = candidate;
                    break;
                }
            }
            frameClosePath = close == null ? null : RelativePath(frame, close.transform);

            foreach (Image image in dialog.GetComponentsInChildren<Image>(true))
            {
                // The dark item-cell background: a small sliced plate that suits HUD text.
                if (image.sprite != null && image.sprite.name == "comm-item_cell-dark")
                {
                    plateSprite = image.sprite;
                    break;
                }
            }

            Type inputType = typeof(TMP_InputField);
            foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(inputType))
            {
                var field = found as TMP_InputField;
                if (field != null && field.gameObject.scene.IsValid() && field.textComponent != null)
                {
                    inputTemplate = field.gameObject;
                    break;
                }
            }

            if (layer == null)
            {
                var go = new GameObject("GK2Coop.UI", typeof(RectTransform));
                go.layer = uiRoot.gameObject.layer;
                layer = (RectTransform)go.transform;
                layer.SetParent(uiRoot, false);
                Stretch(layer, 0f, 0f, 0f, 0f);
                // Its own sorting, so the game's windows (the main menu's logo and buttons among
                // them) cannot draw over the mod's menus whatever order the game keeps its own in.
                Canvas parentCanvas = uiRoot.GetComponentInParent<Canvas>();
                var canvas = go.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingLayerID = parentCanvas != null ? parentCanvas.sortingLayerID : 0;
                // Above any window the game sorts on its own (the main menu does, high).
                canvas.sortingOrder = 30000;
                go.AddComponent<GraphicRaycaster>();
            }
            layer.SetAsLastSibling();
            if (hudLayer == null)
            {
                // Status, names and chat go under every game window; menus go over them.
                var go = new GameObject("GK2Coop.HUD", typeof(RectTransform));
                go.layer = uiRoot.gameObject.layer;
                hudLayer = (RectTransform)go.transform;
                hudLayer.SetParent(uiRoot, false);
                Stretch(hudLayer, 0f, 0f, 0f, 0f);
            }
            hudLayer.SetAsFirstSibling();
            log.LogInfo("Game-styled UI ready: frame, header, buttons" + (inputTemplate != null ? ", input field" : " (no input field; typing uses the plain look)") +
                        " borrowed from the game's dialog window; layer under " + uiRoot.name + ".");
            return true;
        }

        private static object Field(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }
            return null;
        }

        private static string RelativePath(Transform root, Transform child)
        {
            string path = child.name;
            for (Transform t = child.parent; t != null && t != root; t = t.parent)
            {
                path = t.name + "/" + path;
            }
            return path;
        }

        internal static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        internal static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Gives a label a font that has all its characters: its own when it does, otherwise the
        /// game's header font, which carries the fallbacks for Japanese, Chinese and Korean.
        /// </summary>
        internal static void EnsureGlyphs(TextMeshProUGUI label)
        {
            if (label == null || label.font == null || string.IsNullOrEmpty(label.text))
            {
                return;
            }
            uint[] missing;
            if (label.font.HasCharacters(label.text, out missing, true, true))
            {
                return;
            }
            TMP_FontAsset found;
            if (!fontFor.TryGetValue(label.text, out found))
            {
                found = null;
                foreach (TMP_FontAsset candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (candidate != null && candidate != label.font && !IsAnyScript(candidate) && candidate.HasCharacters(label.text, out missing, true, true))
                    {
                        found = candidate;
                        break;
                    }
                }
                if (found == null)
                {
                    // Several scripts in one line (Korean, Japanese, Chinese, Cyrillic…): no single
                    // font has them all, so the label's own font with every loaded font behind it.
                    found = AnyScriptFont(label.font);
                }
                if (fontFor.Count > 200)
                {
                    fontFor.Clear();
                }
                fontFor[label.text] = found;
                if (found != null)
                {
                    log.LogInfo("Text '" + label.text + "' drawn with the game's font " + found.name + " (its own lacks the characters).");
                }
            }
            if (found != null)
            {
                label.font = found;
                // The copy draws with its original's material; another font needs its own.
                label.fontSharedMaterial = IsAnyScript(found) ? label.fontSharedMaterial : found.material;
            }
        }

        private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> anyScript = new Dictionary<TMP_FontAsset, TMP_FontAsset>();

        /// <summary>
        /// The fonts the game uses for Japanese, Chinese, Korean and Russian, loaded through its own
        /// text styles (<c>TextStyle.lazyFont.GetFontAssetFor(language, …)</c>).
        /// </summary>
        private static List<TMP_FontAsset> LanguageFonts()
        {
            var fonts = new List<TMP_FontAsset>();
            foreach (GameObject template in new[] { bodyTemplate, headerTemplate })
            {
                LazyBearTechnology.TextStyleComponent component = template == null ? null : template.GetComponent<LazyBearTechnology.TextStyleComponent>();
                object style = component == null ? null : component.CurrentTextStyle;
                FieldInfo lazyField = style == null ? null : style.GetType().GetField("lazyFont", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object lazy = lazyField == null ? null : lazyField.GetValue(style);
                MethodInfo getFor = lazy == null ? null : lazy.GetType().GetMethod("GetFontAssetFor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getFor == null)
                {
                    continue;
                }
                foreach (string language in new[] { "ja", "zh_cn", "ko", "ru" })
                {
                    try
                    {
                        object[] arguments = new object[getFor.GetParameters().Length];
                        arguments[0] = language;
                        for (int i = 1; i < arguments.Length; i++)
                        {
                            arguments[i] = getFor.GetParameters()[i].ParameterType == typeof(bool) ? (object)false : null;
                        }
                        var found = getFor.Invoke(lazy, arguments) as TMP_FontAsset;
                        if (found != null && !fonts.Contains(found))
                        {
                            fonts.Add(found);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogInfo("Font for " + language + " not available: " + (ex.InnerException ?? ex).Message);
                    }
                }
            }
            return fonts;
        }

        private static bool IsAnyScript(TMP_FontAsset font)
        {
            return font.name.EndsWith(" (any script)", StringComparison.Ordinal);
        }

        /// <summary>
        /// A copy of a font whose fallbacks are every other font the game has loaded, so a line
        /// mixing scripts draws each character from a font that has it. The game's own font assets
        /// are left as they are.
        /// </summary>
        private static TMP_FontAsset AnyScriptFont(TMP_FontAsset font)
        {
            if (font == null)
            {
                return null;
            }
            if (IsAnyScript(font))
            {
                return font;
            }
            TMP_FontAsset copy;
            if (anyScript.TryGetValue(font, out copy) && copy != null)
            {
                return copy;
            }
            try
            {
                copy = UnityEngine.Object.Instantiate(font);
                copy.name = font.name + " (any script)";
                var chain = new List<TMP_FontAsset>();
                if (font.fallbackFontAssetTable != null)
                {
                    chain.AddRange(font.fallbackFontAssetTable);
                }
                // The game loads a language's font only when that language is chosen, so a Korean
                // line in a German game would find no Korean font loaded. Ask the game's text styles
                // for the fonts of the languages with their own scripts.
                foreach (TMP_FontAsset languageFont in LanguageFonts())
                {
                    if (languageFont != null && languageFont != font && !chain.Contains(languageFont))
                    {
                        chain.Add(languageFont);
                    }
                }
                foreach (TMP_FontAsset other in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (other != null && other != font && other != copy && !IsAnyScript(other) && !chain.Contains(other))
                    {
                        chain.Add(other);
                    }
                }
                copy.fallbackFontAssetTable = chain;
                anyScript[font] = copy;
                log.LogInfo("Mixed-script text: " + copy.name + " falls back on " + chain.Count + " of the game's fonts.");
                return copy;
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not prepare a font for mixed-script text: " + ex.Message);
                return null;
            }
        }

        // Text in any language can arrive from other players (chat, names), so a text whose
        // characters the style's font lacks is drawn with a loaded font that has them all.
        private static readonly Dictionary<string, TMP_FontAsset> fontFor = new Dictionary<string, TMP_FontAsset>();

        /// <summary>A copy of the game's header or body text, with its font, material and style.</summary>
        internal static TextMeshProUGUI NewText(Transform parent, TextKind kind, string text)
        {
            GameObject copy = UnityEngine.Object.Instantiate(kind == TextKind.Header ? headerTemplate : bodyTemplate, parent, false);
            copy.name = "Text";
            copy.SetActive(true);
            var label = copy.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = kind != TextKind.Header ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            SetKind(label, kind);
            label.text = text ?? string.Empty;
            var rect = (RectTransform)copy.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            return label;
        }

        internal static void SetKind(TextMeshProUGUI label, TextKind kind)
        {
            // Colour after the style: the body style's grey is the game's hint colour.
            label.color = kind == TextKind.Header ? Color.white : kind == TextKind.Hint ? HintColour : kind == TextKind.Notice ? NoticeColour : BodyColour;
        }

        /// <summary>A copy of the game's dialog button: its sprite, hover and pressed text styles and sounds.</summary>
        internal static Button NewButton(Transform parent, string text, out TextMeshProUGUI label)
        {
            GameObject copy = UnityEngine.Object.Instantiate(buttonTemplate, parent, false);
            copy.name = "Button";
            copy.SetActive(true);
            // The dialog's own component wires the button to the dialog; ours is wired below.
            Type holder = Plugin.FindGameType("UIDialogWindowButton");
            if (holder != null)
            {
                Component owner = copy.GetComponent(holder);
                if (owner != null)
                {
                    UnityEngine.Object.DestroyImmediate(owner);
                }
            }
            // The dialog button sizes itself: a content-size fitter at every level, each keeping
            // its own width. Let the parent's layout size it instead, as the main menu's buttons do.
            foreach (ContentSizeFitter fitter in copy.GetComponentsInChildren<ContentSizeFitter>(true))
            {
                UnityEngine.Object.DestroyImmediate(fitter);
            }
            foreach (HorizontalLayoutGroup group in copy.GetComponentsInChildren<HorizontalLayoutGroup>(true))
            {
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = true;
                group.childForceExpandHeight = true;
            }
            foreach (LayoutElement element in copy.GetComponentsInChildren<LayoutElement>(true))
            {
                element.minWidth = -1f;
            }
            Transform tip = copy.transform.Find("Tip");
            if (tip != null)
            {
                UnityEngine.Object.DestroyImmediate(tip.gameObject);
            }
            Button button = copy.GetComponentInChildren<Button>(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            label = copy.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = text;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.alignment = TextAlignmentOptions.Center;
            }
            RefreshTextTransitions(button);
            return button;
        }

        internal static void RefreshTextTransitions(Button button)
        {
            try
            {
                button.GetType().GetMethod("RefreshTextTransitions", BindingFlags.Instance | BindingFlags.Public)?.Invoke(button, null);
            }
            catch (Exception)
            {
                // Only the hover styles; the button works without them.
            }
        }

        /// <summary>A copy of the game's text input, emptied of the window it came from.</summary>
        internal static TMP_InputField NewInput(Transform parent, int characterLimit)
        {
            if (inputTemplate == null)
            {
                return null;
            }
            GameObject copy = UnityEngine.Object.Instantiate(inputTemplate, parent, false);
            copy.name = "Input";
            copy.SetActive(true);
            var field = copy.GetComponent<TMP_InputField>();
            // The copy keeps listeners pointing at the original window's slider; drop them all.
            field.onValueChanged = new TMP_InputField.OnChangeEvent();
            field.onEndEdit = new TMP_InputField.SubmitEvent();
            field.onSubmit = new TMP_InputField.SubmitEvent();
            field.onSelect = new TMP_InputField.SelectionEvent();
            field.onDeselect = new TMP_InputField.SelectionEvent();
            field.onValidateInput = null;
            field.contentType = TMP_InputField.ContentType.Standard;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.interactable = true;
            if (field.textComponent != null)
            {
                field.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
                field.textComponent.color = Color.white;
            }
            foreach (MonoBehaviour behaviour in copy.GetComponents<MonoBehaviour>())
            {
                // Anything but the field and its graphics belongs to the original window.
                if (behaviour != null && !(behaviour is TMP_InputField) && !(behaviour is Graphic) && !(behaviour is LayoutElement) && !(behaviour is Mask) && !(behaviour is RectMask2D))
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }
            field.SetTextWithoutNotify(string.Empty);
            return field;
        }

        /// <summary>
        /// A copy of the game's window frame — border, dark side panels and header bar — without
        /// the dialog's content. The header text and close button are returned when present.
        /// </summary>
        internal static RectTransform NewFrame(Transform parent, out TextMeshProUGUI title, out Button close)
        {
            GameObject copy = UnityEngine.Object.Instantiate(frameTemplate, parent, false);
            copy.name = "Window";
            copy.SetActive(true);
            title = frameHeaderPath == null ? null : copy.transform.Find(frameHeaderPath)?.GetComponent<TextMeshProUGUI>();
            close = frameClosePath == null ? null : copy.transform.Find(frameClosePath)?.GetComponent<Button>();
            if (close != null)
            {
                close.onClick = new Button.ButtonClickedEvent();
            }
            foreach (Transform child in copy.transform)
            {
                // The frame's own decoration is placed by anchors, not by the content's layout.
                LayoutElement element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
            }
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                {
                    continue;
                }
                string typeName = behaviour.GetType().Name;
                // Window scripts copied with the frame would try to drive the dialog they came from.
                if (typeName.StartsWith("UI", StringComparison.Ordinal) && typeName.EndsWith("Window", StringComparison.Ordinal) || typeName == "GenericWindowLayout")
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }
            var rect = (RectTransform)copy.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            return rect;
        }

        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        /// <summary>One of the game's loaded sprites by name, or null when it is not loaded.</summary>
        internal static Sprite FindSprite(string spriteName)
        {
            Sprite found;
            if (sprites.TryGetValue(spriteName, out found) && found != null)
            {
                return found;
            }
            found = null;
            foreach (Sprite candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (candidate != null && candidate.name == spriteName)
                {
                    found = candidate;
                    break;
                }
            }
            sprites[spriteName] = found;
            return found;
        }

        /// <summary>
        /// The material the game draws its portraits and item icons with: their sprites carry a
        /// pure blue outline that this material turns into the game's own outline colour.
        /// </summary>
        internal static Material IconMaterial()
        {
            if (iconMaterial != null)
            {
                return iconMaterial;
            }
            Material fallback = null;
            foreach (Material candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (candidate == null) continue;
                if (candidate.name.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) >= 0 && candidate.shader != null && candidate.shader.name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    iconMaterial = candidate;
                    return iconMaterial;
                }
                if (candidate.name == "ItemCellMaterial")
                {
                    fallback = candidate;
                }
            }
            iconMaterial = fallback;
            return iconMaterial;
        }

        private static Material iconMaterial;

        /// <summary>True while a story scene shows its cinematic bars; the game hides its HUD then.</summary>
        internal static bool InCinematic
        {
            get
            {
                try
                {
                    UICinematic bars = LazyBearTechnology.LazyUI.Get<UICinematic>();
                    return bars != null && bars.gameObject.activeSelf;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>A dark plate behind HUD text, cut from the game's item-cell background.</summary>
        internal static Image NewPlate(Transform parent, string name)
        {
            RectTransform rect = NewRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            if (plateSprite != null)
            {
                image.sprite = plateSprite;
                image.type = Image.Type.Sliced;
                image.color = new Color(1f, 1f, 1f, 0.92f);
            }
            else
            {
                image.color = new Color(0.08f, 0.07f, 0.07f, 0.85f);
            }
            return image;
        }

        /// <summary>A plate that sizes itself to one text inside it, wrapped at <paramref name="maxWidth"/>.</summary>
        internal static TextMeshProUGUI NewPlateText(Transform parent, string name, TextKind kind, out RectTransform plate)
        {
            Image image = NewPlate(parent, name);
            plate = image.rectTransform;
            var group = image.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(7, 7, 5, 5);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            var fitter = image.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            TextMeshProUGUI text = NewText(image.transform, kind, string.Empty);
            text.gameObject.AddComponent<LayoutElement>();
            return text;
        }

        /// <summary>Sets a plate's text and wraps it at <paramref name="maxWidth"/> UI units.</summary>
        internal static void SetPlateText(TextMeshProUGUI text, string value, float maxWidth)
        {
            if (text.text != value)
            {
                text.text = value;
                EnsureGlyphs(text);
            }
            float natural = text.GetPreferredValues(value, 10000f, 10000f).x;
            LayoutElement element = text.GetComponent<LayoutElement>();
            float wanted = Mathf.Min(natural + 1f, maxWidth);
            if (element != null && Mathf.Abs(element.preferredWidth - wanted) > 0.5f)
            {
                element.preferredWidth = wanted;
            }
        }

        /// <summary>Clicks a button the way the pointer does, through the game's button handler.</summary>
        internal static void ClickForTest(Button button)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }
    }

    /// <summary>
    /// A window in the game's style, filled the immediate-mode way: each frame the owner calls
    /// <see cref="Begin"/>, then Label/Button/Input… in order, then <see cref="End"/>. Widgets are
    /// kept while the sequence stays the same, so text fields keep focus and buttons their hover
    /// state; when a page changes shape the changed tail is rebuilt.
    /// </summary>
    internal sealed class GameUiPanel
    {
        private sealed class Item
        {
            internal string Kind;
            internal GameObject Go;
            internal TextMeshProUGUI Text;
            internal Button Button;
            internal TMP_InputField Input;
            internal LayoutElement Layout;
            internal bool Clicked;
            internal string Shown;
            internal string Typed;
        }

        private readonly string name;
        private readonly List<Item> items = new List<Item>();
        private readonly Stack<Transform> parents = new Stack<Transform>();
        private RectTransform window;
        private RectTransform content;
        private ScrollRect scroll;
        private float topPadding;
        private float maxHeight = 500f;
        private TextMeshProUGUI title;
        private Button close;
        private bool closeClicked;
        private int cursor;
        private float width;

        // Controller navigation: the panel that has it, what is focused
        private static GameUiPanel padOwner;
        // Counted in frames, not seconds: in a slow game (a hitch, a scene loading, a busy PC) more
        // than 0.3 s can pass between two frames, and a time limit then took every frame for a new
        // owner — the panel never settled and A did nothing (seen in the tests at a few frames a second).
        // Both, though: frames alone let a press through to the game when a panel missed one frame
        // (the game's character window opened under the co-op window once); the owner holds while
        // either says so.
        private static int padOwnerFrame = -10;
        private static float padOwnerAt = -10f;
        private float ownerSince = -10f;
        private int ownerSinceFrame = -10;
        private int padFocus = -1;
        private Image padCursor;
        private TextMeshProUGUI padHint;
        private bool backPressed;
        private int navHeld;
        private float navRepeatAt;

        /// <summary>This panel can be used with a controller: direction to choose, A, B back.</summary>
        internal bool PadNavigation { get; set; }

        /// <summary>While shown, this panel alone takes the controller (a keyboard over a menu).</summary>
        internal bool Modal { get; set; }

        private static GameUiPanel modalOwner;
        private static int modalFrame = -10;
        private static float modalAt = -10f;

        /// <summary>A controller is driving one of the mod's panels right now (this frame or the last).</summary>
        internal static bool PadOwned => padOwner != null && OwnerHolds() && CoopInput.PadActive;

        private static bool OwnerHolds()
        {
            return Time.frameCount - padOwnerFrame <= 2 || Time.unscaledTime - padOwnerAt < 0.3f;
        }

        /// <summary>
        /// While a panel has the controller, the game does not see the buttons it uses (the main
        /// menu under it would move and select too).
        /// </summary>
        internal static void HideUsedButtonsFromGame()
        {
            // The bindings load with the game: the set is made on first use.
            HashSet<int> used = null;
            CoopInput.HideDirection(() => PadOwned);
            CoopInput.Hide(key =>
            {
                if (!PadOwned)
                {
                    return false;
                }
                if (used == null || used.Count == 0)
                {
                    used = new HashSet<int>();
                    foreach (GamepadButton button in new[] { GamepadButton.A, GamepadButton.B, GamepadButton.DUp, GamepadButton.DDown, GamepadButton.DLeft, GamepadButton.DRight })
                    {
                        used.UnionWith(CoopInput.KeysOf(button));
                    }
                    foreach (GameKey nav in new[] { GameKey.Up, GameKey.Down, GameKey.Left, GameKey.Right, GameKey.Select, GameKey.Back })
                    {
                        used.Add(nav.value);
                    }
                }
                return used.Contains(key.value);
            });
        }

        internal GameUiPanel(string name)
        {
            this.name = name;
        }

        internal bool IsBuilt => window != null;

        internal RectTransform Window => window;

        /// <summary>Starts a frame's contents. False when the game's UI parts are not available.</summary>
        internal bool Begin(string heading, Vector2 topLeft, float panelWidth)
        {
            RectTransform layer = GameUi.Layer;
            if (layer == null)
            {
                return false;
            }
            if (window == null || Math.Abs(width - panelWidth) > 0.1f)
            {
                Destroy();
                Build(layer, panelWidth);
            }
            window.gameObject.SetActive(true);
            window.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            if (title != null && title.text != heading)
            {
                title.text = heading;
            }
            cursor = 0;
            parents.Clear();
            parents.Push(content);
            return true;
        }

        private void Build(RectTransform layer, float panelWidth)
        {
            width = panelWidth;
            window = GameUi.NewFrame(layer, out title, out close);
            window.name = name;
            window.sizeDelta = new Vector2(panelWidth, 120f);
            if (close != null)
            {
                close.onClick.AddListener(() => closeClicked = true);
            }
            // Content scrolls inside the frame when a page is taller than the screen allows.
            topPadding = title != null ? 40f : 16f;
            RectTransform viewport = GameUi.NewRect(window, "Viewport");
            GameUi.Stretch(viewport, 16f, 14f, 14f, topPadding);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            // An invisible graphic so the mouse wheel scrolls anywhere over the page.
            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            content = GameUi.NewRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, content.offsetMin.y);
            content.offsetMax = new Vector2(-4f, 0f);
            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            // The pixel font draws a pixel or two left of its box; keep that inside the mask.
            group.padding = new RectOffset(3, 0, 0, 2);
            group.spacing = 3f;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18f;
            items.Clear();
        }

        internal void Destroy()
        {
            if (window != null)
            {
                UnityEngine.Object.Destroy(window.gameObject);
            }
            window = null;
            content = null;
            items.Clear();
        }

        internal void Hide()
        {
            if (window != null && window.gameObject.activeSelf)
            {
                window.gameObject.SetActive(false);
            }
        }

        /// <summary>B on a controller: back a page, or close.</summary>
        internal bool BackPressed()
        {
            bool pressed = backPressed;
            backPressed = false;
            return pressed;
        }

        /// <summary>The window's own close button (the cross in its header) was pressed.</summary>
        internal bool CloseClicked()
        {
            bool clicked = closeClicked;
            closeClicked = false;
            return clicked;
        }

        private Item Next(string kind)
        {
            if (cursor < items.Count && items[cursor].Kind == kind)
            {
                return items[cursor++];
            }
            // The page changed shape here: rebuild from this point on.
            for (int i = items.Count - 1; i >= cursor; i--)
            {
                if (items[i].Go != null)
                {
                    UnityEngine.Object.DestroyImmediate(items[i].Go);
                }
                items.RemoveAt(i);
            }
            var item = new Item { Kind = kind };
            items.Add(item);
            cursor++;
            return item;
        }

        private Transform Parent => parents.Peek();

        internal void SetTitle(string heading)
        {
            if (title != null && title.text != heading)
            {
                title.text = heading;
            }
        }

        internal void Label(string text, GameUi.TextKind kind = GameUi.TextKind.Body, float fixedWidth = 0f)
        {
            Item item = Next("label:" + kind);
            if (item.Go == null)
            {
                item.Text = GameUi.NewText(Parent, kind, text);
                item.Go = item.Text.gameObject;
                item.Layout = item.Go.AddComponent<LayoutElement>();
            }
            // Widths are written in 1080p pixels, like the rest of the page code.
            item.Layout.preferredWidth = fixedWidth > 0f ? fixedWidth * 0.5f : -1f;
            item.Layout.minWidth = fixedWidth > 0f ? fixedWidth * 0.5f : -1f;
            item.Layout.flexibleWidth = fixedWidth > 0f ? 0f : 1f;
            if (item.Shown != text)
            {
                item.Shown = text;
                item.Text.text = text ?? string.Empty;
                GameUi.EnsureGlyphs(item.Text);
            }
            // Kept every frame: reapplying the game's style on a language change resets the colour
            // and the alignment.
            GameUi.SetKind(item.Text, kind);
            if (item.Text.alignment != TextAlignmentOptions.TopLeft) item.Text.alignment = TextAlignmentOptions.TopLeft;
        }

        internal void Space(float height)
        {
            Item item = Next("space");
            if (item.Go == null)
            {
                item.Go = GameUi.NewRect(Parent, "Space").gameObject;
                item.Layout = item.Go.AddComponent<LayoutElement>();
            }
            item.Layout.minHeight = height;
            item.Layout.preferredHeight = height;
        }

        /// <summary>A game button; true on the frame after it was clicked.</summary>
        internal bool Button(string text, float preferredWidth = 0f, bool selected = true)
        {
            Item item = Next("button");
            if (item.Go == null)
            {
                item.Button = GameUi.NewButton(Parent, text, out item.Text);
                item.Go = item.Button.gameObject;
                Item captured = item;
                item.Button.onClick.AddListener(() => captured.Clicked = true);
                item.Layout = item.Go.GetComponent<LayoutElement>() ?? item.Go.AddComponent<LayoutElement>();
                item.Layout.minHeight = 26f;
                item.Layout.preferredHeight = 26f;
                item.Shown = text;
            }
            if (item.Shown != text && item.Text != null)
            {
                item.Shown = text;
                item.Text.text = text;
            }
            // Buttons keep their natural width, centred, as in the game's dialogs; a width asks
            // for at least that much, for rows of options that should line up.
            item.Layout.minWidth = preferredWidth > 0f ? preferredWidth * 0.5f : -1f;
            item.Layout.preferredWidth = -1f;
            item.Layout.flexibleWidth = 0f;
            item.Layout.flexibleHeight = 0f;
            // Options that are not chosen are drawn dimmed, like the game's inactive tabs.
            Graphic graphic = item.Button.targetGraphic;
            if (graphic != null)
            {
                Color wanted = selected ? Color.white : new Color(0.42f, 0.4f, 0.4f, 1f);
                if (graphic.color != wanted)
                {
                    graphic.color = wanted;
                }
            }
            bool clicked = item.Clicked;
            item.Clicked = false;
            return clicked;
        }

        /// <summary>A text field showing <paramref name="value"/>; returns what the player typed.</summary>
        internal string Input(string value, int characterLimit)
        {
            Item item = Next("input");
            if (item.Go == null)
            {
                item.Input = GameUi.NewInput(Parent, characterLimit);
                if (item.Input == null)
                {
                    // No template: show the value, read-only.
                    item.Text = GameUi.NewText(Parent, GameUi.TextKind.Notice, value);
                    item.Go = item.Text.gameObject;
                }
                else
                {
                    item.Go = item.Input.gameObject;
                }
                item.Layout = item.Go.GetComponent<LayoutElement>() ?? item.Go.AddComponent<LayoutElement>();
                item.Layout.minHeight = 20f;
                item.Layout.preferredHeight = 20f;
                item.Layout.flexibleWidth = 1f;
            }
            if (item.Input == null)
            {
                item.Text.text = value ?? string.Empty;
                return value;
            }
            if (item.Typed != null)
            {
                // Typed with a controller (Steam's keyboard or the mod's): the page takes it.
                value = item.Typed;
                item.Typed = null;
            }
            if (!item.Input.isFocused && item.Input.text != (value ?? string.Empty))
            {
                item.Input.SetTextWithoutNotify(value ?? string.Empty);
            }
            return item.Input.text;
        }

        internal void BeginRow()
        {
            Item item = Next("row");
            if (item.Go == null)
            {
                RectTransform row = GameUi.NewRect(Parent, "Row");
                var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                group.spacing = 6f;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = false;
                group.childForceExpandHeight = false;
                group.childAlignment = TextAnchor.MiddleLeft;
                item.Go = row.gameObject;
            }
            parents.Push(item.Go.transform);
        }

        internal void EndRow()
        {
            Next("endrow");
            if (parents.Count > 1)
            {
                parents.Pop();
            }
        }

        /// <summary>Ends a frame's contents; anything the page no longer draws is removed.</summary>
        internal void End()
        {
            for (int i = items.Count - 1; i >= cursor; i--)
            {
                if (items[i].Go != null)
                {
                    UnityEngine.Object.DestroyImmediate(items[i].Go);
                }
                items.RemoveAt(i);
            }
            // The frame fits its page, up to what the screen has room for below its top edge.
            float contentHeight = LayoutUtility.GetPreferredHeight(content);
            RectTransform layer = GameUi.Layer;
            float room = layer == null ? maxHeight : layer.rect.height - (-window.anchoredPosition.y) - 12f;
            float wanted = Mathf.Min(contentHeight + topPadding + 16f, Mathf.Max(120f, room));
            if (Mathf.Abs(window.sizeDelta.y - wanted) > 0.5f)
            {
                window.sizeDelta = new Vector2(width, wanted);
            }
            if (PadNavigation)
            {
                UpdatePad();
            }
        }

        // ---------------------------------------------------------------- controller

        private void UpdatePad()
        {
            int frame = Time.frameCount;
            if (Modal && window != null && window.gameObject.activeInHierarchy)
            {
                modalOwner = this;
                modalFrame = frame;
                modalAt = Time.unscaledTime;
            }
            else if (modalOwner != null && modalOwner != this && (frame - modalFrame <= 2 || Time.unscaledTime - modalAt < 0.3f))
            {
                // Another window has the controller for now.
                ShowPadCursor(null);
                ShowPadHint(false);
                return;
            }
            if (!CoopInput.PadActive || window == null || !window.gameObject.activeInHierarchy)
            {
                ShowPadCursor(null);
                ShowPadHint(false);
                if (padOwner == this) padOwner = null;
                return;
            }
            float now = Time.unscaledTime;
            if (padOwner != this || !OwnerHolds())
            {
                ownerSince = now;
                ownerSinceFrame = frame;
            }
            padOwner = this;
            padOwnerFrame = frame;
            padOwnerAt = now;
            var choices = new List<Item>();
            foreach (Item item in items)
            {
                if ((item.Button != null || item.Input != null) && item.Go != null && item.Go.activeInHierarchy)
                {
                    choices.Add(item);
                }
            }
            if (choices.Count == 0)
            {
                ShowPadCursor(null);
                return;
            }
            if (padFocus < 0 || padFocus >= choices.Count)
            {
                padFocus = 0;
            }
            int direction = NavStep(now);
            if (direction != 0)
            {
                padFocus = Move(choices, padFocus, direction);
            }
            Item focused = choices[padFocus];
            // The press that opened the panel is not also an answer inside it: a quarter second, and
            // at least two frames for a slow game, where that much time can pass in one.
            bool settled = now - ownerSince > 0.25f && frame - ownerSinceFrame >= 2;
            if (settled && CoopInput.PadDown(GamepadButton.A))
            {
                if (focused.Button != null)
                {
                    focused.Clicked = true;
                }
                else if (focused.Input != null)
                {
                    Item target = focused;
                    CoopInput.EditText(focused.Input, typedText => target.Typed = typedText);
                }
            }
            if (settled && CoopInput.PadDown(GamepadButton.B))
            {
                backPressed = true;
            }
            ShowPadCursor(focused);
            ShowPadHint(true);
            KeepInView(focused);
        }

        /// <summary>
        /// Under the window, as under the game's own menus: "(A) Select (B) Back" with the
        /// controller's icons and the game's own words (tip_select, tip_back).
        /// </summary>
        private void ShowPadHint(bool show)
        {
            if (!show)
            {
                if (padHint != null && padHint.gameObject.activeSelf) padHint.gameObject.SetActive(false);
                return;
            }
            if (padHint == null)
            {
                padHint = GameUi.NewText(window, GameUi.TextKind.Body, string.Empty);
                padHint.textWrappingMode = TextWrappingModes.NoWrap;
                padHint.alignment = TextAlignmentOptions.Center;
                RectTransform rect = padHint.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -4f);
                rect.sizeDelta = new Vector2(width, 18f);
                padHint.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            string text = (CoopInput.Icon(GameKey.Select) ?? "A ") + (L.Game("tip_select") ?? L.T("Select")) + "    " +
                          (CoopInput.Icon(GameKey.Back) ?? "B ") + (L.Game("tip_back") ?? L.T("Back"));
            if (padHint.text != text) padHint.text = text;
            if (!padHint.gameObject.activeSelf) padHint.gameObject.SetActive(true);
        }

        /// <summary>-1, +1 or 0: the pad or the stick, with repeat while held.</summary>
        // Directions: 0 none, 1 up, 2 down, 3 left, 4 right
        private int NavStep(float now)
        {
            int pressed = CoopInput.PadDown(GamepadButton.DUp) ? 1 : CoopInput.PadDown(GamepadButton.DDown) ? 2
                : CoopInput.PadDown(GamepadButton.DLeft) ? 3 : CoopInput.PadDown(GamepadButton.DRight) ? 4 : 0;
            if (pressed != 0)
            {
                navHeld = pressed;
                navRepeatAt = now + 0.45f;
                return pressed;
            }
            Vector2 stick = CoopInput.Direction();
            int held = Mathf.Abs(stick.y) >= Mathf.Abs(stick.x)
                ? (stick.y > 0.5f ? 1 : stick.y < -0.5f ? 2 : 0)
                : (stick.x < -0.5f ? 3 : stick.x > 0.5f ? 4 : 0);
            if (held == 0)
            {
                navHeld = 0;
                return 0;
            }
            if (held != navHeld)
            {
                navHeld = held;
                navRepeatAt = now + 0.45f;
                return held;
            }
            if (now >= navRepeatAt)
            {
                navRepeatAt = now + 0.16f;
                return held;
            }
            return 0;
        }

        /// <summary>
        /// The choice the direction leads to: the nearest one that way on screen (rows of buttons,
        /// a keyboard); when there is none that way, the previous or next in the page's order.
        /// </summary>
        private static int Move(List<Item> choices, int from, int direction)
        {
            Vector2 origin = Centre(choices[from]);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < choices.Count; i++)
            {
                if (i == from) continue;
                Vector2 d = Centre(choices[i]) - origin;
                float along = direction == 1 ? d.y : direction == 2 ? -d.y : direction == 3 ? -d.x : d.x;
                float across = direction <= 2 ? Mathf.Abs(d.x) : Mathf.Abs(d.y);
                if (along <= 1f || across > along * 2.5f + 4f) continue;
                float score = along + across * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best >= 0)
            {
                return best;
            }
            int step = direction == 1 || direction == 3 ? -1 : 1;
            return (from + step + choices.Count) % choices.Count;
        }

        private static Vector2 Centre(Item item)
        {
            var corners = new Vector3[4];
            ((RectTransform)item.Go.transform).GetWorldCorners(corners);
            return new Vector2((corners[0].x + corners[2].x) * 0.5f, (corners[0].y + corners[2].y) * 0.5f);
        }

        /// <summary>The game's controller cursor around the focused choice.</summary>
        private void ShowPadCursor(Item focused)
        {
            if (focused == null)
            {
                if (padCursor != null && padCursor.gameObject.activeSelf) padCursor.gameObject.SetActive(false);
                return;
            }
            if (padCursor == null)
            {
                RectTransform rect = GameUi.NewRect(focused.Go.transform, "PadCursor");
                padCursor = rect.gameObject.AddComponent<Image>();
                padCursor.raycastTarget = false;
                Sprite sprite = GameUi.FindSprite("gamepad_ui_cursor");
                if (sprite != null)
                {
                    padCursor.sprite = sprite;
                    padCursor.type = Image.Type.Sliced;
                }
                else
                {
                    padCursor.color = new Color(1f, 0.8f, 0.3f, 0.35f);
                }
                rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            RectTransform cursorRect = padCursor.rectTransform;
            if (cursorRect.parent != focused.Go.transform)
            {
                cursorRect.SetParent(focused.Go.transform, false);
            }
            cursorRect.SetAsLastSibling();
            cursorRect.anchorMin = Vector2.zero;
            cursorRect.anchorMax = Vector2.one;
            cursorRect.offsetMin = new Vector2(-3f, -3f);
            cursorRect.offsetMax = new Vector2(3f, 3f);
            if (!padCursor.gameObject.activeSelf) padCursor.gameObject.SetActive(true);
        }

        /// <summary>Scrolls the page so the focused choice is in view.</summary>
        private void KeepInView(Item focused)
        {
            if (scroll == null || content == null || scroll.viewport == null)
            {
                return;
            }
            float viewHeight = scroll.viewport.rect.height;
            if (content.rect.height <= viewHeight + 1f)
            {
                return;
            }
            var corners = new Vector3[4];
            ((RectTransform)focused.Go.transform).GetWorldCorners(corners);
            float top = -content.InverseTransformPoint(corners[1]).y;
            float bottom = -content.InverseTransformPoint(corners[0]).y;
            float scrolled = content.anchoredPosition.y;
            if (top - 4f < scrolled)
            {
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Max(0f, top - 4f));
            }
            else if (bottom + 4f > scrolled + viewHeight)
            {
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, bottom + 4f - viewHeight);
            }
        }

        /// <summary>Tests: what the controller has focused.</summary>
        internal string DescribePad()
        {
            if (padCursor == null || !padCursor.gameObject.activeInHierarchy)
            {
                return "none";
            }
            Transform owner = padCursor.transform.parent;
            foreach (Item item in items)
            {
                if (item.Go != null && item.Go.transform == owner)
                {
                    return item.Button != null ? "<" + item.Shown + ">" : item.Input != null && item.Input.isFocused ? "{input:typing}" : "{input}";
                }
            }
            return "?";
        }

        /// <summary>Back to the top, for a page that has just changed.</summary>
        internal void ScrollToTop()
        {
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>Tests: the first button whose text contains <paramref name="text"/>.</summary>
        internal Button FindButton(string text)
        {
            foreach (Item item in items)
            {
                if (item.Button != null && item.Shown != null && item.Shown.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return item.Button;
                }
            }
            return null;
        }

        /// <summary>Tests: every text shown, in order.</summary>
        internal string Describe()
        {
            var parts = new List<string>();
            if (title != null)
            {
                parts.Add("[" + title.text + "]");
            }
            foreach (Item item in items)
            {
                if (item.Button != null) parts.Add("<" + item.Shown + ">");
                else if (item.Input != null) parts.Add("{" + item.Input.text + "}");
                else if (item.Text != null) parts.Add(item.Text.text);
            }
            return string.Join(" | ", parts.ToArray());
        }
    }
}
