using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// One player at a time at a grave or an autopsy table. Their contents travel whole, the last
    /// change winning (<see cref="CoopContainerSync"/>), and the work on them runs on the machine
    /// of the player doing it: two players at one table could each take the same organ out of the
    /// same body, one burial could be dug twice. So the player who opens one holds it until their
    /// window is closed and nothing is in progress there on their machine; anyone else who tries
    /// meanwhile is told who is working there. The host decides who holds what, and frees what a
    /// player who left was holding.
    /// </summary>
    internal static class CoopWorkLock
    {
        internal const string LockMessage = "GK2Coop.WorkLock.v1";
        private const byte KindClaim = 0;
        private const byte KindRelease = 1;
        private const byte KindState = 2;
        private const ulong Nobody = ulong.MaxValue;

        private sealed class Holder
        {
            internal ulong ClientId;
            internal string Name;
        }

        private static ManualLogSource log;
        // Every machine: who holds what, as the host last said (the host: as it decided).
        private static readonly Dictionary<string, Holder> holders = new Dictionary<string, Holder>();
        // This player's own: the object and its game object, to see when the work there is over.
        private static readonly Dictionary<string, WgoData> mine = new Dictionary<string, WgoData>();
        private static AccessTools.FieldRef<WGOInteractionHandlerBase, Wgo> assignedWgo;
        private static float nextCheck;
        private static int claims;
        private static int refused;
        private static string lastRefusal = string.Empty;

        internal static bool Enabled { get; set; } = true;

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
                assignedWgo = AccessTools.FieldRefAccess<WGOInteractionHandlerBase, Wgo>("assignedWgo");
                var prefix = new HarmonyMethod(typeof(CoopWorkLock), nameof(InteractPrefix));
                harmony.Patch(AccessTools.Method(typeof(AutopsyInteractionHandler), nameof(AutopsyInteractionHandler.Interact)), prefix: prefix);
                harmony.Patch(AccessTools.Method(typeof(GraveInteractionHandler), nameof(GraveInteractionHandler.Interact)), prefix: prefix);
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Work lock disabled: " + ex.Message);
            }
        }

        internal static string Describe()
        {
            var held = new List<string>();
            foreach (KeyValuePair<string, Holder> pair in holders)
            {
                held.Add(Short(pair.Key) + "=" + pair.Value.Name);
            }
            return "work lock: claims=" + claims + ", refused=" + refused + ", mine=" + mine.Count + ", held=[" + string.Join(", ", held.ToArray()) + "]" +
                   (lastRefusal.Length > 0 ? ", last refusal " + lastRefusal : string.Empty);
        }

        private static bool InSession(out NetworkManager netcode)
        {
            netcode = NetworkManager.Singleton;
            return Enabled && netcode != null && netcode.IsListening && (!netcode.IsHost || netcode.ConnectedClientsIds.Count > 1);
        }

        // ---------------------------------------------------------------- this player

        private static bool InteractPrefix(WGOInteractionHandlerBase __instance, ref bool __result)
        {
            if (!InSession(out NetworkManager netcode))
            {
                return true;
            }
            try
            {
                Wgo wgo = assignedWgo(__instance);
                WgoData data = (object)wgo == null ? null : wgo.Data;
                if (data == null)
                {
                    return true;
                }
                string id = data.UniqueId.ToString();
                if (holders.TryGetValue(id, out Holder holder) && holder.ClientId != netcode.LocalClientId)
                {
                    refused++;
                    lastRefusal = Short(id) + " held by " + holder.Name;
                    Tell(string.Format(L.T("{0} is working here."), holder.Name));
                    __result = true;
                    return false;
                }
                Claim(netcode, id, data);
            }
            catch (Exception ex)
            {
                log.LogWarning("Work lock: " + ex.Message);
            }
            return true;
        }

        private static void Claim(NetworkManager netcode, string id, WgoData data)
        {
            mine[id] = data;
            claims++;
            if (netcode.IsHost)
            {
                Decide(netcode, netcode.LocalClientId, id, true);
            }
            else
            {
                // Held here at once; the host's answer corrects it if someone was first.
                holders[id] = new Holder { ClientId = netcode.LocalClientId, Name = CoopSession.LocalName };
                SendToHost(netcode, KindClaim, id);
            }
        }

        /// <summary>From the plugin's Update: let go of what this player is done with.</summary>
        internal static void Update()
        {
            if (Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + 0.5f;
            if (!InSession(out NetworkManager netcode))
            {
                holders.Clear();
                mine.Clear();
                return;
            }
            if (netcode.IsHost)
            {
                // A player who left lets go of everything.
                foreach (KeyValuePair<string, Holder> pair in new List<KeyValuePair<string, Holder>>(holders))
                {
                    if (!IsConnected(netcode, pair.Value.ClientId))
                    {
                        Decide(netcode, pair.Value.ClientId, pair.Key, false);
                    }
                }
            }
            if (mine.Count == 0)
            {
                return;
            }
            bool windowOpen = IsShown<UIGraveWindow>() || IsShown<UIAutopsyWindow>() || IsShown<UIMultiInventoryWindow>();
            foreach (KeyValuePair<string, WgoData> pair in new List<KeyValuePair<string, WgoData>>(mine))
            {
                bool working = pair.Value != null && pair.Value.CraftComponent != null && pair.Value.CraftComponent.IsStarted;
                if (windowOpen || working)
                {
                    continue;
                }
                mine.Remove(pair.Key);
                if (netcode.IsHost)
                {
                    Decide(netcode, netcode.LocalClientId, pair.Key, false);
                }
                else
                {
                    if (holders.TryGetValue(pair.Key, out Holder holder) && holder.ClientId == netcode.LocalClientId)
                    {
                        holders.Remove(pair.Key);
                    }
                    SendToHost(netcode, KindRelease, pair.Key);
                }
            }
        }

        private static bool IsConnected(NetworkManager netcode, ulong clientId)
        {
            foreach (ulong connected in netcode.ConnectedClientsIds)
            {
                if (connected == clientId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsShown<T>() where T : LazyWidgetBase
        {
            try
            {
                T window = LazyUI.GetWindow<T>();
                return window != null && Traverse.Create(window).Property("IsShown").GetValue<bool>();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A line in the game's own notification strip.</summary>
        private static void Tell(string text)
        {
            try
            {
                UINotificator notificator = UINotificator.Instance;
                UISimpleTextNotification note = LazyPooler.GetObject<UISimpleTextNotification>();
                if (notificator != null && note != null)
                {
                    note.LocalizationKey = string.Empty;
                    note.Text = text;
                    AccessTools.Method(typeof(UINotificator), "ShowNotification").Invoke(notificator, new object[] { note });
                    return;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Work lock: could not show the notice: " + ex.Message);
            }
            log.LogInfo("Work lock: " + text);
        }

        // ---------------------------------------------------------------- host

        private static void Decide(NetworkManager netcode, ulong clientId, string id, bool claim)
        {
            holders.TryGetValue(id, out Holder holder);
            if (claim)
            {
                if (holder != null && holder.ClientId != clientId)
                {
                    // Someone was first: tell the one asking (they may have opened it already).
                    SendState(netcode, id, holder, clientId);
                    return;
                }
                holder = new Holder { ClientId = clientId, Name = clientId == netcode.LocalClientId ? CoopSession.LocalName : CoopSession.NameFor(clientId) };
                holders[id] = holder;
                SendState(netcode, id, holder, null);
            }
            else if (holder != null && holder.ClientId == clientId)
            {
                holders.Remove(id);
                SendState(netcode, id, null, null);
            }
        }

        private static void SendToHost(NetworkManager netcode, byte kind, string id)
        {
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(kind);
                writer.WriteValueSafe(new FixedString64Bytes(id));
                writer.WriteValueSafe(Nobody);
                writer.WriteValueSafe(new FixedString64Bytes(string.Empty));
                netcode.CustomMessagingManager.SendNamedMessage(LockMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private static void SendState(NetworkManager netcode, string id, Holder holder, ulong? only)
        {
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe(KindState);
                writer.WriteValueSafe(new FixedString64Bytes(id));
                writer.WriteValueSafe(holder == null ? Nobody : holder.ClientId);
                writer.WriteValueSafe(new FixedString64Bytes(holder == null ? string.Empty : holder.Name ?? string.Empty));
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId && (only == null || only.Value == clientId))
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(LockMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
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
                reader.ReadValueSafe(out byte kind);
                reader.ReadValueSafe(out FixedString64Bytes idText);
                reader.ReadValueSafe(out ulong clientId);
                reader.ReadValueSafe(out FixedString64Bytes name);
                string id = idText.ToString();
                if (netcode.IsHost)
                {
                    if (kind == KindClaim || kind == KindRelease)
                    {
                        Decide(netcode, sender, id, kind == KindClaim);
                    }
                    return;
                }
                if (kind != KindState)
                {
                    return;
                }
                if (clientId == Nobody)
                {
                    if (!mine.ContainsKey(id))
                    {
                        holders.Remove(id);
                    }
                    return;
                }
                holders[id] = new Holder { ClientId = clientId, Name = name.ToString() };
                if (clientId != netcode.LocalClientId && mine.Remove(id))
                {
                    // Someone else was there first: close what opened here.
                    refused++;
                    lastRefusal = Short(id) + " taken first by " + name;
                    CloseWindows();
                    Tell(string.Format(L.T("{0} is working here."), name.ToString()));
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Work lock: could not read a message from " + sender + ": " + ex.Message);
            }
        }

        private static void CloseWindows()
        {
            try
            {
                if (IsShown<UIAutopsyWindow>()) LazyUI.GetWindow<UIAutopsyWindow>().Close();
                if (IsShown<UIGraveWindow>()) LazyUI.GetWindow<UIGraveWindow>().Close();
            }
            catch (Exception ex)
            {
                log.LogWarning("Work lock: could not close the window: " + ex.Message);
            }
        }

        private static string Short(string id)
        {
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests: interact with the nearest grave or autopsy table by object id, as a key press would.</summary>
        internal static string InteractForTest(string uniqueId)
        {
            Wgo wgo = null;
            foreach (object view in Resources.FindObjectsOfTypeAll(Plugin.FindGameType("Wgo")))
            {
                Wgo candidate = view as Wgo;
                if ((object)candidate != null && candidate.Data != null && candidate.Data.UniqueId.ToString() == uniqueId)
                {
                    wgo = candidate;
                    break;
                }
            }
            if ((object)wgo == null)
            {
                return "no object " + uniqueId;
            }
            IWGOInteractionHandler handler = wgo.InteractionHandler;
            if (!(handler is GraveInteractionHandler) && !(handler is AutopsyInteractionHandler))
            {
                // Not near yet: the handler the game would make for it (as its own UI tests do).
                WGODef.InteractionType type = wgo.Data.Definition == null ? WGODef.InteractionType.None : wgo.Data.Definition.interactionType;
                if (type == WGODef.InteractionType.Grave) handler = new GraveInteractionHandler().Init(wgo);
                else if (type == WGODef.InteractionType.Autopsy) handler = new AutopsyInteractionHandler().Init(wgo);
                else return "not a grave or table: " + wgo.Data.id;
            }
            bool done = handler.Interact(MainGame.PlayerController);
            return "interacted " + handler.GetType().Name + " -> " + done + "; " + Describe();
        }

        /// <summary>Tests: the graves and autopsy tables in the loaded scenes, nearest first ("grave" or "autopsy").</summary>
        internal static string FindForTest(string kind)
        {
            Vector3 from = MainGame.PlayerController == null ? Vector3.zero : ((Component)MainGame.PlayerController).transform.position;
            var found = new List<KeyValuePair<float, string>>();
            foreach (object view in Resources.FindObjectsOfTypeAll(Plugin.FindGameType("Wgo")))
            {
                Wgo wgo = view as Wgo;
                // By the object's kind: the game makes a view's handler only once it is near.
                WGODef definition = (object)wgo == null || wgo.Data == null ? null : wgo.Data.Definition;
                bool match = definition != null && definition.interactionType == (kind == "autopsy" ? WGODef.InteractionType.Autopsy : WGODef.InteractionType.Grave);
                if (match && wgo.Data != null)
                {
                    float distance = Vector3.Distance(from, wgo.Data.Position);
                    found.Add(new KeyValuePair<float, string>(distance, wgo.Data.UniqueId + "=" + wgo.Data.id + "@" + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            if (found.Count == 0)
            {
                // What there is instead, for a test to see why.
                var kinds = new Dictionary<string, int>();
                foreach (object view in Resources.FindObjectsOfTypeAll(Plugin.FindGameType("Wgo")))
                {
                    Wgo wgo = view as Wgo;
                    string name = (object)wgo == null ? "?" : wgo.InteractionHandler == null ? "none" : wgo.InteractionHandler.GetType().Name;
                    kinds[name] = kinds.TryGetValue(name, out int n) ? n + 1 : 1;
                }
                var parts = new List<string>();
                foreach (KeyValuePair<string, int> kindCount in kinds) parts.Add(kindCount.Key + "=" + kindCount.Value);
                return "none; views " + string.Join(",", parts.ToArray());
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            var list = new List<string>();
            foreach (KeyValuePair<float, string> entry in found)
            {
                list.Add(entry.Value);
                if (list.Count >= 6) break;
            }
            return string.Join(";", list.ToArray());
        }

        internal static string CloseForTest()
        {
            CloseWindows();
            return Describe();
        }
    }
}
