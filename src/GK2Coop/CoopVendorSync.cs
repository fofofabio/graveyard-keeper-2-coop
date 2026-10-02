using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Vendors are one shop for everyone.
    ///
    /// A vendor's stock, its own money and its weekly "happiness" budget live in
    /// <c>GameSave.vendorSystem</c> — world state — but a trade changed them only on the machine
    /// of the player who traded, so the same shop showed different shelves to each player. (The
    /// player's own money is per player and already follows them.)
    ///
    /// After a deal (<c>Trading.DoAcceptDeal</c>) the trading machine sends that vendor's whole
    /// state, serialized with the game's <c>LazySerializer</c>; the host forwards it. The host also
    /// sends any vendor whose state changed on its own (daily restock, its own trades), checked
    /// every few seconds. A receiver loads the state into its copy of the vendor — except while its
    /// own player has that vendor's trade window open: items in a pending basket are taken out of
    /// the vendor's stock and put back on cancel, so replacing the stock mid-deal could duplicate or
    /// lose them. Such an update waits until the window closes.
    /// </summary>
    internal static class CoopVendorSync
    {
        internal const string VendorMessage = "GK2Coop.Vendor.v1";

        private static ManualLogSource log;
        private static readonly Dictionary<string, byte[]> lastSeen = new Dictionary<string, byte[]>();
        private static readonly Dictionary<string, byte[]> waiting = new Dictionary<string, byte[]>();
        private static bool applying;
        private static float nextCheck;
        private static int sent;
        private static int applied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return $"vendor sync: states sent={sent}, applied={applied}, waiting for a closed window={waiting.Count}";
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                MethodInfo accept = AccessTools.Method(Plugin.FindGameType("Trading"), "DoAcceptDeal");
                if (accept == null)
                {
                    log.LogWarning("Vendor sync: Trading.DoAcceptDeal not found; trades stay local.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(accept, postfix: new HarmonyMethod(typeof(CoopVendorSync).GetMethod(nameof(DealPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Vendor sync: every player trades with the same shops.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Vendor sync disabled: " + ex.Message);
            }
        }

        private static void DealPostfix(object __instance)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || applying || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                object windowData = AccessTools.Field(__instance.GetType(), "cachedWindowData")?.GetValue(__instance);
                object vendor = windowData == null ? null : CoopDiagnostics.GetMember(windowData, "Vendor");
                if (vendor == null)
                {
                    return;
                }
                Share(vendor);
            }
            catch (Exception ex)
            {
                log.LogWarning("Vendor sync: could not share a finished deal: " + Inner(ex).Message);
            }
        }

        /// <summary>Sends one vendor's current state to the other players (after a deal; also used by tests).</summary>
        internal static void Share(object vendor)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || vendor == null)
            {
                return;
            }
            string id = Convert.ToString(CoopDiagnostics.GetMember(vendor, "id"));
            byte[] raw = Serialize(vendor);
            lastSeen[id] = raw;
            Send(netcode, null, id, raw);
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                lastSeen.Clear();
                waiting.Clear();
                return;
            }
            try
            {
                // Updates that arrived while this player was trading with that vendor.
                if (waiting.Count > 0 && !TradeWindowOpen())
                {
                    foreach (KeyValuePair<string, byte[]> update in new List<KeyValuePair<string, byte[]>>(waiting))
                    {
                        waiting.Remove(update.Key);
                        Apply(update.Key, update.Value);
                    }
                }
                if (!netcode.IsHost || netcode.ConnectedClientsIds.Count < 2 || Time.unscaledTime < nextCheck)
                {
                    return;
                }
                nextCheck = Time.unscaledTime + 5f;
                foreach (object vendor in Vendors())
                {
                    string id = Convert.ToString(CoopDiagnostics.GetMember(vendor, "id"));
                    byte[] raw = Serialize(vendor);
                    if (lastSeen.TryGetValue(id, out byte[] previous) && Same(previous, raw))
                    {
                        continue;
                    }
                    bool first = !lastSeen.ContainsKey(id);
                    lastSeen[id] = raw;
                    if (!first)
                    {
                        Send(netcode, null, id, raw);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Vendor sync: " + Inner(ex).Message);
                nextCheck = Time.unscaledTime + 30f;
            }
        }

        private static void Send(NetworkManager netcode, ulong? except, string id, byte[] raw)
        {
            byte[] compressed = Compress(raw);
            using (var writer = new FastBufferWriter(96 + compressed.Length, Allocator.Temp))
            {
                writer.WriteValueSafe(new FixedString128Bytes(id));
                writer.WriteValueSafe(compressed.Length);
                writer.WriteBytesSafe(compressed, compressed.Length);
                NetworkDelivery delivery = compressed.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(VendorMessage, clientId, writer, delivery);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(VendorMessage, NetworkManager.ServerClientId, writer, delivery);
                }
            }
            sent++;
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
                reader.ReadValueSafe(out FixedString128Bytes idText);
                reader.ReadValueSafe(out int length);
                if (length <= 0 || length > 1024 * 1024)
                {
                    return;
                }
                byte[] compressed = new byte[length];
                reader.ReadBytesSafe(ref compressed, length);
                byte[] raw = Decompress(compressed);
                string id = idText.ToString();
                if (netcode.IsHost)
                {
                    Send(netcode, sender, id, raw);
                }
                lastSeen[id] = raw;
                if (TradeWindowOpen())
                {
                    waiting[id] = raw;
                    return;
                }
                Apply(id, raw);
            }
            catch (Exception ex)
            {
                log.LogWarning("Vendor sync: could not apply a vendor from " + sender + ": " + Inner(ex).Message);
            }
        }

        private static void Apply(string id, byte[] raw)
        {
            object system = VendorSystem();
            object vendor = system == null ? null : system.GetType().GetMethod("GetVendor").Invoke(system, new object[] { id });
            if (vendor == null)
            {
                return;
            }
            applying = true;
            try
            {
                SerializerMethod("DeserializeInto").MakeGenericMethod(vendor.GetType()).Invoke(null, new[] { vendor, raw });
                applied++;
            }
            finally
            {
                applying = false;
            }
        }

        private static bool TradeWindowOpen()
        {
            try
            {
                Type windowType = Plugin.FindGameType("UIVendorWindow");
                MethodInfo getWindow = Plugin.FindGameType("LazyBearTechnology.LazyUI").GetMethod("GetWindow", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null).MakeGenericMethod(windowType);
                object window = getWindow.Invoke(null, null);
                return window is Component component && component.gameObject.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static object VendorSystem()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "vendorSystem");
        }

        private static IEnumerable<object> Vendors()
        {
            object system = VendorSystem();
            if (system != null && CoopDiagnostics.GetMember(system, "vendors") is System.Collections.IEnumerable list)
            {
                foreach (object vendor in list)
                {
                    if (vendor != null) yield return vendor;
                }
            }
        }

        private static byte[] Serialize(object value)
        {
            return (byte[])SerializerMethod("Serialize").MakeGenericMethod(value.GetType()).Invoke(null, new[] { value });
        }

        private static MethodInfo SerializerMethod(string name)
        {
            foreach (MethodInfo method in Plugin.FindGameType("LazyBearTechnology.LazySerializer").GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == name && method.IsGenericMethodDefinition &&
                    ((name == "Serialize" && parameters.Length == 1) || (name == "DeserializeInto" && parameters.Length == 2 && parameters[1].ParameterType == typeof(byte[]))))
                {
                    return method;
                }
            }
            throw new MissingMethodException("LazySerializer." + name);
        }

        private static byte[] Compress(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return output.ToArray();
            }
        }

        private static byte[] Decompress(byte[] compressed)
        {
            using (var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
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
