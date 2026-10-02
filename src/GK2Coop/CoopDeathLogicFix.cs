using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Coop
{
    /// <summary>
    /// The single biggest blocker to a client doing anything in the world.
    ///
    /// <c>WgoData.RunLogicsAfterDeath</c> opens with:
    /// <code>
    /// if ((LazyNetwork.IsInitialized &amp;&amp; LazyNetwork.NetworkManager.IsCoopGame
    ///      &amp;&amp; !LazyNetwork.NetworkManager.IsHost) || customDeathWasTriggered) return;
    /// </code>
    /// so on a co-op client every post-death effect is skipped: the <c>WgoDead</c> trigger that
    /// advances quests, the death drops, the inventory spill, and the <c>executeOnDeath</c>
    /// expressions. The object still takes damage and dies, which is why harvesting looks like it
    /// works and then silently produces nothing.
    ///
    /// The shipped design expects the host to run that logic and replicate the result, but the
    /// replication was never implemented — the <c>WorldDataCommand</c> and
    /// <c>NetworkInterceptor</c> scaffolding has no callers at all.
    ///
    /// Until world state is genuinely replicated, each peer already keeps its own world, so the
    /// honest behaviour is for a client to run its own death logic exactly as single-player does.
    /// This clears <c>isCoopGame</c> for the duration of that one call and restores it immediately,
    /// which is the narrowest way to reach a guard buried inside a private method.
    ///
    /// When host-authoritative world replication does arrive this must be turned off, or the
    /// effects will apply twice.
    /// </summary>
    internal static class CoopDeathLogicFix
    {
        private static ManualLogSource log;
        private static FieldInfo isCoopGameField;
        private static object networkManager;
        private static int repaired;

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
            try
            {
                Type wgoData = Plugin.FindGameType("WgoData");
                MethodInfo target = wgoData == null ? null : AccessTools.Method(wgoData, "RunLogicsAfterDeath");
                if (target == null)
                {
                    log.LogWarning("WgoData.RunLogicsAfterDeath not found; clients will keep losing every death drop.");
                    return;
                }
                harmony.Patch(target,
                    new HarmonyMethod(AccessTools.Method(typeof(CoopDeathLogicFix), nameof(Prefix))),
                    new HarmonyMethod(AccessTools.Method(typeof(CoopDeathLogicFix), nameof(Postfix))));
                log.LogInfo("Patched WgoData.RunLogicsAfterDeath so clients run their own death logic.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not patch WgoData.RunLogicsAfterDeath: " + ex.Message);
            }
        }

        internal static string Describe()
        {
            return "client death logic: repaired=" + repaired;
        }

        private static bool Resolve()
        {
            if (isCoopGameField != null && networkManager != null)
            {
                return true;
            }
            networkManager = CoopDiagnostics.GetStatic(Plugin.FindGameType("LazyNetwork"), "NetworkManager");
            if (networkManager == null)
            {
                return false;
            }
            isCoopGameField = networkManager.GetType().GetField("isCoopGame",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return isCoopGameField != null;
        }

        private static void Prefix(out bool __state)
        {
            __state = false;
            try
            {
                if (!Enabled || !Resolve())
                {
                    return;
                }
                bool isCoop = Convert.ToBoolean(isCoopGameField.GetValue(networkManager));
                bool isHost = Convert.ToBoolean(CoopDiagnostics.GetMember(networkManager, "IsHost"));
                if (!isCoop || isHost)
                {
                    return;
                }
                isCoopGameField.SetValue(networkManager, false);
                __state = true;
                repaired++;
                if (repaired <= 3)
                {
                    log.LogInfo("Ran death logic locally for a client-side world object (" + repaired + ").");
                }
            }
            catch (Exception ex)
            {
                __state = false;
                log.LogWarning("Client death-logic repair failed: " + ex.Message);
            }
        }

        private static void Postfix(bool __state)
        {
            if (!__state)
            {
                return;
            }
            try
            {
                isCoopGameField.SetValue(networkManager, true);
            }
            catch (Exception ex)
            {
                // Leaving isCoopGame false would silently turn the client into a pseudo-host.
                log.LogError("Could not restore isCoopGame after death logic: " + ex.Message);
            }
        }
    }
}
