using System;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// A fight is fought on the machine of the player who starts it: its enemies exist only there
    /// (<c>FightingGameController</c>). The others at least learn that it is
    /// happening and how it ended: when a fight becomes active (<c>SetFightState</c>) and when it
    /// is over (won, lost or left), a notice. The fight itself is not shown to the others: a fighting
    /// level is an island only the fighter is teleported onto (watching it was tried in 0.61-0.62 and
    /// dropped; retired/island-spectating-0.62).
    /// </summary>
    internal static class CoopFightWatch
    {
        internal const string FightWatchMessage = "GK2Coop.FightWatch.v1";
        private const byte KindStart = 0;
        private const byte KindEnd = 1;
        private const byte OutcomeLeft = 0;
        private const byte OutcomeWon = 1;
        private const byte OutcomeLost = 2;

        private static ManualLogSource log;
        private static FightState before;
        private static byte outcome;
        private static int sent;
        private static int received;
        private static string lastNotice = string.Empty;

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
            try
            {
                harmony.Patch(AccessTools.Method(typeof(FightingGameController), "SetFightState"),
                    prefix: new HarmonyMethod(typeof(CoopFightWatch), nameof(StatePrefix)),
                    postfix: new HarmonyMethod(typeof(CoopFightWatch), nameof(StatePostfix)));
                harmony.Patch(AccessTools.Method(typeof(FightingGameController), nameof(FightingGameController.FinishAsWon)),
                    prefix: new HarmonyMethod(typeof(CoopFightWatch), nameof(WonPrefix)));
                harmony.Patch(AccessTools.Method(typeof(FightingGameController), nameof(FightingGameController.FinishAsLost)),
                    prefix: new HarmonyMethod(typeof(CoopFightWatch), nameof(LostPrefix)));
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Fight notices disabled: " + ex.Message);
            }
        }

        internal static string Describe()
        {
            return "fight watch: sent=" + sent + ", received=" + received + (lastNotice.Length > 0 ? ", last notice " + lastNotice : string.Empty);
        }

        // ---------------------------------------------------------------- the fighter

        private static void StatePrefix(FightingGameController __instance)
        {
            before = __instance.CurrentFightState;
        }

        private static void WonPrefix()
        {
            outcome = OutcomeWon;
        }

        private static void LostPrefix()
        {
            outcome = OutcomeLost;
        }

        private static void StatePostfix(FightingGameController __instance)
        {
            FightState now = __instance.CurrentFightState;
            if (now == before)
            {
                return;
            }
            string level;
            try
            {
                level = __instance.CurrentLevel == null ? string.Empty : __instance.CurrentLevelId;
            }
            catch (Exception)
            {
                level = string.Empty;
            }
            if (now == FightState.ActiveFight)
            {
                outcome = OutcomeLeft;
                Send(KindStart, level, OutcomeLeft);
            }
            else if (now == FightState.Disabled && before == FightState.ActiveFight)
            {
                Send(KindEnd, level, outcome);
                outcome = OutcomeLeft;
            }
        }

        private static void Send(byte kind, string level, byte result)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening || (netcode.IsHost && netcode.ConnectedClientsIds.Count < 2))
            {
                return;
            }
            try
            {
                string scene = MainGame.PlayerData == null ? string.Empty : MainGame.PlayerData.currentGameSceneId;
                Deliver(netcode, netcode.LocalClientId, kind, level, scene, result, null);
                sent++;
                log.LogInfo("Fight watch: told the others the fight " + level + (kind == KindStart ? " began." : " ended (" + result + ")."));
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight watch: could not send: " + ex.Message);
            }
        }

        private static void Deliver(NetworkManager netcode, ulong from, byte kind, string level, string scene, byte result, ulong? except)
        {
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(kind);
                writer.WriteValueSafe(from);
                writer.WriteValueSafe(new FixedString64Bytes(level ?? string.Empty));
                writer.WriteValueSafe(new FixedString64Bytes(scene ?? string.Empty));
                writer.WriteValueSafe(result);
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except && clientId != from)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(FightWatchMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(FightWatchMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        // ---------------------------------------------------------------- the others

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out byte kind);
                reader.ReadValueSafe(out ulong from);
                reader.ReadValueSafe(out FixedString64Bytes level);
                reader.ReadValueSafe(out FixedString64Bytes scene);
                reader.ReadValueSafe(out byte result);
                if (netcode.IsHost)
                {
                    // The host vouches for who is fighting.
                    from = sender;
                    Deliver(netcode, from, kind, level.ToString(), scene.ToString(), result, sender);
                }
                received++;
                string name = from == netcode.LocalClientId ? CoopSession.LocalName : CoopSession.NameFor(from);
                string notice;
                if (kind == KindStart)
                {
                    notice = L.F("{0} is fighting.", name);
                }
                else if (result == OutcomeWon)
                {
                    notice = L.F("{0} won the fight.", name);
                }
                else if (result == OutcomeLost)
                {
                    notice = L.F("{0} lost the fight.", name);
                }
                else
                {
                    notice = L.F("{0}'s fight is over.", name);
                }
                lastNotice = notice;
                CoopStatus.Announce(notice, false, 6f);
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight watch: could not read a message from " + sender + ": " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests: a fight's start and end as the fighter's game would report them.</summary>
        internal static string SimulateForTest(string what)
        {
            if (what == "start") Send(KindStart, "test_level", OutcomeLeft);
            else if (what == "won") Send(KindEnd, "test_level", OutcomeWon);
            else if (what == "lost") Send(KindEnd, "test_level", OutcomeLost);
            else if (what == "left") Send(KindEnd, "test_level", OutcomeLeft);
            return Describe();
        }
    }
}
