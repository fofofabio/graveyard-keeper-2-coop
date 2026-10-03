using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Opt-in profiler (<c>[Diagnostics] Profiler</c>, off for players). Times the mod's per-frame
    /// entry points, every Harmony patch the mod owns and every named-message handler (the
    /// game's too), and records the frame-time distribution, garbage collections and the network
    /// traffic by message and by peer. Reports every 10 seconds in the log; tests read the totals
    /// since the last reset through the probe ("perf").
    ///
    /// The timing works by patching the mod's own patch methods and entry points with a
    /// Stopwatch prefix and finalizer, so nothing in the measured code changes. "mod ms/frame" is
    /// the sum of the outermost mod sections in a frame (a section inside another is not counted
    /// twice). Sections of the game (Netcode's update, the game's message handlers) are listed
    /// but not added to it.
    /// </summary>
    internal static class CoopProfiler
    {
        private const float ReportSeconds = 10f;
        private const int TopSections = 30;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Per-frame methods of the mod, by name, in any of its types.</summary>
        private static readonly HashSet<string> EntryNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Update", "LateUpdate", "OnGUI", "Tick", "UpdateGameUi", "Draw", "DrawPlain",
            "RestyleOnLanguageChange", "PlaceHud", "VerifyNotLeaked", "UpdatePad", "RefreshReport",
            "PollNetworkState", "DriveSessionAutomation", "PumpSaveTransfer", "DriveAutoStart",
            "DriveConnectRetry", "DriveMovementProbe", "DriveClientStartDiagnostics",
            "TryRunConfiguredStartupAction", "IsOnMainMenu", "EmitCensus", "EmitCounterSummary",
            "OnUnityLog", "OnLogEvent", "LogSnapshot", "RunSecondTicks", "WriteSnapshot",
            // Inside the heavier ticks: where their time goes.
            "Serialize", "SerializeInventory", "SendState", "SendInventory", "Classify", "Search", "Pump",
        };

        private static ManualLogSource log;
        private static Harmony harmony;
        private static readonly HashSet<MethodBase> timed = new HashSet<MethodBase>();
        private static readonly Dictionary<MethodBase, Stat> byMethod = new Dictionary<MethodBase, Stat>();

        private sealed class Stat
        {
            internal string Name;
            internal bool Game;
            internal long Calls;
            internal long Ticks;
            internal long Max;
            internal long Bytes;

            internal void Add(Stat other)
            {
                Calls += other.Calls;
                Ticks += other.Ticks;
                Max = Math.Max(Max, other.Max);
                Bytes += other.Bytes;
            }

            internal void Clear()
            {
                Calls = 0;
                Ticks = 0;
                Max = 0;
                Bytes = 0;
            }
        }

        /// <summary>The counts of one stretch of time: the last 10 seconds, or everything since a reset.</summary>
        private sealed class Window
        {
            internal readonly Dictionary<string, Stat> Sections = new Dictionary<string, Stat>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Stat> Out = new Dictionary<string, Stat>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Stat> In = new Dictionary<string, Stat>(StringComparer.Ordinal);
            internal readonly Dictionary<ulong, Stat> OutPeers = new Dictionary<ulong, Stat>();
            internal readonly Dictionary<ulong, Stat> InPeers = new Dictionary<ulong, Stat>();
            internal readonly List<float> FrameMs = new List<float>();
            internal readonly List<float> ModMs = new List<float>();
            internal int Gc0, Gc1, Gc2;
            internal float StartedAt;

            internal void Begin()
            {
                Sections.Clear();
                Out.Clear();
                In.Clear();
                OutPeers.Clear();
                InPeers.Clear();
                FrameMs.Clear();
                ModMs.Clear();
                Gc0 = GC.CollectionCount(0);
                Gc1 = GC.CollectionCount(1);
                Gc2 = GC.CollectionCount(2);
                StartedAt = Time.realtimeSinceStartup;
            }
        }

        // The hot counters (cleared at each report) and the two windows they are merged into.
        private static readonly Dictionary<string, Stat> outNow = new Dictionary<string, Stat>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Stat> inNow = new Dictionary<string, Stat>(StringComparer.Ordinal);
        private static readonly Dictionary<ulong, Stat> outPeersNow = new Dictionary<ulong, Stat>();
        private static readonly Dictionary<ulong, Stat> inPeersNow = new Dictionary<ulong, Stat>();
        private static readonly Window recent = new Window();
        private static readonly Window total = new Window();

        // Per thread: a BepInEx log event can come from another thread; only the main thread's
        // outermost sections add up to the frame's mod time.
        [ThreadStatic] private static int modDepth;
        private static int mainThread;
        private static long frameModTicks;
        private static float nextReport;
        private static float nextScan;
        private static int scans;
        private static string lastReport = "no report yet";

        internal static bool Enabled { get; private set; }

        /// <summary>From the plugin's Awake, after every patch is in place.</summary>
        internal static void Install(ManualLogSource source, GameObject host, bool enabled)
        {
            log = source;
            Enabled = enabled;
            mainThread = Thread.CurrentThread.ManagedThreadId;
            if (!enabled)
            {
                return;
            }
            try
            {
                harmony = new Harmony(Plugin.Id + ".profiler");
                PatchNetwork();
                Scan();
                host.AddComponent<Sampler>();
                recent.Begin();
                total.Begin();
                nextReport = Time.realtimeSinceStartup + ReportSeconds;
                log.LogInfo("Profiler on: " + timed.Count + " methods timed; a report every " + ReportSeconds + " s.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Profiler could not start: " + ex);
            }
        }

        // ---------------------------------------------------------------- what is timed

        /// <summary>
        /// Times every patch method the mod owns and the mod's per-frame entry points. Patches
        /// added later (some are installed when a session starts) are picked up by later scans.
        /// </summary>
        private static void Scan()
        {
            scans++;
            int added = 0;
            var modPrefix = new HarmonyMethod(AccessTools.Method(typeof(CoopProfiler), nameof(ModPrefix)));
            var modFinalizer = new HarmonyMethod(AccessTools.Method(typeof(CoopProfiler), nameof(ModFinalizer)));
            var targets = new List<MethodInfo>();
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info = Harmony.GetPatchInfo(original);
                if (info == null)
                {
                    continue;
                }
                foreach (Patch patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                {
                    if (patch.owner != null && patch.owner.StartsWith(Plugin.Id, StringComparison.Ordinal) &&
                        !patch.owner.EndsWith(".profiler", StringComparison.Ordinal) &&
                        patch.PatchMethod != null && patch.PatchMethod.DeclaringType != typeof(CoopProfiler))
                    {
                        targets.Add(patch.PatchMethod);
                        Name(patch.PatchMethod, ShortName(patch.PatchMethod) + " (" + original.DeclaringType?.Name + "." + original.Name + ")", false);
                    }
                }
            }
            if (scans == 1)
            {
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (Type type in typeof(CoopProfiler).Assembly.GetTypes())
                {
                    if (type == typeof(CoopProfiler) || type.IsGenericTypeDefinition || type.IsInterface)
                    {
                        continue;
                    }
                    foreach (MethodInfo method in type.GetMethods(all))
                    {
                        if (EntryNames.Contains(method.Name) && !method.IsAbstract && !method.ContainsGenericParameters &&
                            method.GetMethodBody() != null)
                        {
                            targets.Add(method);
                            Name(method, ShortName(method), false);
                        }
                    }
                }
            }
            foreach (MethodInfo method in targets)
            {
                if (timed.Contains(method))
                {
                    continue;
                }
                timed.Add(method);
                try
                {
                    harmony.Patch(method, prefix: modPrefix, finalizer: modFinalizer);
                    added++;
                }
                catch (Exception ex)
                {
                    log.LogWarning("Profiler: could not time " + ShortName(method) + ": " + ex.Message);
                }
            }
            if (added > 0 && scans > 1)
            {
                log.LogInfo("Profiler: " + added + " more methods timed (" + timed.Count + " in all).");
            }
        }

        /// <summary>Netcode's update (the transport, message handling, sends), outbound counts, and a timer around every message handler.</summary>
        private static void PatchNetwork()
        {
            var gamePrefix = new HarmonyMethod(AccessTools.Method(typeof(CoopProfiler), nameof(GamePrefix)));
            var gameFinalizer = new HarmonyMethod(AccessTools.Method(typeof(CoopProfiler), nameof(GameFinalizer)));
            MethodInfo update = AccessTools.Method(typeof(NetworkManager), "NetworkUpdate", new[] { typeof(NetworkUpdateStage) });
            if (update != null)
            {
                Name(update, "Netcode.NetworkUpdate", true);
                timed.Add(update);
                harmony.Patch(update, prefix: gamePrefix, finalizer: gameFinalizer);
            }
            foreach (MethodInfo method in typeof(CustomMessagingManager).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "SendNamedMessage" && parameters.Length >= 3 && parameters[0].ParameterType == typeof(string))
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(CoopProfiler), nameof(SendPrefix))));
                }
                else if (method.Name == "RegisterNamedMessageHandler" && parameters.Length == 2)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(CoopProfiler), nameof(RegisterPrefix))));
                }
            }
        }

        private static void Name(MethodBase method, string name, bool game)
        {
            if (!byMethod.ContainsKey(method))
            {
                byMethod[method] = new Stat { Name = name, Game = game };
            }
        }

        private static string ShortName(MethodBase method)
        {
            string type = method.DeclaringType == null ? "?" : method.DeclaringType.Name;
            return type + "." + method.Name;
        }

        // ---------------------------------------------------------------- the timers

        private static void ModPrefix(out long __state)
        {
            modDepth++;
            __state = Stopwatch.GetTimestamp();
        }

        private static void ModFinalizer(long __state, MethodBase __originalMethod)
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            if (--modDepth == 0 && Thread.CurrentThread.ManagedThreadId == mainThread)
            {
                frameModTicks += elapsed;
            }
            Record(__originalMethod, elapsed);
        }

        private static void GamePrefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void GameFinalizer(long __state, MethodBase __originalMethod)
        {
            Record(__originalMethod, Stopwatch.GetTimestamp() - __state);
        }

        private static void Record(MethodBase method, long elapsed)
        {
            if (method == null || !byMethod.TryGetValue(method, out Stat stat))
            {
                return;
            }
            stat.Calls++;
            stat.Ticks += elapsed;
            if (elapsed > stat.Max)
            {
                stat.Max = elapsed;
            }
        }

        private static void SendPrefix(string messageName, object __1, FastBufferWriter messageStream)
        {
            try
            {
                int length = messageStream.Length;
                string name = messageName ?? "?";
                if (__1 is ulong one)
                {
                    Count(outNow, name, length, 1);
                    Count(outPeersNow, one, length, 1);
                }
                else if (__1 is IEnumerable<ulong> many)
                {
                    foreach (ulong peer in many)
                    {
                        Count(outNow, name, length, 1);
                        Count(outPeersNow, peer, length, 1);
                    }
                }
            }
            catch
            {
                // Never let measuring break sending.
            }
        }

        private static void RegisterPrefix(string __0, ref CustomMessagingManager.HandleNamedMessageDelegate __1)
        {
            CustomMessagingManager.HandleNamedMessageDelegate handler = __1;
            string name = __0 ?? "?";
            if (handler == null)
            {
                return;
            }
            bool mine = name.StartsWith("GK2Coop", StringComparison.Ordinal);
            __1 = (sender, reader) =>
            {
                long start = Stopwatch.GetTimestamp();
                if (mine)
                {
                    modDepth++;
                }
                try
                {
                    handler(sender, reader);
                }
                finally
                {
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    if (mine && --modDepth == 0)
                    {
                        frameModTicks += elapsed;
                    }
                    Stat stat = Count(inNow, name, reader.Length, 1);
                    stat.Ticks += elapsed;
                    if (elapsed > stat.Max)
                    {
                        stat.Max = elapsed;
                    }
                    Count(inPeersNow, sender, reader.Length, 1);
                }
            };
        }

        private static Stat Count<TKey>(Dictionary<TKey, Stat> into, TKey key, long bytes, long calls)
        {
            if (!into.TryGetValue(key, out Stat stat))
            {
                stat = new Stat { Name = Convert.ToString(key, Inv) };
                into[key] = stat;
            }
            stat.Calls += calls;
            stat.Bytes += bytes;
            return stat;
        }

        // ---------------------------------------------------------------- frames and reports

        private sealed class Sampler : MonoBehaviour
        {
            private void Update()
            {
                OnFrame();
            }
        }

        private static void OnFrame()
        {
            recent.FrameMs.Add(Time.unscaledDeltaTime * 1000f);
            recent.ModMs.Add((float)(frameModTicks * TicksToMs));
            frameModTicks = 0;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan && scans < 4)
            {
                // At 30 s, 2 and 5 minutes: patches installed once a session runs.
                nextScan = now + (scans == 1 ? 30f : scans == 2 ? 120f : 300f);
                if (scans > 1 || now > 20f)
                {
                    Scan();
                }
            }
            if (now >= nextReport)
            {
                nextReport = now + ReportSeconds;
                try
                {
                    Report();
                }
                catch (Exception ex)
                {
                    log.LogWarning("Profiler report failed: " + ex.Message);
                }
            }
        }

        private static void Report()
        {
            // Move the hot counters into the last-10-seconds window, then into the totals.
            foreach (Stat stat in byMethod.Values)
            {
                if (stat.Calls > 0)
                {
                    Merge(recent.Sections, stat.Name, stat);
                    stat.Clear();
                }
            }
            MoveCounts(outNow, recent.Out);
            MoveCounts(inNow, recent.In);
            MoveCounts(outPeersNow, recent.OutPeers);
            MoveCounts(inPeersNow, recent.InPeers);

            lastReport = Describe(recent, "last " + ReportSeconds.ToString("0", Inv) + " s");
            log.LogInfo(lastReport);

            foreach (KeyValuePair<string, Stat> entry in recent.Sections) Merge(total.Sections, entry.Key, entry.Value);
            foreach (KeyValuePair<string, Stat> entry in recent.Out) Merge(total.Out, entry.Key, entry.Value);
            foreach (KeyValuePair<string, Stat> entry in recent.In) Merge(total.In, entry.Key, entry.Value);
            foreach (KeyValuePair<ulong, Stat> entry in recent.OutPeers) Merge(total.OutPeers, entry.Key, entry.Value);
            foreach (KeyValuePair<ulong, Stat> entry in recent.InPeers) Merge(total.InPeers, entry.Key, entry.Value);
            total.FrameMs.AddRange(recent.FrameMs);
            total.ModMs.AddRange(recent.ModMs);
            recent.Begin();
        }

        private static void Merge<TKey>(Dictionary<TKey, Stat> into, TKey key, Stat from)
        {
            if (!into.TryGetValue(key, out Stat stat))
            {
                stat = new Stat { Name = from.Name, Game = from.Game };
                into[key] = stat;
            }
            stat.Add(from);
        }

        private static void MoveCounts<TKey>(Dictionary<TKey, Stat> from, Dictionary<TKey, Stat> into)
        {
            foreach (KeyValuePair<TKey, Stat> entry in from)
            {
                if (entry.Value.Calls > 0)
                {
                    Merge(into, entry.Key, entry.Value);
                    entry.Value.Clear();
                }
            }
        }

        private static string Describe(Window window, string label)
        {
            float seconds = Math.Max(0.001f, Time.realtimeSinceStartup - window.StartedAt);
            var text = new StringBuilder(2048);
            text.Append("Perf (").Append(label).Append(", ").Append(seconds.ToString("0", Inv)).Append(" s): ");
            text.Append(FrameLine(window.FrameMs, seconds));
            text.Append("; mod ").Append(ModLine(window.ModMs));
            text.Append("; GC gen0/1/2 +").Append(GC.CollectionCount(0) - window.Gc0).Append('/')
                .Append(GC.CollectionCount(1) - window.Gc1).Append('/').Append(GC.CollectionCount(2) - window.Gc2)
                .Append(", heap ").Append((GC.GetTotalMemory(false) / 1048576L).ToString(Inv)).Append(" MB");
            text.Append("; ").Append(Session());

            text.Append("\n  sections (ms total / calls / max ms):");
            foreach (Stat stat in window.Sections.Values.OrderByDescending(s => s.Ticks).Take(TopSections))
            {
                text.Append("\n    ").Append(stat.Game ? "[game] " : string.Empty).Append(stat.Name).Append(' ')
                    .Append(Ms(stat.Ticks)).Append(" / ").Append(stat.Calls).Append(" / ").Append(Ms(stat.Max));
            }
            text.Append("\n  net out: ").Append(NetLine(window.Out, seconds, false)).Append(" | peers ").Append(PeerLine(window.OutPeers, seconds));
            text.Append("\n  net in: ").Append(NetLine(window.In, seconds, true)).Append(" | peers ").Append(PeerLine(window.InPeers, seconds));
            return text.ToString();
        }

        private static string FrameLine(List<float> frames, float seconds)
        {
            if (frames.Count == 0)
            {
                return "frames 0";
            }
            float[] sorted = frames.ToArray();
            Array.Sort(sorted);
            double sum = 0;
            int over50 = 0, over100 = 0;
            foreach (float ms in sorted)
            {
                sum += ms;
                if (ms > 50f) over50++;
                if (ms > 100f) over100++;
            }
            return "frames " + sorted.Length + " (" + (sorted.Length / seconds).ToString("0.0", Inv) + " fps), ms avg " +
                   (sum / sorted.Length).ToString("0.0", Inv) + " p50 " + Pct(sorted, 0.50) + " p95 " + Pct(sorted, 0.95) +
                   " p99 " + Pct(sorted, 0.99) + " max " + sorted[sorted.Length - 1].ToString("0.0", Inv) +
                   ", over 50 ms " + over50 + ", over 100 ms " + over100;
        }

        private static string ModLine(List<float> frames)
        {
            if (frames.Count == 0)
            {
                return "0";
            }
            float[] sorted = frames.ToArray();
            Array.Sort(sorted);
            double sum = 0;
            foreach (float ms in sorted) sum += ms;
            return "ms/frame avg " + (sum / sorted.Length).ToString("0.00", Inv) + " p99 " + Pct(sorted, 0.99) +
                   " max " + sorted[sorted.Length - 1].ToString("0.0", Inv);
        }

        private static string Pct(float[] sorted, double p)
        {
            int index = Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1);
            return sorted[Math.Max(0, index)].ToString("0.0", Inv);
        }

        private static string Ms(long ticks)
        {
            return (ticks * TicksToMs).ToString(ticks * TicksToMs < 10 ? "0.00" : "0", Inv);
        }

        private static string NetLine(Dictionary<string, Stat> counts, float seconds, bool timedHandlers)
        {
            long messages = counts.Values.Sum(s => s.Calls);
            long bytes = counts.Values.Sum(s => s.Bytes);
            var text = new StringBuilder();
            text.Append((messages / seconds).ToString("0.0", Inv)).Append(" msg/s, ")
                .Append((bytes / seconds).ToString("0", Inv)).Append(" B/s");
            foreach (Stat stat in counts.Values.OrderByDescending(s => s.Bytes).Take(8))
            {
                text.Append("; ").Append(stat.Name).Append(' ').Append(stat.Calls).Append('x').Append(stat.Bytes).Append('B');
                if (timedHandlers)
                {
                    text.Append(' ').Append(Ms(stat.Ticks)).Append("ms");
                }
            }
            return text.ToString();
        }

        private static string PeerLine(Dictionary<ulong, Stat> counts, float seconds)
        {
            if (counts.Count == 0)
            {
                return "none";
            }
            return string.Join(", ", counts.OrderBy(entry => entry.Key)
                .Select(entry => entry.Key + "=" + (entry.Value.Bytes / seconds).ToString("0", Inv) + "B/s").ToArray());
        }

        private static string Session()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsListening)
            {
                return "no session";
            }
            var text = new StringBuilder(netcode.IsHost ? "host" : "joiner");
            try
            {
                NetworkTransport transport = netcode.NetworkConfig?.NetworkTransport;
                if (netcode.IsHost)
                {
                    text.Append(", peers ").Append(Math.Max(0, netcode.ConnectedClientsIds.Count - 1));
                    foreach (ulong id in netcode.ConnectedClientsIds)
                    {
                        if (id != NetworkManager.ServerClientId && transport != null)
                        {
                            text.Append(", rtt ").Append(id).Append('=').Append(transport.GetCurrentRtt(id)).Append("ms");
                        }
                    }
                }
                else if (transport != null)
                {
                    text.Append(", rtt ").Append(transport.GetCurrentRtt(NetworkManager.ServerClientId)).Append("ms");
                }
                text.Append(", ").Append(transport == null ? "?" : transport.GetType().Name);
            }
            catch (Exception ex)
            {
                text.Append(" (").Append(ex.GetType().Name).Append(')');
            }
            return text.ToString();
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests (probe "perf"): the totals since the last reset; "perf|reset" starts over; "perf|last" is the last 10 s.</summary>
        internal static string ReportForTest(string what)
        {
            if (!Enabled)
            {
                return "profiler off";
            }
            if (what == "reset")
            {
                Report();
                total.Begin();
                return "reset";
            }
            if (what == "last")
            {
                return lastReport;
            }
            // Fold in what the current 10 s holds so far.
            Report();
            return Describe(total, "since reset");
        }
    }
}
