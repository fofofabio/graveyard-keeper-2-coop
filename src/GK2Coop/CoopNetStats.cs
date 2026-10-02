using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Counts what actually goes on the wire, so the cost of more players is measured rather than
    /// argued about. Four players passed every gate with no code change, which says the design
    /// does not assume a player count — it says nothing about what the host is paying.
    ///
    /// The host sends every message to every peer, so its outbound volume is expected to grow
    /// with the number of clients. Whether that matters is a question about bytes per second, and
    /// this is what answers it.
    ///
    /// Counted at <c>CustomMessagingManager.SendNamedMessage</c>, which is the one place every
    /// named message passes through — this mod's and the game's alike.
    /// </summary>
    internal static class CoopNetStats
    {
        private static ManualLogSource log;
        private static long messagesSent;
        private static long bytesSent;
        private static long messagesAtLastReport;
        private static long bytesAtLastReport;
        private static float lastReportAt;
        private static readonly Dictionary<string, long> bytesByName = new Dictionary<string, long>();

        internal static bool Enabled { get; set; }

        internal static float ReportSeconds { get; set; } = 30f;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "net: " + messagesSent + " messages / " + bytesSent + " bytes sent";
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                Type manager = typeof(CustomMessagingManager);
                int patched = 0;
                foreach (MethodInfo method in manager.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (method.Name != "SendNamedMessage")
                    {
                        continue;
                    }
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length < 3 || parameters[0].ParameterType != typeof(string))
                    {
                        continue;
                    }
                    harmony.Patch(method, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(CoopNetStats), nameof(SendPrefix))));
                    patched++;
                }
                if (patched == 0)
                {
                    Enabled = false;
                    log.LogWarning("No SendNamedMessage overload found; network volume will not be measured.");
                    return;
                }
                log.LogInfo("Measuring outbound network volume (" + patched + " send overloads).");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Could not measure network volume: " + ex.Message);
            }
        }

        private static void SendPrefix(string messageName, object __1, FastBufferWriter messageStream)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                // A list recipient is one call but many sends, so it is weighted by its length.
                int recipients = 1;
                var many = __1 as System.Collections.ICollection;
                if (many != null)
                {
                    recipients = many.Count;
                }
                int length = messageStream.Length;
                messagesSent += recipients;
                bytesSent += (long)length * recipients;

                long previous;
                bytesByName.TryGetValue(messageName ?? "?", out previous);
                bytesByName[messageName ?? "?"] = previous + (long)length * recipients;
            }
            catch
            {
                // Never let measurement break sending.
            }
        }

        internal static void Tick()
        {
            if (!Enabled || log == null)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (lastReportAt <= 0f)
            {
                lastReportAt = now;
                return;
            }
            float elapsed = now - lastReportAt;
            if (elapsed < ReportSeconds)
            {
                return;
            }

            long messages = messagesSent - messagesAtLastReport;
            long bytes = bytesSent - bytesAtLastReport;
            lastReportAt = now;
            messagesAtLastReport = messagesSent;
            bytesAtLastReport = bytesSent;

            NetworkManager netcode = NetworkManager.Singleton;
            int peers = 0;
            if (netcode != null && netcode.IsListening && netcode.ConnectedClientsIds != null)
            {
                peers = Math.Max(0, netcode.ConnectedClientsIds.Count - (netcode.IsHost ? 1 : 0));
            }

            string busiest = string.Empty;
            long busiestBytes = 0;
            foreach (KeyValuePair<string, long> entry in bytesByName)
            {
                if (entry.Value > busiestBytes)
                {
                    busiestBytes = entry.Value;
                    busiest = entry.Key;
                }
            }

            log.LogInfo(string.Format(
                "Network volume: {0} msg/s, {1:F0} B/s over {2:F0}s; peers={3}; total {4} msg / {5} bytes; busiest='{6}' ({7} bytes)",
                (long)(messages / elapsed), bytes / elapsed, elapsed, peers, messagesSent, bytesSent, busiest, busiestBytes));
        }
    }
}
