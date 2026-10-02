using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Checks every few seconds during a session that the players in the world match the players
    /// in the session, and repairs what it can prove is wrong.
    ///
    /// Each player is a record (<c>GameSave.hostPlayer</c> / <c>clientPlayers</c>: client id plus
    /// <c>PlayerData</c>) and a body drawing it. The game moves a body by looking up the record for
    /// the sender's client id, so a second record or body for one player is a keeper that stands
    /// still forever — the "Host" and the double of the host that the user saw after hosting a second
    /// time. The lifecycle stops that at its cause; this is the safety net for causes not yet found.
    ///
    /// What it repairs (and logs, once per case):
    /// <list type="bullet">
    /// <item>a record in <c>clientPlayers</c> for our own client id that is not our own player, or
    /// for the host's id on a joiner (the host lives in <c>hostPlayer</c>) — a ghost;</item>
    /// <item>a second record for the same client id — the game only ever moves the first;</item>
    /// <item>on the host, a record for a client that is no longer connected, seen on two checks in
    /// a row (the disconnect callback normally removes it at once);</item>
    /// <item>a second body for the same record.</item>
    /// </list>
    /// It never touches our own record or body, and never destroys a body it cannot tie to a record:
    /// those are only counted, since the game may draw a keeper for its own reasons.
    /// </summary>
    internal static class CoopWatchdog
    {
        private const float IntervalSeconds = 2f;

        private static ManualLogSource log;
        private static float nextCheck;
        private static readonly HashSet<ulong> staleOnce = new HashSet<ulong>();
        private static readonly HashSet<string> reported = new HashSet<string>();
        private static int repairs;
        private static int checks;
        private static string lastState = "not checked";

        internal static bool Enabled { get; set; } = true;

        internal static int Repairs => repairs;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Reset()
        {
            staleOnce.Clear();
            reported.Clear();
            nextCheck = 0f;
        }

        internal static string Describe()
        {
            return "watchdog: checks=" + checks + ", repairs=" + repairs + ", last=" + lastState + "; " + CoopLifecycle.Describe();
        }

        /// <summary>For tests: check now, whatever the interval.</summary>
        internal static string CheckForTest()
        {
            nextCheck = 0f;
            Tick();
            return Describe();
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            if (!Enabled || CoopLifecycle.Role == CoopRole.None || Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + IntervalSeconds;
            try
            {
                if (!InGame())
                {
                    return;
                }
                checks++;
                Check(NetworkManager.Singleton);
            }
            catch (Exception ex)
            {
                Report("error:" + ex.GetType().Name, "Watchdog check failed: " + Plugin.Unwrap(ex).Message);
            }
        }

        private static void Check(NetworkManager netcode)
        {
            if (netcode == null)
            {
                return;
            }
            System.Collections.IList clients = ClientRecords();
            object local = LocalPlayerData();
            if (clients == null || local == null)
            {
                return;
            }
            bool host = netcode.IsHost;
            ulong localId = netcode.LocalClientId;

            // Records.
            var seen = new Dictionary<ulong, object>();
            var drop = new List<KeyValuePair<object, string>>();
            foreach (object record in clients.Cast<object>().ToList())
            {
                object data = CoopDiagnostics.GetMember(record, "playerData");
                if (!TryClientId(record, out ulong id) || data == null)
                {
                    continue;
                }
                if (ReferenceEquals(data, local))
                {
                    if (seen.TryGetValue(id, out object earlier) && !ReferenceEquals(earlier, record))
                    {
                        // Our own record came after a copy: the copy is the ghost.
                        drop.Add(new KeyValuePair<object, string>(earlier, "a second record for client " + id));
                    }
                    seen[id] = record;
                    continue;
                }
                if (id == localId)
                {
                    drop.Add(new KeyValuePair<object, string>(record, "a record for our own client " + id + " that is not our player"));
                    continue;
                }
                if (!host && id == NetworkManager.ServerClientId)
                {
                    drop.Add(new KeyValuePair<object, string>(record, "a second record for the host"));
                    continue;
                }
                if (seen.ContainsKey(id))
                {
                    drop.Add(new KeyValuePair<object, string>(record, "a second record for client " + id));
                    continue;
                }
                seen[id] = record;
            }

            if (host)
            {
                var connected = new HashSet<ulong>(netcode.ConnectedClientsIds);
                var stale = new HashSet<ulong>();
                foreach (KeyValuePair<ulong, object> entry in seen)
                {
                    if (entry.Key == localId || connected.Contains(entry.Key))
                    {
                        continue;
                    }
                    stale.Add(entry.Key);
                    if (staleOnce.Contains(entry.Key))
                    {
                        drop.Add(new KeyValuePair<object, string>(entry.Value, "the record of client " + entry.Key + ", who is no longer connected"));
                    }
                }
                staleOnce.Clear();
                staleOnce.UnionWith(stale);
            }

            foreach (KeyValuePair<object, string> entry in drop)
            {
                object data = CoopDiagnostics.GetMember(entry.Key, "playerData");
                int bodies = ReferenceEquals(data, local) ? 0 : DestroyBodiesOf(data);
                clients.Remove(entry.Key);
                repairs++;
                log.LogWarning("Watchdog removed " + entry.Value + " (and " + bodies + " body/bodies).");
            }

            // Bodies.
            var byData = new Dictionary<object, List<Component>>(ReferenceComparer.Instance);
            int orphans = 0;
            object hostData = HostPlayerData();
            var recordData = new HashSet<object>(ReferenceComparer.Instance);
            foreach (object record in clients)
            {
                object data = CoopDiagnostics.GetMember(record, "playerData");
                if (data != null) recordData.Add(data);
            }
            if (hostData != null) recordData.Add(hostData);

            foreach (Component body in Bodies())
            {
                object data = CoopDiagnostics.GetMember(body, "playerData");
                if (data == null)
                {
                    continue;
                }
                if (!recordData.Contains(data) && !ReferenceEquals(data, local))
                {
                    orphans++;
                    continue;
                }
                if (!byData.TryGetValue(data, out List<Component> list))
                {
                    byData[data] = list = new List<Component>();
                }
                list.Add(body);
            }
            int players = 0;
            foreach (KeyValuePair<object, List<Component>> entry in byData)
            {
                players++;
                if (entry.Value.Count < 2)
                {
                    continue;
                }
                if (ReferenceEquals(entry.Key, local))
                {
                    Report("local-double", "Watchdog: our own player has " + entry.Value.Count + " bodies; left alone.");
                    continue;
                }
                Component keep = entry.Value.FirstOrDefault(b => b.gameObject.activeInHierarchy) ?? entry.Value[0];
                foreach (Component extra in entry.Value)
                {
                    if (!ReferenceEquals(extra, keep))
                    {
                        UnityEngine.Object.Destroy(extra.gameObject);
                        repairs++;
                        log.LogWarning("Watchdog removed a second body of " + Describe(entry.Key) + ".");
                    }
                }
            }
            if (orphans > 0)
            {
                Report("orphans:" + orphans, "Watchdog: " + orphans + " keeper body/bodies belong to no player in the session; left alone.");
            }
            lastState = "players=" + players + ", records=" + clients.Count + ", orphans=" + orphans;
        }

        private static string Describe(object data)
        {
            return CoopHud.TryResolveClientId(data, out ulong id) ? CoopSession.NameFor(id) + " (client " + id + ")" : "a player";
        }

        private static void Report(string key, string message)
        {
            if (reported.Add(key))
            {
                log.LogWarning(message);
            }
        }

        private static bool TryClientId(object record, out ulong id)
        {
            id = 0;
            object value = CoopDiagnostics.GetMember(record, "clientId");
            if (value == null)
            {
                return false;
            }
            id = Convert.ToUInt64(value);
            return true;
        }

        private static bool InGame()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            return mainGame != null && string.Equals(Convert.ToString(CoopDiagnostics.GetMember(mainGame, "gameState")), "InGame", StringComparison.Ordinal);
        }

        private static object Save()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            return mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
        }

        internal static System.Collections.IList ClientRecords()
        {
            object save = Save();
            return save == null ? null : CoopDiagnostics.GetMember(save, "clientPlayers") as System.Collections.IList;
        }

        private static object HostPlayerData()
        {
            object save = Save();
            object hostPlayer = save == null ? null : CoopDiagnostics.GetMember(save, "hostPlayer");
            return hostPlayer == null ? null : CoopDiagnostics.GetMember(hostPlayer, "playerData");
        }

        internal static object LocalPlayerData()
        {
            return CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
        }

        private static IEnumerable<Component> Bodies()
        {
            Type bodyType = Plugin.FindGameType("PlayerPhysicalBody");
            if (bodyType == null)
            {
                return Enumerable.Empty<Component>();
            }
            return Resources.FindObjectsOfTypeAll(bodyType).OfType<Component>()
                .Where(body => body != null && body.gameObject.scene.IsValid());
        }

        /// <summary>Destroys every body drawing this player. Never our own.</summary>
        internal static int DestroyBodiesOf(object data)
        {
            if (data == null || ReferenceEquals(data, LocalPlayerData()))
            {
                return 0;
            }
            int destroyed = 0;
            foreach (Component body in Bodies().ToList())
            {
                if (ReferenceEquals(CoopDiagnostics.GetMember(body, "playerData"), data))
                {
                    UnityEngine.Object.Destroy(body.gameObject);
                    destroyed++;
                }
            }
            return destroyed;
        }

        /// <summary>A joiner: another joiner has left. Their record and bodies go; never ours.</summary>
        internal static int RemovePeer(ulong clientId)
        {
            System.Collections.IList clients = ClientRecords();
            object local = LocalPlayerData();
            if (clients == null)
            {
                return 0;
            }
            int destroyed = 0;
            foreach (object record in clients.Cast<object>().ToList())
            {
                object data = CoopDiagnostics.GetMember(record, "playerData");
                if (!TryClientId(record, out ulong id) || id != clientId || data == null || ReferenceEquals(data, local))
                {
                    continue;
                }
                destroyed += DestroyBodiesOf(data);
                clients.Remove(record);
            }
            return destroyed;
        }

        /// <summary>At a session's end: every player body that is not ours.</summary>
        internal static int DestroyOtherBodies()
        {
            object local = LocalPlayerData();
            if (local == null)
            {
                return 0;
            }
            List<Component> bodies = Bodies().ToList();
            // Only when our own body is recognised: otherwise "ours" is not known for certain (the
            // game may be between worlds) and nothing is touched.
            if (!bodies.Any(body => ReferenceEquals(CoopDiagnostics.GetMember(body, "playerData"), local)))
            {
                return 0;
            }
            int destroyed = 0;
            foreach (Component body in bodies)
            {
                object data = CoopDiagnostics.GetMember(body, "playerData");
                if (data != null && !ReferenceEquals(data, local))
                {
                    UnityEngine.Object.Destroy(body.gameObject);
                    destroyed++;
                }
            }
            return destroyed;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
