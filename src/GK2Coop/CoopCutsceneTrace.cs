using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Coop
{
    /// <summary>
    /// Research for opt-in cutscenes: records what
    /// starts the game's scripted scenes on each machine. Observes only; changes nothing.
    ///
    /// A cutscene is a global flow script (<c>GlobalScriptsManager.RunFlowScript</c>) that takes
    /// the player's control (<c>PlayerController.SetControlTakenType(ByFlow, false)</c>) and usually
    /// shows the cinematic bars (<c>UICinematic.EnableCinematic</c>). Each start is logged with the
    /// methods that led to it and whether the mod was mirroring another player's quest transition
    /// at the time — the case where a scene could start on a machine whose player did nothing.
    /// </summary>
    internal static class CoopCutsceneTrace
    {
        internal struct Entry
        {
            internal float At;
            internal string What;
            internal string Detail;
            internal bool FromRemoteQuest;
            internal string Origin;
        }

        private const int MaxEntries = 80;
        private static readonly List<Entry> entries = new List<Entry>();
        private static ManualLogSource log;
        private static int scripts;
        private static int controlTaken;
        private static int cinematics;

        internal static bool Enabled { get; set; } = true;

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
            Patch(harmony, "GlobalScriptsManager", "RunFlowScript", new[] { typeof(string), typeof(Action), null }, nameof(ScriptByName));
            Patch(harmony, "PlayerController", "SetControlTakenType", null, nameof(ControlTaken));
            Patch(harmony, "UICinematic", "EnableCinematic", null, nameof(CinematicOn));
            Patch(harmony, "UICinematic", "DisableCinematic", null, nameof(CinematicOff));
            // A scene that stalls on a line of dialogue: an error there is swallowed by the tween or
            // callback that started the line, so note it here.
            try
            {
                var talkFailed = new HarmonyMethod(typeof(CoopCutsceneTrace).GetMethod(nameof(TalkFailed), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(AccessTools.Method(typeof(Bubble), nameof(Bubble.Talk)), finalizer: talkFailed);
                harmony.Patch(AccessTools.Method(typeof(GK2.FlowCanvasNodes.Flow_MultiTalk), "DoTalkIteration"), finalizer: talkFailed);
            }
            catch (Exception ex)
            {
                log.LogWarning("Cutscene trace: could not watch dialogue: " + ex.Message);
            }
        }

        private static Exception TalkFailed(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null)
            {
                Add("dialogue failed", __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + ": " + __exception.GetType().Name + " " + __exception.Message);
                log.LogWarning("Cutscene trace: " + __exception);
            }
            return __exception;
        }

        private static void Patch(Harmony harmony, string typeName, string methodName, Type[] leading, string prefix)
        {
            try
            {
                Type type = Plugin.FindGameType(typeName);
                MethodInfo target = null;
                foreach (MethodInfo method in type == null ? new MethodInfo[0] : type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != methodName) continue;
                    if (leading != null && (method.GetParameters().Length < 1 || method.GetParameters()[0].ParameterType != leading[0])) continue;
                    target = method;
                    break;
                }
                if (target == null)
                {
                    log.LogWarning("Cutscene trace: " + typeName + "." + methodName + " not found.");
                    return;
                }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(CoopCutsceneTrace).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                log.LogWarning("Cutscene trace: could not watch " + typeName + "." + methodName + ": " + ex.Message);
            }
        }

        private static void ScriptByName(string scriptName)
        {
            scripts++;
            Add("script", scriptName);
        }

        private static void ControlTaken(object[] __args)
        {
            string type = Convert.ToString(__args[0], CultureInfo.InvariantCulture);
            bool isEnabled = __args.Length > 1 && __args[1] is bool enabled && enabled;
            if (type != "ByFlow")
            {
                return;
            }
            if (!isEnabled)
            {
                controlTaken++;
            }
            Add(isEnabled ? "control returned" : "control taken", type);
        }

        private static void CinematicOn()
        {
            cinematics++;
            Add("cinematic on", string.Empty);
        }

        private static void CinematicOff()
        {
            Add("cinematic off", string.Empty);
        }

        private static void Add(string what, string detail)
        {
            try
            {
                var entry = new Entry
                {
                    At = UnityEngine.Time.unscaledTime,
                    What = what,
                    Detail = detail,
                    FromRemoteQuest = CoopQuestSync.ApplyingRemote,
                    Origin = Origin(),
                };
                entries.Add(entry);
                if (entries.Count > MaxEntries)
                {
                    entries.RemoveAt(0);
                }
                log.LogInfo("Cutscene trace: " + what + (detail.Length > 0 ? " [" + detail + "]" : string.Empty) +
                            (entry.FromRemoteQuest ? " WHILE MIRRORING A REMOTE QUEST" : string.Empty) + " from " + entry.Origin);
            }
            catch (Exception)
            {
                // Research logging must never disturb the game.
            }
        }

        /// <summary>The game methods that led here, nearest first, without Harmony and flow plumbing.</summary>
        private static string Origin()
        {
            var names = new List<string>();
            foreach (StackFrame frame in new StackTrace(3, false).GetFrames() ?? new StackFrame[0])
            {
                MethodBase method = frame.GetMethod();
                if (method == null || method.DeclaringType == null) continue;
                string owner = method.DeclaringType.Name;
                if (owner.StartsWith("Coop", StringComparison.Ordinal) || owner.Contains("Harmony") || owner.StartsWith("DMD", StringComparison.Ordinal)) continue;
                if (method.DeclaringType.Namespace != null && (method.DeclaringType.Namespace.StartsWith("ParadoxNotion", StringComparison.Ordinal) || method.DeclaringType.Namespace.StartsWith("System", StringComparison.Ordinal))) continue;
                names.Add(owner + "." + method.Name);
                if (names.Count >= 6) break;
            }
            return names.Count == 0 ? "?" : string.Join(" < ", names.ToArray());
        }

        internal static string Describe()
        {
            return "cutscene trace: scripts=" + scripts + ", control taken=" + controlTaken + ", cinematics=" + cinematics;
        }

        /// <summary>Tests: the recorded entries, oldest first.</summary>
        internal static string Recent()
        {
            var lines = new List<string>();
            foreach (Entry entry in entries)
            {
                lines.Add(entry.At.ToString("F1", CultureInfo.InvariantCulture) + " " + entry.What +
                          (entry.Detail.Length > 0 ? " [" + entry.Detail + "]" : string.Empty) +
                          (entry.FromRemoteQuest ? " REMOTE-QUEST" : string.Empty) + " from " + entry.Origin);
            }
            return string.Join("\n", lines.ToArray());
        }
    }
}
