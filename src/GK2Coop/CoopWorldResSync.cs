using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// World values in the player's resource store are the same for everyone.
    ///
    /// The game keeps the player's wallet and energy in the same <c>GameRes</c> store as values
    /// that belong to the world: village and NPC reputation, the congregation (<c>global_ppl</c>),
    /// <c>happiness</c>, <c>sermon_ready</c>, body counts, farming modifiers, tutorial availability.
    /// Those changed only on the machine where something happened — a sermon, a reputation reward —
    /// so the two worlds drifted apart.
    ///
    /// Each machine compares the store with what it last saw every couple of seconds and sends
    /// changed world values; the host applies a joiner's and passes them on; the last change wins.
    /// Left out: each player's own values (<see cref="CoopPlayerProfiles.PersonalResources"/>,
    /// stamina), zone ratings (<c>wz_*</c>), which the game computes from the world itself, and the
    /// action gems, which <see cref="CoopSharedGems"/> pools by adding up changes instead.
    /// </summary>
    internal static class CoopWorldResSync
    {
        internal const string WorldResMessage = "GK2Coop.WorldRes.v1";

        private static ManualLogSource log;
        private static readonly Dictionary<string, float> seen = new Dictionary<string, float>();
        private static object baselineOf;
        private static float nextCheck;
        private static int sent;
        private static int applied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "world values sync: sent=" + sent + ", applied=" + applied;
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        private static bool IsWorldValue(string type)
        {
            if (string.IsNullOrEmpty(type) || type.StartsWith("wz_", StringComparison.Ordinal) || type == "stamina" || CoopSharedGems.IsGem(type))
            {
                return false;
            }
            return Array.IndexOf(CoopPlayerProfiles.PersonalResources, type) < 0;
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || (!netcode.IsHost && !CoopSession.Welcomed))
            {
                seen.Clear();
                baselineOf = null;
                return;
            }
            if (UnityEngine.Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = UnityEngine.Time.unscaledTime + 2f;
            try
            {
                SendChanges(netcode);
            }
            catch (Exception ex)
            {
                log.LogWarning("World values sync: " + Inner(ex).Message);
                nextCheck = UnityEngine.Time.unscaledTime + 30f;
            }
        }

        /// <summary>Host: every world value, to a player who just joined.</summary>
        internal static void SendAllTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            object res = Store();
            if (!Enabled || netcode == null || !netcode.IsHost || res == null)
            {
                return;
            }
            try
            {
                Send(netcode, clientId, null, new List<KeyValuePair<string, float>>(Read(res)));
            }
            catch (Exception ex)
            {
                log.LogWarning("World values sync: could not send the host's values to client " + clientId + ": " + Inner(ex).Message);
            }
        }

        private static void SendChanges(NetworkManager netcode)
        {
            object res = Store();
            if (res == null)
            {
                return;
            }
            if (!ReferenceEquals(baselineOf, res))
            {
                Rebaseline(res);
                return;
            }
            var changes = new List<KeyValuePair<string, float>>();
            foreach (KeyValuePair<string, float> value in Read(res))
            {
                if (!seen.TryGetValue(value.Key, out float previous) || previous != value.Value)
                {
                    seen[value.Key] = value.Value;
                    changes.Add(value);
                }
            }
            if (changes.Count == 0)
            {
                return;
            }
            Send(netcode, netcode.IsHost ? (ulong?)null : NetworkManager.ServerClientId, null, changes);
            log.LogInfo("World values sync: shared " + Preview(changes) + ".");
        }

        private static void Send(NetworkManager netcode, ulong? target, ulong? except, List<KeyValuePair<string, float>> values)
        {
            var text = new StringBuilder();
            foreach (KeyValuePair<string, float> value in values)
            {
                text.Append(value.Key).Append('\t').Append(value.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            byte[] compressed = Compress(Encoding.UTF8.GetBytes(text.ToString()));
            using (var writer = new FastBufferWriter(16 + compressed.Length, Allocator.Temp))
            {
                writer.WriteValueSafe(compressed.Length);
                writer.WriteBytesSafe(compressed, compressed.Length);
                NetworkDelivery delivery = compressed.Length > 1000 ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.ReliableSequenced;
                if (target.HasValue)
                {
                    netcode.CustomMessagingManager.SendNamedMessage(WorldResMessage, target.Value, writer, delivery);
                }
                else
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(WorldResMessage, clientId, writer, delivery);
                        }
                    }
                }
            }
            sent += values.Count;
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int length);
                if (length <= 0 || length > 256 * 1024)
                {
                    return;
                }
                byte[] compressed = new byte[length];
                reader.ReadBytesSafe(ref compressed, length);
                var values = new List<KeyValuePair<string, float>>();
                foreach (string line in Encoding.UTF8.GetString(Decompress(compressed)).Split('\n'))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0 && IsWorldValue(line.Substring(0, tab)) &&
                        float.TryParse(line.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    {
                        values.Add(new KeyValuePair<string, float>(line.Substring(0, tab), value));
                    }
                }
                object res = Store();
                if (res == null || values.Count == 0)
                {
                    return;
                }
                // Our own unsent changes first, so recording the received values cannot swallow them.
                if (ReferenceEquals(baselineOf, res))
                {
                    SendChanges(netcode);
                }
                int changed = Apply(res, values);
                if (netcode.IsHost && sender != netcode.LocalClientId)
                {
                    Send(netcode, null, sender, values);
                }
                if (changed > 0)
                {
                    applied += changed;
                    log.LogInfo("World values sync: applied " + Preview(values) + " from " + (sender == NetworkManager.ServerClientId ? "the host" : "client " + sender) + ".");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("World values sync: could not apply values from " + sender + ": " + Inner(ex).Message);
            }
        }

        /// <summary>
        /// Written raw, then each value's resource system set silently to refresh its listeners —
        /// the same path player profiles use, since going through <c>GameRes.Set</c> clamps against
        /// balance expressions that can throw mid-update.
        /// </summary>
        private static int Apply(object res, List<KeyValuePair<string, float>> values)
        {
            Type resType = res.GetType();
            MethodInfo setRaw = resType.GetMethod("SetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            MethodInfo getRaw = resType.GetMethod("GetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            MethodInfo getSystem = resType.GetMethod("GetSystem", new[] { typeof(string) });
            int changed = 0;
            foreach (KeyValuePair<string, float> value in values)
            {
                float current = Convert.ToSingle(getRaw.Invoke(res, new object[] { value.Key, 0f }));
                seen[value.Key] = value.Value;
                if (current == value.Value)
                {
                    continue;
                }
                setRaw.Invoke(res, new object[] { value.Key, value.Value });
                try
                {
                    object system = getSystem?.Invoke(res, new object[] { value.Key });
                    system?.GetType().GetMethod("Set", new[] { typeof(float), typeof(bool) })?.Invoke(system, new object[] { value.Value, false });
                }
                catch (Exception)
                {
                    setRaw.Invoke(res, new object[] { value.Key, value.Value });
                }
                changed++;
            }
            return changed;
        }

        private static void Rebaseline(object res)
        {
            seen.Clear();
            foreach (KeyValuePair<string, float> value in Read(res))
            {
                seen[value.Key] = value.Value;
            }
            baselineOf = res;
        }

        private static IEnumerable<KeyValuePair<string, float>> Read(object res)
        {
            MethodInfo getRaw = res.GetType().GetMethod("GetWithoutSystemsCheck", new[] { typeof(string), typeof(float) });
            if (CoopDiagnostics.GetMember(res, "TypesList") is IEnumerable types)
            {
                foreach (object type in new List<object>(Cast(types)))
                {
                    string name = Convert.ToString(type);
                    if (IsWorldValue(name))
                    {
                        yield return new KeyValuePair<string, float>(name, Convert.ToSingle(getRaw.Invoke(res, new object[] { name, 0f })));
                    }
                }
            }
        }

        private static IEnumerable<object> Cast(IEnumerable values)
        {
            foreach (object value in values)
            {
                yield return value;
            }
        }

        private static object Store()
        {
            object player = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            return player == null ? null : CoopDiagnostics.GetMember(player, "res");
        }

        private static string Preview(List<KeyValuePair<string, float>> values)
        {
            var parts = new List<string>();
            for (int i = 0; i < values.Count && i < 5; i++)
            {
                parts.Add(values[i].Key + "=" + values[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(", ", parts.ToArray()) + (values.Count > 5 ? ", … (" + values.Count + ")" : string.Empty);
        }

        private static byte[] Compress(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
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
