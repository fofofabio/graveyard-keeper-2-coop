using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// What a player carries on the head (a body, a zombie, a crate), seen by everyone, and given
    /// to the player it belongs to.
    ///
    /// Each machine sends the item ids its own player carries when they change; the others draw
    /// them on that player's body with the game's own overhead view (<c>PlayerAnimation.SetOverheadItems</c>),
    /// as the game draws the local player's. Before, nobody saw what another player carried.
    ///
    /// The host runs some of a joiner's actions as that joiner (<see cref="CoopPlayerContext"/>: a
    /// joiner's work at a station). The game puts a finished big item on "the player's" head and
    /// draws it on <c>MainGame.PlayerController</c>, which stays the host's: the host carried a
    /// crate it did not have, for good, and the joiner never got it; a full head dropped the rest
    /// at the joiner's spot on the host only (Workshop report, 5 October 2026: iron boxes at the
    /// workbench, one on the host's head). Now such an item goes to the joiner whole, and nothing
    /// is drawn on the host for it.
    /// </summary>
    internal static class CoopOverheadSync
    {
        internal const string StateMessage = "GK2Coop.Overhead.v1";
        internal const string GiveMessage = "GK2Coop.Overhead.Give.v1";

        private static ManualLogSource log;
        private static readonly Dictionary<ulong, string[]> carried = new Dictionary<ulong, string[]>();
        private static readonly Dictionary<int, string> drawn = new Dictionary<int, string>();
        private static string lastSentLocal;
        private static int lastPeerCount = -1;
        private static float nextTick;
        private static int statesSent;
        private static int drawnCount;
        private static int given;
        private static int received;

        internal static bool Enabled { get; set; } = true;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "overhead: states sent=" + statesSent + ", drawn=" + drawnCount + ", given to joiners=" + given + ", received=" + received +
                   ", carried by others=" + carried.Count;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                harmony.Patch(AccessTools.Method(typeof(PlayerData), "RefreshOverheadVisuals"),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(CoopOverheadSync), nameof(RefreshVisualsPrefix))));
                harmony.Patch(AccessTools.Method(typeof(PlayerData), nameof(PlayerData.AddOverheadItem)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(CoopOverheadSync), nameof(AddedPostfix))));
                log.LogInfo("Overhead sync: carried items are shown on every player.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Overhead sync not installed: " + Plugin.Unwrap(ex).Message);
            }
        }

        internal static void ResetSession()
        {
            carried.Clear();
            drawn.Clear();
            lastSentLocal = null;
            lastPeerCount = -1;
        }

        // ------------------------------------------------------------------ the host acting as a joiner

        /// <summary>Another player's head is not drawn on this machine's own player.</summary>
        private static bool RefreshVisualsPrefix()
        {
            return !CoopPlayerContext.IsActive;
        }

        private static void AddedPostfix(PlayerData __instance, Item item)
        {
            if (!CoopPlayerContext.IsActive || item == null || item.IsEmpty)
            {
                return;
            }
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || !netcode.IsHost || !CoopHud.TryResolveClientId(__instance, out ulong clientId) ||
                clientId == netcode.LocalClientId)
            {
                return;
            }
            try
            {
                byte[] whole = CoopDropSync.WholeItem(item, true);
                byte[] zombie = CoopDropSync.ZombieOf(item);
                if (whole == null)
                {
                    return;
                }
                using (var writer = new FastBufferWriter(256 + whole.Length + (zombie?.Length ?? 0), Allocator.Temp))
                {
                    writer.WriteValueSafe(whole.Length);
                    writer.WriteBytesSafe(whole, whole.Length);
                    writer.WriteValueSafe(zombie == null ? 0 : zombie.Length);
                    if (zombie != null)
                    {
                        writer.WriteBytesSafe(zombie, zombie.Length);
                    }
                    netcode.CustomMessagingManager.SendNamedMessage(GiveMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
                // The joiner carries it now; the host's copy of that player follows from the joiner.
                __instance.RemoveOverheadItem(item);
                given++;
                log.LogInfo("Overhead sync: " + item.id + " went onto the head of " + CoopSession.NameFor(clientId) + " (made by the host for them).");
            }
            catch (Exception ex)
            {
                log.LogWarning("Overhead sync: could not give " + item.id + " to " + CoopSession.NameFor(clientId) + ": " + Plugin.Unwrap(ex).Message);
            }
        }

        internal static void ReceiveGive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || netcode.IsHost || sender != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int length);
                if (length <= 0 || length > 4 * 1024 * 1024)
                {
                    return;
                }
                byte[] whole = new byte[length];
                reader.ReadBytesSafe(ref whole, length);
                reader.ReadValueSafe(out int zombieLength);
                byte[] zombie = null;
                if (zombieLength > 0 && zombieLength < 4 * 1024 * 1024)
                {
                    zombie = new byte[zombieLength];
                    reader.ReadBytesSafe(ref zombie, zombieLength);
                }
                if (!(CoopGameSerializer.Deserialize(typeof(Item), CoopDropSync.Decompress(whole)) is Item item) || MainGame.PlayerData == null)
                {
                    return;
                }
                if (zombie != null)
                {
                    CoopDropSync.AddZombie(CoopDropSync.ReadItemGuid(item), zombie);
                }
                MainGame.PlayerData.AddOverheadItem(item);
                received++;
                log.LogInfo("Overhead sync: the host put " + item.id + " on this player's head.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Overhead sync: could not take an item from the host: " + Plugin.Unwrap(ex).Message);
            }
        }

        // ------------------------------------------------------------------ what everyone carries

        /// <summary>From the plugin's update; does its work four times a second.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                if (carried.Count > 0 || lastSentLocal != null)
                {
                    ResetSession();
                }
                return;
            }
            if (Time.unscaledTime < nextTick || CoopPlayerContext.IsActive)
            {
                return;
            }
            nextTick = Time.unscaledTime + 0.25f;
            if (!netcode.IsHost && !(netcode.IsConnectedClient && CoopSession.Welcomed))
            {
                return;
            }
            try
            {
                PlayerData local = MainGame.PlayerData;
                if (local == null)
                {
                    return;
                }
                string[] mine = Describe(local.OverheadItems);
                string signature = string.Join(";", mine);
                int peers = netcode.IsHost ? netcode.ConnectedClientsIds.Count : 2;
                bool peersChanged = peers != lastPeerCount;
                lastPeerCount = peers;
                if (signature != lastSentLocal || peersChanged)
                {
                    lastSentLocal = signature;
                    Send(netcode, netcode.LocalClientId, mine, null);
                    if (netcode.IsHost && peersChanged)
                    {
                        foreach (KeyValuePair<ulong, string[]> known in carried)
                        {
                            Send(netcode, known.Key, known.Value, known.Key);
                        }
                    }
                }
                Draw(local);
            }
            catch (Exception ex)
            {
                log.LogWarning("Overhead sync: " + Plugin.Unwrap(ex).Message);
                nextTick = Time.unscaledTime + 5f;
            }
        }

        private static string[] Describe(IReadOnlyList<Item> items)
        {
            if (items == null || items.Count == 0)
            {
                return new string[0];
            }
            var list = new List<string>(items.Count);
            foreach (Item item in items)
            {
                if (item != null && !item.IsEmpty)
                {
                    list.Add(item.id + "|" + (CoopDropSync.ReadItemGuid(item) ?? string.Empty));
                }
            }
            return list.ToArray();
        }

        private static void Send(NetworkManager netcode, ulong origin, string[] items, ulong? except)
        {
            using (var writer = new FastBufferWriter(64 + items.Length * 200, Allocator.Temp))
            {
                writer.WriteValueSafe(origin);
                writer.WriteValueSafe(items.Length);
                foreach (string entry in items)
                {
                    writer.WriteValueSafe(new FixedString128Bytes(entry));
                }
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except && clientId != origin)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(StateMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(StateMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
            statesSent++;
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
                reader.ReadValueSafe(out int count);
                if (count < 0 || count > 32)
                {
                    return;
                }
                var items = new string[count];
                for (int i = 0; i < count; i++)
                {
                    reader.ReadValueSafe(out FixedString128Bytes entry);
                    items[i] = entry.ToString();
                }
                if (netcode.IsHost)
                {
                    // A joiner speaks for itself only.
                    origin = sender;
                    Send(netcode, origin, items, sender);
                }
                if (origin == netcode.LocalClientId)
                {
                    return;
                }
                carried[origin] = items;
                nextTick = 0f;
            }
            catch (Exception ex)
            {
                log.LogWarning("Overhead sync: could not read what " + sender + " carries: " + Plugin.Unwrap(ex).Message);
            }
        }

        private static void Draw(PlayerData local)
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
                carried.TryGetValue(clientId, out string[] items);
                string signature = items == null ? string.Empty : string.Join(";", items);
                int key = body.GetInstanceID();
                if (drawn.TryGetValue(key, out string previous) ? previous == signature : signature.Length == 0)
                {
                    continue;
                }
                drawn[key] = signature;
                if (!(CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(body, "playerView"), "PlayerAnimation") is PlayerAnimation animation) || animation == null)
                {
                    continue;
                }
                try
                {
                    if (items == null || items.Length == 0)
                    {
                        animation.SetLayerWeight(3, 0f);
                        animation.RemoveOverheadItem();
                        continue;
                    }
                    var shown = new List<Item>(items.Length);
                    foreach (string entry in items)
                    {
                        int bar = entry.IndexOf('|');
                        var item = new Item(bar < 0 ? entry : entry.Substring(0, bar), 1);
                        if (bar >= 0 && bar < entry.Length - 1)
                        {
                            // The zombie a carried zombie item stands for is known by this id.
                            CoopDropSync.ForceItemGuid(item, entry.Substring(bar + 1));
                        }
                        shown.Add(item);
                    }
                    animation.SetLayerWeight(3, 1f);
                    animation.SetOverheadItems(shown);
                    drawnCount++;
                    log.LogInfo("Overhead sync: " + CoopSession.NameFor(clientId) + " carries " + ItemIds(items) + ".");
                }
                catch (Exception ex)
                {
                    log.LogWarning("Overhead sync: could not draw what " + CoopSession.NameFor(clientId) + " carries: " + Plugin.Unwrap(ex).Message);
                }
            }
        }

        private static string ItemIds(string[] items)
        {
            var text = new StringBuilder();
            foreach (string entry in items)
            {
                int bar = entry.IndexOf('|');
                text.Append(text.Length == 0 ? string.Empty : ", ").Append(bar < 0 ? entry : entry.Substring(0, bar));
            }
            return text.ToString();
        }
    }
}
