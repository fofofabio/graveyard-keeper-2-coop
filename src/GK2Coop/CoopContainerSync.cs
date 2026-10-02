using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Phase 4b: shared container contents.
    ///
    /// A chest that only one player can see the contents of is not a shared world. When either
    /// player changes a container, its whole contents are sent to the peer and applied there.
    ///
    /// A client sends what **changed**, not what the container now holds. Sending whole contents
    /// was last-writer-wins: the host overwrote its own state with a snapshot the client had
    /// taken before the host's edit, so one of the two edits vanished. Four berries plus one from
    /// the host plus two from the client settled on six or seven depending purely on message
    /// order. Applying a delta on top of current state conserves both.
    ///
    /// The delta is computed by comparing the contents before and after the change — a prefix
    /// records them, the postfix diffs — rather than by reading each mutator's arguments, so
    /// every way the game changes an inventory, including <c>Clear</c>, uses one code path.
    ///
    /// The host still broadcasts whole contents as the canonical state. Deltas decide *what
    /// happened*; the snapshot decides *what is true*, and a client that missed something is
    /// corrected by the next one rather than drifting.
    ///
    /// Containers are named by the unique id of the world object that owns them, because that id
    /// matches across machines for scene-authored objects. The inventory's own item id does not:
    /// it is generated per machine. The owner is found through a lazily built reverse map, since
    /// <c>Inventory</c> holds no reference back to its owner.
    ///
    /// "id x count" cannot describe an item with state of its own: a body with its organs and
    /// pockets, a worn tool, anything carrying nested items or item properties. Such an item came
    /// out the other side as a fresh default one. A container holding one ("rich") is therefore
    /// sent whole, as the game's own serialized form of its contents, and the last change wins:
    /// graves, autopsy tables and the like are worked by one player at a time. A change inside a
    /// nested item (an organ taken out of a body lying on a table) is traced back to the world
    /// object holding it and sends that object's contents.
    /// </summary>
    internal static class CoopContainerSync
    {
        internal const string RequestMessage = "GK2Coop.ContainerDelta.v3";
        internal const string StateMessage = "GK2Coop.ContainerState.v3";
        internal const string RichMessage = "GK2Coop.ContainerRich.v1";

        private const string RichMarker = "\u0001rich";
        private const int MaxRichBytes = 512 * 1024;
        private static int richSent;
        private static int richApplied;

        private const int MaxStacks = 40;

        private static ManualLogSource log;
        private static int requestsSent;
        private static int requestsReceived;
        private static int statesSent;
        private static int statesApplied;
        private static int unresolved;
        private static int rejectedDeltas;
        private static int outgoingSequence;
        private static readonly Dictionary<int, int> lastRequestSequencePerSender = new Dictionary<int, int>();
        private static readonly Dictionary<string, int> revisions = new Dictionary<string, int>();
        private static readonly HashSet<string> changedOwners = new HashSet<string>();
        private static readonly Dictionary<string, int> lastAppliedRevision = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> inventoryToOwner = new Dictionary<string, string>();
        private static float mapBuiltAt = -1f;
        private static int detailedLogsLeft = 6;

        internal static bool Enabled { get; set; }

        private static bool applyingRemote;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "container sync: requests sent/received=" + requestsSent + "/" + requestsReceived +
                   ", states sent/applied=" + statesSent + "/" + statesApplied +
                   ", rich sent/applied=" + richSent + "/" + richApplied +
                   ", rejected deltas=" + rejectedDeltas + ", unresolved=" + unresolved;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                Type inventory = Plugin.FindGameType("Inventory");
                if (inventory == null)
                {
                    Enabled = false;
                    return;
                }
                foreach (MethodInfo method in inventory.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    // Clear is included because game logic empties containers directly
                    // (WgoData teardown, quest scripts); without it those emptyings never replicate.
                    // Our own Apply calls Clear under applyingRemote, so it cannot feed back.
                    if (method.Name != "AddItemToInventory" && method.Name != "AddItemsToInventory" && method.Name != "RemoveItemById" &&
                        method.Name != "Clear")
                    {
                        continue;
                    }
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(CoopContainerSync), nameof(ChangingPrefix))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(CoopContainerSync), nameof(ChangedPostfix))));
                }
                log.LogInfo("Patched container changes for sharing.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Could not patch container changes: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ capture

        /// <summary>
        /// Captures what the container held before the change, so the postfix can state exactly
        /// what changed. An earlier version diffed against a cached copy and treated "nothing
        /// cached yet" as "everything is new", which sent the whole contents as a delta and made
        /// the host add seven berries to its own five.
        /// </summary>
        private static void ChangingPrefix(object __instance, ref Dictionary<string, int> __state)
        {
            __state = null;
            if (!Enabled || applyingRemote)
            {
                return;
            }
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || !netcode.IsConnectedClient || netcode.IsHost)
            {
                // Only a client needs a delta; the host publishes whole contents either way.
                return;
            }
            try
            {
                __state = ReadContents(__instance);
                if (IsRich(__instance))
                {
                    __state[RichMarker] = 1;
                }
            }
            catch
            {
                __state = null;
            }
        }

        private static void ChangedPostfix(object __instance, Dictionary<string, int> __state)
        {
            if (!Enabled || applyingRemote)
            {
                return;
            }
            try
            {
                string ownerId = ResolveOwner(__instance);
                if (ownerId == null)
                {
                    // A player's own backpack or tool belt, not a container in the world. A joiner's
                    // own changes are stored by the host soon (not at the next 30 s upload): a game
                    // ending in between lost what was just taken from a chest.
                    CoopPlayerProfiles.OwnInventoryChanged(__instance);
                    return;
                }
                // Noted before the session check: a host that fills a chest before anyone
                // connects must still include it in a later joiner's snapshot.
                changedOwners.Add(ownerId);

                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode == null || !netcode.IsListening || !netcode.IsConnectedClient)
                {
                    return;
                }

                bool wasRich = __state != null && __state.Remove(RichMarker);
                object container = ContainerOf(ownerId);
                if (container == null)
                {
                    return;
                }
                // A nested inventory (a body's organs) belongs to an item inside the container.
                if (wasRich || !ReferenceEquals(container, __instance) || IsRich(container))
                {
                    if (netcode.IsHost)
                    {
                        PublishRich(netcode, ownerId, container, null);
                    }
                    else
                    {
                        SendRich(netcode, NetworkManager.ServerClientId, ownerId, container, 0);
                    }
                    return;
                }
                // A big chest (more stacks or a longer list than "id x count" carries) went
                // unshared, both ways: the host sends it whole, a joiner still sends what changed.
                if (netcode.IsHost)
                {
                    PublishContents(netcode, ownerId, __instance);
                }
                else if (__state != null)
                {
                    string delta = BuildDelta(__state, __instance);
                    if (delta == null)
                    {
                        SendRich(netcode, NetworkManager.ServerClientId, ownerId, container, 0);
                    }
                    else if (delta.Length > 0)
                    {
                        SendDelta(netcode, ownerId, delta);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not report a container change: " + ex.Message);
            }
        }

        /// <summary>"itemId xCount" per stack, separated by semicolons.</summary>
        private static string BuildSnapshot(object inventory)
        {
            var builder = new StringBuilder();
            int stacks = 0;
            foreach (object item in EnumerateItems(inventory))
            {
                if (stacks++ >= MaxStacks)
                {
                    return null;
                }
                if (builder.Length > 0)
                {
                    builder.Append(';');
                }
                builder.Append(Convert.ToString(CoopDiagnostics.GetMember(item, "id")))
                       .Append('x')
                       .Append(Convert.ToString(CoopDiagnostics.GetMember(item, "Count")));
            }
            return builder.Length <= 480 ? builder.ToString() : null;
        }

        private static Dictionary<string, int> ReadContents(object inventory)
        {
            var contents = new Dictionary<string, int>();
            foreach (object item in EnumerateItems(inventory))
            {
                string id = Convert.ToString(CoopDiagnostics.GetMember(item, "id"));
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                int count;
                contents.TryGetValue(id, out count);
                contents[id] = count + Convert.ToInt32(CoopDiagnostics.GetMember(item, "Count"));
            }
            return contents;
        }

        /// <summary>
        /// "itemId:signedChange" per changed item id, separated by semicolons. Empty when nothing
        /// actually changed, which happens when a mutator reports a change it did not make.
        /// </summary>
        private static string BuildDelta(Dictionary<string, int> previous, object inventory)
        {
            Dictionary<string, int> current = ReadContents(inventory);
            var builder = new StringBuilder();
            var ids = new HashSet<string>(current.Keys);
            ids.UnionWith(previous.Keys);
            foreach (string id in ids)
            {
                int now;
                int before;
                current.TryGetValue(id, out now);
                previous.TryGetValue(id, out before);
                if (now == before)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(';');
                }
                builder.Append(id).Append(':').Append(now - before);
            }
            return builder.Length <= 480 ? builder.ToString() : null;
        }

        private static IEnumerable EnumerateItems(object inventory)
        {
            object data = CoopDiagnostics.GetMember(inventory, "Data");
            var items = data == null ? null : CoopDiagnostics.GetMember(data, "Inventory") as IEnumerable;
            return items ?? new object[0];
        }

        // ------------------------------------------------------------------ host ordering and apply

        private static void SendDelta(NetworkManager netcode, string ownerId, string delta)
        {
            using (var writer = new FastBufferWriter(1024, Allocator.Temp))
            {
                writer.WriteValueSafe(++outgoingSequence);
                writer.WriteValueSafe(new FixedString128Bytes(ownerId));
                writer.WriteValueSafe(new FixedString512Bytes(delta));
                netcode.CustomMessagingManager.SendNamedMessage(
                    RequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
            requestsSent++;
        }

        internal static void ReceiveRequest(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || !netcode.IsHost || senderClientId == netcode.LocalClientId)
            {
                return;
            }
            try
            {
                int sequence;
                FixedString128Bytes ownerId;
                FixedString512Bytes snapshot;
                reader.ReadValueSafe(out sequence);
                reader.ReadValueSafe(out ownerId);
                reader.ReadValueSafe(out snapshot);

                int last;
                int sender = checked((int)senderClientId);
                if (lastRequestSequencePerSender.TryGetValue(sender, out last) && sequence <= last)
                {
                    return;
                }
                lastRequestSequencePerSender[sender] = sequence;
                string owner = ownerId.ToString();
                requestsReceived++;
                // Applied on top of whatever the host holds now, so a concurrent host edit is
                // kept rather than overwritten by the client's view of the container.
                bool applied = ApplyDelta(owner, snapshot.ToString());

                // Published whether or not it applied. The client has *already* made the change
                // locally, so a delta the host rejects — taking items that are no longer there —
                // would otherwise leave the two permanently different, with nothing to correct
                // it. Sending the host's truth back is what makes a rejection safe.
                object inventory = ContainerOf(owner);
                if (inventory != null)
                {
                    PublishContents(netcode, owner, inventory);
                }
                if (!applied)
                {
                    rejectedDeltas++;
                    log.LogInfo("Container " + Shorten(owner) + " rejected a delta from client " +
                                senderClientId + "; sent the host's contents back to correct it.");
                }
            }
            catch (Exception ex)
            {
                unresolved++;
                log.LogWarning("Could not read a container request: " + ex.Message);
            }
        }

        /// <summary>Host: a container's contents to everyone, short when "id x count" carries them, else whole.</summary>
        private static void PublishContents(NetworkManager netcode, string ownerId, object inventory)
        {
            string snapshot = IsRich(inventory) ? null : BuildSnapshot(inventory);
            if (snapshot != null)
            {
                PublishCanonical(netcode, ownerId, snapshot);
            }
            else
            {
                PublishRich(netcode, ownerId, inventory, null);
            }
        }

        private static void PublishCanonical(NetworkManager netcode, string ownerId, string snapshot)
        {
            int revision;
            revisions.TryGetValue(ownerId, out revision);
            revision++;
            revisions[ownerId] = revision;
            using (var writer = new FastBufferWriter(1024, Allocator.Temp))
            {
                writer.WriteValueSafe(revision);
                writer.WriteValueSafe(new FixedString128Bytes(ownerId));
                writer.WriteValueSafe(new FixedString512Bytes(snapshot));
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId)
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(
                            StateMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
            }
            statesSent++;
        }

        /// <summary>
        /// Sends the current contents of every container this session has changed. The revision
        /// numbers are the ones already issued, so a joining client's later stale-revision check
        /// behaves exactly as it does for live updates.
        /// </summary>
        internal static int SendSnapshotTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || !netcode.IsHost)
            {
                return 0;
            }
            int count = 0;
            foreach (string ownerId in changedOwners)
            {
                object inventory = ContainerOf(ownerId);
                if (inventory != null && IsRich(inventory))
                {
                    int richRevision;
                    revisions.TryGetValue(ownerId, out richRevision);
                    revisions[ownerId] = ++richRevision;
                    SendRich(netcode, clientId, ownerId, inventory, richRevision);
                    count++;
                    continue;
                }
                if (inventory == null)
                {
                    continue;
                }
                string snapshot = BuildSnapshot(inventory);
                if (snapshot == null)
                {
                    // Too big for "id x count": whole.
                    int bigRevision;
                    revisions.TryGetValue(ownerId, out bigRevision);
                    revisions[ownerId] = ++bigRevision;
                    SendRich(netcode, clientId, ownerId, inventory, bigRevision);
                    count++;
                    continue;
                }
                // A container changed before any peer connected has no revision yet; issuing one
                // here keeps the client's stale-revision check working exactly as it does live.
                int revision;
                revisions.TryGetValue(ownerId, out revision);
                revision++;
                revisions[ownerId] = revision;
                using (var writer = new FastBufferWriter(1024, Allocator.Temp))
                {
                    writer.WriteValueSafe(revision);
                    writer.WriteValueSafe(new FixedString128Bytes(ownerId));
                    writer.WriteValueSafe(new FixedString512Bytes(snapshot));
                    netcode.CustomMessagingManager.SendNamedMessage(
                        StateMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                }
                statesSent++;
                count++;
            }
            return count;
        }

        internal static void ReceiveState(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || netcode.IsHost || senderClientId != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                int revision;
                FixedString128Bytes ownerId;
                FixedString512Bytes snapshot;
                reader.ReadValueSafe(out revision);
                reader.ReadValueSafe(out ownerId);
                reader.ReadValueSafe(out snapshot);
                string owner = ownerId.ToString();
                int last;
                if (lastAppliedRevision.TryGetValue(owner, out last) && revision <= last)
                {
                    return;
                }
                if (Apply(owner, snapshot.ToString()))
                {
                    lastAppliedRevision[owner] = revision;
                    statesApplied++;
                }
            }
            catch (Exception ex)
            {
                unresolved++;
                log.LogWarning("Could not read a canonical container state: " + ex.Message);
            }
        }

        /// <summary>
        /// Adds or removes exactly what changed on the sender, leaving everything else alone.
        /// This is what makes two simultaneous edits conserve both rather than one winning.
        /// </summary>
        private static bool ApplyDelta(string ownerId, string delta)
        {
            object wgoData = ResolveWgoData(ownerId);
            object inventory = wgoData == null ? null : CoopDiagnostics.GetMember(wgoData, "Inventory");
            if (inventory == null)
            {
                unresolved++;
                return false;
            }
            if (string.IsNullOrEmpty(delta))
            {
                return false;
            }

            applyingRemote = true;
            try
            {
                Type itemType = Plugin.FindGameType("Item");
                MethodInfo add = AccessTools.Method(inventory.GetType(), "AddItemToInventory",
                    new[] { itemType, itemType, typeof(bool) });
                MethodInfo remove = null;
                foreach (MethodInfo method in inventory.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name == "RemoveItemById" && method.GetParameters().Length == 5)
                    {
                        remove = method;
                        break;
                    }
                }
                if (add == null || remove == null)
                {
                    throw new MissingMethodException("Inventory.AddItemToInventory or RemoveItemById");
                }

                foreach (string entry in delta.Split(';'))
                {
                    int split = entry.LastIndexOf(':');
                    int change;
                    if (split <= 0 || !int.TryParse(entry.Substring(split + 1), out change) || change == 0)
                    {
                        unresolved++;
                        log.LogWarning("Rejected malformed container delta entry '" + entry + "' for " + Shorten(ownerId) + ".");
                        return false;
                    }
                    string itemId = entry.Substring(0, split);
                    if (change > 0)
                    {
                        object item = Activator.CreateInstance(itemType, itemId, change);
                        if (!Convert.ToBoolean(add.Invoke(inventory, new[] { item, null, (object)false })))
                        {
                            unresolved++;
                            log.LogWarning("Container " + Shorten(ownerId) + " could not accept " + itemId + " x" + change + ".");
                            return false;
                        }
                    }
                    else
                    {
                        remove.Invoke(inventory, new object[] { itemId, -change, null, null, false });
                    }
                }
                if (detailedLogsLeft-- > 0)
                {
                    log.LogInfo("Applied container delta for " + Shorten(ownerId) + ": [" + delta + "]");
                }
                return true;
            }
            catch (Exception ex)
            {
                unresolved++;
                log.LogWarning("Could not apply a container delta: " + ex.Message);
                return false;
            }
            finally
            {
                applyingRemote = false;
            }
        }

        private static bool Apply(string ownerId, string snapshot)
        {
            List<SnapshotStack> stacks;
            if (!TryParseSnapshot(snapshot, out stacks))
            {
                unresolved++;
                log.LogWarning("Rejected malformed container snapshot for " + Shorten(ownerId) + ".");
                return false;
            }
            object wgoData = ResolveWgoData(ownerId);
            object inventory = wgoData == null ? null : CoopDiagnostics.GetMember(wgoData, "Inventory");
            if (inventory == null)
            {
                unresolved++;
                return false;
            }

            applyingRemote = true;
            try
            {
                MethodInfo clear = AccessTools.Method(inventory.GetType(), "Clear");
                Type itemType = Plugin.FindGameType("Item");
                MethodInfo add = AccessTools.Method(inventory.GetType(), "AddItemToInventory",
                    new[] { itemType, itemType, typeof(bool) });
                if (clear == null || add == null)
                {
                    throw new MissingMethodException("Inventory.Clear or AddItemToInventory");
                }
                clear.Invoke(inventory, null);
                foreach (SnapshotStack stack in stacks)
                {
                    object item = Activator.CreateInstance(itemType, stack.ItemId, stack.Count);
                    if (!Convert.ToBoolean(add.Invoke(inventory, new[] { item, null, (object)false })))
                    {
                        throw new InvalidOperationException("Container cannot accept " + stack.ItemId + " x" + stack.Count);
                    }
                }
                if (detailedLogsLeft-- > 0)
                {
                    log.LogInfo("Applied canonical container state for " + Shorten(ownerId) + ": [" + snapshot + "]");
                }
                return true;
            }
            catch (Exception ex)
            {
                unresolved++;
                log.LogWarning("Could not apply a container state: " + ex.Message);
                return false;
            }
            finally
            {
                applyingRemote = false;
            }
        }

        private static bool TryParseSnapshot(string snapshot, out List<SnapshotStack> stacks)
        {
            stacks = new List<SnapshotStack>();
            if (snapshot == null || snapshot.Length > 480)
            {
                return false;
            }
            if (snapshot.Length == 0)
            {
                return true;
            }
            foreach (string raw in snapshot.Split(';'))
            {
                int split = raw.LastIndexOf('x');
                int count;
                if (split <= 0 || split >= raw.Length - 1 || raw.IndexOf(';') >= 0 ||
                    !int.TryParse(raw.Substring(split + 1), out count) || count <= 0 ||
                    raw.Length > 128 || stacks.Count >= MaxStacks)
                {
                    return false;
                }
                stacks.Add(new SnapshotStack(raw.Substring(0, split), count));
            }
            return true;
        }

        private sealed class SnapshotStack
        {
            internal readonly string ItemId;
            internal readonly int Count;

            internal SnapshotStack(string itemId, int count)
            {
                ItemId = itemId;
                Count = count;
            }
        }

        /// <summary>
        /// Host: sends an object's contents to everyone now. Used when both machines produced
        /// contents on their own — a garden that finished growing rolls its harvest on each
        /// machine — so the host's roll becomes everyone's.
        /// </summary>
        internal static void PublishNow(string ownerId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || !netcode.IsHost || string.IsNullOrEmpty(ownerId))
            {
                return;
            }
            try
            {
                object inventory = ContainerOf(ownerId);
                if (inventory == null)
                {
                    return;
                }
                changedOwners.Add(ownerId);
                if (IsRich(inventory) || BuildSnapshot(inventory) == null)
                {
                    PublishRich(netcode, ownerId, inventory, null);
                    return;
                }
                string snapshot = BuildSnapshot(inventory);
                if (snapshot != null)
                {
                    PublishCanonical(netcode, ownerId, snapshot);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not publish container " + Shorten(ownerId) + ": " + Inner(ex).Message);
            }
        }

        // ------------------------------------------------------------------ rich containers

        private static bool IsRich(object inventory)
        {
            foreach (object item in EnumerateItems(inventory))
            {
                if (IsRichItem(item))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>An item whose state "id x count" cannot carry: nested items or item properties.</summary>
        private static bool IsRichItem(object item)
        {
            if (item == null)
            {
                return false;
            }
            if (AccessTools.Field(item.GetType(), "inventory")?.GetValue(item) is ICollection nested && nested.Count > 0)
            {
                return true;
            }
            return AccessTools.Field(item.GetType(), "properties")?.GetValue(item) is ICollection properties && properties.Count > 0;
        }

        private static object ContainerOf(string ownerId)
        {
            object wgoData = ResolveWgoData(ownerId);
            return wgoData == null ? null : CoopDiagnostics.GetMember(wgoData, "Inventory");
        }

        /// <summary>Host: sends a container's whole contents to every client except <paramref name="except"/>.</summary>
        private static void PublishRich(NetworkManager netcode, string ownerId, object inventory, ulong? except)
        {
            int revision;
            revisions.TryGetValue(ownerId, out revision);
            revisions[ownerId] = ++revision;
            foreach (ulong clientId in netcode.ConnectedClientsIds)
            {
                if (clientId != netcode.LocalClientId && clientId != except)
                {
                    SendRich(netcode, clientId, ownerId, inventory, revision);
                }
            }
        }

        private static void SendRich(NetworkManager netcode, ulong target, string ownerId, object inventory, int revision)
        {
            object data = CoopDiagnostics.GetMember(inventory, "Data");
            byte[] compressed = Compress(CoopGameSerializer.Serialize(data));
            if (compressed.Length > MaxRichBytes)
            {
                unresolved++;
                log.LogWarning("Container " + Shorten(ownerId) + " is too large to share (" + compressed.Length + " bytes).");
                return;
            }
            using (var writer = new FastBufferWriter(160 + compressed.Length, Allocator.Temp))
            {
                writer.WriteValueSafe(revision);
                writer.WriteValueSafe(new FixedString128Bytes(ownerId));
                writer.WriteValueSafe(compressed.Length);
                writer.WriteBytesSafe(compressed, compressed.Length);
                NetworkDelivery delivery = compressed.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                netcode.CustomMessagingManager.SendNamedMessage(RichMessage, target, writer, delivery);
            }
            richSent++;
        }

        internal static void ReceiveRich(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int revision);
                reader.ReadValueSafe(out FixedString128Bytes ownerText);
                reader.ReadValueSafe(out int length);
                if (length <= 0 || length > MaxRichBytes)
                {
                    return;
                }
                byte[] compressed = new byte[length];
                reader.ReadBytesSafe(ref compressed, length);
                string owner = ownerText.ToString();
                if (netcode.IsHost)
                {
                    if (sender == netcode.LocalClientId)
                    {
                        return;
                    }
                    object container = ContainerOf(owner);
                    if (container != null && ApplyRich(owner, container, Decompress(compressed)))
                    {
                        changedOwners.Add(owner);
                        PublishRich(netcode, owner, container, sender);
                    }
                    return;
                }
                if (sender != NetworkManager.ServerClientId)
                {
                    return;
                }
                if (lastAppliedRevision.TryGetValue(owner, out int last) && revision <= last)
                {
                    return;
                }
                object inventory = ContainerOf(owner);
                if (inventory != null && ApplyRich(owner, inventory, Decompress(compressed)))
                {
                    lastAppliedRevision[owner] = revision;
                }
            }
            catch (Exception ex)
            {
                unresolved++;
                log.LogWarning("Could not apply a whole container from " + sender + ": " + Inner(ex).Message);
            }
        }

        /// <summary>
        /// Swaps in the received items, keeping the container's own item (and so its id, which the
        /// owner map relies on), then tells the game's listeners what left and what arrived.
        /// </summary>
        private static bool ApplyRich(string ownerId, object inventory, byte[] raw)
        {
            object data = CoopDiagnostics.GetMember(inventory, "Data");
            FieldInfo itemsField = data == null ? null : AccessTools.Field(data.GetType(), "inventory");
            if (itemsField == null)
            {
                unresolved++;
                return false;
            }
            object received = CoopGameSerializer.Deserialize(data.GetType(), raw);
            object oldItems = itemsField.GetValue(data);
            object newItems = itemsField.GetValue(received);
            applyingRemote = true;
            try
            {
                itemsField.SetValue(data, newItems);
                inventory.GetType().GetMethod("NotifyItemsRemoved")?.Invoke(inventory, new[] { oldItems });
                inventory.GetType().GetMethod("NotifyItemsAdded")?.Invoke(inventory, new[] { newItems });
            }
            finally
            {
                applyingRemote = false;
            }
            richApplied++;
            mapBuiltAt = -1f;
            if (detailedLogsLeft-- > 0)
            {
                log.LogInfo("Applied whole container " + Shorten(ownerId) + ": [" + BuildSnapshot(inventory) + "]");
            }
            return true;
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

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }

        // ------------------------------------------------------------------ identity

        /// <summary>
        /// Maps an inventory back to the world object that owns it. Rebuilt periodically rather
        /// than cached forever, because objects are created and destroyed during play.
        /// </summary>
        private static string ResolveOwner(object inventory)
        {
            object data = CoopDiagnostics.GetMember(inventory, "Data");
            object guid = data == null ? null : CoopDiagnostics.GetMember(data, "UniqueId");
            object id = guid == null ? null : CoopDiagnostics.GetMember(guid, "Id");
            if (id == null)
            {
                return null;
            }
            string inventoryId = id.ToString();

            string owner;
            if (inventoryToOwner.TryGetValue(inventoryId, out owner))
            {
                return owner;
            }
            // Inventories once found to be nobody's in the world (a backpack, a tool belt) are not
            // looked up again. Any other unknown one is: until 0.65.3 the map was rebuilt at most
            // every 5 seconds, so a chest built and filled within that time was taken for a
            // backpack and its first contents never reached the other players (found by `mix`).
            if (notWorldOwned.Contains(inventoryId))
            {
                return null;
            }
            BuildOwnerMap();
            if (inventoryToOwner.TryGetValue(inventoryId, out owner))
            {
                return owner;
            }
            notWorldOwned.Add(inventoryId);
            return null;
        }

        private static readonly HashSet<string> notWorldOwned = new HashSet<string>();

        /// <summary>
        /// At the end of a session, either side. Revision numbers start afresh with the next host:
        /// a joiner who kept the last session's (a host who restarted their game numbers from 1
        /// again) refused every chest update of the new session as stale.
        /// </summary>
        internal static void ResetSession()
        {
            revisions.Clear();
            changedOwners.Clear();
            lastAppliedRevision.Clear();
            notWorldOwned.Clear();
            inventoryToOwner.Clear();
        }

        private static void BuildOwnerMap()
        {
            mapBuiltAt = Time.unscaledTime;
            inventoryToOwner.Clear();
            object worldData = GetWorldData();
            var scenes = worldData == null ? null : CoopDiagnostics.GetMember(worldData, "gameSceneDataList") as IEnumerable;
            if (scenes == null)
            {
                return;
            }
            foreach (object scene in scenes)
            {
                var wgos = CoopDiagnostics.GetMember(scene, "wgoDataList") as IEnumerable;
                if (wgos == null)
                {
                    continue;
                }
                foreach (object wgo in wgos)
                {
                    object ownerGuid = CoopDiagnostics.GetMember(wgo, "UniqueId");
                    object ownerId = ownerGuid == null ? null : CoopDiagnostics.GetMember(ownerGuid, "Id");
                    object inventory = CoopDiagnostics.GetMember(wgo, "Inventory");
                    object data = inventory == null ? null : CoopDiagnostics.GetMember(inventory, "Data");
                    object invGuid = data == null ? null : CoopDiagnostics.GetMember(data, "UniqueId");
                    object invId = invGuid == null ? null : CoopDiagnostics.GetMember(invGuid, "Id");
                    if (ownerId != null && invId != null)
                    {
                        inventoryToOwner[invId.ToString()] = ownerId.ToString();
                        MapNested(data, ownerId.ToString(), 0);
                    }
                }
            }
        }

        /// <summary>Items inside a container that hold items themselves (a body's organs) map to the container's owner.</summary>
        private static void MapNested(object item, string ownerId, int depth)
        {
            if (depth > 3 || !(AccessTools.Field(item.GetType(), "inventory")?.GetValue(item) is IEnumerable children))
            {
                return;
            }
            foreach (object child in children)
            {
                if (child == null || !(AccessTools.Field(child.GetType(), "inventory")?.GetValue(child) is ICollection nested) || nested.Count == 0)
                {
                    continue;
                }
                object guid = CoopDiagnostics.GetMember(child, "UniqueId");
                object id = guid == null ? null : CoopDiagnostics.GetMember(guid, "Id");
                if (id != null)
                {
                    inventoryToOwner[id.ToString()] = ownerId;
                }
                MapNested(child, ownerId, depth + 1);
            }
        }

        private static object ResolveWgoData(string uniqueId)
        {
            object worldData = GetWorldData();
            Type sguidType = Plugin.FindGameType("SGuid");
            MethodInfo parse = sguidType == null ? null : AccessTools.Method(sguidType, "Parse");
            if (worldData == null || parse == null)
            {
                return null;
            }
            MethodInfo getWgo = AccessTools.Method(worldData.GetType(), "GetWgoData", new[] { sguidType });
            return getWgo == null ? null : getWgo.Invoke(worldData, new[] { parse.Invoke(null, new object[] { uniqueId }) });
        }

        private static object GetWorldData()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "worldData");
        }

        private static string Shorten(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }
    }
}
