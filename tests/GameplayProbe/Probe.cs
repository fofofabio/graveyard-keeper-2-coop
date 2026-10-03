using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

// Temporary integration-test plugin. Never include in a release package.
// Commands run on Unity's main thread against a disposable AutoStartNewGame session.
[BepInPlugin("com.fabio.gk2coop.gameplayprobe", "GK2 Gameplay Probe", "0.1.0")]
public sealed class GameplayProbe : BaseUnityPlugin
{
    private string commandPath;
    private float nextPoll;
    private string scheduled;
    private long dueTicks;
    private string continueSlot;
    private bool menuConnectAllowed;
    private bool continueStarted;
    private float nextContinueCheck;
    // Quiet mode: tests running beside someone's own game stay a small, silent, slow window.
    private bool quiet;
    private float nextQuiet;
    // A slow game on purpose (fps|3): what a hitch or a busy PC does to the mod's input timing.
    private static int slowFps;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private void Awake()
    {
        continueSlot = Environment.GetEnvironmentVariable("GK2COOP_TEST_CONTINUE_SLOT");
        menuConnectAllowed = Environment.GetEnvironmentVariable("GK2COOP_TEST_MENU_CONNECT") == "1";
        quiet = Environment.GetEnvironmentVariable("GK2COOP_TEST_QUIET") == "1";
        KeepQuiet();
        if (!string.IsNullOrEmpty(continueSlot) &&
            !System.Text.RegularExpressions.Regex.IsMatch(continueSlot, @"^GK2Coop_Test_[A-Za-z0-9_]+$"))
        {
            Logger.LogError("Probe refused an unsafe test save slot name.");
            enabled = false;
            return;
        }
        string config = File.ReadAllText(Path.Combine(Paths.ConfigPath, "com.fabio.gk2coop.cfg"));
        if (string.IsNullOrEmpty(continueSlot) && !menuConnectAllowed &&
            !System.Text.RegularExpressions.Regex.IsMatch(config, @"(?m)^AutoStartNewGame\s*=\s*true\s*$"))
        {
            Logger.LogWarning("Probe disabled: only disposable AutoStartNewGame runs are allowed.");
            enabled = false;
            return;
        }
        commandPath = Path.Combine(Paths.BepInExRootPath, "GK2Coop.Probe.command");
        Logger.LogInfo("Temporary gameplay probe ready." + (continueSlot == null ? "" : " Test continuation slot: " + continueSlot));
    }

    private void KeepQuiet()
    {
        if (!quiet || Time.unscaledTime < nextQuiet) return;
        nextQuiet = Time.unscaledTime + 0.5f;
        try
        {
            AudioListener.volume = 0f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = slowFps > 0 ? slowFps : 20;
            Application.runInBackground = true;
            if (Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width > 640)
            {
                Screen.SetResolution(640, 360, FullScreenMode.Windowed);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Quiet mode: " + ex.Message);
        }
    }

    // The test's language (GK2COOP_TEST_LANG), set once the game's settings exist: all test games
    // share one registry, and a game started while another had switched languages would otherwise
    // start in that one.
    private bool testLangApplied;
    private float nextTestLangCheck;

    private void ApplyTestLanguage()
    {
        if (testLangApplied || Time.unscaledTime < nextTestLangCheck) return;
        nextTestLangCheck = Time.unscaledTime + 0.5f;
        string lang = Environment.GetEnvironmentVariable("GK2COOP_TEST_LANG");
        if (string.IsNullOrEmpty(lang)) { testLangApplied = true; return; }
        try
        {
            object settings = G(Named("GameSettings"), "Instance");
            if (settings == null) return;
            if (Convert.ToString(G(Named("LLBase"), "CurrentLang")) != lang)
            {
                settings.GetType().GetField("language", Flags).SetValue(settings, lang);
                settings.GetType().GetMethod("ApplyLanguageSettings", Flags).Invoke(settings, new object[] { false });
            }
            testLangApplied = true;
        }
        catch (Exception)
        {
            // The game is not far enough yet; try again shortly.
        }
    }

    // Pictures: a run of frames at a steady pace, for a short GIF ("burst|folder|count|seconds").
    private static string burstFolder;
    private static int burstLeft;
    private static int burstIndex;
    private static float burstEvery;
    private static float burstNext;

    // Pictures: a pose held every frame (the player controller sets Idle again when not moving).
    private static object heldPose;

    // Someone at the PC: their keys and clicks reach a test game that has the focus (a menu closed by
    // itself, a new game started). Logged, so a failed run can say so; the probe's own presses go
    // through the game's input, not Unity's, and are not counted.
    private float nextRealInputLog;
    private int realInputs;
    private Vector3? lastMouse;
    private void NoticeRealInput()
    {
        try
        {
            if (!Application.isFocused) { lastMouse = null; return; }
            bool key = Input.anyKeyDown;
            // A moved mouse counts too: hovering a menu button selects it (A then pressed Settings, not Co-op).
            Vector3 at = Input.mousePosition;
            bool moved = lastMouse.HasValue && (at - lastMouse.Value).sqrMagnitude > 4f;
            lastMouse = at;
            bool mouse = moved || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.mouseScrollDelta != Vector2.zero;
            if (!key && !mouse) return;
            realInputs++;
            if (Time.unscaledTime < nextRealInputLog) return;
            nextRealInputLog = Time.unscaledTime + 2f;
            Logger.LogWarning("Real input reached this test game (" + (key ? "a key" : moved ? "the mouse moved" : "a click") + "; " + realInputs + " so far): someone is using the PC.");
        }
        catch (Exception)
        {
        }
    }

    private void Update()
    {
        KeepQuiet();
        ApplyTestLanguage();
        NoticeRealInput();
        if (heldPose != null)
        {
            try
            {
                object poseData = G(T("MainGame"), "PlayerData");
                object poseState = G(poseData, "charState");
                if (!Equals(G(poseState, "Value"), heldPose)) poseState.GetType().GetProperty("Value").SetValue(poseState, heldPose, null);
                object poseAnimation = G(G(G(T("MainGame"), "PlayerController"), "View"), "PlayerAnimation");
                poseAnimation?.GetType().GetMethod("SetState", new[] { heldPose.GetType() })?.Invoke(poseAnimation, new[] { heldPose });
            }
            catch (Exception)
            {
                heldPose = null;
            }
        }
        if (burstLeft > 0 && Time.unscaledTime >= burstNext)
        {
            T("UnityEngine.ScreenCapture").GetMethod("CaptureScreenshot", new[] { typeof(string) }).Invoke(null, new object[] { Path.Combine(burstFolder, "frame-" + burstIndex.ToString("D3") + ".png") });
            burstIndex++;
            burstLeft--;
            burstNext = Time.unscaledTime + burstEvery;
        }
        if (!continueStarted && continueSlot != null && Time.unscaledTime >= nextContinueCheck)
        {
            nextContinueCheck = Time.unscaledTime + 2f;
            TryContinueTestSave();
        }
        if (scheduled != null && DateTime.UtcNow.Ticks >= dueTicks)
        {
            string due = scheduled;
            scheduled = null;
            // "a && b": both in this frame, one after the other (a pickup asked, then the bag filled
            // before the answer can come).
            foreach (string part in due.Split(new[] { " && " }, StringSplitOptions.RemoveEmptyEntries)) Execute(part);
        }
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.25f;
        if (!File.Exists(commandPath)) return;
        string command = File.ReadAllText(commandPath).Trim();
        File.Delete(commandPath);
        if (command.StartsWith("at|"))
        {
            string[] parts = command.Split(new[] { '|' }, 3);
            dueTicks = long.Parse(parts[1]);
            scheduled = parts[2];
            File.WriteAllText(commandPath + ".result", command + "\nSCHEDULED");
            return;
        }
        Execute(command);
    }

    private void TryContinueTestSave()
    {
        try
        {
            // The test slot carries an old date, so it is never what a player's own Continue opens;
            // the mod points Continue at it here.
            T("GK2Coop.CoopSaveBootstrap").GetMethod("ForceActiveSlotForTest", Flags).Invoke(null, new object[] { continueSlot });
            Type saveSystem = T("SaveSystem");
            object slot = saveSystem.GetMethod("GetActiveSaveData", Flags).Invoke(null, null);
            if (!string.Equals(Convert.ToString(G(slot, "slotName")), continueSlot, StringComparison.Ordinal)) return;
            MonoBehaviour menu = FindAll(T("UIMainMenuWindow"))
                .OfType<MonoBehaviour>().FirstOrDefault(w => w.gameObject.activeInHierarchy);
            if (menu == null) return;
            continueStarted = true;
            menu.GetType().GetMethod("OnContinueButtonClicked", Flags).Invoke(menu, null);
            Logger.LogInfo("Invoked the game's Continue button for isolated slot " + continueSlot + ".");
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Could not continue test slot yet: " + ex.GetBaseException().Message);
        }
    }

    private void Execute(string command)
    {
        string result;
        try
        {
            // Also for a game started on a test slot: it goes back to the menu and continues again.
            if (command.StartsWith("menu-continue|", StringComparison.Ordinal) && (menuConnectAllowed || continueSlot != null))
            {
                string slot = command.Substring("menu-continue|".Length);
                if (!System.Text.RegularExpressions.Regex.IsMatch(slot, @"^GK2Coop_Test_[A-Za-z0-9_]+$"))
                    throw new Exception("Unsafe test slot name");
                continueSlot = slot;
                continueStarted = false;
                nextContinueCheck = 0f;
                result = "MENU-CONTINUE queued=" + slot;
            }
            else result = Run(command.Split('|'));
        }
        catch (Exception ex) { result = "ERROR " + ex; }
        File.WriteAllText(commandPath + ".result", command + "\n" + result);
        Logger.LogInfo("PROBE " + command + "\n" + result);
    }

    private static UnityEngine.Object[] FindAll(Type type) => type == null ? new UnityEngine.Object[0] : (Resources.FindObjectsOfTypeAll(type) ?? new UnityEngine.Object[0]);
    private static void DumpTree(Transform t, int depth, StringBuilder into)
    {
        var rt = t as RectTransform;
        into.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)");
        if (rt != null) into.Append(" anchors=" + rt.anchorMin + "-" + rt.anchorMax + " pivot=" + rt.pivot + " pos=" + rt.anchoredPosition + " size=" + rt.sizeDelta + " rect=" + rt.rect.size);
        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform) continue;
            into.Append(" [" + c.GetType().Name);
            string[] keys = { "padding", "spacing", "childControlWidth", "childForceExpandWidth", "horizontalFit", "minWidth", "preferredWidth", "flexibleWidth", "ignoreLayout", "fontSize", "text" };
            foreach (string key in keys)
            {
                object value = G(c, key);
                if (value != null && !(value is string && ((string)value).Length > 30)) into.Append(" " + key + "=" + value);
            }
            into.Append("]");
        }
        into.AppendLine();
        if (depth < 6) foreach (Transform child in t) DumpTree(child, depth + 1, into);
    }
    private static Type Named(string simpleName) => AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).FirstOrDefault(t => t.Name == simpleName);
    private static string PathOf(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }
    private static string DescribeGraphic(object graphic)
    {
        if (graphic == null) return "none";
        object sprite = G(graphic, "sprite");
        return graphic.GetType().Name + "(sprite=" + (sprite == null ? "none" : ((UnityEngine.Object)sprite).name + " border=" + G(sprite, "border") + " rect=" + G(sprite, "rect") + " tex=" + ((UnityEngine.Object)G(sprite, "texture"))?.name) +
               " type=" + G(graphic, "type") + " color=" + G(graphic, "color") + " mat=" + ((UnityEngine.Object)G(graphic, "material"))?.name + ")";
    }
    private static string DescribeText(object text)
    {
        if (text == null) return "none";
        Component c = (Component)text;
        object styleComponent = c.GetComponent(Named("TextStyleComponent"));
        object style = styleComponent == null ? null : G(styleComponent, "CurrentTextStyle");
        return "TMP(font=" + ((UnityEngine.Object)G(text, "font"))?.name + " size=" + G(text, "fontSize") + " color=" + G(text, "color") + " mat=" + ((UnityEngine.Object)G(text, "fontSharedMaterial"))?.name +
               " style=" + (style == null ? "none" : ((UnityEngine.Object)style).name) + " text='" + G(text, "text") + "')";
    }
    private static string UiKitReport()
    {
        var lines = new StringBuilder("UI-KIT" + Environment.NewLine);
        Type dialogType = Named("UIDialogWindow");
        UnityEngine.Object[] dialogs = FindAll(dialogType);
        lines.AppendLine("DIALOG windows=" + dialogs.Length);
        foreach (UnityEngine.Object dialogObject in dialogs.Take(2))
        {
            Component dialog = (Component)dialogObject;
            lines.AppendLine("DIALOG path=" + PathOf(dialog.transform) + " scene=" + dialog.gameObject.scene.IsValid() + " size=" + G(dialog.transform, "rect"));
            lines.AppendLine("DIALOG header " + DescribeText(G(dialog, "header")));
            lines.AppendLine("DIALOG information " + DescribeText(G(dialog, "information")));
            lines.AppendLine("DIALOG informationBot " + DescribeText(G(dialog, "informationBotText")));
            object button = G(dialog, "buttonPrefab");
            if (button != null)
            {
                object lazy = G(button, "lazyButton");
                lines.AppendLine("DIALOG button path=" + PathOf(((Component)button).transform) + " size=" + G(((Component)button).transform, "rect") + " graphic=" + DescribeGraphic(G(lazy, "targetGraphic")) + " label=" + DescribeText(G(button, "label")));
                object transitions = G(lazy, "textTransitions");
                if (transitions is System.Collections.IEnumerable list)
                    foreach (object tr in list)
                        lines.AppendLine("DIALOG button transition default=" + ((UnityEngine.Object)G(tr, "defaultStyle"))?.name + " highlighted=" + ((UnityEngine.Object)G(tr, "highlightedStyle"))?.name + " pressed=" + ((UnityEngine.Object)G(tr, "pressedStyle"))?.name + " disabled=" + ((UnityEngine.Object)G(tr, "disabledStyle"))?.name);
                lines.AppendLine("DIALOG button transition=" + G(lazy, "transition") + " sprites=" + G(lazy, "spriteState") + " colors=" + G(lazy, "colors"));
            }
            object layout = G(dialog, "genericWindowLayout");
            if (layout != null && G(layout, "backgroundImages") is Array backgrounds)
                foreach (object image in backgrounds)
                    lines.AppendLine("DIALOG background " + PathOf(((Component)image).transform) + " " + DescribeGraphic(image) + " size=" + G(((Component)image).transform, "rect"));
            foreach (Component graphic in dialog.GetComponentsInChildren(T("UnityEngine.UI.Image"), true).Cast<Component>().Take(40))
                lines.AppendLine("DIALOG image " + PathOf(graphic.transform).Replace(PathOf(dialog.transform), "~") + " " + DescribeGraphic(graphic));
        }
        foreach (Component input in FindAll(T("TMPro.TMP_InputField")).Cast<Component>().Take(12))
            lines.AppendLine("INPUT " + PathOf(input.transform) + " scene=" + input.gameObject.scene.IsValid() + " graphic=" + DescribeGraphic(input.GetComponent(T("UnityEngine.UI.Image"))) + " text=" + DescribeText(G(input, "textComponent")));
        foreach (Component toggle in FindAll(T("UnityEngine.UI.Toggle")).Cast<Component>().Take(6))
            lines.AppendLine("TOGGLE " + PathOf(toggle.transform) + " graphic=" + DescribeGraphic(G(toggle, "targetGraphic")) + " check=" + DescribeGraphic(G(toggle, "graphic")));
        UnityEngine.Object[] styles = FindAll(Named("TextStyle"));
        lines.AppendLine("STYLES " + styles.Length + ": " + string.Join(", ", styles.Select(o => o.name).OrderBy(n => n).ToArray()));
        foreach (UnityEngine.Object font in FindAll(T("TMPro.TMP_FontAsset")))
            lines.AppendLine("FONT " + font.name + " source=" + ((UnityEngine.Object)G(font, "sourceFontFile"))?.name);
        foreach (UnityEngine.Object menu in FindAll(Named("UIMainMenuWindow")).Take(1))
        {
            object load = G(menu, "loadGameButton");
            if (load != null) lines.AppendLine("MAINMENU button path=" + PathOf(((Component)load).transform) + " size=" + G(((Component)load).transform, "rect") + " graphic=" + DescribeGraphic(G(load, "targetGraphic")) + " label=" + DescribeText(((Component)load).GetComponentInChildren(T("TMPro.TextMeshProUGUI"), true)));
        }
        foreach (string windowName in new[] { "UIConnectToHostGameWindow", "UINotificationWindow", "UIHintWindow" })
            lines.AppendLine("WINDOW " + windowName + " loaded=" + (Named(windowName) == null ? "no type" : FindAll(Named(windowName)).Length.ToString()));
        return lines.ToString();
    }
    private static Type T(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    private static void Dump(object value, StringBuilder into, int depth, System.Collections.Generic.HashSet<object> seen)
    {
        if (value == null || depth > 5 || into.Length > 60000) return;
        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string) { into.Append(value); return; }
        if (!type.IsValueType && !seen.Add(value)) { into.Append("<seen>"); return; }
        string pad = new string(' ', depth * 2);
        if (value is System.Collections.IEnumerable list)
        {
            int n = 0;
            into.Append("[" + Environment.NewLine);
            foreach (object item in list)
            {
                if (n++ > 200) break;
                into.Append(pad + "  - ");
                Dump(item, into, depth + 1, seen);
                into.Append(Environment.NewLine);
            }
            into.Append(pad + "]");
            return;
        }
        into.Append(type.Name + " {" + Environment.NewLine);
        foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (typeof(Delegate).IsAssignableFrom(f.FieldType) || typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;
            into.Append(pad + "  " + f.Name + " = ");
            object v; try { v = f.GetValue(value); } catch { v = "?"; }
            Dump(v, into, depth + 1, seen);
            into.Append(Environment.NewLine);
        }
        into.Append(pad + "}");
    }
    private static object FightController()
    {
        Type type = T("FightingGameController");
        for (Type t = type; t != null; t = t.BaseType)
        {
            PropertyInfo instance = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instance != null) return instance.GetValue(null, null);
        }
        throw new Exception("No FightingGameController");
    }

    private static object MainGameBody()
    {
        object controller = G(T("MainGame"), "PlayerController");
        return controller as Component == null ? null : ((Component)controller).GetComponent(T("PlayerPhysicalBody"));
    }

    private static object G(object obj, string name)
    {
        if (obj == null) return null;
        Type type = obj as Type ?? obj.GetType();
        object target = obj is Type ? null : obj;
        return type.GetProperty(name, Flags)?.GetValue(target, null) ?? type.GetField(name, Flags)?.GetValue(target);
    }
    private static object Call(object obj, string name, params object[] args)
    {
        MethodInfo method = obj.GetType().GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length
            && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x));
        return method.Invoke(obj, args);
    }
    private static IEnumerable List(object obj, string name) => G(obj, name) as IEnumerable ?? new object[0];
    private static string Id(object obj) => Convert.ToString(G(G(obj, "UniqueId"), "Id"));
    private static string Items(object inv) => string.Join(";", List(G(inv, "Data"), "Inventory").Cast<object>().Select(i => G(i, "id") + "x" + G(i, "Count")));
    // The items an inventory takes, as the game decides it for an add (empty slots count, bags
    // too): up to max of them, the given id first when it is taken.
    private static System.Collections.Generic.List<string> AcceptedItems(object inventoryData, int max, string first)
    {
        var result = new System.Collections.Generic.List<string>();
        System.Collections.Generic.IEnumerable<string> ids = List(G(T("GameBalance"), "Me"), "itemDefs").Cast<object>().Select(d => Convert.ToString(G(d, "id")));
        if (first != null) ids = new[] { first }.Concat(ids.Where(i => i != first));
        foreach (string id in ids)
        {
            bool ok;
            try { ok = Convert.ToBoolean(Call(inventoryData, "CanAddItemToInventory", Item(id, 1), true, false)); }
            catch { continue; }
            if (ok) { result.Add(id); if (result.Count >= max) break; }
        }
        return result;
    }
    // One item with its nested items and extra properties (durability, quality…), the parts a
    // plain "id x count" copy loses.
    private static string Describe(object item, int depth)
    {
        var text = new StringBuilder(G(item, "id") + "x" + G(item, "Count"));
        var properties = G(item, "properties") as System.Collections.IDictionary;
        if (properties != null && properties.Count > 0)
            text.Append(" props=" + string.Join(",", properties.Keys.Cast<object>().Select(k => ((Type)k).Name).ToArray()));
        var nested = G(item, "inventory") as System.Collections.IEnumerable;
        var children = nested == null ? new object[0] : nested.Cast<object>().ToArray();
        if (children.Length > 0)
            text.Append(depth > 3 ? " [" + children.Length + " nested]" : " [" + string.Join("; ", children.Select(c => Describe(c, depth + 1)).ToArray()) + "]");
        return text.ToString();
    }
    private static void RemoveById(object inventory, string id, int count)
    {
        var remove = inventory.GetType().GetMethods(Flags).First(m => m.Name == "RemoveItemById" && m.GetParameters().Length == 5);
        remove.Invoke(inventory, new object[] { id, count, null, null, false });
    }
    private static object Item(string id, int count) => Activator.CreateInstance(T("Item"), id, count);
    // What the craft window builds when a craft button is pressed.
    private static object NewCraftElement(string craftId, object wgo)
    {
        object parameters = Activator.CreateInstance(T("CraftParamsData"), craftId, wgo, Enum.ToObject(T("CraftParamsData").GetNestedType("CraftParamsType"), 0), -1);
        // With the recipe's ingredients: the (id, count, params) constructor makes a free craft.
        object definition = T("GameBalance").GetMethod("GetCraftDefBase", Flags, null, new[] { typeof(string) }, null).Invoke(null, new object[] { craftId });
        object needs = Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(T("NeedItemData")), G(definition, "needItems"));
        return Activator.CreateInstance(T("CraftElement"), craftId, 1, needs, parameters);
    }

    // A tool of the type the bed is worked with, as PlayerController.HasToolForWork asks for.
    private static string GiveToolFor(object player, object craft, object bed)
    {
        object toolType = G(craft, "customItemTypeAction");
        if (toolType == null || Convert.ToString(toolType) == "None") toolType = G(G(G(bed, "Definition"), "toolAction"), "actionableTool");
        if (toolType == null || Convert.ToString(toolType) == "None") return "none";
        object toolDef = List(G(T("GameBalance"), "Me"), "itemDefs").Cast<object>().FirstOrDefault(d => Equals(G(d, "type"), toolType));
        if (toolDef == null) return "none";
        string id = Convert.ToString(G(toolDef, "id"));
        Call(G(player, "toolBeltInventory"), "AddItemToInventory", Item(id, 1), null, false);
        return id;
    }

    private static string Run(string[] args)
    {
        if (args[0] == "quit")
        {
            Application.Quit();
            return "QUIT requested";
        }
        if (args[0] == "sprites")
        {
            // Loaded sprites whose name matches a pattern: art the mod could borrow.
            var regex = new System.Text.RegularExpressions.Regex(args[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var list = new StringBuilder("SPRITES lang=" + G(Named("LLBase"), "CurrentLang") + Environment.NewLine);
            foreach (Sprite sprite in FindAll(typeof(Sprite)).OfType<Sprite>().Where(sp => regex.IsMatch(sp.name)).OrderBy(sp => sp.name).Take(400))
                list.AppendLine("SPRITE " + sprite.name + " rect=" + sprite.rect + " tex=" + (sprite.texture == null ? "-" : sprite.texture.name) + " border=" + sprite.border);
            return list.ToString();
        }
        if (args[0] == "sprite-save")
        {
            // Writes one sprite to a PNG so its look can be checked.
            Sprite sprite = FindAll(typeof(Sprite)).OfType<Sprite>().First(sp => sp.name == args[1]);
            Rect r = sprite.textureRect;
            var target = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height);
            Graphics.Blit(sprite.texture, target);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            byte[] png = (byte[])T("UnityEngine.ImageConversion").GetMethod("EncodeToPNG", new[] { typeof(Texture2D) }).Invoke(null, new object[] { copy });
            File.WriteAllBytes(args[2], png);
            return "SPRITE-SAVE " + args[1] + " " + r;
        }
        if (args[0] == "ui-tree")
        {
            // A UI object's hierarchy: components, anchors, size, and layout settings.
            Transform found = null;
            foreach (UnityEngine.Object candidate in FindAll(typeof(RectTransform)))
            {
                var rt = candidate as RectTransform;
                if (rt != null && rt.gameObject.scene.IsValid() && PathOf(rt).EndsWith(args[1])) { found = rt; break; }
            }
            if (found == null) return "UI-TREE none " + args[1];
            var tree = new StringBuilder("UI-TREE " + PathOf(found) + Environment.NewLine);
            DumpTree(found, 0, tree);
            return tree.ToString();
        }
        if (args[0] == "resolution")
        {
            // Through the game's own resolution list and settings, as its options menu does: each
            // entry carries the pixel size the UI is scaled by. The test restores saved settings.
            Type config = Named("ResolutionConfig");
            var list = (System.Collections.IList)config.GetField("hardcodedResolutions", Flags).GetValue(null);
            int w = int.Parse(args[1]), h = int.Parse(args[2]);
            object chosen = list.Cast<object>().FirstOrDefault(c => Convert.ToInt32(G(c, "ListedWidth")) == w && Convert.ToInt32(G(c, "ListedHeight")) == h);
            if (chosen == null) chosen = Activator.CreateInstance(config, w, h);
            config.GetField("currentResolution", Flags).SetValue(null, chosen);
            object settings = G(Named("GameSettings"), "Instance");
            settings.GetType().GetMethod("ApplyScreenSettings", Flags).Invoke(settings, null);
            return "RESOLUTION requested " + w + "x" + h + " pixelSize=" + G(config, "PixelSize") + " ui=" + G(config, "Width") + "x" + G(config, "Height") + " screen=" + Screen.width + "x" + Screen.height;
        }
        if (args[0] == "ui-scale")
        {
            // The game's UI scale is its resolution's pixel size (x2 at 1080p, x4 at 4K), so its
            // layout space is the window divided by it. Setting it the way the game does
            // reproduces another resolution's layout in this window.
            float factor = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            foreach (Component scaler in FindAll(T("UnityEngine.UI.CanvasScaler")).OfType<Component>().Where(c => c.gameObject.scene.IsValid()))
                scaler.GetType().GetProperty("scaleFactor").SetValue(scaler, factor, null);
            MethodInfo lazy = Named("LazyUI").GetMethod("SetCanvasScaleFactor", Flags);
            if (lazy != null) lazy.Invoke(null, new object[] { factor });
            RectTransform layerRect = FindAll(typeof(RectTransform)).OfType<RectTransform>().FirstOrDefault(r => r.name == "GK2Coop.UI" && r.gameObject.scene.IsValid());
            return "UI-SCALE " + factor + " layout=" + (layerRect == null ? "?" : layerRect.rect.width.ToString("F0") + "x" + layerRect.rect.height.ToString("F0"));
        }
        if (args[0] == "ui-box")
        {
            // The mod lays out everything inside its two layers. Sizing them to another
            // resolution's layout space (the window divided by the game's pixel size) shows the
            // mod's UI exactly as it is laid out there; "full" puts them back.
            var sized = new StringBuilder("UI-BOX " + args[1]);
            foreach (RectTransform layerRect in FindAll(typeof(RectTransform)).OfType<RectTransform>().Where(r => (r.name == "GK2Coop.UI" || r.name == "GK2Coop.HUD") && r.gameObject.scene.IsValid()))
            {
                if (args[1] == "full")
                {
                    layerRect.anchorMin = Vector2.zero; layerRect.anchorMax = Vector2.one;
                    layerRect.offsetMin = Vector2.zero; layerRect.offsetMax = Vector2.zero;
                }
                else
                {
                    layerRect.anchorMin = layerRect.anchorMax = new Vector2(0.5f, 0.5f);
                    layerRect.sizeDelta = new Vector2(float.Parse(args[1]), float.Parse(args[2]));
                    layerRect.anchoredPosition = Vector2.zero;
                }
                sized.Append(" " + layerRect.name + "=" + layerRect.rect.width.ToString("F0") + "x" + layerRect.rect.height.ToString("F0"));
            }
            return sized.ToString();
        }
        if (args[0] == "pad")
        {
            // Pretend a controller is in use (pad|Xbox_XboxController, pad|Sony_DualSense), or stop.
            Type input = T("LazyBearTechnology.LazyInput");
            if (args[1] == "off")
            {
                input.GetMethod("ClearGamepadActivityState", Flags).Invoke(null, null);
                input.GetMethod("ClearForcedGamepadType", Flags).Invoke(null, null);
            }
            else
            {
                object type = T("LazyBearTechnology.GamepadType").GetField(args[1], BindingFlags.Public | BindingFlags.Static).GetValue(null);
                input.GetMethod("ForceDebugGamepadType", Flags).Invoke(null, new[] { type });
                input.GetMethod("ForceGamepadActivityState", Flags).Invoke(null, new object[] { true });
                input.GetMethod("NotifyInputChanged", Flags)?.Invoke(null, null);
            }
            return "PAD " + args[1] + " active=" + G(input, "IsGamepadActive") + " type=" + G(input, "CurrentGamepadType");
        }
        if (args[0] == "mem")
        {
            // For long sessions: managed heap, process memory, frame time and object counts.
            int objects = UnityEngine.Object.FindObjectsOfType<GameObject>().Length;
            long unity = (long)T("UnityEngine.Profiling.Profiler").GetMethod("GetTotalAllocatedMemoryLong").Invoke(null, null);
            return "MEM heapMB=" + (GC.GetTotalMemory(false) / 1048576L) + " workingMB=" + (unity / 1048576L) +
                   " frameMs=" + (Time.smoothDeltaTime * 1000f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                   " objects=" + objects + " time=" + Time.realtimeSinceStartup.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (args[0] == "scan-cost")
        {
            // What finding objects of a type costs here (scan-cost|PlayerPhysicalBody): the whole
            // memory, the scenes with inactive objects, the active ones; each the median of three.
            Type type = Named(args[1]);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<Func<int>, string> time = find =>
            {
                var runs = new double[3];
                int found = 0;
                for (int i = 0; i < 3; i++)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    found = find();
                    runs[i] = watch.Elapsed.TotalMilliseconds;
                }
                Array.Sort(runs);
                return found + " in " + runs[1].ToString("0.00", inv) + " ms";
            };
            return "SCAN-COST " + args[1] +
                   " all=" + time(() => Resources.FindObjectsOfTypeAll(type).Length) +
                   "; scenes+inactive=" + time(() => UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None).Length) +
                   "; active=" + time(() => UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length) +
                   "; monobehaviours=" + time(() => Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour)).Length) +
                   "; objects=" + time(() => Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object)).Length);
        }
        if (args[0] == "perf")
        {
            // The mod's profiler ([Diagnostics] Profiler): totals since the last reset; "perf|reset", "perf|last".
            return "PERF " + T("GK2Coop.CoopProfiler").GetMethod("ReportForTest", Flags).Invoke(null, new object[] { args.Length > 1 ? args[1] : string.Empty });
        }
        if (args[0] == "pause-coop")
        {
            // The pause menu's co-op entry: describe, or open the pause menu.
            Type entry = T("GK2Coop.CoopPauseEntry");
            if (args.Length > 1 && args[1] == "open") return "PAUSE-COOP " + entry.GetMethod("OpenPauseForTest", Flags).Invoke(null, null);
            if (args.Length > 2 && (args[1] == "click" || args[1] == "choose")) return "PAUSE-COOP " + entry.GetMethod("ClickForTest", Flags).Invoke(null, new object[] { args[1], args[2] });
            return "PAUSE-COOP " + entry.GetMethod("Describe", Flags).Invoke(null, null);
        }
        if (args[0] == "stick")
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return "STICK " + T("GK2Coop.CoopInput").GetMethod("StickForTest", Flags).Invoke(null, new object[] { float.Parse(args[1], inv), float.Parse(args[2], inv), float.Parse(args[3], inv) });
        }
        if (args[0] == "pad-hold")
            return "PAD-HOLD " + T("GK2Coop.CoopInput").GetMethod("HoldForTest", Flags).Invoke(null, new object[] { args[1], float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) });
        if (args[0] == "menu-click")
        {
            // A main menu button as a click would (menu-click|Credits, menu-click|GameSettings).
            Type menuType = Named("UIMainMenuWindow");
            var menu = Resources.FindObjectsOfTypeAll(menuType).OfType<Component>().FirstOrDefault(w => w.gameObject.activeInHierarchy);
            if (menu == null) return "MENU-CLICK no main menu shown";
            menuType.GetMethod("On" + args[1] + "ButtonClicked", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(menu, null);
            return "MENU-CLICK " + args[1];
        }
        if (args[0] == "fps")
        {
            slowFps = int.Parse(args[1]);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = slowFps > 0 ? slowFps : 20;
            return "FPS " + Application.targetFrameRate;
        }
        if (args[0] == "pad-button")
            return "PAD-BUTTON " + T("GK2Coop.CoopInput").GetMethod("PressForTest", Flags).Invoke(null, new object[] { args[1] });
        if (args[0] == "menu-recent")
        {
            T("GK2Coop.CoopMenu").GetMethod("Remember", Flags).Invoke(null, new object[] { args[1], int.Parse(args[2]) });
            return "MENU-RECENT remembered " + args[1] + ":" + args[2];
        }
        if (args[0] == "keyboard")
            return "KEYBOARD " + T("GK2Coop.CoopKeyboard").GetMethod("PressForTest", Flags).Invoke(null, new object[] { args.Length > 1 ? args[1] : "describe" });
        if (args[0] == "menu-pad")
            return "MENU-PAD " + T("GK2Coop.CoopMenu").GetMethod("DescribePadForTest", Flags).Invoke(null, null) +
                   " page=" + T("GK2Coop.CoopMenu").GetField("page", Flags).GetValue(null);
        if (args[0] == "pad-press")
        {
            // A controller press as the game would read it: the game key goes on the pressed list
            // for the next frame (pad-press|DpadDown, pad-press|Select, pad-press|Back).
            Type input = T("LazyBearTechnology.LazyInput");
            Type keyType = T("LazyBearTechnology.GameKey");
            object key = keyType.GetField(args[1], BindingFlags.Public | BindingFlags.Static).GetValue(null);
            object instance = G(input, "Instance");
            MethodInfo add = input.GetMethod("AddPressed", Flags);
            add.Invoke(add.IsStatic ? null : instance, new[] { key });
            return "PAD-PRESS " + args[1];
        }
        if (args[0] == "nav-info")
        {
            // Controller navigation: the game's navigation controllers that are active, what they
            // hold and which item is focused; and whether the mod's buttons carry navigation items.
            Type itemType = T("LazyBearTechnology.GamepadNavigationItem");
            Type controllerType = T("LazyBearTechnology.GamepadNavigationController");
            var info = new StringBuilder("NAV-INFO" + Environment.NewLine);
            foreach (Component c in FindAll(controllerType).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy))
            {
                var items = ((System.Collections.IEnumerable)G(c, "selectableItems") ?? new object[0]).Cast<Component>().ToList();
                Component focused = G(c, "FocusedItem") as Component;
                info.AppendLine("CONTROLLER " + PathOf(c.transform) + " enabled=" + G(c, "IsEnabled") + " items=" + items.Count +
                    " focused=" + (focused == null ? "-" : PathOf(focused.transform)) +
                    " coop=" + items.Any(i => i != null && PathOf(i.transform).Contains("GK2Coop")));
            }
            foreach (Component item in FindAll(itemType).OfType<Component>().Where(i => i.gameObject.scene.IsValid() && PathOf(i.transform).Contains("GK2Coop")))
                info.AppendLine("MODITEM " + PathOf(item.transform) + " active=" + item.gameObject.activeInHierarchy + " controller=" + (G(item, "controller") is Component owner ? PathOf(owner.transform) : "-") +
                    " frame=" + (G(item, "focusFrame") is GameObject frame ? frame.name : "-") + " focused=" + G(item, "IsFocused"));
            return info.ToString();
        }
        if (args[0] == "pad-bindings")
        {
            // Controller buttons and the game keys they trigger, by name, plus the icon library in use.
            Type input = T("LazyBearTechnology.LazyInput");
            object bindings = G(input, "GameBindings");
            Func<object, string> nameOf = o =>
            {
                if (o == null) return "null";
                object v = G(o, "value");
                foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (f.FieldType == o.GetType() && Equals(G(f.GetValue(null), "value"), v)) return f.Name;
                return o.GetType().Name + "#" + v;
            };
            var list = new StringBuilder("PAD-BINDINGS gamepad=" + G(input, "IsGamepadActive") + " type=" + G(input, "CurrentGamepadType") + " icons=" + G(input, "ControllerIconLibrary") + Environment.NewLine);
            foreach (object b in (System.Collections.IEnumerable)G(bindings, "gamepadBindings"))
                list.AppendLine("PAD " + nameOf(G(b, "gamepadButton")) + " -> " + nameOf(G(b, "gameKey")) + " " + G(b, "localeId"));
            foreach (object b in (System.Collections.IEnumerable)G(bindings, "keyBindings"))
                list.AppendLine("KEY " + G(b, "keyCode") + " -> " + nameOf(G(b, "gameKey")) + " " + G(b, "localeId"));
            object icons = G(input, "ControllerIconLibrary");
            if (icons != null)
                foreach (MethodInfo m in icons.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    list.AppendLine("ICONS " + m);
            return list.ToString();
        }
        if (args[0] == "bindings")
        {
            // The game's key bindings, as far as reflection shows them (for choosing the mod's keys).
            object bindings = G(T("LazyBearTechnology.LazyInput"), "GameBindings");
            var dump = new StringBuilder("BINDINGS " + (bindings == null ? "null" : bindings.GetType().FullName) + Environment.NewLine);
            Dump(bindings, dump, 0, new System.Collections.Generic.HashSet<object>());
            return dump.ToString();
        }
        if (args[0] == "panel-fit")
        {
            // One of the mod's windows: on screen, and every text in it fits its box (no text
            // cut off, no button label wider than its button).
            RectTransform layer = FindAll(typeof(RectTransform)).OfType<RectTransform>().FirstOrDefault(r => r.name == "GK2Coop.UI" && r.gameObject.scene.IsValid());
            RectTransform panel = FindAll(typeof(RectTransform)).OfType<RectTransform>().FirstOrDefault(r => r.name == args[1] && r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy);
            if (panel == null || layer == null) return "PANEL-FIT " + args[1] + " not shown";
            var lc = new Vector3[4]; layer.GetWorldCorners(lc);
            var pc = new Vector3[4]; panel.GetWorldCorners(pc);
            bool inside = pc.Min(c => c.x) >= lc.Min(c => c.x) - 1f && pc.Min(c => c.y) >= lc.Min(c => c.y) - 1f && pc.Max(c => c.x) <= lc.Max(c => c.x) + 1f && pc.Max(c => c.y) <= lc.Max(c => c.y) + 1f;
            var problems = new System.Collections.Generic.List<string>();
            Type tmp = T("TMPro.TMP_Text");
            foreach (Component text in panel.GetComponentsInChildren(tmp, false))
            {
                var rt = (RectTransform)text.transform;
                string shown = Convert.ToString(G(text, "text"));
                if (string.IsNullOrEmpty(shown)) continue;
                bool overflowing = G(text, "isTextOverflowing") is bool o && o;
                bool wraps = G(text, "enableWordWrapping") is bool w && w;
                float preferred = Convert.ToSingle(G(text, "preferredWidth"));
                // A window's heading has room for one line only.
                int lines = Convert.ToInt32(G(G(text, "textInfo"), "lineCount"));
                if (!PathOf(rt).Contains("/Viewport/") && lines > 1)
                    problems.Add("heading \"" + shown + "\" on " + lines + " lines");
                else if (overflowing || (!wraps && preferred > rt.rect.width + 2f))
                    problems.Add("\"" + shown + "\" " + preferred.ToString("F0") + ">" + rt.rect.width.ToString("F0"));
            }
            return "PANEL-FIT " + args[1] + " size=" + panel.rect.width.ToString("F0") + "x" + panel.rect.height.ToString("F0") + (inside ? " in" : " OUT") +
                   (problems.Count == 0 ? " texts fit" : " TOO LONG " + string.Join(" ; ", problems.ToArray()));
        }
        if (args[0] == "ui-bounds")
        {
            // Where the mod's windows are on screen, in pixels, and whether they fit.
            // Measured against the mod's layer, which is the screen unless a test sized it.
            RectTransform box = FindAll(typeof(RectTransform)).OfType<RectTransform>().FirstOrDefault(r => r.name == "GK2Coop.UI" && r.gameObject.scene.IsValid());
            var boxCorners = new Vector3[4];
            box.GetWorldCorners(boxCorners);
            float boxMinX = boxCorners.Min(c => c.x), boxMaxX = boxCorners.Max(c => c.x), boxMinY = boxCorners.Min(c => c.y), boxMaxY = boxCorners.Max(c => c.y);
            var bounds = new StringBuilder("UI-BOUNDS screen=" + Screen.width + "x" + Screen.height + " box=" + box.rect.width.ToString("F0") + "x" + box.rect.height.ToString("F0"));
            foreach (string name in new[] { "GK2Coop.Menu", "GK2Coop.Status", "Column", "GK2Coop.Chat", "GK2CoopButton" })
            {
                RectTransform found = FindAll(typeof(RectTransform)).OfType<RectTransform>()
                    .FirstOrDefault(r => r.name == name && r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy && PathOf(r).Contains("UIRoot"));
                if (found == null) continue;
                var corners = new Vector3[4];
                found.GetWorldCorners(corners);
                float minX = corners.Min(c => c.x), maxX = corners.Max(c => c.x), minY = corners.Min(c => c.y), maxY = corners.Max(c => c.y);
                bool inside = minX >= boxMinX - 1f && minY >= boxMinY - 1f && maxX <= boxMaxX + 1f && maxY <= boxMaxY + 1f;
                bounds.Append(" " + name + "=[" + minX.ToString("F0") + "," + minY.ToString("F0") + " " + (maxX - minX).ToString("F0") + "x" + (maxY - minY).ToString("F0") + (inside ? " in" : " OUT") + "]");
            }
            return bounds.ToString();
        }
        if (args[0] == "canvases")
        {
            var list = new StringBuilder("CANVASES" + Environment.NewLine);
            foreach (Component canvas in FindAll(T("UnityEngine.Canvas")).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy))
                list.AppendLine("CANVAS " + PathOf(canvas.transform) + " mode=" + G(canvas, "renderMode") + " root=" + G(canvas, "isRootCanvas") +
                                " override=" + G(canvas, "overrideSorting") + " layer=" + G(canvas, "sortingLayerName") + " order=" + G(canvas, "sortingOrder") +
                                " camera=" + ((UnityEngine.Object)G(canvas, "worldCamera"))?.name + " plane=" + G(canvas, "planeDistance"));
            return list.ToString();
        }
        if (args[0] == "languages")
        {
            // The game's languages: id, ISO code and name.
            var names = new StringBuilder("LANGUAGES current=" + G(Named("LLBase"), "CurrentLang") + Environment.NewLine);
            object available = G(Named("LLBase"), "AvailableLanguages");
            foreach (object entry in (System.Collections.IEnumerable)available)
            {
                // A dictionary of id to info, or a plain list of infos.
                object info = G(entry, "Value") ?? entry;
                object id = G(entry, "Key") ?? G(info, "id");
                names.AppendLine("LANGUAGE " + id + " iso=" + G(info, "iso") + " name=" + G(info, "name"));
            }
            return names.ToString();
        }
        if (args[0] == "game-lang")
        {
            // Switch the game's own language as its options do, without saving the setting.
            object settings = G(Named("GameSettings"), "Instance");
            settings.GetType().GetField("language", Flags).SetValue(settings, args[1]);
            settings.GetType().GetMethod("ApplyLanguageSettings", Flags).Invoke(settings, new object[] { false });
            return "GAME-LANG " + G(Named("LLBase"), "CurrentLang");
        }
        if (args[0] == "font-of")
        {
            // The font, its fallbacks and the label's components, for one label by path end.
            Component label = FindAll(T("TMPro.TMP_Text")).OfType<Component>().FirstOrDefault(c => c.gameObject.scene.IsValid() && PathOf(c.transform).EndsWith(args[1]));
            if (label == null) return "FONT-OF none " + args[1];
            object font = G(label, "font");
            object fallbacks = G(font, "fallbackFontAssetTable");
            return "FONT-OF " + PathOf(label.transform) + " text='" + G(label, "text") + "' font=" + ((UnityEngine.Object)font).name + "#" + ((UnityEngine.Object)font).GetInstanceID() +
                   " atlasMode=" + G(font, "atlasPopulationMode") + " fallbacks=" + (fallbacks is System.Collections.IEnumerable list ? string.Join(",", list.Cast<object>().Select(f => f == null ? "null" : ((UnityEngine.Object)f).name + "#" + ((UnityEngine.Object)f).GetInstanceID()).ToArray()) : "-") +
                   " components=" + string.Join(",", label.GetComponents<Component>().Select(c => c.GetType().Name).ToArray());
        }
        if (args[0] == "glyphs")
        {
            // Every character the mod shows must exist in the font it is drawn with (or its fallbacks).
            var report = new StringBuilder("GLYPHS");
            int texts = 0, missingTotal = 0;
            Type tmpText = T("TMPro.TMP_Text");
            foreach (Component label in FindAll(tmpText).OfType<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy))
            {
                string path = PathOf(label.transform);
                if (!path.Contains("GK2Coop")) continue;
                string text = Convert.ToString(G(label, "text"));
                if (string.IsNullOrEmpty(text)) continue;
                object font = G(label, "font");
                MethodInfo has = font.GetType().GetMethods().First(m => m.Name == "HasCharacters" && m.GetParameters().Length == 4 && m.GetParameters()[1].ParameterType.IsByRef);
                object[] hasArgs = { text, null, true, false };
                has.Invoke(font, hasArgs);
                var missing = hasArgs[1] as System.Collections.IList;
                texts++;
                if (missing != null && missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    object fallbacks = G(font, "fallbackFontAssetTable");
                    string fallbackNames = fallbacks is System.Collections.IEnumerable list ? string.Join(",", list.Cast<object>().Select(f => f == null ? "null" : ((UnityEngine.Object)f).name + "#" + ((UnityEngine.Object)f).GetInstanceID()).ToArray()) : "-";
                    report.Append(" MISSING[" + ((UnityEngine.Object)font).name + "#" + ((UnityEngine.Object)font).GetInstanceID() + " fallbacks=" + fallbackNames + " path=" + path + ": " + string.Join("", missing.Cast<object>().Select(c => c.ToString()).Distinct().ToArray()) + " in '" + (text.Length > 30 ? text.Substring(0, 30) : text) + "']");
                }
            }
            return report.Insert(6, " texts=" + texts + " missing=" + missingTotal).ToString();
        }
        if (args[0] == "announce")
        {
            // A message in the current language, as the mod would show it.
            Type text = T("GK2Coop.L");
            string message = args.Length > 2
                ? (string)text.GetMethod("F", Flags).Invoke(null, new object[] { args[1], args.Skip(2).Cast<object>().ToArray() })
                : (string)text.GetMethod("T", Flags).Invoke(null, new object[] { args[1] });
            T("GK2Coop.CoopStatus").GetMethod("Announce", Flags).Invoke(null, new object[] { message, args[1].StartsWith("Could"), 30f });
            return "ANNOUNCE " + message;
        }
        if (args[0] == "script-names")
        {
            // Every global flow script the game can run, from the Addressables catalog.
            const string prefix = "Assets/AddressableAssets/VisualScripts/GlobalScripts/";
            var names = new System.Collections.Generic.SortedSet<string>();
            object locators = G(T("UnityEngine.AddressableAssets.Addressables"), "ResourceLocators");
            foreach (object locator in (System.Collections.IEnumerable)locators)
                foreach (object key in (System.Collections.IEnumerable)G(locator, "Keys"))
                {
                    string k = key as string;
                    if (k != null && k.StartsWith(prefix)) names.Add(k.Substring(prefix.Length));
                }
            var filter = args.Length > 1 ? new System.Text.RegularExpressions.Regex(args[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase) : null;
            var picked = names.Where(n => filter == null || filter.IsMatch(n)).ToArray();
            return "SCRIPT-NAMES total=" + names.Count + " shown=" + picked.Length + Environment.NewLine + string.Join(Environment.NewLine, picked);
        }
        if (args[0] == "script-catalog")
        {
            // Every global script's graph, loaded without running it: which cutscene nodes it has
            // (control, cinematic bars, camera, fades, dialogue) and the events it listens for.
            const string prefix = "Assets/AddressableAssets/VisualScripts/GlobalScripts/";
            Type policy = Named("FlowScriptAssetLoadPolicy");
            MethodInfo load = policy.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "Load" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(string));
            MethodInfo release = policy.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "ReleaseGraph");
            var keys = new System.Collections.Generic.SortedSet<string>();
            foreach (object locator in (System.Collections.IEnumerable)G(T("UnityEngine.AddressableAssets.Addressables"), "ResourceLocators"))
                foreach (object key in (System.Collections.IEnumerable)G(locator, "Keys"))
                    if (key is string k && k.StartsWith(prefix) && k.EndsWith(".asset")) keys.Add(k);
            var filter = args.Length > 1 ? new System.Text.RegularExpressions.Regex(args[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase) : null;
            var report = new StringBuilder("SCRIPT-CATALOG" + Environment.NewLine);
            var allTypes = new System.Collections.Generic.Dictionary<string, int>();
            var typeScripts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (string key in keys)
            {
                string name = key.Substring(prefix.Length, key.Length - prefix.Length - ".asset".Length);
                if (filter != null && !filter.IsMatch(name)) continue;
                try
                {
                    object[] loadArgs = load.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : null).ToArray();
                    loadArgs[0] = prefix + name;
                    object graph = load.Invoke(null, loadArgs);
                    if (graph == null) { report.AppendLine("SCRIPT " + name + " (not loaded)"); continue; }
                    PropertyInfo allNodes = graph.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First(pi => pi.Name == "allNodes");
                    var nodes = ((System.Collections.IEnumerable)allNodes.GetValue(graph, null)).Cast<object>().ToList();
                    var counts = new System.Collections.Generic.Dictionary<string, int>();
                    var events = new System.Collections.Generic.SortedSet<string>();
                    var eventTypes = new System.Collections.Generic.SortedSet<string>();
                    foreach (object node in nodes)
                    {
                        string type = node.GetType().Name;
                        counts[type] = counts.TryGetValue(type, out int c) ? c + 1 : 1;
                        if (type.IndexOf("Event", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        eventTypes.Add(type);
                        // The event's name: a field of the node, often a BBParameter<string> holding it.
                        for (Type t = node.GetType(); t != null && t != typeof(object); t = t.BaseType)
                            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                            {
                                if (f.Name.IndexOf("name", StringComparison.OrdinalIgnoreCase) < 0 && f.Name.IndexOf("event", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                object raw = f.GetValue(node);
                                if (raw == null) continue;
                                object value = raw;
                                if (!(raw is string))
                                {
                                    // BBParameter<T> redeclares "value"; take the most derived one.
                                    PropertyInfo valueProperty = raw.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                        .Where(pi => pi.Name == "value" && pi.GetIndexParameters().Length == 0)
                                        .OrderBy(pi => pi.DeclaringType == raw.GetType() ? 0 : 1).FirstOrDefault();
                                    try { value = valueProperty?.GetValue(raw, null); } catch (Exception) { value = null; }
                                }
                                string text = value as string;
                                if (!string.IsNullOrEmpty(text)) events.Add(text);
                            }
                    }
                    foreach (var kv in counts)
                    {
                        allTypes[kv.Key] = (allTypes.TryGetValue(kv.Key, out int total) ? total : 0) + kv.Value;
                        typeScripts[kv.Key] = (typeScripts.TryGetValue(kv.Key, out int inScripts) ? inScripts : 0) + 1;
                    }
                    Func<string, int> n = t => counts.Where(kv => kv.Key.StartsWith(t)).Sum(kv => kv.Value);
                    report.AppendLine("SCRIPT " + name + " nodes=" + nodes.Count +
                        " control=" + n("Flow_SetControlActive") + " cinematic=" + n("Flow_Cinematic") +
                        " camera=" + (n("Flow_CameraFly") + n("Flow_SetCameraTarget") + n("Flow_CameraAnim")) +
                        " fade=" + n("Flow_Fade") + " dialogue=" + counts.Where(kv => kv.Key.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0).Sum(kv => kv.Value) +
                        " teleport=" + counts.Where(kv => kv.Key.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) >= 0).Sum(kv => kv.Value) +
                        " events=[" + string.Join(",", events.ToArray()) + "] eventNodes=[" + string.Join(",", eventTypes.ToArray()) + "]");
                    if (release != null) release.Invoke(null, new[] { graph });
                }
                catch (Exception ex)
                {
                    report.AppendLine("SCRIPT " + name + " (error " + (ex.InnerException ?? ex).Message + ")");
                }
            }
            foreach (var kv in allTypes.OrderByDescending(kv => typeScripts[kv.Key]))
                report.AppendLine("TYPE " + kv.Key + " nodes=" + kv.Value + " scripts=" + typeScripts[kv.Key]);
            return report.ToString();
        }
        if (args[0] == "script-graph")
        {
            // One global script's flowchart: each step, what it names, and which steps follow it.
            const string prefix = "Assets/AddressableAssets/VisualScripts/GlobalScripts/";
            Type policy = Named("FlowScriptAssetLoadPolicy");
            MethodInfo load = policy.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "Load" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(string));
            object[] loadArgs = load.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : null).ToArray();
            loadArgs[0] = prefix + args[1];
            object graph = load.Invoke(null, loadArgs);
            if (graph == null) return "SCRIPT-GRAPH " + args[1] + " not loaded";
            PropertyInfo allNodes = graph.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First(pi => pi.Name == "allNodes");
            var nodes = ((System.Collections.IEnumerable)allNodes.GetValue(graph, null)).Cast<object>().ToList();
            var report = new StringBuilder("SCRIPT-GRAPH " + args[1] + " nodes=" + nodes.Count + Environment.NewLine);
            for (int i = 0; i < nodes.Count; i++)
            {
                object node = nodes[i];
                var detail = new System.Collections.Generic.List<string>();
                for (Type t = node.GetType(); t != null && t != typeof(object) && !t.Name.StartsWith("Node"); t = t.BaseType)
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (typeof(Delegate).IsAssignableFrom(f.FieldType) || f.FieldType.Name.StartsWith("Flow") || f.FieldType.Name.StartsWith("Value")) continue;
                        object raw; try { raw = f.GetValue(node); } catch { continue; }
                        if (raw == null) continue;
                        object value = raw;
                        if (raw.GetType().Name.StartsWith("BBParameter"))
                        {
                            PropertyInfo vp = raw.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(pi => pi.Name == "value" && pi.GetIndexParameters().Length == 0).OrderBy(pi => pi.DeclaringType == raw.GetType() ? 0 : 1).FirstOrDefault();
                            try { value = vp?.GetValue(raw, null); } catch { value = null; }
                        }
                        if (value == null) continue;
                        string text;
                        if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum) text = value.ToString();
                        else if (value is Array arr) text = "[" + arr.Length + "]" + (arr.Length > 0 && arr.GetValue(0) is string ? " " + string.Join(",", arr.Cast<object>().Take(4).Select(x => Convert.ToString(x)).ToArray()) : string.Empty);
                        else continue;
                        if (text.Length == 0) continue;
                        detail.Add(f.Name + "=" + (text.Length > 40 ? text.Substring(0, 40) : text));
                    }
                // Values typed into the node's inputs (ids of NPCs, points, texts)
                FieldInfo portValues = null;
                for (Type t = node.GetType(); t != null && portValues == null; t = t.BaseType)
                    portValues = t.GetField("_inputPortValues", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (portValues?.GetValue(node) is System.Collections.IDictionary inputs)
                    foreach (System.Collections.DictionaryEntry kv in inputs)
                    {
                        string text = Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(text)) detail.Add(kv.Key + "=" + (text.Length > 40 ? text.Substring(0, 40) : text));
                    }
                var outs = new System.Collections.Generic.List<string>();
                foreach (object connection in (System.Collections.IEnumerable)G(node, "outConnections"))
                {
                    object target = G(connection, "targetNode");
                    string port = null;
                    try { port = Convert.ToString(G(connection, "sourcePortID")); } catch { }
                    outs.Add((string.IsNullOrEmpty(port) ? string.Empty : port + ">") + nodes.IndexOf(target));
                }
                report.AppendLine(i + " " + node.GetType().Name.Replace("Flow_", string.Empty) + " {" + string.Join(" ", detail.ToArray()) + "} -> " + string.Join(", ", outs.ToArray()));
            }
            return report.ToString();
        }
        if (args[0] == "close-windows")
        {
            // Closes the game's open windows (a "bonus content" notice on load, a tutorial page).
            var closed = new System.Collections.Generic.List<string>();
            foreach (MonoBehaviour window in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                Type t = window.GetType();
                Type b = t;
                while (b != null && !(b.IsGenericType && b.GetGenericTypeDefinition().Name == "LazyWindow`1")) b = b.BaseType;
                if (b == null) continue;
                if (!(G(window, "IsShown") is bool shown) || !shown) continue;
                MethodInfo close = b.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => m.Name == "Close").OrderBy(m => m.GetParameters().Length).First();
                close.Invoke(window, close.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : (pi.ParameterType.IsValueType ? Activator.CreateInstance(pi.ParameterType) : null)).ToArray());
                closed.Add(t.Name);
            }
            return "CLOSE-WINDOWS " + string.Join(",", closed.ToArray());
        }
        if (args[0] == "answers" || args[0] == "answer-pick")
        {
            // The answer choices on screen, and picking one as a click would.
            Type multi = Named("UIMultiAnswer");
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var shown = ((System.Collections.IEnumerable)multi.GetField("multiAnswers", all).GetValue(null)).Cast<object>().ToList();
            if (shown.Count == 0) return "ANSWERS none";
            var ids = ((System.Collections.IEnumerable)multi.GetField("visualData", all).GetValue(shown[0])).Cast<object>().Select(a => Convert.ToString(G(a, "id"))).ToList();
            if (args[0] == "answers") return "ANSWERS " + string.Join(" ; ", ids.ToArray());
            string pick = ids[int.Parse(args[1])];
            multi.GetMethod("OnAnswerSelect", all).Invoke(shown[0], new object[] { pick });
            return "ANSWER-PICK " + pick;
        }
        if (args[0] == "say")
            return "SAY " + T("GK2Coop.CoopSpeechShare").GetMethod("SayForTest", Flags).Invoke(null, new object[] { args[1], args[2] });
        if (args[0] == "fight-watch")
            return "FIGHT-WATCH " + T("GK2Coop.CoopFightWatch").GetMethod(args.Length > 1 ? "SimulateForTest" : "Describe", Flags).Invoke(null, args.Length > 1 ? new object[] { args[1] } : null);
        if (args[0] == "active-slot")
        {
            // What the main menu's Continue would open, and the world copies in the save folder.
            object active = T("SaveSystem").GetMethod("GetActiveSaveData", Flags).Invoke(null, null);
            string folder = Convert.ToString(T("SaveSystem").GetProperty("SaveFolder", BindingFlags.Public | BindingFlags.Static).GetValue(null, null));
            var copies = Directory.GetFiles(folder, "GK2Coop_*.info").Select(f => Path.GetFileNameWithoutExtension(f) + "@" +
                System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f), "\"saveDateTime\":\"([^\"]*)\"").Groups[1].Value);
            return "ACTIVE-SLOT " + G(active, "slotName") + " copies=" + string.Join(";", copies.ToArray());
        }
        if (args[0] == "test-tools")
            return "TEST-TOOLS " + T("GK2Coop.CoopTestTools").GetMethod(args.Length > 1 ? "RunForTest" : "Describe", Flags).Invoke(null, args.Length > 1 ? new object[] { args[1] } : null);
        if (args[0] == "night-rule")
        {
            // The host's "The night passes when" setting, as its buttons set it: Everyone or Host.
            object setting = T("GK2Coop.CoopMenu").GetProperty("NightSetting", Flags).GetValue(null, null);
            setting.GetType().GetProperty("Value").SetValue(setting, args[1], null);
            return "NIGHT-RULE " + setting.GetType().GetProperty("Value").GetValue(setting, null);
        }
        if (args[0] == "menu-host")
        {
            // As the co-op menu's Host button does: the next world loaded is hosted.
            T("GK2Coop.CoopMenu").GetMethod("Apply", Flags).Invoke(null, new object[] { "Host", "0.0.0.0" });
            return "MENU-HOST armed";
        }
        if (args[0] == "watchdog")
            return "WATCHDOG " + T("GK2Coop.CoopWatchdog").GetMethod("CheckForTest", Flags).Invoke(null, null);
        if (args[0] == "tag-centres")
            return "TAG-CENTRES " + T("GK2Coop.CoopHud").GetMethod("TagCentresForTest", Flags).Invoke(null, null);
        if (args[0] == "tag-trace")
        {
            Type hud = T("GK2Coop.CoopHud");
            if (args.Length > 1) return "TAG-TRACE " + hud.GetMethod("TraceTagsForTest", Flags).Invoke(null, new object[] { int.Parse(args[1]) });
            return "TAG-TRACE " + hud.GetMethod("TraceResultForTest", Flags).Invoke(null, null);
        }
        if (args[0] == "look-draw")
        {
            // (Not "looks": that one, further down, fingerprints each player's customization.)
            // Each player body's drawn preset: whether each part has a colour palette (without one it
            // is drawn in the raw source colours: blue hair, pink clothes), and whether a remote body
            // shares a palette or part with the local player's preset.
            object local = G(T("MainGame"), "PlayerData");
            object localPreset = G(T("PlayerSkinHelper"), "CurrentPreset");
            var lines = new System.Collections.Generic.List<string>();
            foreach (Component body in FindAll(T("PlayerPhysicalBody")).Cast<Component>().Where(b => b != null && b.gameObject.activeInHierarchy))
            {
                object data = G(body, "playerData");
                object animation = G(G(body, "playerView"), "PlayerAnimation");
                object preset = null;
                for (Type t = animation?.GetType(); t != null && preset == null; t = t.BaseType)
                    preset = t.GetField("skinPreset", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(animation);
                var parts = new System.Collections.Generic.List<string>();
                int missing = 0, shared = 0;
                foreach (string part in new[] { "head", "hairstyle", "beard", "body", "arms" })
                {
                    object p = G(preset, part);
                    object palette = G(p, "palette");
                    object localPart = G(localPreset, part);
                    bool isLocalBody = ReferenceEquals(data, local);
                    // Missing: no palette where the local player's same part has one (the game leaves the head without).
                    bool usesPalette = G(localPart, "palette") != null;
                    if (p != null && palette == null && usesPalette) missing++;
                    if (!isLocalBody && p != null && (ReferenceEquals(p, localPart) || (palette != null && ReferenceEquals(palette, G(localPart, "palette"))))) shared++;
                    parts.Add(part + "=" + (p == null ? "-" : (palette == null ? (usesPalette ? "NO-PALETTE" : "own") : "palette#" + ((UnityEngine.Object)palette).GetInstanceID())));
                }
                string who = ReferenceEquals(data, local) ? "local" : "remote";
                // As drawn: the game's colour-replace shader takes its tint from the material's _Color, other sprites from the renderer.
                float alpha = body.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.enabled && r.sprite != null).Select(r => (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "Sprites/ColorReplace" && r.sharedMaterial.HasProperty("_Color") ? r.sharedMaterial.GetColor("_Color").a : 1f) * r.color.a).DefaultIfEmpty(1f).Max();
                string tint = string.Join(",", body.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.enabled && r.sprite != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color")).Take(1).Select(r => r.sharedMaterial.GetColor("_Color").ToString("F2")));
                lines.Add($"LOOK {who} alpha={alpha.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} tint={tint.Replace(" ", "")} preset={(preset == null ? "none" : "yes")} missing={missing} shared-with-local={shared} {string.Join(" ", parts)}");
            }
            return lines.Count == 0 ? "LOOK none" : string.Join("\n", lines);
        }
        if (args[0] == "zombie-look" || args[0] == "body-pose")
        {
            // Experiment: a player's body drawn with a zombie's skin preset (as the game rolls one for a
            // new zombie), and posed in one animation state, to see which keeper animations a zombie look has.
            bool wantLocal = args[1] == "local";
            object localData = G(T("MainGame"), "PlayerData");
            Component body = FindAll(T("PlayerPhysicalBody")).Cast<Component>().FirstOrDefault(b => b != null && b.gameObject.activeInHierarchy && b.gameObject.scene.IsValid() && ReferenceEquals(G(b, "playerData"), localData) == wantLocal);
            if (body == null) return "ZOMBIE-LOOK no " + args[1] + " body";
            object view = G(body, "playerView");
            object animation = G(view, "PlayerAnimation");
            Vector3 screen = Camera.main == null ? Vector3.zero : Camera.main.WorldToScreenPoint(body.transform.position);
            string at = " screen=" + ((int)screen.x) + "," + (Screen.height - (int)screen.y) + " of " + Screen.width + "x" + Screen.height;
            if (args[0] == "body-pose")
            {
                Type stateType = T("AnimationState");
                object state = Enum.Parse(stateType, args[2]);
                animation.GetType().GetMethod("SetState", new[] { stateType }).Invoke(animation, new[] { state });
                return "BODY-POSE " + args[1] + " " + state + at;
            }
            if (args[2] == "off")
            {
                // The player's own look back: the local one as the game applies it, a remote one through the appearance sync.
                if (wantLocal) localData.GetType().GetMethod("ApplyCustomization").Invoke(localData, new[] { G(localData, "customization") });
                else
                {
                    Type sync = T("GK2Coop.CoopAppearanceSync");
                    ((System.Collections.IDictionary)sync.GetField("drawn", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Clear();
                    sync.GetField("nextTick", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0f);
                }
                return "ZOMBIE-LOOK " + args[1] + " off" + at;
            }
            Type helper = T("ZombieSkinHelper");
            object rolled = helper.GetMethod("RollZombie").Invoke(null, new object[] { args[2] });
            object b1 = G(rolled, "Item1"), h1 = G(rolled, "Item2"), bl = G(rolled, "Item3"), hl = G(rolled, "Item4");
            object preset = helper.GetMethod("GetPresetForCustomizationData").Invoke(null, new[] { args[2], b1, h1, bl, hl });
            view.GetType().GetMethod("SetPlayerPreset").Invoke(view, new[] { preset, (object)false });
            object changer = null;
            for (Type t = animation.GetType(); t != null && changer == null; t = t.BaseType)
                changer = t.GetField("skinChanger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)?.GetValue(animation);
            changer?.GetType().GetMethod("ApplyShaderParameters")?.Invoke(changer, null);
            return "ZOMBIE-LOOK " + args[1] + " " + args[2] + " body=" + b1 + " head=" + h1 + " bodyLut=" + bl + " headLut=" + hl + " changer=" + (changer != null) + at;
        }
        if (args[0] == "colliders-near")
        {
            // Every active, solid physics collider within a radius of the local player (what could hold
            // them in), by object path, sorted, so two calls can be compared.
            object localData = G(T("MainGame"), "PlayerData");
            Component body = FindAll(T("PlayerPhysicalBody")).Cast<Component>().FirstOrDefault(b => b != null && b.gameObject.activeInHierarchy && b.gameObject.scene.IsValid() && ReferenceEquals(G(b, "playerData"), localData));
            if (body == null) return "COLLIDERS no local body";
            float radius = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 4f;
            Type physics = Type.GetType("UnityEngine.Physics, UnityEngine.PhysicsModule");
            Type colliderType = Type.GetType("UnityEngine.Collider, UnityEngine.PhysicsModule");
            MethodInfo overlap = physics.GetMethods().First(m => m.Name == "OverlapSphere" && m.GetParameters().Length == 2);
            var found = ((Array)overlap.Invoke(null, new object[] { body.transform.position, radius })).Cast<Component>()
                .Where(c => c != null && !c.transform.IsChildOf(body.transform))
                .Select(c => PathOf(c.transform) + "[" + c.GetType().Name + (Convert.ToBoolean(colliderType.GetProperty("isTrigger").GetValue(c, null)) ? " trigger" : "") + "]")
                .Distinct().OrderBy(x => x).ToArray();
            return "COLLIDERS at " + body.transform.position.ToString("F1") + " r=" + radius + " n=" + found.Length + "\n" + string.Join("\n", found.Select(x => "COLLIDER " + x));
        }
        if (args[0] == "arena")
            return "ARENA " + T("GK2Coop.CoopArenaRescue").GetMethod("Describe", Flags).Invoke(null, null);
        if (args[0] == "wisp")
            return "WISP " + T("GK2Coop.CoopWispFollow").GetMethod("DescribeForTest", Flags).Invoke(null, null);
        if (args[0] == "dialog")
        {
            // The game's dialog window (the one "you're dead" uses): shown or not, and its header.
            Component dialog = FindAll(Named("UIDialogWindow")).Cast<Component>().FirstOrDefault(c => c != null && c.gameObject.scene.IsValid());
            if (dialog == null) return "DIALOG none";
            object shown = dialog.GetType().GetProperty("IsShown", Flags)?.GetValue(dialog, null);
            return "DIALOG shown=" + shown + " active=" + dialog.gameObject.activeInHierarchy + " header='" + G(G(dialog, "header"), "text") + "' guard=" + T("GK2Coop.CoopRemoteBodyGuard").GetMethod("Describe", Flags).Invoke(null, null);
        }
        if (args[0] == "fight-lock")
        {
            Type fightLock = T("GK2Coop.CoopFightLock");
            if (args.Length > 2 && args[1] == "choose") return "FIGHT-LOCK " + fightLock.GetMethod("ChooseForTest", Flags).Invoke(null, new object[] { args[2], false });
            if (args.Length > 2 && args[1] == "choose-at-once") return "FIGHT-LOCK " + fightLock.GetMethod("ChooseForTest", Flags).Invoke(null, new object[] { args[2], true });
            object engine = G(G(G(G(T("MainGame"), "Instance"), "GameSave"), "environmentData"), "EnvironmentEngine");
            object env = G(G(G(T("MainGame"), "Instance"), "GameSave"), "environmentData");
            return "FIGHT-LOCK " + fightLock.GetMethod("Describe", Flags).Invoke(null, null) + "; paused=" + G(engine, "IsPaused") + " time=" + G(env, "TimeOfDay");
        }
        if (args[0] == "work-lock")
        {
            Type lockType = T("GK2Coop.CoopWorkLock");
            string what = args.Length > 1 ? args[1] : "describe";
            if (what == "find") return "WORK-LOCK " + lockType.GetMethod("FindForTest", Flags).Invoke(null, new object[] { args[2] });
            if (what == "interact") return "WORK-LOCK " + lockType.GetMethod("InteractForTest", Flags).Invoke(null, new object[] { args[2] });
            if (what == "close") return "WORK-LOCK " + lockType.GetMethod("CloseForTest", Flags).Invoke(null, null);
            return "WORK-LOCK " + lockType.GetMethod("Describe", Flags).Invoke(null, null);
        }
        if (args[0] == "sermon-prepare")
        {
            // As the pray window does before firing sermon_start: the sermon about to be held.
            object sermonDef = List(G(T("GameBalance"), "Me"), "sermonDefs").Cast<object>().FirstOrDefault();
            if (sermonDef == null) return "SERMON no sermon definitions";
            string sermonId = Convert.ToString(G(sermonDef, "id"));
            object sermon = Activator.CreateInstance(T("SermonResultData"), new object[] { sermonId, string.Empty, 3, true, 1, 1 });
            object playerData = G(T("MainGame"), "PlayerData");
            playerData.GetType().GetField("currentSermon", Flags).SetValue(playerData, sermon);
            return "SERMON prepared " + sermonId;
        }
        if (args[0] == "speech")
            return "SPEECH " + T("GK2Coop.CoopSpeechShare").GetMethod("Describe", Flags).Invoke(null, null);
        if (args[0] == "bubbles")
        {
            // Speech bubbles on screen: what they say and what keeps them there.
            Type bubbleType = T("LazyBearTechnology.UISpeechBubble");
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var report = new StringBuilder("BUBBLES paused=" + bubbleType.GetField("isPaused", all)?.GetValue(null));
            var active = bubbleType.GetField("activeBubbles", all)?.GetValue(null) as System.Collections.IDictionary;
            report.Append(" active=" + (active == null ? "?" : active.Count.ToString()));
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(bubbleType))
            {
                var c = (UnityEngine.Component)o;
                if (!c.gameObject.activeInHierarchy) continue;
                Func<string, object> f = n => bubbleType.GetField(n, all)?.GetValue(o);
                string text = Convert.ToString(f("text"));
                report.Append(Environment.NewLine + "BUBBLE " + c.GetType().Name + " text=" + (text.Length > 50 ? text.Substring(0, 50) : text) +
                    " showTime=" + f("bubbleShowTime") + " pauseCounter=" + f("bubblePauseCounter") + " disappearing=" + f("disappearing") +
                    " canSkip=" + f("canSkipBubble") + " disableCounter=" + f("disableBubbleCounter") + " pos=" + ((RectTransform)c.transform).position +
                    " voice=" + f("voiceOverLocalKey") + " skipCondition=" + (f("customDialogSkipCondition") != null));
            }
            return report.ToString();
        }
        if (args[0] == "scene-share")
            return "SCENE-SHARE " + T("GK2Coop.CoopSceneShare").GetMethod("Describe", Flags).Invoke(null, null) +
                   " prompt=" + T("GK2Coop.CoopSceneShare").GetMethod("DescribePrompt", Flags).Invoke(null, null);
        if (args[0] == "scene-answer")
            return "SCENE-ANSWER " + T("GK2Coop.CoopSceneShare").GetMethod("AnswerForTest", Flags).Invoke(null, new object[] { args[1] });
        if (args[0] == "fire-script-event")
        {
            // As the game starts a story scene: run the script and fire one of its events.
            Named("GlobalScriptsManager").GetMethod("FireEvent", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { args[1], args[2], null });
            return "FIRE-SCRIPT-EVENT " + args[1] + " " + args[2];
        }
        if (args[0] == "run-script")
        {
            T("GameScriptUtility").GetMethod("RunGlobalScript", new[] { typeof(string), typeof(Action) }).Invoke(null, new object[] { args[1], null });
            return "RUN-SCRIPT " + args[1];
        }
        if (args[0] == "cutscene-trace")
            return "CUTSCENE-TRACE " + T("GK2Coop.CoopCutsceneTrace").GetMethod("Describe", Flags).Invoke(null, null) + Environment.NewLine +
                   T("GK2Coop.CoopCutsceneTrace").GetMethod("Recent", Flags).Invoke(null, null);
        if (args[0] == "game-keys")
        {
            // The game's own translation keys matching a pattern, with their text in the current language.
            var regex = new System.Text.RegularExpressions.Regex(args[1], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Type lang = Named("LLBase");
            // The current language's instance: a static field of the class's own type.
            object current = null;
            var statics = new StringBuilder();
            string currentId = Convert.ToString(G(lang, "CurrentLang"));
            foreach (Type owner in new[] { lang, Named("LL") })
                foreach (FieldInfo f in owner.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object v = f.GetValue(null);
                    statics.Append(" " + owner.Name + "." + f.Name + ":" + (v == null ? "null" : v.GetType().Name));
                    if (current == null && v != null && lang.IsInstanceOfType(v)) current = v;
                    if (current == null && v is System.Collections.IDictionary d && d.Contains(currentId) && lang.IsInstanceOfType(d[currentId])) current = d[currentId];
                }
            object ids = current == null ? null : (lang.GetField("dictionary", Flags)?.GetValue(current) ?? lang.GetField("txtIds", Flags)?.GetValue(current));
            var found = new StringBuilder("GAME-KEYS");
            int count = 0;
            System.Collections.IEnumerable keys = ids is System.Collections.IDictionary dict ? dict.Keys : ids as System.Collections.IEnumerable;
            if (keys != null)
                foreach (object key in keys)
                {
                    string k = Convert.ToString(key);
                    bool byValue = args.Length > 2 && args[2] == "value";
                    if (!byValue && !regex.IsMatch(k)) continue;
                    string value = Convert.ToString(lang.GetMethod("L", new[] { typeof(string) }).Invoke(null, new object[] { k }));
                    if (byValue && !regex.IsMatch(value)) continue;
                    found.Append(" | " + k + "=" + (value.Length > 40 ? value.Substring(0, 40) : value));
                    if (++count >= 80) break;
                }
            return found.Insert(9, " count=" + count + " source=" + (ids == null ? "none" : ids.GetType().Name) + (ids == null ? " statics=" + statics : "")).ToString();
        }
        if (args[0] == "places")
        {
            // Every scene of the world with the name the mod shows for it, and whether that name is
            // the game's own translation.
            var names = new StringBuilder("PLACES");
            object placesWorld = G(G(G(T("MainGame"), "Instance"), "GameSave"), "worldData");
            object placeScenes = G(placesWorld, "gameSceneDataList");
            Type text = T("GK2Coop.L");
            if (placeScenes is System.Collections.IEnumerable list)
                foreach (object scene in list)
                {
                    string id = Convert.ToString(G(scene, "id"));
                    string shown = (string)text.GetMethod("Place", Flags).Invoke(null, new object[] { id });
                    bool fromGame = false;
                    foreach (string key in new[] { "wz_" + id, id, "scene_" + id, "location_" + id })
                        if (text.GetMethod("Game", Flags).Invoke(null, new object[] { key }) != null) { fromGame = true; break; }
                    names.Append(" " + id + "=" + shown + (fromGame ? "(game)" : ""));
                }
            return names.ToString();
        }
        if (args[0] == "lang")
        {
            T("GK2Coop.L").GetProperty("Forced", Flags).SetValue(null, args[1] == "game" ? null : args[1], null);
            return "LANG " + args[1] + " game=" + T("GK2Coop.L").GetProperty("Language", Flags).GetValue(null, null);
        }
        if (args[0] == "pose" || args[0] == "face")
        {
            // Pictures: the local player in a pose (a work animation) or looking one way. Both are
            // what the game sends to the others with every move, so everyone sees it.
            object data = G(T("MainGame"), "PlayerData");
            object controller = G(T("MainGame"), "PlayerController");
            if (args[0] == "face")
            {
                Vector2 look = new Vector2(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                data.GetType().GetProperty("Direction").SetValue(data, look, null);
                return "FACE " + look;
            }
            Type stateType = T("AnimationState");
            object state = Enum.Parse(stateType, args[1]);
            heldPose = args[1] == "Idle" ? null : state;
            object charState = G(data, "charState");
            charState.GetType().GetProperty("Value").SetValue(charState, state, null);
            object animation = G(G(controller, "View"), "PlayerAnimation");
            animation?.GetType().GetMethod("SetState", new[] { stateType })?.Invoke(animation, new[] { state });
            return "POSE " + state;
        }
        if (args[0] == "walk")
        {
            // Pictures: the local player walks (path and walking animation) to a spot this far away.
            object controller = G(T("MainGame"), "PlayerController");
            Vector3 from = ((Component)controller).transform.position;
            Vector3 to = from + new Vector3(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
            object movement = G(controller, "PlayerLocalAreaMovement");
            MethodInfo start = movement.GetType().GetMethods(Flags).First(m => m.Name == "StartMovement" && m.GetParameters().Length == 2);
            start.Invoke(movement, new object[] { to, Enum.ToObject(start.GetParameters()[1].ParameterType, 0) });
            return "WALK " + from.ToString("F2") + " -> " + to.ToString("F2");
        }
        if (args[0] == "burst")
        {
            burstFolder = args[1];
            Directory.CreateDirectory(burstFolder);
            burstIndex = 0;
            burstLeft = int.Parse(args[2]);
            burstEvery = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
            burstNext = Time.unscaledTime;
            return "BURST " + burstLeft + " frames every " + burstEvery + " s into " + burstFolder;
        }
        if (args[0] == "time")
        {
            // Pictures: the time of day (0..1; 0.45 is late morning). On the host; the clock sync
            // brings the others along.
            object engine = G(G(G(G(T("MainGame"), "Instance"), "GameSave"), "environmentData"), "EnvironmentEngine");
            if (args.Length > 1) engine.GetType().GetMethod("SetTimeOfDay", new[] { typeof(float) }).Invoke(engine, new object[] { float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) });
            object environment = G(G(G(T("MainGame"), "Instance"), "GameSave"), "environmentData");
            return "TIME " + Convert.ToString(G(environment, "TimeOfDay"), System.Globalization.CultureInfo.InvariantCulture) +
                   " day=" + G(environment, "Day");
        }
        if (args[0] == "look-set")
        {
            // Pictures: the local player's colours (hair, three clothes), one index per colour group,
            // through the game's own customization data; the appearance sync sends it to the others.
            object customization = G(G(T("MainGame"), "PlayerData"), "customization");
            Type helper = T("PlayerSkinHelper");
            object characterData = G(helper, "CharacterCustomizationData");
            var elements = (System.Collections.IList)G(characterData, "customizationElements");
            object preset = G(helper, "CurrentPreset");
            var set = new StringBuilder("LOOK-SET");
            for (int i = 0; i < elements.Count && i + 1 < args.Length; i++)
            {
                object element = elements[i];
                object type = G(element, "playerColorCustomizationType");
                int skin = Convert.ToInt32(preset.GetType().GetMethod("GetSkinPresetPartId").Invoke(preset, new[] { type }));
                int count = Math.Max(1, Convert.ToInt32(element.GetType().GetMethod("GetPalettesCountForSkin").Invoke(element, new object[] { skin })));
                int index = int.Parse(args[i + 1]) % count;
                customization.GetType().GetMethod("SetColorCustomizationIndexForType").Invoke(customization, new[] { type, (object)index });
                set.Append(" " + type + "=" + index + "/" + count);
            }
            helper.GetMethod("ApplySkin").Invoke(null, new[] { customization, (object)false });
            helper.GetMethod("ApplyPlayerColorsByData").Invoke(null, new[] { customization, (object)false });
            return set.ToString();
        }
        if (args[0] == "go-to-menu")
        {
            // As Pause > main menu does: the game's own way back to the main menu.
            object mainGame = G(T("MainGame"), "Instance");
            MethodInfo goToMenu = mainGame.GetType().GetMethod("GoToMenu", Flags);
            goToMenu.Invoke(mainGame, new object[] { null, false, Enum.ToObject(goToMenu.GetParameters()[2].ParameterType, 0) });
            return "GO-TO-MENU requested";
        }
        if (args[0] == "game-state")
        {
            object mainGame = G(T("MainGame"), "Instance");
            return "GAME-STATE " + (mainGame == null ? "none" : Convert.ToString(G(mainGame, "gameState")));
        }
        if (args[0] == "status-ui")
        {
            if (args.Length > 1 && args[1] != "describe")
                T("GK2Coop.CoopProgressWindow").GetMethod("ShowForTest", Flags).Invoke(null, new object[] { args[1], args.Length > 2 ? args[2] : "" });
            return "STATUS-UI " + T("GK2Coop.CoopProgressWindow").GetMethod("Describe", Flags).Invoke(null, null);
        }
        if (args[0] == "hud-ui")
            return "HUD-UI " + T("GK2Coop.CoopHud").GetMethod("DescribeGameUi", Flags).Invoke(null, null);
        if (args[0] == "chat-ui")
            return "CHAT-UI " + T("GK2Coop.CoopChat").GetMethod("DescribeGameUi", Flags).Invoke(null, null);
        if (args[0] == "chat-open")
        {
            T("GK2Coop.CoopChat").GetMethod("OpenForTest", Flags).Invoke(null, null);
            return "CHAT-OPEN";
        }
        if (args[0] == "menu-ui")
            return "MENU-UI " + T("GK2Coop.CoopMenu").GetMethod("TestUi", Flags).Invoke(null, new object[] { args.Length > 1 ? args[1] : "describe", args.Length > 2 ? args[2] : "" });
        if (args[0] == "shot")
        {
            T("UnityEngine.ScreenCapture").GetMethod("CaptureScreenshot", new[] { typeof(string) }).Invoke(null, new object[] { args[1] });
            return "SHOT " + args[1];
        }
        if (args[0] == "menu-status")
        {
            object network = G(T("LazyNetwork"), "NetworkManager");
            object netcode = G(T("Unity.Netcode.NetworkManager"), "Singleton");
            return "MENU network=" + (network == null ? "missing" : network.GetType().Name) +
                " netcode=" + (netcode == null ? "missing" : netcode.GetType().Name) +
                " listening=" + G(netcode, "IsListening") + " connected=" + G(netcode, "IsConnectedClient") +
                " gameState=" + G(G(T("MainGame"), "Instance"), "gameState");
        }
        if (args[0] == "menu-connect")
        {
            object network = G(T("LazyNetwork"), "NetworkManager");
            if (network == null) throw new Exception("LazyNetwork is not initialized on the menu");
            return "MENU-CONNECT started=" + Call(network, "ConnectToHost", args[1], ushort.Parse(args[2]));
        }
        if (args[0].StartsWith("steam-")) return SteamSpike.Run(args);
        if (args[0] == "transport")
        {
            Type wire = T("GK2Coop.CoopSteamTransportSwitch");
            return "TRANSPORT " + wire.GetProperty("ActiveName", Flags).GetValue(null, null) + " " + wire.GetMethod("Describe", Flags).Invoke(null, null);
        }
        if (args[0] == "menu-bootstrap")
        {
            Type menu = T("GK2Coop.CoopMenu");
            menu.GetField("nameField", Flags).SetValue(null, args.Length > 3 ? args[3] : "BootstrapGuest");
            menu.GetField("portField", Flags).SetValue(null, args[2]);
            menu.GetMethod("Apply", Flags).Invoke(null, new object[] { "Connect", args[1] });
            Type bootstrap = T("GK2Coop.CoopSaveBootstrap");
            bootstrap.GetMethod("Begin", Flags).Invoke(null, new object[] { args[1], ushort.Parse(args[2]) });
            object localName = T("GK2Coop.CoopSession").GetProperty("LocalName", Flags).GetValue(null, null);
            return "MENU-BOOTSTRAP started localName=" + localName;
        }
        if (args[0] == "menu-disconnect")
        {
            object network = G(T("LazyNetwork"), "NetworkManager");
            Call(network, "DisconnectFromHost");
            return "MENU-DISCONNECT requested";
        }
        object game = G(T("MainGame"), "Instance");
        object save = G(game, "GameSave");
        object player = G(T("MainGame"), "PlayerData");
        if (save == null || player == null) throw new Exception("World not ready");
        object world = G(save, "worldData");
        object dropSystem = G(game, "dropSystem");
        object quests = G(save, "questSystemData");
        var scenes = List(world, "gameSceneDataList").Cast<object>().ToArray();
        var wgos = scenes.SelectMany(s => List(s, "wgoDataList").Cast<object>()).ToArray();
        var drops = scenes.SelectMany(s => List(s, "droppedItems").Cast<object>()).ToArray();
        switch (args[0])
        {
            case "baseline":
                var baseline = new StringBuilder();
                foreach (object wgo in wgos.OrderBy(w => Id(w)))
                    baseline.AppendLine("WGO " + Id(wgo) + " " + G(wgo, "id") + " " + G(wgo, "currentGameSceneId"));
                foreach (object quest in List(G(quests, "questCollection"), "quests").Cast<object>().OrderBy(q => Convert.ToString(G(q, "id"))))
                    baseline.AppendLine("QUEST " + G(quest, "id") + " " + G(quest, "status") + " hidden=" + G(quest, "isHidden") + " unknown=" + G(quest, "isUnknown"));
                return baseline.ToString();
            case "progress":
                var summary = new StringBuilder();
                var allQuests = List(G(quests, "questCollection"), "quests").Cast<object>().ToArray();
                summary.AppendLine("SAVE version=" + G(save, "GameSaveVer") + " scene=" + G(player, "currentGameSceneId"));
                summary.AppendLine("WORLD scenes=" + scenes.Length + " objects=" + wgos.Length + " drops=" + drops.Length);
                summary.AppendLine("PLAYER items=" + Items(G(player, "inventory")));
                foreach (var group in allQuests.GroupBy(q => Convert.ToString(G(q, "status"))))
                    summary.AppendLine("QUESTS " + group.Key + "=" + group.Count());
                foreach (object quest in allQuests.Where(q =>
                    !string.Equals(Convert.ToString(G(q, "status")), "Available", StringComparison.Ordinal)).Take(40))
                    summary.AppendLine("ACTIVE " + G(quest, "id") + " " + G(quest, "status"));
                return summary.ToString();
            case "inspect":
                var output = new StringBuilder();
                output.AppendLine("PLAYER scene=" + G(player, "currentGameSceneId") + " items=" + Items(G(player, "inventory")));
                foreach (object drop in drops)
                    output.AppendLine("DROP " + Id(drop) + " " + G(drop, "Id") + " count=" + G(drop, "Count") + " scene=" + G(drop, "WorldId"));
                foreach (object wgo in wgos.Where(w => G(w, "Inventory") != null && Convert.ToInt32(G(G(G(w, "Inventory"), "Data"), "InventorySize")) > 0).Take(25))
                    output.AppendLine("CONTAINER " + Id(wgo) + " " + G(wgo, "id") + " size=" + G(G(G(wgo, "Inventory"), "Data"), "InventorySize") + " items=" + Items(G(wgo, "Inventory")));
                foreach (object quest in List(G(quests, "questCollection"), "quests").Cast<object>().Where(q => Convert.ToString(G(q, "id")).StartsWith("1_intro_prison")))
                    output.AppendLine("QUEST " + G(quest, "id") + " " + G(quest, "status"));
                return output.ToString();
            case "spawn":
                object item = Item(args[1], int.Parse(args[2]));
                var pos = (Vector3)G(G(player, "position"), "Value");
                Call(dropSystem, "DropItemAsDropView", item, Convert.ToString(G(player, "currentGameSceneId")), pos + new Vector3(8f, 0f, 0f));
                return "SPAWN " + Id(item);
            case "collect":
                object view = FindAll(T("DropView")).Cast<object>().Single(v => Id(G(v, "Data")) == args[1]);
                Call(player, "CollectDrop", view);
                return "COLLECT inventory=" + Items(G(player, "inventory"));
            case "container-add":
            {
                object inv = G(wgos.Single(w => Id(w) == args[1]), "Inventory");
                // "auto": the first item the container takes, wood first (since game 1.007 some
                // containers, a conveyor source among them, refuse wood).
                string addId = args[2] == "auto" ? AcceptedItems(G(inv, "Data"), 1, "wood").FirstOrDefault() : args[2];
                if (addId == null) return "ADD False item=none items=" + Items(inv);
                object added = Call(inv, "AddItemToInventory", Item(addId, int.Parse(args[3])), null, false);
                return "ADD " + added + " item=" + addId + " items=" + Items(inv);
            }
            case "container-accepts":
            {
                // What the game says the container takes (Item.CanAddItemToInventory), the first N of
                // the item catalogue, and whether it takes wood.
                object accWgo = wgos.Single(w => Id(w) == args[1]);
                object accData = G(G(accWgo, "Inventory"), "Data");
                var accepted = AcceptedItems(accData, args.Length > 2 ? int.Parse(args[2]) : 10, null);
                return "ACCEPTS " + args[1] + " " + G(accWgo, "id") + " size=" + G(accData, "inventorySize") +
                    " wood=" + AcceptedItems(accData, 1, "wood").Contains("wood") + " items=" + string.Join(",", accepted.ToArray());
            }
            case "container-remove":
                object removeInv = G(wgos.Single(w => Id(w) == args[1]), "Inventory");
                Call(removeInv, "RemoveItemById", args[2], int.Parse(args[3]), null, null, false);
                return "REMOVE items=" + Items(removeInv);
            // Phase 5: progress the host world before a client joins.
            case "plant":
            {
                // What the garden window's Plant button does, with the seed given to the player first.
                object bed = wgos.First(w => Id(w) == args[1]);
                Type handler = T("GardenInteractionHandler");
                MethodInfo findCrop = handler.GetMethod("TryFindGardenCraft", Flags);
                object seed = null, crop = null;
                // "auto": the first seed in the catalogue that grows in this bed.
                foreach (string id in args[2] == "auto"
                    ? List(G(T("GameBalance"), "Me"), "itemDefs").Cast<object>().Select(d => Convert.ToString(G(d, "id")))
                    : new[] { args[2] })
                {
                    object candidate = Item(id, 1);
                    if (!Convert.ToBoolean(G(candidate, "IsSeed"))) continue;
                    object found = findCrop.Invoke(null, new[] { candidate, bed, (object)false });
                    if (found != null) { seed = candidate; crop = found; break; }
                }
                if (crop == null) return "PLANT nocrop " + args[2];
                // Supply what a player planting for real would carry: every seed the crop needs,
                // and a tool of the type the bed is worked with (as PlayerController.HasToolForWork asks).
                foreach (object need in (IEnumerable)handler.GetMethod("FormNeedItems", Flags).Invoke(null, new[] { crop, seed }))
                    Call(G(player, "inventory"), "AddItemToInventory", Item(Convert.ToString(G(need, "id")), Convert.ToInt32(Call(G(need, "count"), "EvaluateFloat"))), null, false);
                string gaveTool = GiveToolFor(player, crop, bed);
                // As the garden window does: the player works the bed while planting, which is what
                // puts the player's inventory (and the seed) within the craft's reach.
                object controller = G(T("MainGame"), "PlayerController");
                bed.GetType().GetMethod("TrySetWorker", Flags).Invoke(bed, new[] { controller, null });
                Call(G(bed, "CraftComponent"), "Clear");
                object ok = handler.GetMethod("TryApplySeed", Flags).Invoke(null, new[] { seed, crop, bed });
                string why = "";
                if (!Convert.ToBoolean(ok))
                {
                    // The game's own verdict, built the way TryApplySeed builds it.
                    object needs = handler.GetMethod("FormNeedItems", Flags).Invoke(null, new[] { crop, seed });
                    Type paramsType = T("CraftParamsData");
                    object parameters = Activator.CreateInstance(paramsType, G(crop, "id"), bed, Enum.Parse(paramsType.GetNestedType("CraftParamsType"), "GardenPlanting"), Convert.ToInt32(G(G(seed, "Definition"), "talentValue")));
                    object element = Activator.CreateInstance(T("CraftElement"), G(crop, "id"), 1, needs, parameters);
                    why = " status=" + Call(G(bed, "CraftComponent"), "GetStartCraftStatus", element, null) +
                          " needs=" + string.Join(",", ((IEnumerable)needs).Cast<object>().Select(n => G(n, "id") + ":" + Call(G(n, "count"), "EvaluateFloat")).ToArray()) +
                          " player=" + Items(G(player, "inventory"));
                }
                Call(bed, "ClearWorker");
                return "PLANT " + G(seed, "id") + " crop=" + G(crop, "id") + " tool=" + gaveTool + " ok=" + ok + why;
            }
            case "fertilize":
            {
                // As the garden interaction does on an empty bed: fertiliser in hand, player working
                // the bed, then the perk slot for the new fertiliser perk.
                object bed = wgos.First(w => Id(w) == args[1]);
                Type handler = T("GardenInteractionHandler");
                MethodInfo findCrop = handler.GetMethod("TryFindGardenCraft", Flags);
                object fertilizer = null, craft = null;
                foreach (string id in List(G(T("GameBalance"), "Me"), "itemDefs").Cast<object>().Select(d => Convert.ToString(G(d, "id"))))
                {
                    object candidate = Item(id, 1);
                    if (!Convert.ToBoolean(G(candidate, "IsFertilizer"))) continue;
                    object found = findCrop.Invoke(null, new[] { candidate, bed, (object)false });
                    if (found != null) { fertilizer = candidate; craft = found; break; }
                }
                if (craft == null) return "FERTILIZE nocraft";
                foreach (object need in (IEnumerable)handler.GetMethod("FormNeedItems", Flags).Invoke(null, new[] { craft, fertilizer }))
                    Call(G(player, "inventory"), "AddItemToInventory", Item(Convert.ToString(G(need, "id")), Convert.ToInt32(Call(G(need, "count"), "EvaluateFloat"))), null, false);
                GiveToolFor(player, craft, bed);
                object controller = G(T("MainGame"), "PlayerController");
                bed.GetType().GetMethod("TrySetWorker", Flags).Invoke(bed, new[] { controller, null });
                object ok = handler.GetMethod("TryApplyFertilizer", Flags).Invoke(null, new[] { fertilizer, craft, bed });
                Call(bed, "ClearWorker");
                if (Convert.ToBoolean(ok))
                {
                    // TryAssignPerkSlotForNewestAddedPerk, which the interaction runs next.
                    var free = new System.Collections.Generic.List<int> { 1, 2, 3 };
                    object newest = null;
                    foreach (object perk in List(bed, "ActivePerks"))
                    {
                        object def = G(perk, "Definition");
                        if (!Convert.ToBoolean(G(def, "IsFertilizerPerk"))) continue;
                        int slot = Convert.ToInt32(Call(bed, "GetGameResInt", "perk_fertilize_" + G(def, "id")));
                        if (slot > 0) free.Remove(slot); else newest = perk;
                    }
                    if (newest != null && free.Count > 0)
                        bed.GetType().GetMethod("SetGameRes", Flags, null, new[] { typeof(string), typeof(int) }, null).Invoke(bed, new object[] { "perk_fertilize_" + G(G(newest, "Definition"), "id"), free[0] });
                }
                string why = "";
                if (!Convert.ToBoolean(ok))
                {
                    object needs = handler.GetMethod("FormNeedItems", Flags).Invoke(null, new[] { craft, fertilizer });
                    object parameters = Activator.CreateInstance(T("CraftParamsData"), G(craft, "id"), bed, Enum.ToObject(T("CraftParamsData").GetNestedType("CraftParamsType"), 0), Convert.ToInt32(G(G(fertilizer, "Definition"), "talentValue")));
                    object element = Activator.CreateInstance(T("CraftElement"), G(craft, "id"), 1, needs, parameters);
                    why = " status=" + Call(G(bed, "CraftComponent"), "GetStartCraftStatus", element, null) + " bedStarted=" + G(G(bed, "CraftComponent"), "IsStarted");
                }
                return "FERTILIZE " + G(fertilizer, "id") + " craft=" + G(craft, "id") + " ok=" + ok + why;
            }
            case "bed-perks":
            {
                object bed = wgos.First(w => Id(w) == args[1]);
                return "PERKS " + string.Join(",", List(bed, "ActivePerks").Cast<object>().Select(p =>
                    G(G(p, "Definition"), "id") + "@" + Call(bed, "GetGameResInt", "perk_fertilize_" + G(G(p, "Definition"), "id"))).OrderBy(x => x).ToArray());
            }
            case "sleep":
            {
                // The game's own sleep, without saving at the end (a test must not write the slot).
                object energy = G(player, "energySystem");
                MethodInfo start = energy.GetType().GetMethod("StartSleeping", Flags);
                start.Invoke(energy, new object[] { null, null, true, true, Enum.ToObject(T("SleepAnimType"), 0), 40f });
                return "SLEEP started";
            }
            case "speed":
            {
                object manager = G(T("MainGame"), "UpdateManager");
                return "SPEED " + manager.GetType().GetField("timeMultiplier", Flags).GetValue(manager) +
                       " asleep=" + G(G(player, "energySystem"), "IsSleeping");
            }
            case "open-look":
            {
                // The F11 path: open the game's customization window, confirm it is open, close it.
                T("GK2Coop.CoopAppearanceSync").GetMethod("OpenCustomizationWindow", Flags).Invoke(null, null);
                object window = T("LazyBearTechnology.LazyUI").GetMethod("GetWindow", Flags, null, Type.EmptyTypes, null).MakeGenericMethod(T("UICustomizationWindow")).Invoke(null, null);
                object open = G(window, "IsOpened") ?? G(window, "IsOpen") ?? G(window, "isActiveAndEnabled");
                try { window.GetType().GetMethod("Close", Flags, null, Type.EmptyTypes, null)?.Invoke(window, null); } catch { }
                return "OPEN-LOOK open=" + open;
            }
            case "recolor":
            {
                // As if a colour were changed in the customization window and applied.
                Type customizationType = T("PlayerCustomizationData");
                object copy = customizationType.GetMethod("Copy", Flags).Invoke(null, new[] { G(player, "customization") });
                object pair = List(copy, "colorCustomizationPairData").Cast<object>().First();
                FieldInfo index = pair.GetType().GetField("index", Flags);
                index.SetValue(pair, Convert.ToInt32(index.GetValue(pair)) == 0 ? 1 : 0);
                Call(player, "ApplyCustomization", copy);
                return "RECOLOR index=" + index.GetValue(pair);
            }
            case "vendors":
            {
                // Each vendor: id, its money and its stock, one line each.
                var lines = new StringBuilder();
                foreach (object vendor in List(G(save, "vendorSystem"), "vendors"))
                    lines.AppendLine("VENDOR " + G(vendor, "id") + " money=" + G(vendor, "CurMoney") + " stock=" + Items(G(vendor, "Inventory")));
                return lines.ToString();
            }
            case "vendor-deal":
            {
                // What a finished deal does to the vendor (one item bought from it, paid for), then
                // the same sharing a real deal triggers.
                object vendor = List(G(save, "vendorSystem"), "vendors").Cast<object>().First(v => Convert.ToString(G(v, "id")) == args[1]);
                object stock = G(vendor, "Inventory");
                object stocked = List(G(stock, "Data"), "Inventory").Cast<object>().First();
                string itemId = Convert.ToString(G(stocked, "id"));
                Call(stock, "RemoveItemById", itemId, 1, null, null, false);
                vendor.GetType().GetProperty("CurMoney", Flags).SetValue(vendor, Convert.ToInt32(G(vendor, "CurMoney")) + 7, null);
                T("GK2Coop.CoopVendorSync").GetMethod("Share", Flags).Invoke(null, new[] { vendor });
                return "DEAL " + args[1] + " bought " + itemId + " money=" + G(vendor, "CurMoney");
            }
            case "looks":
            {
                // A hash of each player's customization: the local one, and each remote body's.
                Func<object, string> hash = c =>
                {
                    if (c == null) return "none";
                    MethodInfo serialize = T("LazyBearTechnology.LazySerializer").GetMethods(Flags).First(m => m.Name == "Serialize" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                    byte[] raw = (byte[])serialize.MakeGenericMethod(c.GetType()).Invoke(null, new[] { c });
                    unchecked { int h = (int)2166136261; foreach (byte b in raw) h = (h ^ b) * 16777619; return h.ToString("x8"); }
                };
                var text = new StringBuilder("LOCAL " + hash(G(player, "customization")));
                foreach (Component body in FindAll(T("PlayerPhysicalBody")).Cast<Component>().Where(b => b.gameObject.activeInHierarchy))
                {
                    object data = G(body, "playerData");
                    if (data == null || ReferenceEquals(data, player)) continue;
                    text.Append(" REMOTE " + hash(G(data, "customization")));
                }
                return text.ToString();
            }
            case "objects":
                return string.Join("\n", wgos.Where(w => Convert.ToString(G(w, "id")).StartsWith(args[1], StringComparison.Ordinal))
                    .Select(w => "OBJ " + Id(w) + " " + G(w, "id")).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            case "wgo-count":
            {
                // "npc_monk_*": every object whose id starts so.
                string wanted = args[1];
                var found = wgos.Where(w => wanted.EndsWith("*") ? Convert.ToString(G(w, "id")).StartsWith(wanted.TrimEnd('*')) : Convert.ToString(G(w, "id")) == wanted).ToList();
                return "WGO-COUNT " + args[1] + "=" + found.Count;
            }
            case "wgos":
                return "WGOS " + string.Join(";", wgos.Where(w => G(w, "hpComponent") != null)
                    .Select(w => Id(w) + "=" + G(w, "id")).Take(int.Parse(args[1])).ToArray());
            case "wgo-kill":
                object target = wgos.Single(w => Id(w) == args[1]);
                Call(G(target, "hpComponent"), "ApplyDamage", int.MaxValue / 2);
                return "KILL " + args[1] + " alive=" + (wgos.Any(w => Id(w) == args[1]));
            case "wgo-alive":
                return "ALIVE " + wgos.Any(w => Id(w) == args[1]);
            // Only this machine's copy of the container shrinks, which is the point: the peer
            // still believes there is room, so its change cannot be applied here.
            case "container-capacity":
                object capInv = G(wgos.Single(w => Id(w) == args[1]), "Inventory");
                object capData = G(capInv, "Data");
                capData.GetType().GetProperty("InventorySize", Flags).SetValue(capData, int.Parse(args[2]), null);
                return "CONTAINER-CAPACITY " + G(capData, "InventorySize") + " items=" + Items(capInv);
            case "container-clear":
                object clearInv = G(wgos.Single(w => Id(w) == args[1]), "Inventory");
                Call(clearInv, "Clear");
                return "CLEAR items=" + Items(clearInv);
            // Three-player checks: move this machine's player, and read every player's position
            // as this machine believes it to be.
            // The local body itself, so the camera follows and settles as after walking.
            case "move-body":
            {
                MonoBehaviour local = null;
                foreach (object body in FindAll(T("PlayerPhysicalBody")))
                {
                    var candidate = body as MonoBehaviour;
                    if (candidate != null && candidate.gameObject.activeInHierarchy && G(candidate, "playerController") != null && Convert.ToBoolean(G(G(candidate, "playerController"), "enabled")))
                        local = candidate;
                }
                if (local == null) local = (MainGameBody() as MonoBehaviour);
                if (local == null) return "MOVE-BODY no body";
                Vector3 start = local.transform.position;
                Vector3 end = start + new Vector3(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), 0f, float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                object rb = local.GetComponent(Type.GetType("UnityEngine.Rigidbody, UnityEngine.PhysicsModule"));
                if (rb != null) rb.GetType().GetProperty("position").SetValue(rb, end, null);
                local.transform.position = end;
                return "MOVE-BODY " + start.ToString("F2") + " -> " + end.ToString("F2");
            }
            case "move":
                object movePos = G(player, "position");
                var from = (Vector3)G(movePos, "Value");
                var to = from + new Vector3(float.Parse(args[1]), 0f, float.Parse(args[2]));
                movePos.GetType().GetProperty("Value", Flags).SetValue(movePos, to, null);
                return "MOVE " + from.ToString("R") + " -> " + to.ToString("R");
            case "players":
                var players = new StringBuilder();
                object hostPlayer = G(save, "hostPlayer");
                if (hostPlayer != null)
                    players.AppendLine("PLAYER id=" + G(hostPlayer, "clientId") + " host pos=" +
                        ((Vector3)G(G(G(hostPlayer, "playerData"), "position"), "Value")).ToString("R"));
                foreach (object client in List(save, "clientPlayers"))
                    players.AppendLine("PLAYER id=" + G(client, "clientId") + " client pos=" +
                        ((Vector3)G(G(G(client, "playerData"), "position"), "Value")).ToString("R"));
                return players.ToString();
            // A replicated move may move the body without writing playerData.position, so the
            // bodies are read directly rather than inferred from the peer record.
            case "bodies":
                var bodies = new StringBuilder();
                foreach (object body in FindAll(T("PlayerPhysicalBody")))
                {
                    var mb = body as MonoBehaviour;
                    if (mb == null || !mb.gameObject.activeInHierarchy) continue;
                    bodies.AppendLine("BODY " + mb.name + " pos=" + mb.transform.position.ToString("R"));
                }
                return bodies.ToString();
            case "body-players":
                var identifiedBodies = new StringBuilder();
                object hostData = G(G(save, "hostPlayer"), "playerData");
                var clientRecords = List(save, "clientPlayers").Cast<object>().ToArray();
                foreach (object body in FindAll(T("PlayerPhysicalBody")))
                {
                    var mb = body as MonoBehaviour;
                    if (mb == null || !mb.gameObject.activeInHierarchy) continue;
                    object bodyData = G(body, "playerData");
                    string owner = ReferenceEquals(bodyData, hostData) ? "0" :
                        Convert.ToString(G(clientRecords.FirstOrDefault(c => ReferenceEquals(bodyData, G(c, "playerData"))), "clientId"));
                    identifiedBodies.AppendLine("BODY id=" + owner + " pos=" + mb.transform.position.ToString("R"));
                }
                return identifiedBodies.ToString();
            case "quest-await":
                Call(quests, "AwaitQuest", args[1], 0f);
                return "AWAIT dispatched; inspect both peers for actual status";
            case "capacity":
                object inventoryData = G(G(player, "inventory"), "Data");
                if (args.Length > 1) inventoryData.GetType().GetProperty("InventorySize", Flags).SetValue(inventoryData, int.Parse(args[1]), null);
                return "CAPACITY " + G(inventoryData, "InventorySize");
            // Item ids must come from the installed catalogue: a fabricated id is rejected by
            // the game before any mod code runs, which looks like a mod failure but is not one.
            // Ids for building a test yard: what build mode can place, and any definition list.
            case "buildables":
            {
                var keys = ((System.Collections.IDictionary)G(G(T("GameBalance"), "Me"), "buildableWgos")).Keys.Cast<object>().Select(Convert.ToString);
                string filter = args.Length > 1 ? args[1] : "";
                return "BUILDABLES " + string.Join(",", keys.Where(k => k.Contains(filter)).OrderBy(k => k).ToArray());
            }
            case "defs":
            {
                string filter = args.Length > 2 ? args[2] : "";
                var ids = List(G(T("GameBalance"), "Me"), args[1]).Cast<object>().Select(d => Convert.ToString(G(d, "id"))).Where(id => id.Contains(filter));
                return "DEFS " + args[1] + " " + string.Join(",", ids.Take(80).ToArray());
            }
            case "items":
                return "ITEMS " + string.Join(",", List(G(T("GameBalance"), "Me"), "itemDefs").Cast<object>()
                    .Select(d => Convert.ToString(G(d, "id")))
                    .Where(id => id.StartsWith(args[1], StringComparison.Ordinal)).Take(40).ToArray());
            case "stations":
            {
                var lines = new StringBuilder();
                foreach (object wgo in wgos)
                {
                    object craft = G(wgo, "CraftComponent");
                    if (craft == null) continue;
                    var available = List(craft, "AvailableCrafts").Cast<object>().ToArray();
                    if (available.Length == 0) continue;
                    object current = G(craft, "CurrentCraftElement");
                    string startable = "none";
                    foreach (object def in available.Take(40))
                    {
                        string craftId = Convert.ToString(G(def, "id"));
                        object element = NewCraftElement(craftId, wgo);
                        if (Convert.ToString(Call(craft, "GetStartCraftStatus", element, null)) == "OK") { startable = craftId; break; }
                    }
                    lines.AppendLine("STATION " + Id(wgo) + " " + G(wgo, "id") + " type=" + G(G(wgo, "Definition"), "interactionType") +
                        " shared=" + T("GK2Coop.CoopCraftSync").GetMethod("IsShared", Flags).Invoke(null, new[] { craft }) + " status=" + G(craft, "Status") +
                        " queue=" + List(craft, "CraftElementsQueue").Cast<object>().Count() +
                        " cur=" + (current == null ? "-" : G(current, "CraftId") + "@" + G(current, "ProgressTimeNormalized")) +
                        " startable=" + startable + " crafts=" + available.Length);
                }
                return lines.ToString();
            }
            case "station-recipes":
            {
                object station = wgos.First(w => Id(w) == args[1]);
                var recipes = List(G(station, "CraftComponent"), "AvailableCrafts").Cast<object>();
                return string.Join("\n", recipes.Select(def => "RECIPE " + G(def, "id") + " auto=" + G(def, "isAuto") +
                    " lock=" + G(def, "talentLock") + " tool=" + G(def, "customItemTypeAction") +
                    " needs=" + string.Join(",", List(def, "needItems").Cast<object>().Select(n => G(n, "id") + "x" + Call(n, "GetCount", station)))));
            }
            case "station-stock":
            {
                object station = wgos.First(w => Id(w) == args[1]);
                object inventory = G(station, "CraftableObjectCraftInventory");
                return "STATION-STOCK " + Id(station) + " " + Items(inventory);
            }
            case "pause-state":
            case "pause-call":
            {
                object pauseGame = G(T("MainGame"), "Instance");
                if (args[0] == "pause-call") Call(pauseGame, "PauseGame");
                object updates = G(pauseGame, "updateManager");
                return "PAUSE-STATE paused=" + G(T("MainGame"), "IsGamePaused") +
                    " updating=" + G(updates, "IsActive") +
                    " suppressed=" + T("GK2Coop.CoopPauseSync").GetMethod("Describe", Flags).Invoke(null, null);
            }
            case "conveyors":
            {
                object conveyor = G(G(T("MainGame"), "Instance"), "conveyorSystem");
                object conveyorData = G(save, "conveyorSystemData");
                var components = List(conveyorData, "conveyorComponents").Cast<object>().ToArray();
                var report = new StringBuilder();
                report.AppendLine("CONVEYORS count=" + components.Length + " workers=" + List(conveyorData, "zombieCraftActivities").Cast<object>().Count() +
                    " paused=" + G(conveyor, "IsPaused") + " powered=" + G(conveyor, "HasEnoughPower") + " timer=" + G(conveyorData, "timer"));
                foreach (object component in components)
                {
                    object data = G(component, "WgoData");
                    if (data == null) continue;
                    object conveyorInventory = G(data, "Inventory");
                    object conveyorOwner = T("GK2Coop.CoopContainerSync").GetMethod("ResolveOwner", Flags).Invoke(null, new[] { conveyorInventory });
                    report.AppendLine("CONVEYOR " + Id(data) + " " + G(data, "id") + " type=" + component.GetType().Name +
                        " parents=" + List(component, "parentsUniqueIds").Cast<object>().Count() +
                        " connected=" + List(component, "connectedWgoDataUniqueId").Cast<object>().Count() +
                        " pos=" + G(data, "Position") +
                        " invId=" + G(G(G(conveyorInventory, "Data"), "UniqueId"), "Id") + " owner=" + conveyorOwner +
                        " items=" + Items(conveyorInventory) + " in=" + G(G(component, "InItem"), "itemId") +
                        " out=" + G(G(component, "OutItem"), "itemId"));
                }
                return report.ToString();
            }
            case "station-take":
            {
                object station = wgos.First(w => Id(w) == args[1]);
                object inventory = G(station, "CraftableObjectCraftInventory");
                object taken = Call(inventory, "RemoveItemById", args[2], int.Parse(args[3]), null, null, false);
                foreach (object itemTaken in (IEnumerable)taken)
                    Call(G(player, "inventory"), "AddItemToInventory", itemTaken, null, false);
                return "STATION-TAKE " + Id(station) + " stock=" + Items(inventory) + " player=" + Items(G(player, "inventory"));
            }
            case "craft-broadcast-pause":
            {
                Type sync = T("GK2Coop.CoopCraftSync");
                sync.GetField("nextBroadcast", Flags).SetValue(null, Time.unscaledTime + float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
                return "CRAFT-BROADCAST-PAUSE seconds=" + args[1];
            }
            case "porter-defs":
            {
                object balance = G(T("GameBalance"), "Me");
                var lines = new StringBuilder();
                foreach (object def in List(balance, "porterStationDefs"))
                    lines.AppendLine("PORTER-DEF " + G(def, "id") + " target=" + G(def, "targetWorldZone") +
                        " items=" + string.Join(",", List(def, "items").Cast<object>().Select(i => Convert.ToString(G(i, "id")))));
                foreach (object def in List(balance, "worldZoneDefs"))
                    if (!string.IsNullOrEmpty(Convert.ToString(G(def, "porterStartPoint"))))
                        lines.AppendLine("PORTER-ZONE " + G(def, "id") + " start=" + G(def, "porterStartPoint") + " end=" + G(def, "porterEndPoint"));
                return lines.ToString();
            }
            case "porter-setup":
            {
                object porterPoint = Call(G(world, "gdPointsData"), "GetGDPointDataById", "carrier_porter_point");
                if (porterPoint == null) return "PORTER-SETUP missing carrier route point";
                Vector3 porterAt = (Vector3)G(porterPoint, "Position");
                object porterZone = Call(world, "GetWorldZoneDataById", "carrier");
                if (porterZone == null) return "PORTER-SETUP missing carrier world zone";
                object porterController = T("MainGame").GetProperty("PlayerController", Flags).GetValue(null, null);
                object porterScene = G(porterController, "CurrentGameScene");
                string porterWorld = Convert.ToString(G(player, "currentGameSceneId"));
                object sourceChest = Activator.CreateInstance(T("WgoData"), "chest_rough", porterAt + new Vector3(1f, 0f, 1f), porterWorld);
                G(sourceChest, "WorldZoneData");
                sourceChest.GetType().GetProperty("WorldZoneData", Flags).SetValue(sourceChest, porterZone, null);
                T("GK2Coop.CoopBuildSync").GetMethod("TestPlace", Flags).Invoke(null, new[] { porterScene, sourceChest });
                bool hasStone = Convert.ToBoolean(Call(G(sourceChest, "Inventory"), "AddItemToInventory", Item("1h_stone", 1), null, false));
                object porterStation = Activator.CreateInstance(T("WgoData"), "porter_station_carrier", porterAt + new Vector3(-1f, 0f, 1f), porterWorld);
                porterStation.GetType().GetProperty("WorldZoneData", Flags).SetValue(porterStation, porterZone, null);
                T("GK2Coop.CoopBuildSync").GetMethod("TestPlace", Flags).Invoke(null, new[] { porterScene, porterStation });
                Call(porterStation, "SetGameRes", "1h_stone", 1f);
                return "PORTER-SETUP chest=" + Id(sourceChest) + " station=" + Id(porterStation) + " stone=" + hasStone +
                    " chestZone=" + G(G(sourceChest, "WorldZoneData"), "id") + " stationZone=" + G(G(porterStation, "WorldZoneData"), "id") +
                    " at=" + porterAt.ToString("F1");
            }
            case "porter-zombie":
            {
                object porterStation = wgos.First(w => Id(w) == args[1]);
                object balance = G(T("GameBalance"), "Me");
                MethodInfo getBody = balance.GetType().GetMethods(Flags).First(m => m.Name == "GetData" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)).MakeGenericMethod(T("BodyDef"));
                object bodyDef = getBody.Invoke(balance, new object[] { "body_zombie_test_5" });
                object bodyItem = bodyDef.GetType().GetMethod("GenerateItem", Type.EmptyTypes).Invoke(bodyDef, null);
                object zombies = G(T("MainGame"), "ZombieSystemData");
                Vector3 at = (Vector3)G(porterStation, "Position");
                string sceneId = Convert.ToString(G(porterStation, "WorldId"));
                MethodInfo create = zombies.GetType().GetMethod("CreateZombieDrop");
                object[] createArgs = create.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : null).ToArray();
                createArgs[0] = "zombie"; createArgs[1] = at; createArgs[2] = sceneId; createArgs[3] = bodyItem;
                create.Invoke(zombies, createArgs);
                player.GetType().GetMethod("AddOverheadItem").Invoke(player, new[] { bodyItem });
                MethodInfo asCommon = zombies.GetType().GetMethods(Flags).First(m => m.Name == "PutZombieFromStoreToGameSceneAsCommon" && m.GetParameters().Length == 5);
                object direction = Enum.ToObject(asCommon.GetParameters()[4].ParameterType, 0);
                object zombie = asCommon.Invoke(zombies, new[] { player, bodyItem, sceneId, (object)at, direction });
                MethodInfo attach = zombie.GetType().GetMethod("AttachToPorterStation", Flags);
                attach.Invoke(zombie, new[] { G(porterStation, "UniqueId"), G(zombie, "ZombieItem"), null });
                return "PORTER-ZOMBIE " + Id(zombie) + " at=" + Id(porterStation) + " zone=" + G(G(zombie, "WorldZoneData"), "id") +
                    " pos=" + ((Vector3)G(zombie, "Position")).ToString("F1");
            }
            case "porter-state":
            {
                object porter = wgos.First(w => Id(w) == args[1]);
                object source = wgos.First(w => Id(w) == args[2]);
                Func<object, int> stone = inv => inv == null ? 0 : List(G(inv, "Data"), "Inventory").Cast<object>()
                    .Where(i => Convert.ToString(G(i, "id")) == "1h_stone").Sum(i => Convert.ToInt32(G(i, "Count")));
                object destination = Call(world, "GetWorldZoneDataById", "conveyor");
                int delivered = List(destination, "MultiInventoryWgoDatas").Cast<object>().Sum(w => stone(G(w, "Inventory")));
                object bag = G(porter, "porterInventory");
                return "PORTER-STATE " + Id(porter) + " pos=" + ((Vector3)G(porter, "Position")).ToString("F1") +
                    " source=" + stone(G(source, "Inventory")) + " bag=" + stone(bag) + " target=" + delivered +
                    " staying=" + Call(porter, "GetGameResInt", "is_staying_at_porter_station") +
                    " moving=" + Call(porter, "GetGameResInt", "is_moving_to_target_world_zone");
            }
            // Research: which kinds of world object this save has, and what the stateful ones hold.
            // Shared knowledge: learn a tech this world does not know yet (free), or report one.
            case "learn":
            {
                object knowledge = G(save, "knowledgeSystem");
                var unlocked = new System.Collections.Generic.HashSet<string>(List(knowledge, "unlockedTechs").Cast<object>().Select(Convert.ToString));
                object balance = G(T("GameBalance"), "Me");
                object tech = List(balance, "techDefs").Cast<object>().First(t =>
                    !unlocked.Contains(Convert.ToString(G(t, "id"))) && Convert.ToBoolean(G(t, "isAvailableInDemo")) &&
                    List(t, "craftsAfterUnlock").Cast<object>().Any() &&
                    (args.Length < 2 || Convert.ToString(G(t, "id")) != args[1]));
                tech.GetType().GetMethod("Unlock", new[] { typeof(bool) }).Invoke(tech, new object[] { true });
                return "LEARN " + G(tech, "id") + " crafts=" + string.Join(",", List(tech, "craftsAfterUnlock").Cast<object>().Select(Convert.ToString).ToArray());
            }
            case "knows":
            {
                object knowledge = G(save, "knowledgeSystem");
                object balance = G(T("GameBalance"), "Me");
                object tech = List(balance, "techDefs").Cast<object>().First(t => Convert.ToString(G(t, "id")) == args[1]);
                var crafts = new System.Collections.Generic.HashSet<string>(List(knowledge, "unlockedCrafts").Cast<object>().Select(Convert.ToString));
                bool allCrafts = List(tech, "craftsAfterUnlock").Cast<object>().All(c => crafts.Contains(Convert.ToString(c)));
                return "KNOWS " + args[1] + " tech=" + List(knowledge, "unlockedTechs").Cast<object>().Any(t => Convert.ToString(t) == args[1]) +
                       " crafts=" + allCrafts + " techs=" + List(knowledge, "unlockedTechs").Cast<object>().Count() + " recipes=" + crafts.Count;
            }
            // Per-player talents: a fingerprint of every branch's progress, and a way to change one.
            case "talents":
            {
                var branches = List(G(save, "talentSystemData"), "talentData").Cast<object>().ToArray();
                return "TALENTS " + string.Join(";", branches.Select(b => G(b, "id") + ":" + G(b, "curExp") + "/" + G(b, "curTalentLevel") + "/" + G(b, "curTalentValue")).ToArray());
            }
            case "talent-exp":
            {
                object branch = List(G(save, "talentSystemData"), "talentData").Cast<object>().First();
                branch.GetType().GetField("curExp").SetValue(branch, int.Parse(args[1]));
                return "TALENT-EXP " + G(branch, "id") + "=" + G(branch, "curExp");
            }
            // Research: every value in the player's resource store (personal and world values share it).
            case "res-all":
            {
                object allRes = player.GetType().GetField("res", Flags).GetValue(player);
                var atoms = List(allRes, "List").Cast<object>().ToArray();
                return "RES-ALL " + string.Join(";", atoms.Select(a => string.Join(",", a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => Convert.ToString(f.GetValue(a))).ToArray())).OrderBy(x => x).ToArray());
            }
            // Divergence audit: a line per scene for its objects, its drops, and every filled container.
            case "audit":
            {
                var lines = new StringBuilder();
                object env = G(save, "environmentData");
                lines.AppendLine("AUDIT-CLOCK day=" + G(env, "CurrentDayNumber"));
                foreach (object scene in scenes)
                {
                    var sceneWgos = List(scene, "wgoDataList").Cast<object>().ToArray();
                    var types = sceneWgos.GroupBy(w => Convert.ToString(G(w, "id"))).OrderBy(g => g.Key).Select(g => g.Key + "x" + g.Count());
                    lines.AppendLine("AUDIT-OBJECTS " + G(scene, "id") + " n=" + sceneWgos.Length + " " + string.Join(",", types.ToArray()));
                    var sceneDrops = List(scene, "droppedItems").Cast<object>().Select(d => { object item = G(d, "Item") ?? G(d, "item"); return item == null ? "?" : G(item, "id") + "x" + G(item, "Count"); }).OrderBy(x => x);
                    lines.AppendLine("AUDIT-DROPS " + G(scene, "id") + " " + string.Join(",", sceneDrops.ToArray()));
                    foreach (object w in sceneWgos)
                    {
                        object auditInv = G(w, "Inventory");
                        if (auditInv == null) continue;
                        var held = List(G(auditInv, "Data"), "Inventory").Cast<object>().ToArray();
                        if (held.Length > 0) lines.AppendLine("AUDIT-CONTAINER " + Id(w) + " " + G(w, "id") + " " + string.Join(";", held.Select(i => Describe(i, 1)).ToArray()));
                    }
                }
                return lines.ToString();
            }
            // Build sync: place a buildable object next to this player, or remove one, through the
            // mod's capture path around the game's own scene add and removal.
            case "build-place":
            {
                object balance = G(T("GameBalance"), "Me");
                var buildable = ((System.Collections.IDictionary)G(balance, "buildableWgos")).Keys.Cast<object>().Select(Convert.ToString).ToArray();
                string type = buildable.FirstOrDefault(k => k == args[1]) ?? buildable.First(k => k.Contains("chest"));
                object controller = T("MainGame").GetProperty("PlayerController", Flags).GetValue(null, null);
                object gameScene = G(controller, "CurrentGameScene");
                Vector3 at = (Vector3)G(G(player, "position"), "Value") + new Vector3(2f, 0f, 2f);
                object data = Activator.CreateInstance(T("WgoData"), type, at, Convert.ToString(G(player, "currentGameSceneId")));
                T("GK2Coop.CoopBuildSync").GetMethod("TestPlace", Flags).Invoke(null, new[] { gameScene, data });
                return "BUILD-PLACE " + type + " " + Id(data);
            }
            case "build-place-fight":
            {
                // A fight building (e.g. barricade_1_fight) next to the player, as the pre-fight places it.
                object controller = T("MainGame").GetProperty("PlayerController", Flags).GetValue(null, null);
                object gameScene = G(controller, "CurrentGameScene");
                Vector3 at = (Vector3)G(G(player, "position"), "Value") + new Vector3(-2f, 0f, 2f);
                object data = Activator.CreateInstance(T("WgoData"), args[1], at, Convert.ToString(G(player, "currentGameSceneId")));
                T("GK2Coop.CoopBuildSync").GetMethod("TestPlaceFight", Flags).Invoke(null, new[] { gameScene, data });
                return "BUILD-PLACE-FIGHT " + args[1] + " " + Id(data);
            }
            case "military-base":
            {
                object militaryBase = G(save, "militaryBaseData");
                return "MILITARY-BASE base=" + List(militaryBase, "baseBuildings").Cast<object>().Count() + " fight=" + List(militaryBase, "fightBuildings").Cast<object>().Count();
            }
            case "build-remove":
            {
                object data = wgos.First(w => Id(w) == args[1]);
                T("GK2Coop.CoopBuildSync").GetMethod("TestRemove", Flags).Invoke(null, new[] { data });
                return "BUILD-REMOVE " + args[1];
            }
            case "has":
                return "HAS " + args[1] + " " + wgos.Any(w => Id(w) == args[1]);
            // Zombies: raise one from a test body next to this player (the game's own creation and
            // placement), and list the zombies each machine has in its world.
            case "zombie-make":
            {
                object balance = G(T("GameBalance"), "Me");
                MethodInfo getData = balance.GetType().GetMethods(Flags).First(m => m.Name == "GetData" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)).MakeGenericMethod(T("BodyDef"));
                object body = getData.Invoke(balance, new object[] { args.Length > 1 ? args[1] : "body_zombie_test_5" });
                object bodyItem = body.GetType().GetMethod("GenerateItem", Type.EmptyTypes).Invoke(body, null);
                object zombies = G(T("MainGame"), "ZombieSystemData");
                string zombieScene = Convert.ToString(G(player, "currentGameSceneId"));
                Vector3 near = (Vector3)G(G(player, "position"), "Value") + new Vector3(1.5f, 0f, 1.5f);
                MethodInfo create = zombies.GetType().GetMethod("CreateZombieDrop");
                object[] createArgs = create.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : null).ToArray();
                createArgs[0] = "zombie"; createArgs[1] = near; createArgs[2] = zombieScene; createArgs[3] = bodyItem;
                object zombie = create.Invoke(zombies, createArgs);
                MethodInfo place = zombies.GetType().GetMethods(Flags).First(m => m.Name == "PutZombieFromStoreToGameScene" && m.GetParameters().Length == 4);
                place.Invoke(zombies, new object[] { G(zombie, "UniqueId"), zombieScene, near, Enum.ToObject(place.GetParameters()[3].ParameterType, 0) });
                return "ZOMBIE-MAKE " + Id(zombie) + " " + G(zombie, "Name");
            }
            case "zombie-pickup":
            {
                object zombies = G(T("MainGame"), "ZombieSystemData");
                object zombie = wgos.First(w => Id(w) == args[1]);
                zombies.GetType().GetMethod("PutZombieFromGameSceneToStoreForPlayer").Invoke(zombies, new[] { player, zombie });
                return "ZOMBIE-PICKUP " + args[1];
            }
            // What CraftInteractionHandler does when a player brings a zombie to a station: raise a
            // zombie, carry it, put it down at the station's worker dock point as a common worker,
            // attach it there.
            case "zombie-work":
            {
                object balance = G(T("GameBalance"), "Me");
                MethodInfo getBody = balance.GetType().GetMethods(Flags).First(m => m.Name == "GetData" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)).MakeGenericMethod(T("BodyDef"));
                object bodyItem = getBody.Invoke(balance, new object[] { "body_zombie_test_5" }).GetType().GetMethod("GenerateItem", Type.EmptyTypes).Invoke(getBody.Invoke(balance, new object[] { "body_zombie_test_5" }), null);
                object zombies = G(T("MainGame"), "ZombieSystemData");
                string zombieScene = Convert.ToString(G(player, "currentGameSceneId"));
                Vector3 here = (Vector3)G(G(player, "position"), "Value");
                MethodInfo create = zombies.GetType().GetMethod("CreateZombieDrop");
                object[] createArgs = create.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : null).ToArray();
                createArgs[0] = "zombie"; createArgs[1] = here; createArgs[2] = zombieScene; createArgs[3] = bodyItem;
                object storedZombie = create.Invoke(zombies, createArgs);
                object station = wgos.First(w => Id(w) == args[1]);
                object stationDef = G(station, "Definition");
                string talentId = Convert.ToString(G(stationDef, "talent"));
                object talent = storedZombie.GetType().GetMethod("GetTalentBranch").Invoke(storedZombie, new object[] { talentId });
                if (talent == null) throw new InvalidOperationException("Test zombie has no talent branch " + talentId);
                int testSkill = args.Length > 3 ? int.Parse(args[3]) : 20;
                talent.GetType().GetField("curTalentValue", Flags).SetValue(talent, testSkill);
                object craftDef = args.Length > 2 ? T("GameBalance").GetMethod("GetCraftDefBase", Flags, null, new[] { typeof(string) }, null).Invoke(null, new object[] { args[2] }) : null;
                object toolType = craftDef == null ? null : G(craftDef, "customItemTypeAction");
                if (toolType == null || Convert.ToString(toolType) == "None") toolType = G(G(stationDef, "toolAction"), "actionableTool");
                string equipped = "none";
                if (Convert.ToString(toolType) != "None" && Convert.ToString(toolType) != "Hand")
                {
                    object toolDef = List(balance, "itemDefs").Cast<object>().First(d => Equals(G(d, "type"), toolType));
                    equipped = Convert.ToString(G(toolDef, "id"));
                    object toolItem = Item(equipped, 1);
                    ((IList)G(bodyItem, "Inventory")).Add(toolItem);
                    Call(G(storedZombie, "equippedHand"), "SetGuid", G(toolItem, "UniqueId"));
                }
                player.GetType().GetMethod("AddOverheadItem").Invoke(player, new[] { bodyItem });
                object stationView = T("GameScene").GetMethod("GetWgoViewGlobal", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { G(station, "UniqueId") });
                if (stationView == null) return "ZOMBIE-WORK no view for " + args[1];
                if (G(stationView, "DockPoints") == null) return "ZOMBIE-WORK station view has no loaded dock points for " + args[1];
                object dock = stationView.GetType().GetMethod("TryGetDockPointForWorker").Invoke(stationView, new object[] { true, here });
                if (dock == null) return "ZOMBIE-WORK no dock point on " + args[1];
                Vector3 dockAt = ((Component)dock).transform.position;
                object dockDirection = G(dock, "Direction");
                MethodInfo asCommon = zombies.GetType().GetMethods(Flags).First(m => m.Name == "PutZombieFromStoreToGameSceneAsCommon" && m.GetParameters().Length == 5);
                object zombie = asCommon.Invoke(zombies, new[] { player, bodyItem, zombieScene, dockAt, dockDirection });
                object dockData = stationView.GetType().GetMethod("GetDockPointData").Invoke(stationView, new[] { dock });
                MethodInfo attach = zombie.GetType().GetMethods(Flags).First(m => m.Name == "AttachToCraftWgoData" && m.GetParameters().Length == 3);
                attach.Invoke(zombie, new[] { G(station, "UniqueId"), G(zombie, "ZombieItem"), dockData });
                return "ZOMBIE-WORK " + Id(zombie) + " at " + G(station, "id") + " dock=" + dockAt.ToString("F1") + " type=" + G(zombie, "ZombieType") + " skill=" + testSkill + " tool=" + equipped;
            }
            case "zombie-assign":
            {
                object zombie = wgos.First(w => Id(w) == args[1]);
                object station = wgos.First(w => Id(w) == args[2]);
                MethodInfo attach = zombie.GetType().GetMethods(Flags).First(m => m.Name == "AttachToCraftWgoData" && m.GetParameters().Length == 3);
                attach.Invoke(zombie, new[] { G(station, "UniqueId"), G(zombie, "ZombieItem"), null });
                return "ZOMBIE-ASSIGN " + args[1] + " to " + G(station, "id") + " type=" + G(zombie, "ZombieType");
            }
            case "zombie-diagnostics":
            {
                object zombie = wgos.First(w => Id(w) == args[1]);
                object station = wgos.First(w => Id(w) == args[2]);
                object craft = G(station, "CraftComponent");
                object current = G(craft, "CurrentCraftElement");
                object def = G(current, "Def");
                return "ZOMBIE-DIAG worker=" + (G(station, "Worker") == null ? "none" : G(G(station, "Worker"), "UniqueId")) +
                    " current=" + (current == null ? "none" : G(current, "CraftId")) +
                    " skill=" + (current == null ? "n/a" : Convert.ToString(Call(zombie, "CrafterIsEnoughMastery", station))) +
                    " tool=" + (def == null ? "n/a" : Convert.ToString(Call(zombie, "CrafterCanUseTool", station, def))) +
                    " order=" + (G(zombie, "CrafterCurrentOrder") == null ? "none" : G(zombie, "CrafterCurrentOrder").GetType().Name) +
                    " activity=" + (G(zombie, "ZombieCraftActivity") == null ? "none" : "yes") +
                    " wood=" + List(G(G(zombie, "WorkerInventory"), "Data"), "Inventory").Cast<object>().Where(i => Convert.ToString(G(i, "id")) == "wood").Sum(i => Convert.ToInt32(G(i, "Count"))) +
                    " wood1=" + List(G(G(zombie, "WorkerInventory"), "Data"), "Inventory").Cast<object>().Where(i => Convert.ToString(G(i, "id")) == "wood1").Sum(i => Convert.ToInt32(G(i, "Count"))) +
                    " status=" + G(craft, "Status");
            }
            case "zombie-stock":
            {
                object station = wgos.First(w => Id(w) == args[1]);
                object inventory = G(station, "CraftableObjectCraftInventory");
                bool stocked = Convert.ToBoolean(Call(inventory, "AddItemToInventory", Item(args[2], int.Parse(args[3])), null, false));
                return "ZOMBIE-STOCK " + args[2] + "x" + args[3] + " added=" + stocked;
            }
            case "zombies":
            {
                object zombies = G(T("MainGame"), "ZombieSystemData");
                var lines = new StringBuilder("ZOMBIES onScene=" + List(zombies, "zombieOnSceneWgoIds").Cast<object>().Count() + " stored=" + List(zombies, "zombieDrops").Cast<object>().Count() + Environment.NewLine);
                foreach (object w in wgos.Where(w => w.GetType().Name == "ZombieWgoData"))
                    lines.AppendLine("ZOMBIE " + Id(w) + " pos=" + ((Vector3)G(w, "Position")).ToString("F1") + " type=" + G(w, "ZombieType") + " name=" + G(w, "Name") + " at=" + (G(w, "AttachedWgoData") is object attachedTo ? Id(attachedTo) : "-"));
                return lines.ToString();
            }
            // Save hygiene: save the (disposable test) slot the way the game does after sleeping,
            // then read the written file back and count the network player records in it.
            case "save-now":
            {
                object slotData = G(game, "SaveSlotData");
                string slotName = Convert.ToString(G(slotData, "slotName"));
                if (!slotName.StartsWith("GK2Coop_")) return "SAVE-NOW refused: not a test slot (" + slotName + ")";
                MethodInfo saveMethod = T("SaveSystem").GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "Save" && m.GetParameters().Length >= 2);
                object[] saveArgs = saveMethod.GetParameters().Select(pi => pi.HasDefaultValue ? pi.DefaultValue : null).ToArray();
                saveArgs[0] = slotData; saveArgs[1] = save;
                saveMethod.Invoke(null, saveArgs);
                return "SAVE-NOW " + slotName + " live host=" + (G(save, "hostPlayer") != null) + " liveClients=" + List(save, "clientPlayers").Cast<object>().Count();
            }
            case "save-inspect":
            {
                object slotData = G(game, "SaveSlotData");
                string slotName = Convert.ToString(G(slotData, "slotName"));
                string folder = Convert.ToString(T("SaveSystem").GetProperty("SaveFolder", BindingFlags.Public | BindingFlags.Static).GetValue(null, null));
                byte[] bytes = File.ReadAllBytes(Path.Combine(folder, slotName + ".dat"));
                object serializer = T("SaveSystem").GetProperty("OdinBinaryFileSerializer", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
                MethodInfo deserialize = serializer.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).First(m => m.Name == "Deserialize" && m.IsGenericMethodDefinition).MakeGenericMethod(T("GameSave"));
                object saved = deserialize.Invoke(serializer, new object[] { bytes });
                return "SAVE-INSPECT " + slotName + " bytes=" + bytes.Length + " host=" + (G(saved, "hostPlayer") != null) + " clients=" + List(saved, "clientPlayers").Cast<object>().Count();
            }
            // Fighting levels: list their stages, or set one the way a fight's progress does.
            case "fight-levels":
            {
                var lines = new StringBuilder();
                foreach (object scene in scenes)
                    foreach (object level in List(scene, "fightingLevels"))
                        lines.AppendLine("FIGHT-LEVEL " + G(level, "id") + " stage=" + G(level, "CurStageId") + " scene=" + G(scene, "id"));
                return lines.ToString();
            }
            // A fight, for watching it: where the levels are, start one, what is fighting, stop it.
            case "fight-where":
            {
                var lines = new StringBuilder();
                foreach (Component level in FindAll(T("FightingLevel")).Cast<Component>().Where(c => c.gameObject.scene.IsValid()))
                    lines.AppendLine("FIGHT-WHERE " + G(level, "id") + " at=" + level.transform.position.ToString("F1") + " active=" + level.gameObject.activeInHierarchy);
                return lines.Length == 0 ? "FIGHT-WHERE none" : lines.ToString();
            }
            case "wgo-positions":
            {
                // Where each object of one kind is shown (its view), by id: "uid@x,z;...".
                MethodInfo viewOf = T("GameScene").GetMethod("GetWgoViewGlobal", new[] { T("SGuid") });
                var found = wgos.Where(w => Convert.ToString(G(w, "id")) == args[1]).Select(w =>
                {
                    var view = viewOf.Invoke(null, new[] { G(w, "UniqueId") }) as Component;
                    Vector3 at = view != null ? view.transform.position : (Vector3)G(w, "Position");
                    return Id(w) + "@" + at.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," + at.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                }).ToArray();
                return "WGO-POSITIONS " + args[1] + " n=" + found.Length + " " + string.Join(";", found);
            }
            case "wgo-anims":
            {
                // How the objects of one kind are animated and which way they face: "states=Walk:12,Idle:3 facings=E:10,W:5".
                MethodInfo viewOf = T("GameScene").GetMethod("GetWgoViewGlobal", new[] { T("SGuid") });
                var states = new System.Collections.Generic.Dictionary<string, int>();
                var facings = new System.Collections.Generic.Dictionary<string, int>();
                foreach (object w in wgos.Where(w => Convert.ToString(G(w, "id")) == args[1]))
                {
                    var animView = viewOf.Invoke(null, new[] { G(w, "UniqueId") }) as Component;
                    object animation = G(G(animView, "MainWgoPart"), "AnimationComponent");
                    string state = animView == null ? "noview" : animation == null ? (G(animView, "MainWgoPart") == null ? "nopart" : "noanim") : Convert.ToString(animation.GetType().GetMethod("GetState", Type.EmptyTypes).Invoke(animation, null));
                    states[state] = (states.TryGetValue(state, out int n) ? n : 0) + 1;
                    Vector2 d = G(G(w, "direction"), "Value") is Vector2 v ? v : Vector2.zero;
                    string facing = d.sqrMagnitude < 0.0001f ? "none" : (Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x > 0 ? "E" : "W") : (d.y > 0 ? "N" : "S"));
                    facings[facing] = (facings.TryGetValue(facing, out int m) ? m : 0) + 1;
                }
                return "WGO-ANIMS " + args[1] + " states=" + string.Join(",", states.Select(k => k.Key + ":" + k.Value)) + " facings=" + string.Join(",", facings.Select(k => k.Key + ":" + k.Value));
            }
            case "fight-start":
            {
                object controller = FightController();
                controller.GetType().GetMethod("Play", new[] { typeof(string) }).Invoke(controller, new object[] { args[1] });
                return "FIGHT-START " + args[1] + " state=" + G(controller, "CurrentFightState");
            }
            case "fight-stop":
            {
                object controller = FightController();
                if (args.Length > 1 && args[1] == "hard") controller.GetType().GetMethod("Stop").Invoke(controller, new object[] { false, false });
                else controller.GetType().GetMethod("FinishAsLost").Invoke(controller, null);
                return "FIGHT-STOP state=" + G(controller, "CurrentFightState");
            }
            case "fight-state":
            {
                object controller = FightController();
                var agents = FindAll(T("FightingAgent")).Cast<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy).ToArray();
                var named = agents.Select(c => Convert.ToString(G(G(G(c, "Wgo"), "Data"), "id")) + "@" + c.transform.position.ToString("F1")).Take(8).ToArray();
                object data = null;
                try { data = G(controller, "CurrentLevelId"); } catch { }
                return "FIGHT-STATE state=" + G(controller, "CurrentFightState") + " level=" + data + " agents=" + agents.Length + " " + string.Join(" ", named);
            }
            case "body-to":
            {
                object body = MainGameBody();
                var local = body as MonoBehaviour;
                float bodyY = args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : local.transform.position.y;
                Vector3 bodyTarget = new Vector3(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), bodyY, float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
                object rb = local.GetComponent(Type.GetType("UnityEngine.Rigidbody, UnityEngine.PhysicsModule"));
                if (rb != null) rb.GetType().GetProperty("position").SetValue(rb, bodyTarget, null);
                local.transform.position = bodyTarget;
                return "BODY-TO " + bodyTarget.ToString("F1");
            }
            case "fight-stage":
            {
                object level = scenes.SelectMany(sc => List(sc, "fightingLevels").Cast<object>()).First(l => Convert.ToString(G(l, "id")) == args[1]);
                level.GetType().GetProperty("CurStageId").SetValue(level, int.Parse(args[2]), null);
                return "FIGHT-STAGE " + args[1] + " stage=" + G(level, "CurStageId");
            }
            // The host's copy of a player's bag (the record the host decides pickups with): its size
            // and items, to set beside that player's own "pinv".
            case "record-bag":
            {
                object record = args[1] == "0" ? G(save, "hostPlayer") : List(save, "clientPlayers").Cast<object>().FirstOrDefault(c => Convert.ToString(G(c, "clientId")) == args[1]);
                if (record == null) return "RECORD-BAG none for " + args[1];
                object bag = G(G(G(record, "playerData"), "inventory"), "Data");
                var stacks = List(bag, "Inventory").Cast<object>().Select(i => G(i, "id") + "x" + G(i, "Count")).OrderBy(x => x).ToArray();
                return "RECORD-BAG " + args[1] + " size=" + G(bag, "InventorySize") + " stacks=" + stacks.Length + " " + string.Join(",", stacks);
            }
            case "pinv":
            {
                var lines = new StringBuilder();
                foreach (string field in new[] { "inventory", "toolBeltInventory" })
                    foreach (object held in List(G(G(player, field), "Data"), "Inventory"))
                        lines.AppendLine("PINV " + field + " " + Describe(held, 1));
                return lines.ToString();
            }
            // Scene checks: go somewhere through the game's own teleport, and count how much of
            // each remote body is drawn.
            case "teleport-wgo":
            {
                object destination = args[1].StartsWith("scene:")
                    ? List(scenes.First(sc => Convert.ToString(G(sc, "id")) == args[1].Substring(6)), "wgoDataList").Cast<object>().First()
                    : wgos.First(w => Id(w) == args[1] || Convert.ToString(G(w, "id")) == args[1] || Convert.ToString(G(w, "customTag")) == args[1]);
                object data = Activator.CreateInstance(T("WgoTeleportData"), Convert.ToString(G(destination, "id")), "outdoor", "", false, null, true, 0.3f);
                object ok = T("PlayerController").GetMethod("Teleport", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { data });
                return "TELEPORT to " + G(destination, "id") + " " + Id(destination) + " started=" + ok + " from=" + G(player, "currentGameSceneId");
            }
            case "remote-drawn":
            {
                var lines = new StringBuilder("REMOTE-DRAWN scene=" + G(player, "currentGameSceneId"));
                foreach (Component body in FindAll(T("PlayerPhysicalBody")).Cast<Component>().Where(b => b.gameObject.scene.IsValid()))
                {
                    if (ReferenceEquals(G(body, "playerData"), player)) continue;
                    var renderers = body.GetComponentsInChildren<Renderer>(true);
                    object[] resolveArgs = { G(body, "playerData"), 0UL };
                    bool resolved = (bool)T("GK2Coop.CoopHud").GetMethod("TryResolveClientId", Flags).Invoke(null, resolveArgs);
                    lines.Append(" body=" + renderers.Count(r => r.enabled && !r.forceRenderingOff) + "/" + renderers.Length +
                                 "[client=" + (resolved ? Convert.ToString(resolveArgs[1]) : "?") + "]");
                }
                return lines.ToString();
            }
            case "scenes":
            {
                var lines = new StringBuilder("SCENES player=" + G(player, "currentGameSceneId") + Environment.NewLine);
                foreach (object scene in scenes)
                    lines.AppendLine("SCENE " + G(scene, "id") + " offset=" + G(scene, "offset") + " wgos=" + List(scene, "wgoDataList").Cast<object>().Count());
                return lines.ToString();
            }
            case "kinds":
            {
                var lines = new StringBuilder();
                foreach (var group in wgos.GroupBy(w => Convert.ToString(G(G(w, "Definition"), "interactionType"))).OrderBy(g => g.Key))
                {
                    var filled = group.Where(w => G(w, "Inventory") != null && List(G(G(w, "Inventory"), "Data"), "Inventory").Cast<object>().Any()).ToArray();
                    lines.AppendLine("KIND " + group.Key + " count=" + group.Count() + " withItems=" + filled.Length +
                        " examples=" + string.Join(",", group.Select(w => Convert.ToString(G(w, "id"))).Distinct().Take(6).ToArray()) +
                        (filled.Length == 0 ? "" : " filled=" + string.Join(",", filled.Take(3).Select(w => Id(w) + ":" + G(w, "id")).ToArray())));
                }
                return lines.ToString();
            }
            case "inv":
            {
                object wgo = wgos.First(w => Id(w) == args[1]);
                var lines = new StringBuilder("INV " + args[1] + " " + G(wgo, "id") + Environment.NewLine);
                foreach (object invItem in List(G(G(wgo, "Inventory"), "Data"), "Inventory"))
                    lines.AppendLine("  " + Describe(invItem, 1));
                return lines.ToString();
            }
            // Rich containers: take one item (with whatever it carries) out of a container into the
            // player's inventory, or take one item out of an item nested in it (an organ from a body).
            case "inv-take":
            {
                object wgo = wgos.First(w => Id(w) == args[1]);
                object container = G(wgo, "Inventory");
                object taken = List(G(container, "Data"), "Inventory").Cast<object>().First(i => Convert.ToString(G(i, "id")) == args[2]);
                string takenText = Describe(taken, 1);
                RemoveById(container, args[2], 1);
                Call(G(player, "inventory"), "AddItemToInventory", taken, null, false);
                return "INV-TAKE " + takenText;
            }
            case "inv-nested-take":
            {
                object wgo = wgos.First(w => Id(w) == args[1]);
                object container = G(wgo, "Inventory");
                object holder = List(G(container, "Data"), "Inventory").Cast<object>()
                    .First(i => G(i, "inventory") is System.Collections.ICollection c && c.Count > 0);
                object inner = Activator.CreateInstance(T("Inventory"), holder);
                object child = List(G(inner, "Data"), "Inventory").Cast<object>().First();
                string childId = Convert.ToString(G(child, "id"));
                RemoveById(inner, childId, 1);
                return "INV-NESTED-TAKE " + childId + " from " + G(holder, "id") + " now " + Describe(holder, 1);
            }
            case "craft-needs":
            {
                // Recipes a station offers, with their ingredients, so a test can pick one it can supply.
                object wgo = wgos.First(w => Id(w) == args[1]);
                var lines = new StringBuilder();
                foreach (object def in List(G(wgo, "CraftComponent"), "AvailableCrafts").Cast<object>().Take(60))
                {
                    var needs = List(def, "needItems").Cast<object>().Select(n =>
                        G(n, "id") + ":" + Convert.ToInt32(Call(G(n, "count"), "EvaluateFloat")) + ":" + G(n, "groupType"));
                    lines.AppendLine("RECIPE " + G(def, "id") + " auto=" + G(def, "isAuto") + " replace=" + (G(def, "replaceWgoId") ?? "") + " needs=" + string.Join(",", needs.ToArray()));
                }
                return lines.ToString();
            }
            case "craft-tools":
            {
                // As craft-needs, with the tool each recipe is worked with (PlayerController.HasToolForWork).
                object wgo = wgos.First(w => Id(w) == args[1]);
                object stationTool = G(G(G(wgo, "Definition"), "toolAction"), "actionableTool");
                var lines = new StringBuilder();
                foreach (object def in List(G(wgo, "CraftComponent"), "AvailableCrafts").Cast<object>().Take(60))
                {
                    object tool = G(def, "customItemTypeAction");
                    if (tool == null || Convert.ToString(tool) == "None" || Equals(G(def, "isAuto"), true)) tool = stationTool;
                    var needs = List(def, "needItems").Cast<object>().Select(n =>
                        G(n, "id") + ":" + Convert.ToInt32(Call(G(n, "count"), "EvaluateFloat")) + ":" + G(n, "groupType"));
                    lines.AppendLine("RECIPE " + G(def, "id") + " auto=" + G(def, "isAuto") + " tool=" + (tool ?? "None") + " needs=" + string.Join(",", needs.ToArray()));
                }
                return lines.ToString();
            }
            case "belt-drop":
            {
                // Takes every tool of a type off the player's tool belt.
                var belt = (IList)List(G(G(player, "toolBeltInventory"), "Data"), "Inventory");
                var gone = belt.Cast<object>().Where(i => Convert.ToString(G(G(i, "Definition"), "type")) == args[1]).ToList();
                foreach (object tool in gone) belt.Remove(tool);
                return "BELT-DROP removed=" + gone.Count + " left=" + string.Join(",", belt.Cast<object>().Select(i => Convert.ToString(G(i, "id"))).ToArray());
            }
            case "belt-give":
            {
                object toolDef = List(G(T("GameBalance"), "Me"), "itemDefs").Cast<object>().FirstOrDefault(d => Convert.ToString(G(d, "type")) == args[1]);
                if (toolDef == null) return "BELT-GIVE none for " + args[1];
                Call(G(player, "toolBeltInventory"), "AddItemToInventory", Item(Convert.ToString(G(toolDef, "id")), 1), null, false);
                return "BELT-GIVE " + G(toolDef, "id");
            }
            // Moving, as a player does (nothing made or destroyed): up to n of an item from the bag
            // into a chest, or from a chest into the bag; what the other side does not take goes back.
            case "move-to-chest":
            case "move-from-chest":
            {
                object chestInv = G(wgos.Single(w => Id(w) == args[1]), "Inventory");
                object bag = G(player, "inventory");
                bool toChest = args[0] == "move-to-chest";
                object moveFrom = toChest ? bag : chestInv, moveTo = toChest ? chestInv : bag;
                Func<object, int> have = inv => List(G(inv, "Data"), "Inventory").Cast<object>().Where(i => Convert.ToString(G(i, "id")) == args[2]).Sum(i => Convert.ToInt32(G(i, "Count")));
                int wanted = Math.Min(int.Parse(args[3]), have(moveFrom));
                if (wanted <= 0) return "MOVED 0";
                RemoveById(moveFrom, args[2], wanted);
                int before = have(moveTo);
                Call(moveTo, "AddItemToInventory", Item(args[2], wanted), null, false);
                int moved = Math.Max(0, have(moveTo) - before);
                if (moved < wanted) Call(moveFrom, "AddItemToInventory", Item(args[2], wanted - moved), null, false);
                return "MOVED " + moved;
            }
            case "give":
                Call(G(player, "inventory"), "AddItemToInventory", Item(args[1], int.Parse(args[2])), null, false);
                return "GIVE " + args[1] + "x" + args[2] + " player=" + Items(G(player, "inventory"));
            case "count":
            {
                // One item across the player's inventory and every container in the world.
                Func<object, int> inInventory = inv => inv == null ? 0 : List(G(inv, "Data"), "Inventory").Cast<object>()
                    .Where(i => Convert.ToString(G(i, "id")) == args[1]).Sum(i => Convert.ToInt32(G(i, "Count")));
                int own = inInventory(G(player, "inventory"));
                int containers = wgos.Sum(w => inInventory(G(w, "Inventory")));
                // Station input/output inventories are separate from a container's own inventory.
                int stations = wgos.Sum(w => inInventory(G(w, "CraftInventory")));
                return "COUNT " + args[1] + " player=" + own + " containers=" + (containers + stations) + " stations=" + stations;
            }
            case "work":
            {
                // One or more tool ticks at a station through the player's own ToolComponent, the
                // path a real button press takes (so tool sync forwards it to the host).
                object wgo = wgos.First(w => Id(w) == args[1]);
                var controller = (Component)G(T("MainGame"), "PlayerController");
                object workComponent = controller.GetComponentInChildren(T("PlayerWorkComponent"), true);
                object tool = G(workComponent, "ToolComponent");
                object activity = Activator.CreateInstance(T("PlayerCraftActivity"), player, wgo);
                object hand = List(G(G(player, "toolBeltInventory"), "Data"), "Inventory").Cast<object>().First();
                tool.GetType().GetField("toolActor", Flags).SetValue(tool, activity);
                tool.GetType().GetField("toolInUse", Flags).SetValue(tool, hand);
                MethodInfo apply = tool.GetType().GetMethod("ApplyAction", Flags);
                for (int i = 0; i < int.Parse(args[2]); i++) apply.Invoke(tool, null);
                object craft = G(wgo, "CraftComponent");
                object current = G(craft, "CurrentCraftElement");
                return "WORK x" + args[2] + " status=" + G(craft, "Status") + " cur=" + (current == null ? "-" : G(current, "CraftId") + "@" + G(current, "ProgressTimeNormalized"));
            }
            case "finish":
            {
                // Completes a recipe on an object the way the game ends a craft (WgoData.OnCraftEnd),
                // as if this player had just finished working it.
                object wgo = wgos.First(w => Id(w) == args[1]);
                object parameters = Activator.CreateInstance(T("CraftParamsData"), args[2], wgo, Enum.ToObject(T("CraftParamsData").GetNestedType("CraftParamsType"), 0), -1);
                object none = Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(T("NeedItemData")));
                object element = Activator.CreateInstance(T("CraftElement"), args[2], 1, none, parameters);
                Call(element, "BindCraftable", wgo);
                Call(element, "UpdateActualOutputBeforeFinish");
                Call(G(wgo, "CraftComponent"), "Clear");
                wgo.GetType().GetMethod("OnCraftEnd", Flags, null, new[] { T("CraftElementBase") }, null).Invoke(wgo, new[] { element });
                return "FINISH " + args[2] + " on " + args[1] + " now=" + (wgos.FirstOrDefault(w => Id(w) == args[1]) == null ? "gone" : "same list entry");
            }
            case "start-craft":
            {
                // What the grave window does when a part is chosen: TryStartCraft, ingredients from the player.
                object wgo = wgos.First(w => Id(w) == args[1]);
                object craft = G(wgo, "CraftComponent");
                object element = NewCraftElement(args[2], wgo);
                string startStatus = Convert.ToString(Call(craft, "GetStartCraftStatus", element, null));
                object started = craft.GetType().GetMethods(Flags).First(m => m.Name == "TryStartCraft" && m.GetParameters().Length >= 1)
                    .Invoke(craft, new[] { element }.Concat(Enumerable.Repeat(Type.Missing, craft.GetType().GetMethods(Flags).First(m => m.Name == "TryStartCraft" && m.GetParameters().Length >= 1).GetParameters().Length - 1)).ToArray());
                return "START-CRAFT " + args[2] + " check=" + startStatus + " started=" + started + " status=" + G(craft, "Status") + " player=" + Items(G(player, "inventory"));
            }
            case "craft":
            {
                object wgo = wgos.First(w => Id(w) == args[1]);
                object craft = G(wgo, "CraftComponent");
                object element = NewCraftElement(args[2], wgo);
                string status = Convert.ToString(Call(craft, "GetStartCraftStatus", element, null));
                Call(craft, "AddToQueue", element, false, -1);
                return "CRAFT " + args[2] + " startStatus=" + status + " queue=" + List(craft, "CraftElementsQueue").Cast<object>().Count() +
                       " status=" + G(craft, "Status") + " player=" + Items(G(player, "inventory"));
            }
            // Rejoin checks: move the local body the way the game does, and read where it is.
            case "nudge":
                object nudgeController = T("MainGame").GetProperty("PlayerController", Flags).GetValue(null, null);
                var nudgeFrom = ((Component)nudgeController).transform.position;
                nudgeController.GetType().GetMethod("SetPosition", new[] { typeof(Vector3), typeof(bool), typeof(bool) })
                    .Invoke(nudgeController, new object[] { nudgeFrom + new Vector3(float.Parse(args[1]), 0f, float.Parse(args[2])), true, true });
                return "NUDGE " + nudgeFrom.ToString("F2") + " -> " + ((Component)nudgeController).transform.position.ToString("F2");
            case "where":
                object whereController = T("MainGame").GetProperty("PlayerController", Flags).GetValue(null, null);
                Vector3 whereBody = ((Component)whereController).transform.position;
                return "WHERE " + whereBody.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " " +
                       whereBody.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " scene=" + G(player, "currentGameSceneId");
            case "profile":
                return "PROFILE " + T("GK2Coop.CoopPlayerProfiles").GetMethod("Summary", Flags).Invoke(null, new[] { player }) +
                       " outcome=" + T("GK2Coop.CoopPlayerProfiles").GetProperty("LastOutcome", Flags).GetValue(null, null);
            case "setres":
                object res = player.GetType().GetField("res", Flags).GetValue(player);
                res.GetType().GetMethod("Set", new[] { typeof(string), typeof(float) }).Invoke(res, new object[] { args[1], float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) });
                return "SETRES " + args[1] + "=" + res.GetType().GetMethod("Get", new[] { typeof(string), typeof(float) }).Invoke(res, new object[] { args[1], 0f });
            case "resource":
                return "RESOURCE " + args[1] + "=" + Call(player, "GetResInt", args[1]);
            case "addres":
            {
                object addTo = player.GetType().GetField("res", Flags).GetValue(player);
                float before = Convert.ToSingle(addTo.GetType().GetMethod("Get", new[] { typeof(string), typeof(float) }).Invoke(addTo, new object[] { args[1], 0f }));
                addTo.GetType().GetMethod("Set", new[] { typeof(string), typeof(float) }).Invoke(addTo, new object[] { args[1], before + float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) });
                return "ADDRES " + args[1] + " " + before + "->" + addTo.GetType().GetMethod("Get", new[] { typeof(string), typeof(float) }).Invoke(addTo, new object[] { args[1], 0f });
            }
            // Shared action gems, weather and chat (0.46).
            case "gems":
                return "GEMS " + T("GK2Coop.CoopSharedGems").GetMethod("Describe", Flags).Invoke(null, null);
            case "weather":
                return "WEATHER state=" + T("GK2Coop.CoopWeatherSync").GetMethod("CurrentState", Flags).Invoke(null, null) +
                       " force=" + G(G(save, "weatherData"), "hasForceState") + " " + T("GK2Coop.CoopWeatherSync").GetMethod("Describe", Flags).Invoke(null, null);
            case "weather-roll":
            {
                object weather = T("WeatherSystem").GetProperty("Instance", Flags).GetValue(null, null);
                string rollBefore = Convert.ToString(G(G(save, "weatherData"), "stateName"));
                weather.GetType().GetMethod("RollNextWeatherState", Flags).Invoke(weather, null);
                return "WEATHER-ROLL from=" + rollBefore;
            }
            case "weather-set":
            {
                object weather = T("WeatherSystem").GetProperty("Instance", Flags).GetValue(null, null);
                weather.GetType().GetMethod("SetWeatherState", new[] { typeof(string), typeof(bool) }).Invoke(weather, new object[] { args[1], false });
                return "WEATHER-SET " + G(G(save, "weatherData"), "stateName");
            }
            // UI restyle spike (0.47): what the game's own windows are made of.
            case "ui-kit":
                return UiKitReport();
            case "screenshot":
                T("UnityEngine.ScreenCapture").GetMethod("CaptureScreenshot", new[] { typeof(string) }).Invoke(null, new object[] { args[1] });
                return "SCREENSHOT " + args[1];
            case "chat-send":
                T("GK2Coop.CoopChat").GetMethod("SendForTest", Flags).Invoke(null, new object[] { args[1] });
                return "CHAT-SEND " + args[1];
            case "chat-last":
                return "CHAT-LAST " + T("GK2Coop.CoopChat").GetMethod("LastLine", Flags).Invoke(null, null);
            default: throw new Exception("Unknown probe command");
        }
    }
}
