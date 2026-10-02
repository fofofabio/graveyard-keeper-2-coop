using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Coop
{
    /// <summary>
    /// Saves made during co-op are ordinary single-player saves.
    ///
    /// While hosting, <c>GameSave</c> carries the session's network player records
    /// (<c>hostPlayer</c>, <c>clientPlayers</c> — one per joiner, each with a full <c>PlayerData</c>).
    /// The game saves the whole <c>GameSave</c> (after sleeping, on quit), so those records went into
    /// the file. Loaded again, they were stale: <c>CreateClient</c> appends a new record for each
    /// joiner while <c>GetClient</c> returns the first match, so a later joiner could be matched to
    /// an old record, and every session grew the save by another player.
    ///
    /// <c>SaveSystem.Save</c> is synchronous: the records are taken out for the call and put back
    /// right after, exactly as the live-world copy for joiners does. Joiners' own progress is kept
    /// by the player profiles, not by these records.
    /// </summary>
    internal static class CoopSaveHygiene
    {
        private static ManualLogSource log;
        private static object stashedHost;
        private static object stashedClients;
        private static object stashedSave;
        private static int cleaned;

        internal static void Install(Harmony harmony, ManualLogSource source)
        {
            log = source;
            try
            {
                MethodInfo save = null;
                foreach (MethodInfo method in Plugin.FindGameType("SaveSystem").GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (method.Name == "Save" && method.GetParameters().Length >= 2 &&
                        method.GetParameters()[1].ParameterType == Plugin.FindGameType("GameSave"))
                    {
                        save = method;
                    }
                }
                if (save == null)
                {
                    log.LogWarning("Save hygiene: SaveSystem.Save not found; co-op saves keep session records.");
                    return;
                }
                harmony.Patch(save,
                    prefix: new HarmonyMethod(typeof(CoopSaveHygiene).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(CoopSaveHygiene).GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Save hygiene: co-op session records are left out of saves.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Save hygiene not installed: " + ex.Message);
            }
        }

        private static void Prefix(object gameSave)
        {
            stashedSave = null;
            if (gameSave == null)
            {
                return;
            }
            try
            {
                FieldInfo hostField = gameSave.GetType().GetField("hostPlayer");
                FieldInfo clientsField = gameSave.GetType().GetField("clientPlayers");
                object host = hostField?.GetValue(gameSave);
                object clients = clientsField?.GetValue(gameSave);
                bool hasClients = clients is System.Collections.ICollection list && list.Count > 0;
                if (host == null && !hasClients)
                {
                    return;
                }
                stashedSave = gameSave;
                stashedHost = host;
                stashedClients = clients;
                hostField?.SetValue(gameSave, null);
                clientsField?.SetValue(gameSave, Activator.CreateInstance(clientsField.FieldType));
                cleaned++;
            }
            catch (Exception ex)
            {
                stashedSave = null;
                log.LogWarning("Save hygiene: could not set aside the session records: " + ex.Message);
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (stashedSave != null)
            {
                try
                {
                    stashedSave.GetType().GetField("hostPlayer")?.SetValue(stashedSave, stashedHost);
                    stashedSave.GetType().GetField("clientPlayers")?.SetValue(stashedSave, stashedClients);
                    log.LogInfo("Save hygiene: saved without the co-op session records (" + cleaned + " save(s) this session).");
                }
                catch (Exception ex)
                {
                    log.LogError("Save hygiene: could not restore the session records after saving: " + ex.Message);
                }
                stashedSave = null;
                stashedHost = null;
                stashedClients = null;
            }
            return __exception;
        }
    }
}
