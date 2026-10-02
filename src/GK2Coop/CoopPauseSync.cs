using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>Keep the shared world running while one player has a modal UI open.</summary>
    internal static class CoopPauseSync
    {
        private static ManualLogSource log;
        private static int suppressed;

        internal static bool Enabled { get; set; } = true;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "co-op modal pauses suppressed=" + suppressed;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled) return;
            try
            {
                MethodInfo pause = AccessTools.Method(Plugin.FindGameType("MainGame"), "PauseGame");
                if (pause == null) throw new MissingMethodException("MainGame", "PauseGame");
                harmony.Patch(pause, prefix: new HarmonyMethod(typeof(CoopPauseSync).GetMethod(nameof(PausePrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Co-op pause: modal windows leave the shared world running.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Co-op pause disabled: " + ex.Message);
            }
        }

        private static bool PausePrefix()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || (!netcode.IsHost && !netcode.IsConnectedClient))
                return true;
            suppressed++;
            return false;
        }
    }
}
