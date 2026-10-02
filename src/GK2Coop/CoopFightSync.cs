using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// A fighting level's progress is the same for everyone.
    ///
    /// Fights run on the machine of the player who starts them — their enemies are temporary and
    /// removed when the fight ends — but what a fight achieves is kept per level in world state:
    /// <c>FightingLevelData.CurStageId</c> in each scene's <c>fightingLevels</c>. A joiner's
    /// victory changed only their copy, so the next copied world had the level unbeaten again.
    ///
    /// The game raises <c>FightingLevelData.OnFightingLevelDataChanged</c> whenever a stage
    /// changes; each change is sent, the host passes it on, and a receiver sets the same stage —
    /// unless it is itself fighting in that level right now, where its own fight decides. The
    /// stage a fight ends on is the last one sent, so everyone ends where the fighter did.
    /// </summary>
    internal static class CoopFightSync
    {
        internal const string FightMessage = "GK2Coop.FightLevel.v1";

        private static ManualLogSource log;
        private static bool applying;
        private static int sent;
        private static int applied;
        private static Delegate handler;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "fight level sync: sent=" + sent + ", applied=" + applied;
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Install()
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                Type levelType = Plugin.FindGameType("FightingLevelData");
                EventInfo changed = levelType?.GetEvent("OnFightingLevelDataChanged", BindingFlags.Static | BindingFlags.Public);
                if (changed == null)
                {
                    log.LogWarning("Fight level sync: FightingLevelData.OnFightingLevelDataChanged not found; fight progress stays local.");
                    Enabled = false;
                    return;
                }
                handler = Delegate.CreateDelegate(changed.EventHandlerType, typeof(CoopFightSync).GetMethod(nameof(OnChanged), BindingFlags.Static | BindingFlags.NonPublic));
                changed.AddEventHandler(null, handler);
                log.LogInfo("Fight level sync: fighting level progress is shared.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Fight level sync disabled: " + ex.Message);
            }
        }

        private static void OnChanged(object level)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || applying || level == null || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                string id = Convert.ToString(CoopDiagnostics.GetMember(level, "id"));
                int stage = Convert.ToInt32(CoopDiagnostics.GetMember(level, "CurStageId"));
                Send(netcode, null, id, stage);
                log.LogInfo("Fight level sync: " + id + " is now at stage " + stage + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight level sync: could not share a level change: " + ex.Message);
            }
        }

        private static void Send(NetworkManager netcode, ulong? except, string id, int stage)
        {
            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(new FixedString128Bytes(id));
                writer.WriteValueSafe(stage);
                if (netcode.IsHost)
                {
                    foreach (ulong clientId in netcode.ConnectedClientsIds)
                    {
                        if (clientId != netcode.LocalClientId && clientId != except)
                        {
                            netcode.CustomMessagingManager.SendNamedMessage(FightMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                        }
                    }
                }
                else
                {
                    netcode.CustomMessagingManager.SendNamedMessage(FightMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
            sent++;
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out FixedString128Bytes idText);
                reader.ReadValueSafe(out int stage);
                string id = idText.ToString();
                if (netcode.IsHost)
                {
                    Send(netcode, sender, id, stage);
                }
                if (FightingHere(id))
                {
                    log.LogInfo("Fight level sync: kept our own fight's stage for " + id + " (another player reported " + stage + ").");
                    return;
                }
                object level = FindLevel(id);
                if (level == null || Convert.ToInt32(CoopDiagnostics.GetMember(level, "CurStageId")) == stage)
                {
                    return;
                }
                applying = true;
                try
                {
                    level.GetType().GetProperty("CurStageId").SetValue(level, stage, null);
                }
                finally
                {
                    applying = false;
                }
                applied++;
                log.LogInfo("Fight level sync: " + id + " set to stage " + stage + " as another player's fight left it.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight level sync: could not apply a level change from " + sender + ": " + ex.Message);
            }
        }

        /// <summary>Whether this machine is running a fight in that level right now.</summary>
        private static bool FightingHere(string id)
        {
            try
            {
                // LazySingleton<T>.Instance lives on the generic base class.
                object controller = Plugin.FindGameType("FightingGameController")?
                    .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null, null);
                if (controller == null || Convert.ToString(CoopDiagnostics.GetMember(controller, "fightState")) != "ActiveFight")
                {
                    return false;
                }
                object current = CoopDiagnostics.GetMember(controller, "currentLevel");
                return current != null && Convert.ToString(CoopDiagnostics.GetMember(current, "id")) == id;
            }
            catch
            {
                return false;
            }
        }

        private static object FindLevel(string id)
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            object world = save == null ? null : CoopDiagnostics.GetMember(save, "worldData");
            if (!(CoopDiagnostics.GetMember(world, "gameSceneDataList") is IEnumerable scenes))
            {
                return null;
            }
            foreach (object scene in scenes)
            {
                if (CoopDiagnostics.GetMember(scene, "fightingLevels") is IEnumerable levels)
                {
                    foreach (object level in levels)
                    {
                        if (level != null && Convert.ToString(CoopDiagnostics.GetMember(level, "id")) == id)
                        {
                            return level;
                        }
                    }
                }
            }
            return null;
        }
    }
}
