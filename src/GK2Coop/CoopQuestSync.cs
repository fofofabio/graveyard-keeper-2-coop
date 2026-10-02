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
    /// Phase 3b: share quest progress between players.
    ///
    /// Until now each peer ran its own copy of every quest, which is why a shared world was
    /// unplayable: once an object was harvested it was gone for both, but only the player who
    /// harvested it advanced. With progress shared, one player doing the work advances the story
    /// for both, and world objects no longer need to yield twice.
    ///
    /// Quest transitions are mirrored rather than requested, matching how world deaths already
    /// work. Each transition is applied through the game's own <c>QuestSystemData</c> methods, so
    /// the quest's own guards still run: a quest already Completed or Cancelled is left alone, and
    /// an unknown id is reported rather than invented. That makes a duplicate or out-of-order
    /// message harmless on its own, with sequence numbers as a second line of defence.
    /// </summary>
    internal static class CoopQuestSync
    {
        internal const string QuestMessage = "GK2Coop.Quest.v1";

        private const byte OpStart = 1;
        private const byte OpAwait = 2;
        private const byte OpComplete = 3;
        private const byte OpCancel = 4;

        private static ManualLogSource log;
        private static int sent;
        private static int received;
        private static int applied;
        private static int rejected;
        private static int outgoingSequence;
        private static readonly Dictionary<int, int> lastSequencePerSender = new Dictionary<int, int>();
        private static int detailedLogsLeft = 8;

        internal static bool Enabled { get; set; }

        /// <summary>True while mirroring a peer's transition, so it cannot echo back.</summary>
        private static bool applyingRemote;

        /// <summary>True while another player's quest transition is being applied here.</summary>
        internal static bool ApplyingRemote => applyingRemote;
        private const int MaxJournal = 512;
        private static readonly List<QuestTransition> journal = new List<QuestTransition>();
        private static bool journalTruncated;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "quest sync: sent=" + sent + ", received=" + received +
                   ", applied=" + applied + ", rejected=" + rejected;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            Type questSystem = Plugin.FindGameType("QuestSystemData");
            if (questSystem == null)
            {
                log.LogWarning("QuestSystemData not found; quest progress will not be shared.");
                Enabled = false;
                return;
            }
            Hook(harmony, questSystem, "StartQuest", nameof(StartPostfix));
            Hook(harmony, questSystem, "AwaitQuest", nameof(AwaitPostfix));
            Hook(harmony, questSystem, "CompleteQuest", nameof(CompletePostfix));
            Hook(harmony, questSystem, "CancelQuest", nameof(CancelPostfix));
        }

        private static void Hook(Harmony harmony, Type owner, string method, string handler)
        {
            try
            {
                MethodInfo original = AccessTools.Method(owner, method);
                if (original == null)
                {
                    log.LogWarning("Quest sync: " + method + " not found.");
                    return;
                }
                harmony.Patch(original, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(CoopQuestSync), handler)));
                log.LogInfo("Quest sync: patched " + method + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Quest sync: could not patch " + method + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ capture

        private static void StartPostfix(object[] __args) { Report(OpStart, __args); }
        private static void AwaitPostfix(object[] __args) { Report(OpAwait, __args); }
        private static void CompletePostfix(object[] __args) { Report(OpComplete, __args); }
        private static void CancelPostfix(object[] __args) { Report(OpCancel, __args); }

        /// <summary>
        /// Quest transitions are ordered, so the journal keeps them in the order they happened
        /// and replays them the same way. Only the last transition per quest would be wrong:
        /// the game's own guards expect Start before Complete.
        /// </summary>
        /// <summary>
        /// At the end of a session, either side: the next host numbers from the start again, and this
        /// session's transitions belong to this world — replayed after the host loaded another save,
        /// they would have walked a joiner's quests in that world.
        /// </summary>
        internal static void ResetSession()
        {
            lastSequencePerSender.Clear();
            journal.Clear();
            journalTruncated = false;
        }

        private static void RecordTransition(byte op, string questId)
        {
            journal.Add(new QuestTransition(op, questId));
            if (journal.Count > MaxJournal)
            {
                journal.RemoveAt(0);
                journalTruncated = true;
            }
        }

        internal static int SendSnapshotTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || !netcode.IsHost)
            {
                return 0;
            }
            if (journalTruncated)
            {
                log.LogWarning("Quest journal was truncated at " + MaxJournal +
                               " entries; a joining client may miss early transitions.");
            }
            int count = 0;
            foreach (QuestTransition transition in journal)
            {
                using (var writer = new FastBufferWriter(256, Allocator.Temp))
                {
                    writer.WriteValueSafe((int)netcode.LocalClientId);
                    writer.WriteValueSafe(++outgoingSequence);
                    writer.WriteValueSafe(transition.Op);
                    writer.WriteValueSafe(new FixedString128Bytes(transition.QuestId));
                    netcode.CustomMessagingManager.SendNamedMessage(
                        QuestMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
                sent++;
                count++;
            }
            return count;
        }

        private struct QuestTransition
        {
            internal readonly byte Op;
            internal readonly string QuestId;

            internal QuestTransition(byte op, string questId)
            {
                Op = op;
                QuestId = questId;
            }
        }

        private static void Report(byte op, object[] args)
        {
            if (!Enabled || applyingRemote)
            {
                return;
            }
            try
            {
                // Journalled before the session check, so quest progress made before anyone
                // connected still reaches a client that joins later.
                string questId = args != null && args.Length > 0 ? Convert.ToString(args[0]) : null;
                if (string.IsNullOrEmpty(questId))
                {
                    return;
                }
                RecordTransition(op, questId);

                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode == null || !netcode.IsListening || !netcode.IsConnectedClient)
                {
                    return;
                }

                using (var writer = new FastBufferWriter(256, Allocator.Temp))
                {
                    writer.WriteValueSafe((int)netcode.LocalClientId);
                    writer.WriteValueSafe(++outgoingSequence);
                    writer.WriteValueSafe(op);
                    writer.WriteValueSafe(new FixedString128Bytes(questId));
                    if (netcode.IsHost)
                    {
                        foreach (ulong clientId in netcode.ConnectedClientsIds)
                        {
                            if (clientId != netcode.LocalClientId)
                            {
                                netcode.CustomMessagingManager.SendNamedMessage(
                                    QuestMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                            }
                        }
                    }
                    else
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(
                            QuestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                    }
                }
                sent++;
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not report a quest transition: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ apply

        internal static void Receive(ulong senderClientId, FastBufferReader reader)
        {
            received++;
            try
            {
                int actorClientId;
                int sequence;
                byte op;
                FixedString128Bytes questId;
                reader.ReadValueSafe(out actorClientId);
                reader.ReadValueSafe(out sequence);
                reader.ReadValueSafe(out op);
                reader.ReadValueSafe(out questId);

                int lastSequence;
                if (lastSequencePerSender.TryGetValue(actorClientId, out lastSequence) && sequence <= lastSequence)
                {
                    return;
                }
                lastSequencePerSender[actorClientId] = sequence;

                Apply(op, questId.ToString());

                // Forwarded to the other clients: a client sends only to the server, so the third
                // player would otherwise keep a quest log that quietly diverges.
                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode != null && netcode.IsHost)
                {
                    using (var writer = new FastBufferWriter(256, Allocator.Temp))
                    {
                        writer.WriteValueSafe(actorClientId);
                        writer.WriteValueSafe(++outgoingSequence);
                        writer.WriteValueSafe(op);
                        writer.WriteValueSafe(questId);
                        foreach (ulong clientId in netcode.ConnectedClientsIds)
                        {
                            if (clientId != netcode.LocalClientId && clientId != senderClientId)
                            {
                                netcode.CustomMessagingManager.SendNamedMessage(
                                    QuestMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                            }
                        }
                    }
                    sent++;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read a replicated quest transition: " + ex.Message);
            }
        }

        private static void Apply(byte op, string questId)
        {
            if (string.IsNullOrEmpty(questId))
            {
                return;
            }
            // Journalled here as well: a client-originated transition applied on the host is
            // part of the world's history, and Report cannot see it (applyingRemote is set).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            {
                RecordTransition(op, questId);
            }
            object questSystem = GetQuestSystem();
            if (questSystem == null)
            {
                rejected++;
                return;
            }

            string method;
            switch (op)
            {
                case OpStart: method = "StartQuest"; break;
                case OpAwait: method = "AwaitQuest"; break;
                case OpComplete: method = "CompleteQuest"; break;
                case OpCancel: method = "CancelQuest"; break;
                default: return;
            }

            applyingRemote = true;
            try
            {
                MethodInfo target = AccessTools.Method(questSystem.GetType(), method);
                if (target == null)
                {
                    rejected++;
                    return;
                }
                // CancelQuest takes only the id; the others take an optional delay.
                object[] args = target.GetParameters().Length >= 2
                    ? new object[] { questId, 0f }
                    : new object[] { questId };
                target.Invoke(questSystem, args);
                applied++;
                if (detailedLogsLeft > 0)
                {
                    detailedLogsLeft--;
                    log.LogInfo("Mirrored quest transition: " + method + "('" + questId + "').");
                }
            }
            catch (Exception ex)
            {
                rejected++;
                log.LogWarning("Could not mirror " + method + "('" + questId + "'): " + ex.Message);
            }
            finally
            {
                applyingRemote = false;
            }
        }

        private static object GetQuestSystem()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "questSystemData");
        }
    }
}
