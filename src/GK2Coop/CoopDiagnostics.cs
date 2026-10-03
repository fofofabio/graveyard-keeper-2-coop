using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GK2Coop
{
    /// <summary>
    /// Runtime observation for the co-op prototype. Everything here is read-only; the repairs
    /// live in <see cref="CoopPatches"/>. Output is throttled: shape lines are emitted on change,
    /// position lines on movement, counters as periodic summaries.
    /// </summary>
    internal static class CoopDiagnostics
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource log;
        private static bool unityBridgeInstalled;
        private static readonly Dictionary<string, UnityLogEntry> unityLogSeen = new Dictionary<string, UnityLogEntry>();

        private static string lastShape;
        private static string lastPositions;
        private static float nextCensus;
        private static float nextForcedCensus;
        private static float nextCounterSummary;

        private static int outCommands;
        private static int inCommands;
        private static readonly Dictionary<string, int> outByType = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> inByType = new Dictionary<string, int>();
        private static int moveApplied;
        private static int moveSkipped;
        private static int moveUnresolved;
        private static string lastMoveDetail = "none";
        private static int detailedMoveLogsLeft = 5;

        private sealed class UnityLogEntry
        {
            public float LastLogged;
            public int Suppressed;
        }

        /// <summary>[Diagnostics] DetailedLogs: the detailed logs are written (tests, looking into a problem).</summary>
        internal static bool Detailed { get; set; }

        internal static bool CensusEnabled { get; set; }
        internal static bool TelemetryEnabled { get; set; }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        // ---------------------------------------------------------------- Unity log bridge

        /// <summary>
        /// Forwards the game's own diagnostics into the BepInEx log. All errors and exceptions
        /// pass through; informational messages only when they mention networking or players.
        /// Repeats of the same message are collapsed to one line every five seconds.
        /// </summary>
        internal static void InstallUnityLogBridge()
        {
            if (unityBridgeInstalled)
            {
                return;
            }
            Application.logMessageReceived += OnUnityLog;
            unityBridgeInstalled = true;
            log.LogInfo("Unity log bridge installed (errors always, filtered informational messages).");
        }

        internal static void RemoveUnityLogBridge()
        {
            if (!unityBridgeInstalled)
            {
                return;
            }
            Application.logMessageReceived -= OnUnityLog;
            unityBridgeInstalled = false;
        }

        private static readonly string[] InterestingFragments =
        {
            "connection state", "Destination client", "Spawned player", "clientId", "Client ",
            "Command", "command", "package", "Package", "Network", "network", "coop", "Coop",
            "player", "Player", "RB by position"
        };

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                if (condition == null)
                {
                    return;
                }
                bool isFailure = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
                // The game's informational messages only with the detailed logs; its errors always.
                if (!isFailure && (!Detailed || !InterestingFragments.Any(fragment => condition.IndexOf(fragment, StringComparison.Ordinal) >= 0)))
                {
                    return;
                }

                string key = condition.Length > 160 ? condition.Substring(0, 160) : condition;
                if (unityLogSeen.TryGetValue(key, out UnityLogEntry entry))
                {
                    if (Time.unscaledTime - entry.LastLogged < 5f)
                    {
                        entry.Suppressed++;
                        return;
                    }
                }
                else
                {
                    entry = new UnityLogEntry();
                    unityLogSeen[key] = entry;
                }

                string repeat = entry.Suppressed > 0 ? " (+" + entry.Suppressed + " repeats suppressed)" : string.Empty;
                entry.LastLogged = Time.unscaledTime;
                entry.Suppressed = 0;

                string message = "[game/" + type + "] " + condition + repeat;
                if (isFailure)
                {
                    log.LogWarning(message);
                    // Errors carry a stack trace too, and a library's LogError is often the only
                    // record of where a failure came from — the navigation graph rebuild is one.
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        log.LogWarning("[game/stack] " + stackTrace.Trim().Replace("\n", " | "));
                    }
                }
                else
                {
                    log.LogInfo(message);
                }
            }
            catch
            {
                // Never let diagnostics break the game's logging path.
            }
        }

        // ---------------------------------------------------------------- command telemetry

        internal static void CountOutgoingCommand(string typeName)
        {
            outCommands++;
            int count;
            outByType.TryGetValue(typeName, out count);
            outByType[typeName] = count + 1;
        }

        internal static void CountIncomingCommand(string typeName)
        {
            inCommands++;
            int count;
            inByType.TryGetValue(typeName, out count);
            inByType[typeName] = count + 1;
        }

        internal static void RecordMoveCommand(int playerId, ulong senderClientId, Vector3 position, bool applied, bool resolved)
        {
            if (!applied)
            {
                // Our own echo: the command names us as the mover, so there is nothing to apply.
                moveSkipped++;
            }
            else if (resolved)
            {
                moveApplied++;
            }
            else
            {
                // Dropped before execution, because GetClient would have thrown on it.
                moveUnresolved++;
            }
            lastMoveDetail = "playerId=" + playerId + ", sender=" + senderClientId + ", pos=" + Format(position) +
                             ", applied=" + applied + ", resolved=" + resolved;
            if (applied && detailedMoveLogsLeft > 0)
            {
                detailedMoveLogsLeft--;
                log.LogInfo("Move command applied: " + lastMoveDetail);
            }
        }

        // ---------------------------------------------------------------- periodic driver

        internal static void Tick()
        {
            if (log == null)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (TelemetryEnabled && now >= nextCounterSummary)
            {
                nextCounterSummary = now + 5f;
                EmitCounterSummary();
            }
            if (CensusEnabled && now >= nextCensus)
            {
                nextCensus = now + 1f;
                try
                {
                    EmitCensus(now);
                }
                catch (Exception ex)
                {
                    log.LogWarning("Player census failed: " + ex.Message);
                    nextCensus = now + 10f;
                }
            }
        }

        private static void EmitCounterSummary()
        {
            if (outCommands == 0 && inCommands == 0 && moveApplied == 0 && moveSkipped == 0)
            {
                return;
            }

            string outs = string.Join(",", outByType.Select(pair => pair.Key + "=" + pair.Value).ToArray());
            string ins = string.Join(",", inByType.Select(pair => pair.Key + "=" + pair.Value).ToArray());
            log.LogInfo(CoopPlayerContext.Describe() + "; " + CoopToolSync.Describe() + "; " + CoopDeathLogicFix.Describe() + "; " + CoopWorldSync.Describe() + "; " + CoopQuestSync.Describe() + "; " + CoopDropSync.Describe() + "; " + CoopContainerSync.Describe() + "; " + CoopJoinSnapshot.Describe() + "; " + CoopHostRelay.Describe() + "; " + CoopNetStats.Describe());
            log.LogInfo("Command telemetry: out=" + outCommands + " [" + outs + "]; in=" + inCommands + " [" + ins + "]; " +
                        "move applied=" + moveApplied + ", own-echo=" + moveSkipped + ", dropped-unresolved=" + moveUnresolved +
                        "; last=" + lastMoveDetail);
        }

        // ---------------------------------------------------------------- player census

        private static void EmitCensus(float now)
        {
            Type bodyType = Plugin.FindGameType("PlayerPhysicalBody");
            if (bodyType == null)
            {
                return;
            }

            Type mainGameType = Plugin.FindGameType("MainGame");
            object mainGame = GetStatic(mainGameType, "Instance");
            object save = mainGame == null ? null : GetMember(mainGame, "GameSave");
            object localPlayerData = GetStatic(mainGameType, "PlayerData");

            var identities = new Dictionary<object, string>(ReferenceComparer.Instance);
            if (save != null)
            {
                object hostPlayer = GetMember(save, "hostPlayer");
                if (hostPlayer != null)
                {
                    object data = GetMember(hostPlayer, "playerData");
                    if (data != null)
                    {
                        identities[data] = "host#" + GetMember(hostPlayer, "clientId");
                    }
                }
                var clients = GetMember(save, "clientPlayers") as System.Collections.IEnumerable;
                if (clients != null)
                {
                    foreach (object client in clients)
                    {
                        object data = GetMember(client, "playerData");
                        if (data != null)
                        {
                            identities[data] = "client#" + GetMember(client, "clientId");
                        }
                    }
                }
            }

            var shape = new StringBuilder();
            var positions = new StringBuilder();
            int index = 0;
            foreach (Component body in CoopBodies.All().OrderBy(candidate => candidate.GetInstanceID()))
            {
                index++;
                GameObject go = body.gameObject;
                object bodyData = GetMember(body, "playerData");
                string identity;
                if (bodyData == null)
                {
                    identity = "no-playerdata";
                }
                else
                {
                    string known;
                    identity = identities.TryGetValue(bodyData, out known) ? known : "unregistered";
                    if (ReferenceEquals(bodyData, localPlayerData))
                    {
                        identity += "/local";
                    }
                }

                var rigidbody = GetMember(body, "rb") as Rigidbody;
                Component controller = go.GetComponent("PlayerController");
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                Animator[] animators = go.GetComponentsInChildren<Animator>(true);

                shape.Append("\n  [" + index + "] id=" + body.GetInstanceID() +
                             " path=" + HierarchyPath(go) +
                             " scene=" + go.scene.name +
                             " activeInHierarchy=" + go.activeInHierarchy +
                             " bodyEnabled=" + ((Behaviour)body).enabled +
                             " identity=" + identity +
                             " dataHash=" + (bodyData == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bodyData)) +
                             " kinematic=" + (rigidbody == null ? "no-rb" : rigidbody.isKinematic.ToString()) +
                             " gravity=" + (rigidbody == null ? "n/a" : rigidbody.useGravity.ToString()) +
                             " controller=" + (controller == null ? "missing" : ((Behaviour)controller).enabled ? "enabled" : "disabled") +
                             " renderers=" + renderers.Length + "/" + renderers.Count(r => r.enabled) +
                             " visible=" + renderers.Count(r => r.isVisible) +
                             " animators=" + animators.Length + "/" + animators.Count(a => a.enabled));

                Vector3 dataPosition = bodyData == null ? Vector3.zero : ReadPositionValue(bodyData);
                positions.Append(" [" + index + "]" + identity + " t=" + Format(go.transform.position) + " d=" + Format(dataPosition));
                if (bodyData != null && !ReferenceEquals(bodyData, localPlayerData))
                {
                    // For a remote body this is the gap between what replication asked for and where
                    // the transform actually is. It should stay near zero; sustained growth means
                    // the body has stopped following its replicated data.
                    float drift = (go.transform.position - dataPosition).magnitude;
                    positions.Append(" drift=" + drift.ToString("F2"));
                }
                positions.Append(";");
            }

            string scenes = string.Join(",", Enumerable.Range(0, SceneManager.sceneCount)
                .Select(i => SceneManager.GetSceneAt(i).name).ToArray());
            string shapeLine = "Player census: bodies=" + index + "; scenes=" + scenes + "; " + DescribeConnection() + shape;

            bool forced = now >= nextForcedCensus;
            if (forced || !string.Equals(shapeLine, lastShape, StringComparison.Ordinal))
            {
                nextForcedCensus = now + 30f;
                lastShape = shapeLine;
                log.LogInfo(shapeLine);
            }

            string positionLine = positions.ToString();
            if (!string.Equals(positionLine, lastPositions, StringComparison.Ordinal))
            {
                lastPositions = positionLine;
                log.LogInfo("Player positions:" + positionLine);
            }
        }

        private static string DescribeConnection()
        {
            Type lazyNetwork = Plugin.FindGameType("LazyNetwork");
            object connectionManager = GetStatic(lazyNetwork, "ConnectionManager");
            if (connectionManager == null)
            {
                return "connection=uninitialized";
            }
            object state = GetMember(connectionManager, "CurrentState");
            string destinations = "n/a";
            var clients = state == null ? null : GetMember(state, "DestinationClients") as System.Collections.IEnumerable;
            if (clients != null)
            {
                destinations = string.Join("/", clients.Cast<object>().Select(c => c.ToString()).ToArray());
                if (destinations.Length == 0)
                {
                    destinations = "empty";
                }
            }
            object manager = GetStatic(lazyNetwork, "NetworkManager");
            string coop = manager == null ? "?" : Convert.ToString(GetMember(manager, "IsCoopGame"));
            string myId = manager == null ? "?" : Convert.ToString(GetMember(manager, "MyId"));
            return "connection=" + (state == null ? "null" : state.GetType().Name) +
                   "; destinations=" + destinations + "; isCoop=" + coop + "; myId=" + myId;
        }

        // ---------------------------------------------------------------- helpers

        internal static Vector3 ReadPositionValue(object playerData)
        {
            object position = GetMember(playerData, "position");
            object value = position == null ? null : GetMember(position, "Value");
            return value is Vector3 ? (Vector3)value : Vector3.zero;
        }

        internal static string Format(Vector3 value)
        {
            return "(" + value.x.ToString("F2") + "," + value.y.ToString("F2") + "," + value.z.ToString("F2") + ")";
        }

        internal static string HierarchyPath(GameObject go)
        {
            var parts = new List<string>();
            Transform current = go.transform;
            while (current != null && parts.Count < 8)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts.ToArray());
        }

        /// <summary>The top frames of an exception's stack, on one line, for a warning in a player's log.</summary>
        internal static string FirstFrames(Exception ex, int frames = 3)
        {
            if (ex == null || string.IsNullOrEmpty(ex.StackTrace))
            {
                return "(no stack)";
            }
            string[] lines = ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var picked = new List<string>();
            for (int i = 0; i < lines.Length && picked.Count < frames; i++)
            {
                picked.Add(lines[i].Trim());
            }
            return string.Join(" | ", picked.ToArray());
        }

        internal static object GetMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }
            return Read(Member(target.GetType(), name, false), target);
        }

        internal static object GetStatic(Type type, string name)
        {
            if (type == null)
            {
                return null;
            }
            return Read(Member(type, name, true), null);
        }

        private static object Read(MemberInfo member, object target)
        {
            if (member is PropertyInfo property)
            {
                return property.GetValue(target, null);
            }
            return member is FieldInfo field ? field.GetValue(target) : null;
        }

        private static readonly Dictionary<MemberKey, MemberInfo> memberCache = new Dictionary<MemberKey, MemberInfo>();

        private struct MemberKey : IEquatable<MemberKey>
        {
            internal Type Type;
            internal string Name;
            internal bool Static;

            public bool Equals(MemberKey other)
            {
                return Type == other.Type && Static == other.Static && string.Equals(Name, other.Name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is MemberKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Type.GetHashCode() * 397) ^ Name.GetHashCode() ^ (Static ? 1 : 0);
            }
        }

        /// <summary>
        /// The property (else the field) of that name, looked up once per type: the ticks and the
        /// per-frame station checks read the same members over and over, and each read searched
        /// the type's members again.
        /// </summary>
        private static MemberInfo Member(Type type, string name, bool isStatic)
        {
            var key = new MemberKey { Type = type, Name = name, Static = isStatic };
            lock (memberCache)
            {
                if (memberCache.TryGetValue(key, out MemberInfo cached))
                {
                    return cached;
                }
            }
            BindingFlags flags = isStatic ? AnyStatic : Any;
            MemberInfo found = (MemberInfo)type.GetProperty(name, flags) ?? type.GetField(name, flags);
            lock (memberCache)
            {
                memberCache[key] = found;
            }
            return found;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
