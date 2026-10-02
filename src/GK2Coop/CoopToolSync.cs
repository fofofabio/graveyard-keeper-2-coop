using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Phase 2: replicate one gameplay action — applying a tool to a world object — and prove the
    /// player context works on it.
    ///
    /// Every local tool tick goes through <c>ToolComponent.ApplyAction</c>. A postfix reports what
    /// the local player just did to which object with which tool; the peer re-runs the same work
    /// activity inside <see cref="CoopPlayerContext"/> so the game's own code attributes the
    /// damage, the energy and the drops to the acting player rather than to whoever is watching.
    ///
    /// Both peers currently run their own tool ticks and replicate them, so this is symmetric
    /// rather than host-authoritative. That is deliberate for a first experiment: the point is to
    /// find out whether the context approach holds up at all. Authority comes after it does.
    /// </summary>
    internal static class CoopToolSync
    {
        internal const string ToolUseMessage = "GK2Coop.ToolUse.v1";

        private static ManualLogSource log;
        private static int sent;
        private static int received;
        private static int applied;
        private static int failed;
        private static string lastDetail = "none";
        private static int detailedLogsLeft = 6;
        private static int outgoingSequence;
        private static int duplicates;
        private static readonly System.Collections.Generic.Dictionary<int, int> lastSequencePerSender =
            new System.Collections.Generic.Dictionary<int, int>();
        private static readonly System.Collections.Generic.Dictionary<string, float> recentlyApplied =
            new System.Collections.Generic.Dictionary<string, float>();
        private const float EchoSuppressionSeconds = 0.25f;

        internal static bool Enabled { get; set; }

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
                Type toolComponent = Plugin.FindGameType("ToolComponent");
                MethodInfo applyAction = toolComponent == null ? null : AccessTools.Method(toolComponent, "ApplyAction");
                if (applyAction == null)
                {
                    log.LogWarning("ToolComponent.ApplyAction not found; tool use will not replicate.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(applyAction, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(CoopToolSync), nameof(ApplyActionPostfix))));
                log.LogInfo("Patched ToolComponent.ApplyAction for tool replication.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Could not patch ToolComponent.ApplyAction: " + ex.Message);
            }
        }

        internal static string Describe()
        {
            return "tool sync: sent=" + sent + ", received=" + received + ", applied=" + applied +
                   ", duplicates=" + duplicates + ", failed=" + failed + "; last=" + lastDetail;
        }

        // ------------------------------------------------------------------ capture

        /// <summary>
        /// Reports the local player's tool tick. Replaying a remote tick calls the work activity
        /// directly rather than going through <c>ApplyAction</c>, so this cannot echo.
        /// </summary>
        private static void ApplyActionPostfix(object __instance)
        {
            if (!Enabled || CoopPlayerContext.IsActive)
            {
                return;
            }
            try
            {
                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode == null || !netcode.IsListening || !netcode.IsConnectedClient)
                {
                    return;
                }

                object activity = CoopDiagnostics.GetMember(__instance, "toolActor");
                object tool = CoopDiagnostics.GetMember(__instance, "toolInUse");
                if (activity == null)
                {
                    return;
                }
                object wgoData = CoopDiagnostics.GetMember(activity, "wgoData");
                string wgoId = ReadGuid(wgoData, "UniqueId");
                if (string.IsNullOrEmpty(wgoId))
                {
                    return;
                }

                recentlyApplied[wgoId] = Time.unscaledTime;
                Send(netcode, wgoId, ReadGuid(tool, "UniqueId"), activity.GetType().FullName);
            }
            catch (Exception ex)
            {
                failed++;
                log.LogWarning("Could not report a tool use: " + ex.Message);
            }
        }

        private static string ReadGuid(object owner, string member)
        {
            object guid = owner == null ? null : CoopDiagnostics.GetMember(owner, member);
            object id = guid == null ? null : CoopDiagnostics.GetMember(guid, "Id");
            return id == null ? null : id.ToString();
        }

        private static void Send(NetworkManager netcode, string wgoId, string toolId, string activityType)
        {
            using (var writer = new FastBufferWriter(640, Allocator.Temp))
            {
                writer.WriteValueSafe((int)netcode.LocalClientId);
                writer.WriteValueSafe(++outgoingSequence);
                writer.WriteValueSafe(new FixedString128Bytes(wgoId ?? string.Empty));
                writer.WriteValueSafe(new FixedString128Bytes(toolId ?? string.Empty));
                writer.WriteValueSafe(new FixedString512Bytes(activityType ?? string.Empty));

                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(
                                ToolUseMessage, clientId, writer, NetworkDelivery.Reliable);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(
                        ToolUseMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
                }
            }
            sent++;
        }

        // ------------------------------------------------------------------ apply

        internal static void Receive(ulong senderClientId, FastBufferReader reader)
        {
            received++;
            try
            {
                int actorClientId;
                int sequence;
                FixedString128Bytes wgoId;
                FixedString128Bytes toolId;
                FixedString512Bytes activityTypeName;
                reader.ReadValueSafe(out actorClientId);
                reader.ReadValueSafe(out sequence);
                reader.ReadValueSafe(out wgoId);
                reader.ReadValueSafe(out toolId);
                reader.ReadValueSafe(out activityTypeName);

                // Per-sender sequence: a redelivered or reordered tick must not be applied twice.
                int lastSequence;
                if (lastSequencePerSender.TryGetValue(actorClientId, out lastSequence) && sequence <= lastSequence)
                {
                    duplicates++;
                    return;
                }
                lastSequencePerSender[actorClientId] = sequence;

                Apply(actorClientId, wgoId.ToString(), toolId.ToString(), activityTypeName.ToString());
            }
            catch (Exception ex)
            {
                failed++;
                log.LogWarning("Could not read a replicated tool use: " + ex.Message);
            }
        }

        private static void Apply(int actorClientId, string wgoId, string toolId, string activityTypeName)
        {
            object actor = ResolveNetworkPlayer(actorClientId);
            object actorData = actor == null ? null : CoopDiagnostics.GetMember(actor, "playerData");
            if (actorData == null)
            {
                failed++;
                Record("no player record for client " + actorClientId, false);
                return;
            }

            float workedLocallyAt;
            if (recentlyApplied.TryGetValue(wgoId, out workedLocallyAt) &&
                Time.unscaledTime - workedLocallyAt < EchoSuppressionSeconds)
            {
                // Both players working the same object within a frame or two: applying the remote
                // tick as well would double-damage it.
                duplicates++;
                return;
            }

            object wgoData = ResolveWgoData(wgoId);
            if (wgoData == null)
            {
                failed++;
                Record("unknown world object " + wgoId, false);
                return;
            }

            Type activityType = string.IsNullOrEmpty(activityTypeName) ? null : Plugin.FindGameType(activityTypeName);
            if (activityType == null)
            {
                failed++;
                Record("unknown work activity " + activityTypeName, false);
                return;
            }

            object tool = ResolveTool(actorData, toolId);
            object controller = ResolveController(actorData);

            bool ok = CoopPlayerContext.Run(actorData, controller, "tool use by client " + actorClientId, delegate
            {
                // Built inside the context: the activity's constructor and UseTool both read the
                // player statics, and they must see the acting player, not us.
                object activity = Activator.CreateInstance(activityType, actorData, wgoData);
                MethodInfo useTool = AccessTools.Method(activityType, "UseTool");
                if (useTool == null)
                {
                    throw new MissingMethodException(activityType.FullName, "UseTool");
                }
                useTool.Invoke(activity, new[] { tool, (object)1 });
            });

            if (ok)
            {
                applied++;
            }
            else
            {
                failed++;
            }
            Record("client " + actorClientId + " on " + Shorten(wgoId) + " via " + activityType.Name +
                   (tool == null ? " (no tool resolved)" : string.Empty), ok);
        }

        private static void Record(string detail, bool ok)
        {
            lastDetail = (ok ? "ok: " : "failed: ") + detail;
            if (detailedLogsLeft > 0)
            {
                detailedLogsLeft--;
                if (ok)
                {
                    log.LogInfo("Replicated tool use " + lastDetail);
                }
                else
                {
                    log.LogWarning("Replicated tool use " + lastDetail);
                }
            }
        }

        private static string Shorten(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }

        private static object ResolveNetworkPlayer(int clientId)
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            if (save == null)
            {
                return null;
            }
            object hostPlayer = CoopDiagnostics.GetMember(save, "hostPlayer");
            if (hostPlayer != null && Convert.ToInt32(CoopDiagnostics.GetMember(hostPlayer, "clientId")) == clientId)
            {
                return hostPlayer;
            }
            var clients = CoopDiagnostics.GetMember(save, "clientPlayers") as System.Collections.IEnumerable;
            if (clients == null)
            {
                return null;
            }
            foreach (object client in clients)
            {
                if (Convert.ToInt32(CoopDiagnostics.GetMember(client, "clientId")) == clientId)
                {
                    return client;
                }
            }
            return null;
        }

        private static object ResolveWgoData(string uniqueId)
        {
            if (string.IsNullOrEmpty(uniqueId))
            {
                return null;
            }
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            object worldData = save == null ? null : CoopDiagnostics.GetMember(save, "worldData");
            if (worldData == null)
            {
                return null;
            }

            Type sguidType = Plugin.FindGameType("SGuid");
            MethodInfo parse = sguidType == null ? null : AccessTools.Method(sguidType, "Parse");
            object sguid = parse == null ? null : parse.Invoke(null, new object[] { uniqueId });
            if (sguid == null)
            {
                return null;
            }
            MethodInfo getWgo = AccessTools.Method(worldData.GetType(), "GetWgoData", new[] { sguidType });
            return getWgo == null ? null : getWgo.Invoke(worldData, new[] { sguid });
        }

        /// <summary>
        /// Finds the acting player's own copy of the tool. Inventories are not replicated yet, so
        /// the remote record holds whatever the start state equipped; a miss is reported rather
        /// than substituted, because silently swapping in the wrong player's tool would corrupt
        /// durability on the wrong item.
        /// </summary>
        private static object ResolveTool(object actorData, string toolId)
        {
            if (string.IsNullOrEmpty(toolId))
            {
                return null;
            }
            object belt = CoopDiagnostics.GetMember(actorData, "toolBeltInventory");
            if (belt == null)
            {
                return null;
            }
            MethodInfo byId = AccessTools.Method(belt.GetType(), "GetItemByUniqueId");
            return byId == null ? null : byId.Invoke(belt, new object[] { toolId });
        }

        private static object ResolveController(object actorData)
        {
            Type bodyType = Plugin.FindGameType("PlayerPhysicalBody");
            if (bodyType == null)
            {
                return null;
            }
            foreach (Component body in Resources.FindObjectsOfTypeAll(bodyType))
            {
                if (!body.gameObject.scene.IsValid() ||
                    !ReferenceEquals(CoopDiagnostics.GetMember(body, "playerData"), actorData))
                {
                    continue;
                }
                return body.gameObject.GetComponent("PlayerController");
            }
            return null;
        }
    }
}
