using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Coop
{
    /// <summary>
    /// Repairs the host's rebroadcast of a client's command to the *other* clients.
    ///
    /// <code>
    /// protected override void ExecuteCommand(ICommand command, ulong senderClientId)
    /// {
    ///     command.Execute(senderClientId, MainGame.Instance.GameSave);
    ///     SendNetworkData(this);   // the state object, not the command
    /// }
    /// </code>
    ///
    /// <c>OnlineState.SendNetworkData</c> queues its argument only when it is a <c>Command</c> or
    /// a <c>UniqueCommandHolder</c>. The connection state is neither, so it matches no branch and
    /// is silently discarded: the host executes a client's command locally and never forwards it.
    ///
    /// At two players that is invisible, because the only other participant is the host itself.
    /// At three it means client A's movement never reaches client B. Fixing it before it matters
    /// is cheaper than discovering it as "the third player is frozen for everyone but the host".
    ///
    /// The command is queued through the game's own <c>AddCommand</c> / <c>AddNotRepeatableCommand</c>,
    /// so it travels in the ordinary batched <c>CommandPackage</c> rather than a parallel path.
    /// The sender receives its own command back, because the game's publish has no per-client
    /// exclusion; commands carry their actor and the existing echo suppression handles that.
    /// </summary>
    internal static class CoopHostRelay
    {
        private static ManualLogSource log;
        private static MethodInfo addCommand;
        private static MethodInfo addNotRepeatable;
        private static Type commandType;
        private static Type holderType;
        private static int relayed;
        private static int skipped;
        private static int detailedLogsLeft = 8;

        internal static bool Enabled { get; set; }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "host relay: forwarded=" + relayed + ", nothing-to-forward=" + skipped;
        }

        internal static void Install(Harmony harmony)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                Type hostState = Plugin.FindGameType("StartedHostState");
                MethodInfo execute = hostState == null
                    ? null
                    : AccessTools.Method(hostState, "ExecuteCommand");
                if (execute == null)
                {
                    log.LogWarning("StartedHostState.ExecuteCommand not found; client commands will not reach other clients.");
                    Enabled = false;
                    return;
                }

                Type onlineState = hostState.BaseType;
                addCommand = AccessTools.Method(onlineState, "AddCommand");
                addNotRepeatable = AccessTools.Method(onlineState, "AddNotRepeatableCommand");
                commandType = Plugin.FindGameType("Command");
                holderType = Plugin.FindGameType("UniqueCommandHolder");
                if (addCommand == null || commandType == null)
                {
                    log.LogWarning("OnlineState.AddCommand or Command not found; client commands will not reach other clients.");
                    Enabled = false;
                    return;
                }

                harmony.Patch(execute, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(CoopHostRelay), nameof(ExecuteCommandPostfix))));
                log.LogInfo("Patched the host to forward client commands to the other clients.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Could not patch host command forwarding: " + ex.Message);
            }
        }

        private static void ExecuteCommandPostfix(object __instance, object[] __args)
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                object command = __args != null && __args.Length > 0 ? __args[0] : null;
                if (command == null)
                {
                    return;
                }

                // With one client there is no one else to tell, and the game's own AddCommand
                // drops the command in that case anyway. Counted so the diagnostics do not look
                // like a failure during ordinary two-player play.
                if (CountOtherClients() < 2)
                {
                    skipped++;
                    return;
                }

                if (commandType.IsInstanceOfType(command))
                {
                    addCommand.Invoke(__instance, new[] { command });
                }
                else if (holderType != null && holderType.IsInstanceOfType(command) && addNotRepeatable != null)
                {
                    addNotRepeatable.Invoke(__instance, new[] { command });
                }
                else
                {
                    skipped++;
                    return;
                }

                relayed++;
                if (detailedLogsLeft-- > 0)
                {
                    log.LogInfo("Forwarded a " + command.GetType().Name + " from a client to the other clients.");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not forward a client command: " + ex.Message);
            }
        }

        private static int CountOtherClients()
        {
            object native = CoopDiagnostics.GetStatic(Plugin.FindGameType("LazyNetwork"), "NetworkManager");
            var others = native == null ? null : CoopDiagnostics.GetMember(native, "OtherClients") as ICollection;
            return others == null ? 0 : others.Count;
        }
    }
}
