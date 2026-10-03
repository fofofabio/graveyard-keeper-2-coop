using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Each player looks like themselves on everyone's screen.
    ///
    /// The game draws a player from <c>PlayerData.customization</c> through static helpers that
    /// only know the local player (<c>PlayerSkinHelper.ApplySkin</c>, <c>CurrentPreset</c>), and a
    /// remote body is initialised from the local player's current preset — so every remote player
    /// looked like whoever was watching.
    ///
    /// Each machine sends its own player's customization, serialized with the game's
    /// <c>LazySerializer</c>, when it changes and whenever the set of players changes; the host
    /// forwards joiners' looks to the other joiners. Every machine then draws each remote body
    /// with that player's preset and colour palette — the same pair the game builds for the local
    /// player — and redraws a body that respawns.
    /// </summary>
    internal static class CoopAppearanceSync
    {
        internal const string AppearanceMessage = "GK2Coop.Appearance.v1";

        private static ManualLogSource log;
        private static readonly Dictionary<ulong, byte[]> looks = new Dictionary<ulong, byte[]>();
        private static readonly Dictionary<int, int> drawn = new Dictionary<int, int>();
        private static byte[] lastSentLocal;
        private static int lastPeerCount = -1;
        private static float nextTick;
        private static int applied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return $"appearance sync: looks known={looks.Count}, bodies drawn={applied}";
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || Time.unscaledTime < nextTick)
            {
                if (netcode == null || !netcode.IsListening)
                {
                    looks.Clear();
                    drawn.Clear();
                    lastSentLocal = null;
                    lastPeerCount = -1;
                }
                return;
            }
            nextTick = Time.unscaledTime + 2f;
            if (!netcode.IsHost && !(netcode.IsConnectedClient && CoopSession.Welcomed))
            {
                return;
            }
            try
            {
                object local = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
                object customization = local == null ? null : CoopDiagnostics.GetMember(local, "customization");
                if (customization == null)
                {
                    return;
                }
                byte[] mine = Serialize(customization);
                int peers = netcode.IsHost ? netcode.ConnectedClientsIds.Count : 2;
                bool peersChanged = peers != lastPeerCount;
                lastPeerCount = peers;
                if (lastSentLocal == null || !Same(lastSentLocal, mine) || peersChanged)
                {
                    lastSentLocal = mine;
                    Send(netcode, netcode.LocalClientId, mine, null);
                    if (netcode.IsHost && peersChanged)
                    {
                        // A newcomer also needs everyone else's look.
                        foreach (KeyValuePair<ulong, byte[]> known in looks)
                        {
                            Send(netcode, known.Key, known.Value, known.Key);
                        }
                    }
                }
                DrawRemoteBodies(local);
            }
            catch (Exception ex)
            {
                log.LogWarning("Appearance sync: " + Inner(ex).Message);
                nextTick = Time.unscaledTime + 10f;
            }
        }

        private static void Send(NetworkManager netcode, ulong origin, byte[] look, ulong? except)
        {
            using (var writer = new FastBufferWriter(32 + look.Length, Allocator.Temp))
            {
                writer.WriteValueSafe(origin);
                writer.WriteValueSafe(look.Length);
                writer.WriteBytesSafe(look, look.Length);
                NetworkDelivery delivery = look.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except && clientId != origin)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(AppearanceMessage, clientId, writer, delivery);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(AppearanceMessage, NetworkManager.ServerClientId, writer, delivery);
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
                reader.ReadValueSafe(out ulong origin);
                reader.ReadValueSafe(out int length);
                if (length <= 0 || length > 256 * 1024)
                {
                    return;
                }
                byte[] look = new byte[length];
                reader.ReadBytesSafe(ref look, length);
                if (netcode.IsHost)
                {
                    // A joiner speaks for itself only.
                    origin = sender;
                    Send(netcode, origin, look, sender);
                }
                looks[origin] = look;
                drawn.Clear();
                nextTick = 0f;
            }
            catch (Exception ex)
            {
                log.LogWarning("Appearance sync: could not read a look from " + sender + ": " + Inner(ex).Message);
            }
        }

        private static void DrawRemoteBodies(object local)
        {
            foreach (Component body in CoopBodies.All())
            {
                if (body == null || !body.gameObject.activeInHierarchy)
                {
                    continue;
                }
                object data = CoopDiagnostics.GetMember(body, "playerData");
                if (data == null || ReferenceEquals(data, local) || !CoopHud.TryResolveClientId(data, out ulong clientId))
                {
                    continue;
                }
                if (!looks.TryGetValue(clientId, out byte[] look))
                {
                    continue;
                }
                int key = body.GetInstanceID();
                int stamp = Fnv(look);
                if (drawn.TryGetValue(key, out int previous) && previous == stamp)
                {
                    continue;
                }
                try
                {
                    object customization = Deserialize(look, Plugin.FindGameType("PlayerCustomizationData"));
                    if (!(CoopDiagnostics.GetMember(customization, "customizationPartsData") is System.Collections.ICollection parts) || parts.Count == 0)
                    {
                        // An empty look cannot be drawn (the game's skin builder throws on it).
                        drawn[key] = stamp;
                        continue;
                    }
                    AccessTools.Field(data.GetType(), "customization")?.SetValue(data, customization);
                    if (Draw(body, customization))
                    {
                        drawn[key] = stamp;
                        applied++;
                        log.LogInfo($"Appearance sync: drew {CoopSession.NameFor(clientId)} with their own look.");
                    }
                }
                catch (Exception ex)
                {
                    drawn[key] = stamp;
                    log.LogWarning($"Appearance sync: could not draw {CoopSession.NameFor(clientId)}: {Inner(ex).Message}");
                }
            }
        }

        /// <summary>
        /// Opens the game's own customization window for the local player, as the game's
        /// <c>OpenCustomizationWindow</c> script function does, and applies the chosen look the same
        /// way. A joiner can change their look without finding the object in the host's world that
        /// normally opens it; the new look reaches the others through the usual appearance sync.
        /// </summary>
        internal static void OpenCustomizationWindow()
        {
            try
            {
                object local = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
                Type customizationType = Plugin.FindGameType("PlayerCustomizationData");
                Type windowType = Plugin.FindGameType("UICustomizationWindow");
                Type dataType = Plugin.FindGameType("UICustomizationWindowData");
                if (local == null || customizationType == null || windowType == null || dataType == null)
                {
                    return;
                }
                object data = Activator.CreateInstance(dataType);
                object copy = customizationType.GetMethod("Copy", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { CoopDiagnostics.GetMember(local, "customization") });
                MemberInfo current = (MemberInfo)dataType.GetField("CurrentData") ?? dataType.GetProperty("CurrentData");
                if (current is FieldInfo field) field.SetValue(data, copy); else ((PropertyInfo)current).SetValue(data, copy, null);
                MethodInfo getWindow = Plugin.FindGameType("LazyBearTechnology.LazyUI").GetMethod("GetWindow", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null).MakeGenericMethod(windowType);
                object window = getWindow.Invoke(null, null);
                window.GetType().GetMethod("Open", new[] { dataType }).Invoke(window, new[] { data });
                typeof(CoopAppearanceSync).GetMethod(nameof(ApplyOnce), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(customizationType).Invoke(null, new[] { window, local });
            }
            catch (Exception ex)
            {
                log.LogWarning("Appearance sync: could not open the customization window: " + Inner(ex).Message);
            }
        }

        private static void ApplyOnce<T>(object window, object local)
        {
            EventInfo applied = window.GetType().GetEvent("OnCustomizationApplied");
            Action<T> handler = null;
            handler = chosen =>
            {
                local.GetType().GetMethod("ApplyCustomization").Invoke(local, new object[] { chosen });
                applied.RemoveEventHandler(window, handler);
                log.LogInfo("Appearance sync: the local player chose a new look.");
            };
            applied.AddEventHandler(window, handler);
        }

        /// <summary>
        /// What <c>PlayerData.ApplyCustomization</c> does for the local player, on a remote view.
        ///
        /// Two things in the game's helpers only work for the local player and are done here for
        /// the remote preset instead: <c>PlayerAnimation.ApplyPlayerColors</c> writes the palette
        /// into <c>PlayerSkinHelper.CurrentPreset</c> (the local player's) unless given a preset, so
        /// a remote body kept a preset without palette and was drawn in the raw source colours
        /// (blue hair, pink clothes); and <c>GetColorReplacementPalette</c> picks each part's
        /// palette by the local preset's part ids.
        /// </summary>
        private static bool Draw(Component body, object customization)
        {
            object view = CoopDiagnostics.GetMember(body, "playerView");
            object animation = view == null ? null : CoopDiagnostics.GetMember(view, "PlayerAnimation");
            Type helper = Plugin.FindGameType("PlayerSkinHelper");
            MethodInfo build = AccessTools.Method(helper, "GetPresetForCustomizationData", new[] { customization.GetType() });
            object preset = build?.Invoke(null, new[] { customization });
            object controller = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerController");
            object characterData = controller == null ? null : CoopDiagnostics.GetMember(controller, "CharacterCustomizationData");
            object parts = characterData == null ? null : CoopDiagnostics.GetMember(characterData, "affectedPartTypes");
            if (view == null || animation == null || preset == null || parts == null)
            {
                return false;
            }
            // The builder shares DefaultPreset.head between presets: colouring it would recolour the local player's head too.
            OwnPart(preset, "head");
            Texture2D palette = PaletteFor(customization, preset, characterData, helper);
            MethodInfo setPreset = AccessTools.Method(view.GetType(), "SetPlayerPreset");
            MethodInfo applyColors = AccessTools.Method(animation.GetType(), "ApplyPlayerColors", new[] { typeof(Texture2D), parts.GetType(), preset.GetType() });
            if (palette == null || setPreset == null || applyColors == null)
            {
                return false;
            }
            setPreset.Invoke(view, new[] { preset, (object)false });
            applyColors.Invoke(animation, new[] { palette, parts, preset });
            return true;
        }

        /// <summary><c>PlayerSkinHelper.GetColorReplacementPalette</c>, with the part ids of the remote preset.</summary>
        private static Texture2D PaletteFor(object customization, object preset, object characterData, Type helper)
        {
            MethodInfo savedIndex = AccessTools.Method(helper, "GetSavedColorPaletteIndex");
            MethodInfo combine = AccessTools.Method(Plugin.FindGameType("PaletteReplaceHelper"), "CombinePalettes");
            object source = CoopDiagnostics.GetMember(characterData, "sourcePalette");
            if (savedIndex != null && combine != null && CoopDiagnostics.GetMember(characterData, "customizationElements") is System.Collections.IList elements)
            {
                try
                {
                    var palettes = new List<Texture2D>();
                    foreach (object element in elements)
                    {
                        object type = CoopDiagnostics.GetMember(element, "playerColorCustomizationType");
                        object partId = AccessTools.Method(preset.GetType(), "GetSkinPresetPartId").Invoke(preset, new[] { type });
                        object index = savedIndex.Invoke(null, new[] { type, customization });
                        palettes.Add((Texture2D)AccessTools.Method(element.GetType(), "GetPaletteByIndex").Invoke(element, new[] { index, partId }));
                    }
                    object combined = combine.Invoke(null, new[] { palettes, source });
                    if (combined != null && CoopDiagnostics.GetMember(combined, "palette") is Texture2D own)
                    {
                        return own;
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning("Appearance sync: own palette failed, using the game's: " + Inner(ex).Message);
                }
            }
            object replacePalette = AccessTools.Method(helper, "GetColorReplacementPalette", new[] { customization.GetType() })?.Invoke(null, new[] { customization });
            return replacePalette == null ? null : CoopDiagnostics.GetMember(replacePalette, "palette") as Texture2D;
        }

        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private static void OwnPart(object preset, string name)
        {
            FieldInfo field = AccessTools.Field(preset.GetType(), name);
            object part = field?.GetValue(preset);
            if (part != null)
            {
                field.SetValue(preset, CloneMethod.Invoke(part, null));
            }
        }

        private static byte[] Serialize(object customization)
        {
            return (byte[])SerializerMethod("Serialize").MakeGenericMethod(customization.GetType()).Invoke(null, new[] { customization });
        }

        private static object Deserialize(byte[] raw, Type type)
        {
            return SerializerMethod("Deserialize").MakeGenericMethod(type).Invoke(null, new object[] { raw });
        }

        private static MethodInfo SerializerMethod(string name)
        {
            foreach (MethodInfo method in Plugin.FindGameType("LazyBearTechnology.LazySerializer").GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == name && method.IsGenericMethodDefinition && parameters.Length == 1 &&
                    (name == "Serialize" || parameters[0].ParameterType == typeof(byte[])))
                {
                    return method;
                }
            }
            throw new MissingMethodException("LazySerializer." + name);
        }

        private static int Fnv(byte[] data)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (byte value in data) hash = (hash ^ value) * 16777619;
                return hash;
            }
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }
    }
}
