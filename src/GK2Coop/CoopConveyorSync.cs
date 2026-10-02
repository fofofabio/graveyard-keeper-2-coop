using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>The host alone advances conveyor transfers and their crafting workers.</summary>
    internal static class CoopConveyorSync
    {
        private static ManualLogSource log;
        private static int clientTicksSkipped;

        internal static bool Enabled { get; set; } = true;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "joiner conveyor ticks skipped=" + clientTicksSkipped;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled) return;
            try
            {
                MethodInfo update = AccessTools.Method(Plugin.FindGameType("ConveyorSystem"), "CustomUpdate", new[] { typeof(float) });
                if (update == null) throw new MissingMethodException("ConveyorSystem", "CustomUpdate");
                harmony.Patch(update, prefix: new HarmonyMethod(typeof(CoopConveyorSync).GetMethod(nameof(UpdatePrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Conveyor sync: host alone advances conveyor items and workers.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Conveyor sync disabled: " + ex.Message);
            }
        }

        private static bool UpdatePrefix()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || netcode.IsHost ||
                !netcode.IsConnectedClient || !CoopSession.Welcomed) return true;
            clientTicksSkipped++;
            return false;
        }
    }
}
