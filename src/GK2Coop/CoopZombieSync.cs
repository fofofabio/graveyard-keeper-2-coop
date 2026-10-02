using System;
using System.Collections;
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
    /// Zombies are the host's: one set of workers, run in one place.
    ///
    /// A zombie placed into the world (<c>ZombieSystemData.PutZombieFromStoreToGameScene</c>)
    /// existed only on the machine where it was placed, and each machine ran its own zombies'
    /// work. Zombie work moves things — porters carry items between chests, gardeners tend beds,
    /// crafters run stations — so two copies of the same zombie working would do everything twice.
    ///
    /// Placing and picking up are shared: a placement sends the zombie whole (game serializer)
    /// and the others place it through the same game call; a pickup takes it off everyone's
    /// scene. The zombies' own update (<c>ZombieSystem</c>, <c>ZombiePorterSystem</c>) runs only on
    /// the host. Joiners show the host's zombies: the host sends each zombie's position, facing
    /// and animation a few times a second, and joiners apply them to their copies.
    /// </summary>
    internal static class CoopZombieSync
    {
        internal const string PresenceMessage = "GK2Coop.Zombie.Presence.v1";
        internal const string MotionMessage = "GK2Coop.Zombie.Motion.v1";

        private const byte KindPlace = 0;
        private const byte KindPickUp = 1;
        private const byte KindAttach = 2;
        private const float MotionInterval = 0.25f;

        private sealed class Motion
        {
            internal Vector3 Position;
            internal Vector2 Direction;
            internal int Anim;
        }

        private static ManualLogSource log;
        private static readonly Dictionary<string, Motion> lastSent = new Dictionary<string, Motion>();
        private static bool applying;
        private static int placeDepth;
        private static float nextMotion;
        private static int placements;
        private static int pickups;
        private static int motionsApplied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "zombie sync: placements=" + placements + ", pickups=" + pickups + ", motions applied=" + motionsApplied;
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
                Type systemData = Plugin.FindGameType("ZombieSystemData");
                MethodInfo place = null;
                foreach (MethodInfo method in systemData.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name == "PutZombieFromStoreToGameScene" && method.GetParameters().Length == 4)
                    {
                        place = method;
                    }
                }
                MethodInfo pickUp = AccessTools.Method(systemData, "PutZombieFromGameSceneToStore", new[] { Plugin.FindGameType("ZombieWgoData") });
                MethodInfo zombieUpdate = AccessTools.Method(Plugin.FindGameType("ZombieSystem"), "CustomUpdate");
                MethodInfo porterUpdate = AccessTools.Method(Plugin.FindGameType("ZombiePorterSystem"), "CustomUpdate");
                if (place == null || pickUp == null || zombieUpdate == null || porterUpdate == null)
                {
                    log.LogWarning("Zombie sync: zombie system methods not found; zombies stay local.");
                    Enabled = false;
                    return;
                }
                BindingFlags own = BindingFlags.Static | BindingFlags.NonPublic;
                // Every way a zombie goes from the store into the world: as itself, and converted
                // into a common worker, gardener, assistant and so on. Only the outermost call of a
                // nested chain announces.
                var enter = new HarmonyMethod(typeof(CoopZombieSync).GetMethod(nameof(PlacingPrefix), own));
                var placed = new HarmonyMethod(typeof(CoopZombieSync).GetMethod(nameof(PlacedPostfix), own));
                var leave = new HarmonyMethod(typeof(CoopZombieSync).GetMethod(nameof(PlacingFinalizer), own));
                Type zombieType = Plugin.FindGameType("ZombieWgoData");
                foreach (MethodInfo method in systemData.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name.StartsWith("PutZombieFromStoreToGameScene", StringComparison.Ordinal) && method.ReturnType == zombieType)
                    {
                        harmony.Patch(method, prefix: enter, postfix: placed, finalizer: leave);
                    }
                }
                // Putting a zombie to work, or taking it off: replayed on the other machines.
                var attached = new HarmonyMethod(typeof(CoopZombieSync).GetMethod(nameof(AttachPostfix), own));
                foreach (MethodInfo method in zombieType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    bool attach = method.Name.StartsWith("AttachTo", StringComparison.Ordinal) && parameters.Length == 3 && parameters[0].ParameterType == Plugin.FindGameType("SGuid");
                    bool detach = method.Name == "UnAttachFromWgoData" && parameters.Length == 1;
                    if (attach || detach)
                    {
                        harmony.Patch(method, postfix: attached);
                    }
                }
                harmony.Patch(pickUp, prefix: new HarmonyMethod(typeof(CoopZombieSync).GetMethod(nameof(PickUpPrefix), own)));
                var hostOnly = new HarmonyMethod(typeof(CoopZombieSync).GetMethod(nameof(HostOnlyPrefix), own));
                harmony.Patch(zombieUpdate, prefix: hostOnly);
                harmony.Patch(porterUpdate, prefix: hostOnly);
                log.LogInfo("Zombie sync: zombies are placed for everyone and run by the host.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Zombie sync disabled: " + ex.Message);
            }
        }

        private static bool IsJoiner()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            return netcode != null && netcode.IsListening && !netcode.IsHost && CoopSession.Welcomed;
        }

        /// <summary>Joiners do not run zombies; they show the host's.</summary>
        private static bool HostOnlyPrefix()
        {
            return !Enabled || !IsJoiner();
        }

        // ------------------------------------------------------------------ presence

        private static void PlacingPrefix()
        {
            placeDepth++;
        }

        private static Exception PlacingFinalizer(Exception __exception)
        {
            placeDepth = Math.Max(0, placeDepth - 1);
            return __exception;
        }

        private static void AttachPostfix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || applying || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                string target = __originalMethod.Name == "UnAttachFromWgoData"
                    ? string.Empty
                    : Convert.ToString(CoopDiagnostics.GetMember(__args[0], "Id"));
                byte[] detail = System.Text.Encoding.UTF8.GetBytes(__originalMethod.Name + "|" + target);
                SendPresence(netcode, null, KindAttach, IdOf(__instance), detail);
                log.LogInfo("Zombie sync: shared " + __originalMethod.Name + " for " + Short(IdOf(__instance)) + (target.Length > 0 ? " to " + Short(target) : "") + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Zombie sync: could not share a zombie assignment: " + Inner(ex).Message);
            }
        }

        private static void PlacedPostfix(object __result)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || applying || placeDepth > 1 || __result == null || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                SendPresence(netcode, null, KindPlace, IdOf(__result), Compress(CoopGameSerializer.Serialize(__result)));
                log.LogInfo("Zombie sync: shared the placement of " + CoopDiagnostics.GetMember(__result, "Name") + " " + Short(IdOf(__result)) + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Zombie sync: could not share a placed zombie: " + Inner(ex).Message);
            }
        }

        private static void PickUpPrefix(object zombieWgoData)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || applying || zombieWgoData == null || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                SendPresence(netcode, null, KindPickUp, IdOf(zombieWgoData), new byte[0]);
            }
            catch (Exception ex)
            {
                log.LogWarning("Zombie sync: could not share a picked-up zombie: " + Inner(ex).Message);
            }
        }

        // ------------------------------------------------------------------ join gap
        // As for buildings (CoopBuildSync): a zombie made, assigned or picked up while a friend was
        // joining reached them in the main menu and was missing for good (found by the `mix` run).
        // The host keeps this session's zombie changes and plays them to every joiner when they are
        // let in. Playing one again changes nothing: a zombie already on the scene is not placed
        // twice, one already picked up is not there to pick up.
        private sealed class Change
        {
            internal byte Kind;
            internal string Id;
            internal byte[] Payload;
        }

        private static readonly List<Change> journal = new List<Change>();
        private const int JournalLimit = 1000;

        private static void Journal(byte kind, string id, byte[] payload)
        {
            if (journal.Count >= JournalLimit)
            {
                journal.RemoveAt(0);
            }
            journal.Add(new Change { Kind = kind, Id = id, Payload = payload });
        }

        /// <summary>At the end of a hosted session.</summary>
        internal static void ResetJournal()
        {
            journal.Clear();
        }

        /// <summary>The join snapshot: this session's zombie changes, to one joiner, in order.</summary>
        internal static int SendSnapshotTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsHost || journal.Count == 0)
            {
                return 0;
            }
            foreach (Change change in journal)
            {
                using (var writer = new FastBufferWriter(128 + change.Payload.Length, Allocator.Temp))
                {
                    writer.WriteValueSafe(change.Kind);
                    writer.WriteValueSafe(new FixedString64Bytes(change.Id));
                    writer.WriteValueSafe(change.Payload.Length);
                    writer.WriteBytesSafe(change.Payload, change.Payload.Length);
                    NetworkDelivery delivery = change.Payload.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                    netcode.CustomMessagingManager.SendNamedMessage(PresenceMessage, clientId, writer, delivery);
                }
            }
            return journal.Count;
        }

        private static void SendPresence(NetworkManager netcode, ulong? except, byte kind, string id, byte[] payload)
        {
            using (var writer = new FastBufferWriter(128 + payload.Length, Allocator.Temp))
            {
                writer.WriteValueSafe(kind);
                writer.WriteValueSafe(new FixedString64Bytes(id));
                writer.WriteValueSafe(payload.Length);
                writer.WriteBytesSafe(payload, payload.Length);
                NetworkDelivery delivery = payload.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                if (netcode.IsHost)
                {
                    Journal(kind, id, payload);
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(PresenceMessage, clientId, writer, delivery);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(PresenceMessage, NetworkManager.ServerClientId, writer, delivery);
                }
            }
        }

        internal static void ReceivePresence(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                return;
            }
            byte kind = 255;
            string id = "?";
            try
            {
                reader.ReadValueSafe(out kind);
                reader.ReadValueSafe(out FixedString64Bytes idText);
                reader.ReadValueSafe(out int length);
                if (length < 0 || length > 1024 * 1024)
                {
                    return;
                }
                byte[] payload = new byte[length];
                if (length > 0)
                {
                    reader.ReadBytesSafe(ref payload, length);
                }
                id = idText.ToString();
                if (netcode.IsHost)
                {
                    SendPresence(netcode, sender, kind, id, payload);
                }
                applying = true;
                try
                {
                    if (kind == KindPlace)
                    {
                        Place(id, Decompress(payload));
                    }
                    else if (kind == KindAttach)
                    {
                        Attach(id, System.Text.Encoding.UTF8.GetString(payload));
                    }
                    else if (kind == KindPickUp)
                    {
                        PickUp(id);
                    }
                }
                finally
                {
                    applying = false;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Zombie sync: could not apply a zombie change from " + sender + " (kind " + kind + ", " + id + "): " + Inner(ex).Message + " at " + CoopDiagnostics.FirstFrames(Inner(ex)));
            }
        }

        /// <summary>
        /// Puts the zombie into this machine's store (or refreshes the stored one from the sender's),
        /// then places it through the game's own call, exactly as on the sender.
        /// </summary>
        private static void Place(string id, byte[] raw)
        {
            object system = SystemData();
            if (OnScene(system, id))
            {
                return;
            }
            object received = CoopGameSerializer.Deserialize(Plugin.FindGameType("ZombieWgoData"), raw);
            // As every stored zombie is set up when a save loads (see CoopBuildSync.Place).
            received.GetType().GetMethod("PrepareForGame", Type.EmptyTypes)?.Invoke(received, null);
            object guid = CoopDiagnostics.GetMember(received, "UniqueId");
            IDictionary cache = CoopDiagnostics.GetMember(system, "Cache") as IDictionary;
            IList store = CoopDiagnostics.GetMember(system, "zombieDrops") as IList;
            object key = CoopDiagnostics.GetMember(guid, "Guid");
            if (cache != null && cache.Contains(key))
            {
                store?.Remove(cache[key]);
                cache.Remove(key);
            }
            cache?.Add(key, received);
            store?.Add(received);
            MethodInfo place = null;
            foreach (MethodInfo method in system.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.Name == "PutZombieFromStoreToGameScene" && method.GetParameters().Length == 4)
                {
                    place = method;
                }
            }
            Vector2 facing = CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(received, "direction"), "Value") is Vector2 d ? d : Vector2.down;
            object direction = Enum.ToObject(place.GetParameters()[3].ParameterType, DirectionIndex(facing));
            place.Invoke(system, new[] { guid, CoopDiagnostics.GetMember(received, "WorldId"), CoopDiagnostics.GetMember(received, "Position"), direction });
            placements++;
            log.LogInfo("Zombie sync: placed " + CoopDiagnostics.GetMember(received, "Name") + " " + Short(id) + " as another player did.");
        }

        /// <summary>The same assignment the sender made, to the same object, on this machine's copy.</summary>
        private static void Attach(string id, string detail)
        {
            object zombie = FindZombie(SystemData(), id);
            int bar = detail.IndexOf('|');
            if (zombie == null || bar <= 0)
            {
                return;
            }
            string method = detail.Substring(0, bar);
            string target = detail.Substring(bar + 1);
            if (method == "UnAttachFromWgoData")
            {
                zombie.GetType().GetMethod(method, new[] { typeof(bool) })?.Invoke(zombie, new object[] { false });
            }
            else
            {
                MethodInfo attach = null;
                foreach (MethodInfo candidate in zombie.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (candidate.Name == method && candidate.GetParameters().Length == 3)
                    {
                        attach = candidate;
                    }
                }
                object guid = AccessTools.Method(Plugin.FindGameType("SGuid"), "Parse").Invoke(null, new object[] { target });
                attach?.Invoke(zombie, new[] { guid, CoopDiagnostics.GetMember(zombie, "ZombieItem"), null });
            }
            log.LogInfo("Zombie sync: " + method + " for " + Short(id) + " as another player did.");
        }

        private static void PickUp(string id)
        {
            object system = SystemData();
            object zombie = FindZombie(system, id);
            if (zombie == null || !OnScene(system, id))
            {
                return;
            }
            system.GetType().GetMethod("PutZombieFromGameSceneToStore", new[] { zombie.GetType() })?.Invoke(system, new[] { zombie });
            lastSent.Remove(id);
            pickups++;
            log.LogInfo("Zombie sync: " + Short(id) + " was picked up by another player.");
        }

        // ------------------------------------------------------------------ motion

        /// <summary>Driven from the plugin's frame update: the host streams its zombies' motion.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || !netcode.IsHost || netcode.ConnectedClientsIds.Count < 2)
            {
                return;
            }
            if (Time.unscaledTime < nextMotion)
            {
                return;
            }
            nextMotion = Time.unscaledTime + MotionInterval;
            try
            {
                object system = SystemData();
                if (system == null || !(CoopDiagnostics.GetMember(system, "zombieOnSceneWgoIds") is IList onScene) || onScene.Count == 0)
                {
                    return;
                }
                var changed = new List<KeyValuePair<string, Motion>>();
                foreach (object guid in onScene)
                {
                    object zombie = system.GetType().GetMethod("GetZombie").Invoke(system, new[] { guid });
                    if (zombie == null)
                    {
                        continue;
                    }
                    string id = IdOf(zombie);
                    var now = new Motion
                    {
                        Position = (Vector3)CoopDiagnostics.GetMember(zombie, "Position"),
                        Direction = CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(zombie, "direction"), "Value") is Vector2 d ? d : Vector2.zero,
                        Anim = Convert.ToInt32(AccessTools.Field(zombie.GetType(), "curAnimState")?.GetValue(zombie) ?? 0)
                    };
                    if (lastSent.TryGetValue(id, out Motion before) && (before.Position - now.Position).sqrMagnitude < 0.0001f &&
                        before.Direction == now.Direction && before.Anim == now.Anim)
                    {
                        continue;
                    }
                    lastSent[id] = now;
                    changed.Add(new KeyValuePair<string, Motion>(id, now));
                }
                if (changed.Count > 0)
                {
                    SendMotion(netcode, changed);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Zombie sync: " + Inner(ex).Message);
                nextMotion = Time.unscaledTime + 10f;
            }
        }

        private static void SendMotion(NetworkManager netcode, List<KeyValuePair<string, Motion>> motions)
        {
            using (var writer = new FastBufferWriter(8 + motions.Count * 96, Allocator.Temp))
            {
                writer.WriteValueSafe(motions.Count);
                foreach (KeyValuePair<string, Motion> entry in motions)
                {
                    writer.WriteValueSafe(new FixedString64Bytes(entry.Key));
                    writer.WriteValueSafe(entry.Value.Position);
                    writer.WriteValueSafe(entry.Value.Direction);
                    writer.WriteValueSafe(entry.Value.Anim);
                }
                NetworkDelivery delivery = writer.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId)
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(MotionMessage, clientId, writer, delivery);
                    }
                }
            }
        }

        internal static void ReceiveMotion(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || netcode.IsHost || sender != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int count);
                object system = SystemData();
                MethodInfo setAnim = Plugin.FindGameType("ZombieWgoData")?.GetMethod("SetCustomAnimationState");
                Type animType = setAnim?.GetParameters()[0].ParameterType;
                for (int i = 0; i < count && i < 512; i++)
                {
                    reader.ReadValueSafe(out FixedString64Bytes idText);
                    reader.ReadValueSafe(out Vector3 position);
                    reader.ReadValueSafe(out Vector2 direction);
                    reader.ReadValueSafe(out int anim);
                    object zombie = FindZombie(system, idText.ToString());
                    if (zombie == null)
                    {
                        continue;
                    }
                    zombie.GetType().GetProperty("Position").SetValue(zombie, position, null);
                    object facing = CoopDiagnostics.GetMember(zombie, "direction");
                    facing?.GetType().GetProperty("Value").SetValue(facing, direction, null);
                    int current = Convert.ToInt32(AccessTools.Field(zombie.GetType(), "curAnimState")?.GetValue(zombie) ?? 0);
                    if (current != anim && setAnim != null)
                    {
                        setAnim.Invoke(zombie, new[] { Enum.ToObject(animType, anim) });
                    }
                    motionsApplied++;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Zombie sync: could not apply zombie motion: " + Inner(ex).Message);
            }
        }

        // ------------------------------------------------------------------ helpers

        private static object SystemData()
        {
            return CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "ZombieSystemData");
        }

        private static object FindZombie(object system, string id)
        {
            if (system == null)
            {
                return null;
            }
            Type sguid = Plugin.FindGameType("SGuid");
            object guid = AccessTools.Method(sguid, "Parse").Invoke(null, new object[] { id });
            return system.GetType().GetMethod("GetZombie").Invoke(system, new[] { guid });
        }

        private static bool OnScene(object system, string id)
        {
            if (system == null || !(CoopDiagnostics.GetMember(system, "zombieOnSceneWgoIds") is IList onScene))
            {
                return false;
            }
            foreach (object guid in onScene)
            {
                if (Convert.ToString(CoopDiagnostics.GetMember(guid, "Id")) == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The game's <c>Direction</c> from a facing vector (None 0, Right 1, Up 2, Left 3, Down 4).</summary>
        private static int DirectionIndex(Vector2 facing)
        {
            if (Mathf.Abs(facing.x) > Mathf.Abs(facing.y))
            {
                return facing.x < 0 ? 3 : 1;
            }
            return facing.y > 0 ? 2 : 4;
        }

        private static string IdOf(object wgoData)
        {
            return Convert.ToString(CoopDiagnostics.GetMember(CoopDiagnostics.GetMember(wgoData, "UniqueId"), "Id"));
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
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
    }
}
