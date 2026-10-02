using System;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// One pool of action gems (<c>tech_red</c>, <c>tech_green</c>, <c>tech_blue</c>) for everyone.
    ///
    /// Gems are earned by work and spent on the progression tree, so both players filling and
    /// drawing on one pool is what makes the tree feel shared. The host's setting decides
    /// (<c>[Coop] ShareActionGems</c>); with it off, each player keeps their own gems as before.
    ///
    /// Joiners send what changed since they last looked (+3 red, −10 blue), never a total, so two
    /// players earning at the same moment both count. The host adds each change to its own values,
    /// which are the pool, and sends the totals to everyone; its own gains go out the same way.
    /// A joiner's unsent gains are sent before the host's totals are written, so none are lost.
    /// </summary>
    internal static class CoopSharedGems
    {
        internal const string GemsMessage = "GK2Coop.Gems.v1";

        private static readonly string[] Gems = { "tech_red", "tech_green", "tech_blue" };
        private const byte TotalsKind = 0;
        private const byte ChangeKind = 1;

        private static ManualLogSource log;
        private static float[] seen;
        private static bool? hostShares;
        private static int changesSent;
        private static int changesApplied;

        /// <summary>This machine's setting; the host's applies to everyone in its game.</summary>
        internal static bool Configured { get; set; } = true;

        /// <summary>Whether gems are pooled in the current game.</summary>
        internal static bool Shared
        {
            get
            {
                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode != null && netcode.IsListening && !netcode.IsHost && hostShares.HasValue)
                {
                    return hostShares.Value;
                }
                return Configured;
            }
        }

        internal static bool IsGem(string type)
        {
            return Array.IndexOf(Gems, type) >= 0;
        }

        internal static string Describe()
        {
            float[] now = Read();
            return "action gems: " + (Shared ? "shared" : "per player") +
                   (now == null ? string.Empty : string.Format(CultureInfo.InvariantCulture, " (red {0}, green {1}, blue {2})", now[0], now[1], now[2])) +
                   ", changes sent=" + changesSent + ", applied=" + changesApplied;
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening || (!netcode.IsHost && !CoopSession.Welcomed))
            {
                seen = null;
                hostShares = null;
                return;
            }
            if (!Shared)
            {
                seen = null;
                return;
            }
            try
            {
                if (netcode.IsHost)
                {
                    float[] now = Read();
                    if (now == null)
                    {
                        return;
                    }
                    if (seen == null || Differs(now, seen))
                    {
                        bool first = seen == null;
                        seen = now;
                        if (!first)
                        {
                            SendTotals(netcode, null, now);
                        }
                    }
                }
                else if (seen != null)
                {
                    SendOwnChanges(netcode);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Action gems: " + Inner(ex).Message);
            }
        }

        /// <summary>Host: the mode and the pool, to a player who just joined.</summary>
        internal static void SendAllTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsHost)
            {
                return;
            }
            float[] now = Read() ?? new float[Gems.Length];
            SendTotals(netcode, clientId, now);
        }

        private static void SendTotals(NetworkManager netcode, ulong? target, float[] totals)
        {
            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteValueSafe(TotalsKind);
                writer.WriteValueSafe(Configured);
                foreach (float value in totals)
                {
                    writer.WriteValueSafe(value);
                }
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId && (!target.HasValue || clientId == target.Value))
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(GemsMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
            }
        }

        private static void SendOwnChanges(NetworkManager netcode)
        {
            float[] now = Read();
            if (now == null || seen == null || !Differs(now, seen))
            {
                return;
            }
            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteValueSafe(ChangeKind);
                writer.WriteValueSafe(true);
                for (int i = 0; i < Gems.Length; i++)
                {
                    writer.WriteValueSafe(now[i] - seen[i]);
                }
                netcode.CustomMessagingManager.SendNamedMessage(GemsMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
            log.LogInfo("Action gems: sent changes " + Change(now, seen) + " to the pool.");
            seen = now;
            changesSent++;
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out byte kind);
                reader.ReadValueSafe(out bool shares);
                float[] values = new float[Gems.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    reader.ReadValueSafe(out values[i]);
                }

                if (kind == TotalsKind && !netcode.IsHost && sender == NetworkManager.ServerClientId)
                {
                    bool announced = !hostShares.HasValue || hostShares.Value != shares;
                    hostShares = shares;
                    if (announced)
                    {
                        log.LogInfo("Action gems: the host plays with " + (shares ? "one shared pool." : "gems per player."));
                    }
                    if (!shares)
                    {
                        seen = null;
                        return;
                    }
                    if (seen != null)
                    {
                        SendOwnChanges(netcode);
                    }
                    Write(values);
                    seen = Read();
                    return;
                }

                if (kind == ChangeKind && netcode.IsHost && sender != netcode.LocalClientId && Shared)
                {
                    float[] now = Read();
                    if (now == null)
                    {
                        return;
                    }
                    float[] pooled = new float[Gems.Length];
                    for (int i = 0; i < pooled.Length; i++)
                    {
                        pooled[i] = Math.Max(0f, now[i] + values[i]);
                    }
                    Write(pooled);
                    seen = Read();
                    changesApplied++;
                    log.LogInfo("Action gems: " + CoopSession.NameFor(sender) + " added " + Change(values, null) + "; pool now red " + pooled[0] + ", green " + pooled[1] + ", blue " + pooled[2] + ".");
                    SendTotals(netcode, null, seen ?? pooled);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Action gems: could not apply a message from " + sender + ": " + Inner(ex).Message);
            }
        }

        private static bool Differs(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return true;
                }
            }
            return false;
        }

        private static string Change(float[] now, float[] before)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < now.Length; i++)
            {
                float delta = before == null ? now[i] : now[i] - before[i];
                if (delta != 0f)
                {
                    parts.Add((delta > 0 ? "+" : string.Empty) + delta + " " + Gems[i].Substring(5));
                }
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts.ToArray());
        }

        private static object Store()
        {
            object player = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            return player == null ? null : CoopDiagnostics.GetMember(player, "res");
        }

        internal static float[] Read()
        {
            object res = Store();
            if (res == null)
            {
                return null;
            }
            MethodInfo getRaw = res.GetType().GetMethod("GetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            float[] values = new float[Gems.Length];
            for (int i = 0; i < Gems.Length; i++)
            {
                values[i] = Convert.ToSingle(getRaw.Invoke(res, new object[] { Gems[i], 0f }));
            }
            return values;
        }

        /// <summary>
        /// Written raw, then each gem's resource system set silently to refresh the HUD — the same
        /// path player profiles and world values use.
        /// </summary>
        internal static void Write(float[] values)
        {
            object res = Store();
            if (res == null)
            {
                return;
            }
            Type resType = res.GetType();
            MethodInfo setRaw = resType.GetMethod("SetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            MethodInfo getSystem = resType.GetMethod("GetSystem", new[] { typeof(string) });
            for (int i = 0; i < Gems.Length; i++)
            {
                setRaw.Invoke(res, new object[] { Gems[i], values[i] });
                try
                {
                    object system = getSystem?.Invoke(res, new object[] { Gems[i] });
                    system?.GetType().GetMethod("Set", new[] { typeof(float), typeof(bool) })?.Invoke(system, new object[] { values[i], false });
                }
                catch (Exception)
                {
                    setRaw.Invoke(res, new object[] { Gems[i], values[i] });
                }
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
