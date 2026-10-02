using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Streams the client's plugin log to the host over the session that is already open, so the
    /// host's own log file contains both sides of a test. Without this, reading the guest's
    /// diagnostics means a manual file transfer out of the VM for every single change.
    ///
    /// Lines are queued from plugin startup, so the backlog that accumulates before the connection
    /// exists is delivered once the client connects.
    /// </summary>
    internal static class CoopLogRelay
    {
        internal const string LogMessage = "GK2Coop.Log.v1";

        /// <summary>Kept under the transport's payload limit with room for the count prefix.</summary>
        private const int MaxBatchBytes = 900;
        private const int MaxQueuedLines = 600;
        private const float FlushInterval = 0.5f;

        private static ManualLogSource log;
        private static readonly Queue<string> pending = new Queue<string>();
        private static float nextFlush;
        private static int droppedLines;
        private static bool subscribed;

        internal static bool Enabled { get; set; }

        internal static void Init(ManualLogSource source)
        {
            log = source;
            if (subscribed || source == null)
            {
                return;
            }
            source.LogEvent += OnLogEvent;
            subscribed = true;
        }

        internal static void Shutdown()
        {
            if (subscribed && log != null)
            {
                log.LogEvent -= OnLogEvent;
                subscribed = false;
            }
            pending.Clear();
        }

        private static void OnLogEvent(object sender, LogEventArgs args)
        {
            if (!Enabled || args == null || args.Data == null)
            {
                return;
            }
            // Only the client relays, so a host writing "[guest] ..." can never loop back.
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsHost)
            {
                return;
            }
            string line = args.Level + ": " + args.Data;
            if (line.Length > 480)
            {
                line = line.Substring(0, 480) + "…";
            }
            if (pending.Count >= MaxQueuedLines)
            {
                pending.Dequeue();
                droppedLines++;
            }
            pending.Enqueue(line);
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            if (!Enabled ||
                pending.Count == 0 ||
                Time.unscaledTime < nextFlush ||
                NetworkManager.Singleton == null ||
                NetworkManager.Singleton.IsHost ||
                !NetworkManager.Singleton.IsConnectedClient ||
                NetworkManager.Singleton.CustomMessagingManager == null)
            {
                return;
            }
            nextFlush = Time.unscaledTime + FlushInterval;

            try
            {
                if (droppedLines > 0)
                {
                    pending.Enqueue("Warning: " + droppedLines + " relayed log lines were dropped (queue full).");
                    droppedLines = 0;
                }

                var batch = new List<string>();
                int bytes = 0;
                while (pending.Count > 0 && batch.Count < 12)
                {
                    string next = pending.Peek();
                    int cost = next.Length + 4;
                    if (bytes + cost > MaxBatchBytes && batch.Count > 0)
                    {
                        break;
                    }
                    batch.Add(pending.Dequeue());
                    bytes += cost;
                }
                if (batch.Count == 0)
                {
                    return;
                }

                using (var writer = new FastBufferWriter(MaxBatchBytes + 128, Allocator.Temp))
                {
                    writer.WriteValueSafe(batch.Count);
                    foreach (string line in batch)
                    {
                        writer.WriteValueSafe(new FixedString512Bytes(line));
                    }
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                        LogMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
            catch (Exception ex)
            {
                // Never let the relay disturb the session; back off and keep playing.
                Enabled = false;
                UnityEngine.Debug.LogWarning("GK2Coop log relay disabled after a failure: " + ex.Message);
            }
        }

        /// <summary>Host side: write the client's lines into this machine's log.</summary>
        internal static void Receive(ulong senderClientId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                return;
            }
            try
            {
                int count;
                reader.ReadValueSafe(out count);
                if (count <= 0 || count > 64)
                {
                    return;
                }
                for (int index = 0; index < count; index++)
                {
                    FixedString512Bytes line;
                    reader.ReadValueSafe(out line);
                    log.LogInfo("[guest " + senderClientId + "] " + line);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read relayed guest log lines: " + ex.Message);
            }
        }
    }
}
