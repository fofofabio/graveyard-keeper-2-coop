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
    /// Phase 3a: keep the world itself in step. When a world object dies on one machine it should
    /// die on the other, so the two players are looking at the same world rather than two private
    /// copies that drift apart the moment anyone chops anything.
    ///
    /// Scene-authored objects carry the same unique id on both machines — proved by replicated tool
    /// ticks resolving their targets across the link — so the id is enough to name the object.
    ///
    /// The peer applies the death through the game's own HP path, so the usual death animation,
    /// sound and cleanup all run. Whether the drops and quest effects also run there is a trade
    /// governed by <see cref="RemoteDeathDrops"/>.
    /// </summary>
    internal static class CoopWorldSync
    {
        internal const string DeathMessage = "GK2Coop.WgoDeath.v1";

        private static ManualLogSource log;
        private static int sent;
        private static int received;
        private static int applied;
        private static int unresolved;
        private static int outgoingSequence;
        private static readonly Dictionary<int, int> lastSequencePerSender = new Dictionary<int, int>();
        // When each remote death was applied here, so its own echo is not reported back. Only for
        // a while: a bed keeps its id through harvest, replanting and the next harvest, and that
        // later death is a new one.
        private static readonly Dictionary<string, float> appliedRemotely = new Dictionary<string, float>();
        private const float EchoSeconds = 30f;
        // This session's deaths: object id -> what died there (a harvested bed is replaced under
        // the same id by an empty one, so the id alone does not say whether a world has seen it).
        private static readonly Dictionary<string, string> knownDeaths = new Dictionary<string, string>();
        private static int detailedLogsLeft = 6;

        internal static bool Enabled { get; set; }

        /// <summary>
        /// Whether a mirrored death also produces its drops and quest effects on the receiving
        /// machine.
        ///
        /// True duplicates resources — one bush yields to both players — but keeps both of them
        /// unblocked, because quest progress is still per-player: with the object now gone from the
        /// shared world, a player who receives nothing simply cannot finish their own copy of the
        /// quest. False is economically correct and will be right once quest and inventory state
        /// are genuinely shared.
        /// </summary>
        internal static bool RemoteDeathDrops { get; set; }

        /// <summary>True while a remote death is being applied: suppresses drops and re-broadcast.</summary>
        internal static bool ApplyingRemoteDeath { get; private set; }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "world sync: deaths sent=" + sent + ", received=" + received +
                   ", applied=" + applied + ", unresolved=" + unresolved;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                Type wgoData = Plugin.FindGameType("WgoData");
                MethodInfo death = wgoData == null ? null : AccessTools.Method(wgoData, "RunLogicsAfterDeath");
                if (death == null)
                {
                    log.LogWarning("WgoData.RunLogicsAfterDeath not found; world deaths will not replicate.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(death,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(CoopWorldSync), nameof(DeathPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(CoopWorldSync), nameof(DeathPostfix))),
                    finalizer: new HarmonyMethod(AccessTools.Method(typeof(CoopWorldSync), nameof(DeathFinalizer))));

                // Suppressing the drop at its source is narrower than unwinding the effects
                // afterwards, and keeps the rest of the death sequence intact.
                // The death also fires WgoDead, which can complete a quest whose expression spawns
                // items on a later frame — outside any window around the death call. Suppressing
                // the trigger stops that at source: quest progress belongs to whoever earned it.
                Type events = Plugin.FindGameType("GlobalEventsSystem");
                MethodInfo fireTrigger = events == null ? null : AccessTools.Method(events, "FireTrigger");
                if (fireTrigger != null)
                {
                    harmony.Patch(fireTrigger, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(CoopWorldSync), nameof(FireTriggerPrefix))));
                }

                Type dropSystem = Plugin.FindGameType("DropSystem");
                MethodInfo dropItem = dropSystem == null ? null : AccessTools.Method(dropSystem, "DropItem");
                if (dropItem != null)
                {
                    harmony.Patch(dropItem, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(CoopWorldSync), nameof(DropItemPrefix))));
                }
                log.LogInfo("Patched world-object death for replication.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Could not patch world-object death: " + ex.Message);
            }
        }

        private static bool FireTriggerPrefix()
        {
            // Returning false skips the trigger entirely while a remote death is being mirrored,
            // which also stops any quest it would complete from spawning items a frame later.
            return RemoteDeathDrops || !(ApplyingRemoteDeath || mirroringLaterDeath);
        }

        private static bool DropItemPrefix(ref bool __result)
        {
            if (!suppressOwnDeathDrops && (RemoteDeathDrops || !(ApplyingRemoteDeath || mirroringLaterDeath)))
            {
                return true;
            }
            __result = false;
            return false;
        }

        /// <summary>
        /// The world object whose own death logic is running here (its drops are its harvest), so a
        /// drop can say where it came from; null otherwise.
        /// </summary>
        internal static string LocalDeathOrigin { get; private set; }

        // The host's own death logic of an object another player harvested a moment ago: both
        // finished it at once, and their harvest is the one that stands.
        private static bool suppressOwnDeathDrops;

        // The death logic of an object whose death came from another player, run by the game on a
        // later frame (after Apply had already finished): it is still theirs. Run as the player's own,
        // it dropped the whole harvest a second time on this machine, and shared it (seen in `mix`:
        // "Ran death logic locally" after "Replicated death applied", with the harvest's drops).
        private static bool mirroringLaterDeath;

        // ------------------------------------------------------------------ capture

        // What died, read before the death runs: a harvested bed becomes the empty bed inside it.
        private static void DeathPrefix(object __instance, out string __state)
        {
            __state = null;
            try
            {
                __state = KindOf(__instance);
                if (Enabled && !ApplyingRemoteDeath)
                {
                    string dying = ReadUniqueId(__instance);
                    if (!string.IsNullOrEmpty(dying) && IsEcho(dying))
                    {
                        mirroringLaterDeath = true;
                        if (laterDeathNotes++ < 3)
                        {
                            log.LogInfo("World sync: the death of " + Shorten(dying) + " from another player finished here a moment later; its harvest stays theirs.");
                        }
                    }
                    else
                    {
                        LocalDeathOrigin = dying;
                        suppressOwnDeathDrops = CoopDropSync.HarvestedByAnotherJustNow(LocalDeathOrigin);
                    }
                }
            }
            catch
            {
                // The postfix reads it again.
            }
        }

        private static int laterDeathNotes;

        private static Exception DeathFinalizer(Exception __exception)
        {
            LocalDeathOrigin = null;
            suppressOwnDeathDrops = false;
            mirroringLaterDeath = false;
            return __exception;
        }

        private static void DeathPostfix(object __instance, string __state)
        {
            if (!Enabled || ApplyingRemoteDeath || mirroringLaterDeath)
            {
                return;
            }
            try
            {
                // Journalled before the session check: a host that clears the world before anyone
                // connects must still be able to bring a joiner level. The guard below only
                // decides whether to send now, not whether this happened.
                string uniqueId = ReadUniqueId(__instance);
                if (string.IsNullOrEmpty(uniqueId) || IsEcho(uniqueId))
                {
                    return;
                }
                string kind = string.IsNullOrEmpty(__state) ? KindOf(__instance) : __state;
                knownDeaths[uniqueId] = kind;

                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode == null || !netcode.IsListening || !netcode.IsConnectedClient)
                {
                    return;
                }

                using (var writer = new FastBufferWriter(256, Allocator.Temp))
                {
                    writer.WriteValueSafe((int)netcode.LocalClientId);
                    writer.WriteValueSafe(++outgoingSequence);
                    writer.WriteValueSafe(new FixedString128Bytes(uniqueId));
                    writer.WriteValueSafe(new FixedString128Bytes(kind));
                    if (netcode.IsHost)
                    {
                        foreach (ulong clientId in netcode.ConnectedClientsIds)
                        {
                            if (clientId != netcode.LocalClientId)
                            {
                                netcode.CustomMessagingManager.SendNamedMessage(
                                    DeathMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                            }
                        }
                    }
                    else
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(
                            DeathMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    }
                }
                sent++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not report a world-object death: " + ex.Message);
            }
        }

        private static string ReadUniqueId(object wgoData)
        {
            object guid = CoopDiagnostics.GetMember(wgoData, "UniqueId");
            object id = guid == null ? null : CoopDiagnostics.GetMember(guid, "Id");
            return id == null ? null : id.ToString();
        }

        /// <summary>What the object is ("garden_wheat_ready"); empty when unknown.</summary>
        private static string KindOf(object wgoData)
        {
            string kind = wgoData == null ? null : Convert.ToString(CoopDiagnostics.GetMember(wgoData, "id"));
            return string.IsNullOrEmpty(kind) || kind.Length > 120 ? string.Empty : kind;
        }

        private static bool IsEcho(string uniqueId)
        {
            return appliedRemotely.TryGetValue(uniqueId, out float at) && Time.realtimeSinceStartup - at < EchoSeconds;
        }

        /// <summary>
        /// Replays every death this machine knows about to one client. A joining client starts
        /// from its own fresh world, which equals the world this session began in, so replaying
        /// the journal brings it level. Uses the ordinary death message, so the receiving side
        /// runs the same verified apply path as a live death.
        /// </summary>
        internal static int SendSnapshotTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || !netcode.IsHost)
            {
                return 0;
            }
            int count = 0;
            foreach (KeyValuePair<string, string> death in knownDeaths)
            {
                // The same thing is there again in the host's world (a bed grown ripe again): the
                // joiner's copy has it either way. A harvested bed replaced by an empty one is sent
                // with what died, and a joiner whose copy already has the empty bed leaves it be
                // (found by `mix`: the bed was missing after a rejoin; by `mix3`: a bed harvested
                // while a friend was loading the copy stayed ripe for them).
                object now = ResolveWgoData(death.Key);
                if (now != null && (death.Value.Length == 0 || KindOf(now) == death.Value))
                {
                    continue;
                }
                using (var writer = new FastBufferWriter(512, Allocator.Temp))
                {
                    writer.WriteValueSafe((int)netcode.LocalClientId);
                    writer.WriteValueSafe(++outgoingSequence);
                    writer.WriteValueSafe(new FixedString128Bytes(death.Key));
                    writer.WriteValueSafe(new FixedString128Bytes(death.Value));
                    netcode.CustomMessagingManager.SendNamedMessage(
                        DeathMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
                sent++;
                count++;
            }
            return count;
        }

        // ------------------------------------------------------------------ apply

        internal static void Receive(ulong senderClientId, FastBufferReader reader)
        {
            received++;
            try
            {
                int actorClientId;
                int sequence;
                FixedString128Bytes uniqueId;
                reader.ReadValueSafe(out actorClientId);
                reader.ReadValueSafe(out sequence);
                reader.ReadValueSafe(out uniqueId);
                // What died; absent from a sender before 0.65.3.
                FixedString128Bytes kind = default;
                if (reader.Position < reader.Length)
                {
                    reader.ReadValueSafe(out kind);
                }

                int lastSequence;
                if (lastSequencePerSender.TryGetValue(actorClientId, out lastSequence) && sequence <= lastSequence)
                {
                    return;
                }
                lastSequencePerSender[actorClientId] = sequence;

                Apply(uniqueId.ToString(), kind.ToString());

                // Forwarded to the other clients: a client sends only to the server, so without
                // this the third player never learns the object died.
                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode != null && netcode.IsHost)
                {
                    using (var writer = new FastBufferWriter(512, Allocator.Temp))
                    {
                        writer.WriteValueSafe(actorClientId);
                        writer.WriteValueSafe(++outgoingSequence);
                        writer.WriteValueSafe(uniqueId);
                        writer.WriteValueSafe(kind);
                        foreach (ulong clientId in netcode.ConnectedClientsIds)
                        {
                            if (clientId != netcode.LocalClientId && clientId != senderClientId)
                            {
                                netcode.CustomMessagingManager.SendNamedMessage(
                                    DeathMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                            }
                        }
                    }
                    sent++;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read a replicated world-object death: " + ex.Message);
            }
        }

        private static bool InWorld()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            return mainGame != null && string.Equals(Convert.ToString(CoopDiagnostics.GetMember(mainGame, "gameState")), "InGame", StringComparison.Ordinal);
        }

        /// <summary>
        /// At the end of a session, either side. The next host numbers from the start again (a
        /// joiner who kept the old numbers ignored every death of a restarted host), and this
        /// session's deaths belong to this world (the next one may be another save).
        /// </summary>
        internal static void ResetSession()
        {
            lastSequencePerSender.Clear();
            appliedRemotely.Clear();
            knownDeaths.Clear();
        }

        private static void Apply(string uniqueId, string kind)
        {
            // Not yet in a world (connected in the main menu to copy it): nothing to apply it to, and
            // marking it as done made the join snapshot's copy of it be skipped — an object felled
            // while a friend was joining stayed standing for them.
            if (string.IsNullOrEmpty(uniqueId) || !InWorld() || IsEcho(uniqueId))
            {
                return;
            }
            object wgoData = ResolveWgoData(uniqueId);
            // Something else is there now under that id (the empty bed after the harvest): this
            // world has seen the death already.
            if (wgoData != null && !string.IsNullOrEmpty(kind) && KindOf(wgoData) != kind)
            {
                if (detailedLogsLeft > 0)
                {
                    detailedLogsLeft--;
                    log.LogInfo("Replicated death of " + kind + " " + Shorten(uniqueId) + " already seen here: " + KindOf(wgoData) + " stands there now.");
                }
                return;
            }
            appliedRemotely[uniqueId] = Time.realtimeSinceStartup;
            knownDeaths[uniqueId] = string.IsNullOrEmpty(kind) ? KindOf(wgoData) : kind;

            if (wgoData == null)
            {
                unresolved++;
                if (detailedLogsLeft > 0)
                {
                    detailedLogsLeft--;
                    log.LogWarning("Replicated death: no world object " + Shorten(uniqueId) + " on this machine.");
                }
                return;
            }

            ApplyingRemoteDeath = true;
            try
            {
                object hp = CoopDiagnostics.GetMember(wgoData, "hpComponent");
                MethodInfo applyDamage = hp == null ? null : AccessTools.Method(hp.GetType(), "ApplyDamage");
                if (applyDamage != null)
                {
                    // Through the game's own HP path so the death animation and cleanup still run.
                    applyDamage.Invoke(hp, new object[] { int.MaxValue / 2 });
                    applied++;
                }
                else
                {
                    RemoveOutright(uniqueId);
                }
                if (detailedLogsLeft > 0)
                {
                    detailedLogsLeft--;
                    log.LogInfo("Replicated death applied to world object " + Shorten(uniqueId) + ".");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not apply a replicated death to " + Shorten(uniqueId) + ": " + ex.Message);
            }
            finally
            {
                ApplyingRemoteDeath = false;
            }
        }

        private static void RemoveOutright(string uniqueId)
        {
            object worldData = GetWorldData();
            Type sguidType = Plugin.FindGameType("SGuid");
            MethodInfo parse = sguidType == null ? null : AccessTools.Method(sguidType, "Parse");
            MethodInfo remove = worldData == null
                ? null
                : AccessTools.Method(worldData.GetType(), "RemoveWgoDataFromGameScene", new[] { sguidType });
            if (parse == null || remove == null)
            {
                return;
            }
            remove.Invoke(worldData, new[] { parse.Invoke(null, new object[] { uniqueId }) });
            applied++;
        }

        private static object GetWorldData()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "worldData");
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

        private static string Shorten(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }
    }
}
