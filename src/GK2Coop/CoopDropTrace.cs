using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Traces where a harvested item ends up. A client player can complete a harvest and receive
    /// nothing, with no exception anywhere, so the loss is silent and somewhere along
    /// spawn -> collect -> inventory. This reports each of those three points with the identity of
    /// the player data involved, which is what distinguishes "never spawned" from "collected by the
    /// wrong player" from "added to an inventory nobody is looking at".
    ///
    /// Diagnostic only: nothing here changes behaviour.
    /// </summary>
    internal static class CoopDropTrace
    {
        private static ManualLogSource log;
        private static int drops;
        private static int collects;
        private static int adds;
        private static float nextSummary;
        private static int stacksLeft = 6;

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
            Patch(harmony, "DropSystem", "DropItem", nameof(DropItemPostfix));
            Patch(harmony, "PlayerData", "CollectDrop", nameof(CollectDropPrefix), prefix: true);
            Patch(harmony, "DropCollector", "TryCollectDrop", nameof(TryCollectPrefix), prefix: true);
            Patch(harmony, "CraftComponent", "UpdateManual", nameof(UpdateManualPrefix), prefix: true);
            // The mushrooms are a quest reward, not a harvest: the real chain is
            // FireInteractionEvent -> FireTrigger -> CompleteQuest -> the quest's drop expression.
            Patch(harmony, "GlobalEventsSystem", "FireTrigger", nameof(FireTriggerPrefix), prefix: true);
            Patch(harmony, "QuestSystemData", "CompleteQuest", nameof(CompleteQuestPrefix), prefix: true);
            // Work ticks are gated by CanProceedWork; a client finds the object but never produces
            // anything, so report which of its checks rejects the tick.
            Patch(harmony, "ToolComponent", "CanProceedWork", nameof(CanProceedWorkPostfix));
        }

        private static void Patch(Harmony harmony, string typeName, string methodName, string handler, bool prefix = false)
        {
            try
            {
                Type target = Plugin.FindGameType(typeName);
                MethodInfo original = target == null ? null : AccessTools.Method(target, methodName);
                if (original == null)
                {
                    log.LogWarning("Drop trace: " + typeName + "." + methodName + " not found.");
                    return;
                }
                var patch = new HarmonyMethod(AccessTools.Method(typeof(CoopDropTrace), handler));
                harmony.Patch(original, prefix ? patch : null, prefix ? null : patch);
                log.LogInfo("Drop trace: patched " + typeName + "." + methodName + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Drop trace: could not patch " + typeName + "." + methodName + ": " + ex.Message);
            }
        }

        private static void DropItemPostfix(object[] __args, bool __result)
        {
            try
            {
                drops++;
                object item = __args != null && __args.Length > 0 ? __args[0] : null;
                log.LogInfo("Drop spawned: item=" + Describe(item) +
                            "; worldId=" + (__args != null && __args.Length > 1 ? Convert.ToString(__args[1]) : "?") +
                            "; accepted=" + __result);
                // The harvest path turned out not to run through CraftComponent at all, so capture
                // who actually asks for the drop rather than guessing at the route again.
                if (stacksLeft > 0)
                {
                    stacksLeft--;
                    var trace = new System.Diagnostics.StackTrace(1, false);
                    var frames = new System.Text.StringBuilder();
                    for (int i = 0; i < trace.FrameCount && i < 12; i++)
                    {
                        MethodBase frame = trace.GetFrame(i).GetMethod();
                        if (frame == null)
                        {
                            continue;
                        }
                        frames.Append(" <- ").Append(frame.DeclaringType == null ? "?" : frame.DeclaringType.Name)
                              .Append('.').Append(frame.Name);
                    }
                    log.LogInfo("Drop origin:" + frames);
                }
            }
            catch
            {
            }
        }

        private static void TryCollectPrefix(object __instance)
        {
            try
            {
                collects++;
                var component = __instance as Component;
                if (component == null || collects > 40)
                {
                    return;
                }
                log.LogInfo("Drop collector fired on " + CoopDiagnostics.HierarchyPath(component.gameObject));
            }
            catch
            {
            }
        }

        /// <summary>
        /// The decisive line: <c>DropCollector</c> always credits <c>MainGame.PlayerData</c>, so if
        /// that static is not this machine's own player the item silently goes to the proxy.
        /// </summary>
        private static void CollectDropPrefix(object __instance, object[] __args)
        {
            try
            {
                adds++;
                object localPlayerData = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
                bool isLocal = ReferenceEquals(__instance, localPlayerData);
                log.LogInfo("CollectDrop -> playerData#" + Hash(__instance) +
                            " (MainGame.PlayerData#" + Hash(localPlayerData) + ", same=" + isLocal + ")" +
                            "; contextActive=" + CoopPlayerContext.IsActive +
                            "; drop=" + Describe(__args != null && __args.Length > 0 ? __args[0] : null));
            }
            catch
            {
            }
        }

        /// <summary>
        /// <c>UpdateManual</c> silently does nothing unless a craft element is present and started,
        /// which is the shape of "the work animation plays but nothing is produced". Logs the craft
        /// state per object, once per object per second, so the two machines can be compared.
        /// </summary>
        private static void UpdateManualPrefix(object __instance, object[] __args)
        {
            try
            {
                object element = CoopDiagnostics.GetMember(__instance, "CurrentCraftElement");
                string key = Hash(__instance).ToString();
                float last;
                if (lastCraftLog.TryGetValue(key, out last) && Time.unscaledTime - last < 1f)
                {
                    return;
                }
                lastCraftLog[key] = Time.unscaledTime;

                object def = element == null ? null : CoopDiagnostics.GetMember(element, "Def");
                log.LogInfo("Craft tick on component#" + key +
                            ": deltaTicks=" + (__args != null && __args.Length > 0 ? Convert.ToString(__args[0]) : "?") +
                            "; status=" + CoopDiagnostics.GetMember(__instance, "Status") +
                            "; element=" + (element == null ? "NULL" : Convert.ToString(CoopDiagnostics.GetMember(def, "id"))) +
                            "; started=" + (element == null ? "n/a" : Convert.ToString(CoopDiagnostics.GetMember(element, "IsStarted"))) +
                            "; progress=" + (element == null ? "n/a" : Convert.ToString(CoopDiagnostics.GetMember(element, "ProgressTicks"))) +
                            "/" + (element == null ? "n/a" : Convert.ToString(CoopDiagnostics.GetMember(element, "TotalProgressTicks"))) +
                            "; queueDelayed=" + CoopDiagnostics.GetMember(__instance, "IsQueueDelayed"));
            }
            catch
            {
            }
        }

        private static void FireTriggerPrefix(object[] __args)
        {
            try
            {
                string type = __args != null && __args.Length > 0 ? Convert.ToString(__args[0]) : "?";
                string id = __args != null && __args.Length > 1 ? Convert.ToString(__args[1]) : "?";
                // Each distinct trigger once: a flat cap stopped before the interesting moment,
                // and unfiltered logging would drown the session in repeats.
                string key = type + "|" + id;
                if (seenTriggers.Contains(key) || seenTriggers.Count > 400)
                {
                    return;
                }
                seenTriggers.Add(key);
                log.LogInfo("Trigger fired: type=" + type + "; id='" + id + "'");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Reports the quest's status before the call, because <c>CompleteQuest</c> silently returns
        /// for anything already Completed or Cancelled, or not yet in the collection at all.
        /// </summary>
        private static void CompleteQuestPrefix(object __instance, object[] __args)
        {
            try
            {
                string id = __args != null && __args.Length > 0 ? Convert.ToString(__args[0]) : "?";
                object collection = CoopDiagnostics.GetMember(__instance, "questCollection");
                object cache = collection == null ? null : CoopDiagnostics.GetMember(collection, "questsCache");
                string status = "not-in-collection";
                if (cache is System.Collections.IDictionary map && map.Contains(id))
                {
                    status = Convert.ToString(CoopDiagnostics.GetMember(map[id], "status"));
                }
                log.LogInfo("CompleteQuest('" + id + "') requested; currentStatus=" + status);
            }
            catch
            {
            }
        }

        private static void CanProceedWorkPostfix(object __instance, bool __result)
        {
            try
            {
                string status = Convert.ToString(CoopDiagnostics.GetMember(__instance, "UseStatus"));
                string key = __result + "|" + status;
                if (key == lastWorkStatus)
                {
                    return;
                }
                lastWorkStatus = key;

                object activity = CoopDiagnostics.GetMember(__instance, "toolActor");
                object activityData = activity == null ? null : CoopDiagnostics.GetMember(activity, "playerData");
                object localData = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
                log.LogInfo("Work check: proceed=" + __result + "; status=" + status +
                            "; activity=" + (activity == null ? "null" : activity.GetType().Name) +
                            "; activityPlayer#" + Hash(activityData) +
                            "; MainGame.PlayerData#" + Hash(localData) +
                            "; same=" + ReferenceEquals(activityData, localData));
            }
            catch
            {
            }
        }

        private static string lastWorkStatus;

        private static readonly System.Collections.Generic.HashSet<string> seenTriggers =
            new System.Collections.Generic.HashSet<string>();

        private static readonly System.Collections.Generic.Dictionary<string, float> lastCraftLog =
            new System.Collections.Generic.Dictionary<string, float>();

        private static string Describe(object value)
        {
            if (value == null)
            {
                return "null";
            }
            object id = CoopDiagnostics.GetMember(value, "id");
            object count = CoopDiagnostics.GetMember(value, "Count");
            if (id != null)
            {
                return Convert.ToString(id) + " x" + Convert.ToString(count);
            }
            object data = CoopDiagnostics.GetMember(value, "Data");
            return data != null ? Describe(data) : value.GetType().Name;
        }

        private static int Hash(object value)
        {
            return value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        internal static void Tick()
        {
            if (!Enabled || Time.unscaledTime < nextSummary)
            {
                return;
            }
            nextSummary = Time.unscaledTime + 10f;
            if (drops > 0 || collects > 0 || adds > 0)
            {
                log.LogInfo("Drop trace: spawned=" + drops + ", collectorFired=" + collects + ", collectDrop=" + adds);
            }
        }
    }
}
