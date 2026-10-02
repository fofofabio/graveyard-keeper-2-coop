using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// One fight at a time in a shared world. A fight runs on the machine of the player who starts
    /// it, with the military base's zombie fighters (one army for the whole world) and with the
    /// world's clock stopped; two fights at once would each use their own copy of that army, and a
    /// second fight of the same level would decide its stage for everyone when it ends (a loss after
    /// the other's win made the level unbeaten again). So while one player is in a pre-fight or a
    /// fight, the others cannot choose one at a fight point: they are told who is fighting, and they
    /// get the fight notices (<see cref="CoopFightWatch"/>).
    ///
    /// The host decides, as for the work lock (<see cref="CoopWorkLock"/>): a player's fight state
    /// leaving "disabled" claims the fight, returning to it lets go, and the host frees the claim of
    /// a player who left. If two players start within the same moment, the later one's pre-fight is
    /// cancelled when the host's answer arrives. Fights a story script starts are never blocked or
    /// called off.
    /// </summary>
    internal static class CoopFightLock
    {
        internal const string LockMessage = "GK2Coop.FightLock.v1";
        private const byte KindClaim = 0;
        private const byte KindRelease = 1;
        private const byte KindState = 2;
        private const ulong Nobody = ulong.MaxValue;

        private static ManualLogSource log;
        // Every machine: who is fighting, as the host last said (the host: as it decided).
        private static ulong holder = Nobody;
        private static string holderName = string.Empty;
        private static bool claimed;
        // This player's fight was chosen at a fight point (only such a fight is ever called off).
        private static bool chosen;
        private static float nextCheck;
        private static FieldInfo chooseOut;
        // The world's clock, stopped here because someone else is fighting (their game stops its own).
        private static bool clockHeld;
        // Someone was fighting at the last tick (to notice the moment no one is any more).
        private static bool wasHeld;
        private static int levelsReset;
        private static int claims;
        private static int refused;
        private static int cancelled;
        private static string lastRefusal = string.Empty;

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
                Type choose = Plugin.FindGameType("GK2.FlowCanvasNodes.Flow_ChooseFightLevel");
                chooseOut = AccessTools.Field(choose, "out");
                harmony.Patch(AccessTools.Method(choose, "ShowChooseLevelMultiAnswer"),
                    prefix: new HarmonyMethod(typeof(CoopFightLock), nameof(ChoosePrefix)));
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Fight lock disabled: " + ex.Message);
            }
        }

        internal static string Describe()
        {
            return "fight lock: holder=" + (holder == Nobody ? "nobody" : holderName) + ", mine=" + claimed + ", clock held=" + clockHeld + ", claims=" + claims +
                   ", refused=" + refused + ", cancelled=" + cancelled + ", levels reset=" + levelsReset + (lastRefusal.Length > 0 ? ", last refusal " + lastRefusal : string.Empty);
        }

        private static bool InSession(out NetworkManager netcode)
        {
            netcode = NetworkManager.Singleton;
            return Enabled && netcode != null && netcode.IsListening && (!netcode.IsHost || netcode.ConnectedClientsIds.Count > 1);
        }

        /// <summary>Someone else is fighting: their name.</summary>
        private static bool BlockedBy(out string name)
        {
            name = holderName;
            return InSession(out NetworkManager netcode) && holder != Nobody && holder != netcode.LocalClientId;
        }

        // ---------------------------------------------------------------- this player

        /// <summary>The fight point's level list: not while someone else fights.</summary>
        private static bool ChoosePrefix(object __instance, object flow)
        {
            try
            {
                if (!BlockedBy(out string name))
                {
                    chosen = true;
                    return true;
                }
                Refuse(name);
                // Leave the node as its "Exit" answer does, so the fight point's script goes on.
                object port = chooseOut?.GetValue(__instance);
                port?.GetType().GetMethod("Call")?.Invoke(port, new[] { flow });
                return false;
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight lock: " + ex.Message);
                return true;
            }
        }

        private static void Refuse(string name)
        {
            refused++;
            lastRefusal = "held by " + name;
            Tell(L.F("{0} is fighting.", name));
        }

        private static FightState State()
        {
            return LazySingleton<FightingGameController>.Instance.CurrentFightState;
        }

        /// <summary>From the plugin's Update: claim while this player fights, let go after.</summary>
        internal static void Update()
        {
            if (Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + 0.25f;
            if (!InSession(out NetworkManager netcode))
            {
                holder = Nobody;
                holderName = string.Empty;
                claimed = false;
                HoldClock(false);
                return;
            }
            bool fighting = State() != FightState.Disabled;
            if (fighting && !claimed)
            {
                claimed = true;
                claims++;
                if (netcode.IsHost)
                {
                    Decide(netcode, netcode.LocalClientId, true);
                }
                else
                {
                    if (holder == Nobody)
                    {
                        holder = netcode.LocalClientId;
                        holderName = CoopSession.LocalName;
                    }
                    SendToHost(netcode, KindClaim);
                }
            }
            else if (!fighting && claimed)
            {
                claimed = false;
                chosen = false;
                if (netcode.IsHost)
                {
                    Decide(netcode, netcode.LocalClientId, false);
                }
                else
                {
                    if (holder == netcode.LocalClientId)
                    {
                        holder = Nobody;
                        holderName = string.Empty;
                    }
                    SendToHost(netcode, KindRelease);
                }
            }
            if (netcode.IsHost && holder != Nobody && holder != netcode.LocalClientId && !IsConnected(netcode, holder))
            {
                // The fighter left: the fight is free again.
                Decide(netcode, holder, false);
            }
            HoldClock(holder != Nobody && holder != netcode.LocalClientId);
            bool held = holder != Nobody || State() != FightState.Disabled;
            if (wasHeld && !held)
            {
                ResetUnfinishedLevels();
            }
            wasHeld = held;
        }

        /// <summary>
        /// A fight that ended without a result — the fighter's game crashed or lost the connection,
        /// or left the fight another way — leaves its level at stage 3 or 4 (pre-fight, fight
        /// running): the arena's barricades and zones stay up. The fighter is taken out of the level
        /// by their game, but a player who stood inside watching would be shut in. Once no one fights,
        /// such a level goes back to stage 2, as the game itself sets it after a lost fight.
        /// </summary>
        private static void ResetUnfinishedLevels()
        {
            try
            {
                foreach (object sceneData in (System.Collections.IEnumerable)CoopDiagnostics.GetMember(MainGame.WorldData, "gameSceneDataList"))
                {
                    if (!(CoopDiagnostics.GetMember(sceneData, "fightingLevels") is System.Collections.IEnumerable levels))
                    {
                        continue;
                    }
                    foreach (FightingLevelData level in levels)
                    {
                        if (level == null || (level.CurStageId != 3 && level.CurStageId != 4))
                        {
                            continue;
                        }
                        int was = level.CurStageId;
                        FightingLevel loaded = MainGame.GetFightingLevel(level.id);
                        if (loaded != null)
                        {
                            loaded.ApplyStageId(2);
                        }
                        else
                        {
                            level.CurStageId = 2;
                        }
                        levelsReset++;
                        log.LogInfo("Fight lock: " + level.id + " was left at stage " + was + " by a fight without a result; back to stage 2.");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight lock: could not reset an unfinished fighting level: " + ex.Message);
            }
        }

        /// <summary>
        /// A fight stops the world's clock on the fighter's machine (<c>StartPreFight</c>); the
        /// others stop theirs too while it lasts, so no one's day moves on without the fighter and
        /// the clock sync has nothing to pull back and forth.
        /// </summary>
        private static void HoldClock(bool hold)
        {
            if (!hold && !clockHeld)
            {
                return;
            }
            try
            {
                object engine = MainGame.Instance == null || MainGame.Instance.GameSave == null ? null : MainGame.Instance.GameSave.environmentData.EnvironmentEngine;
                PropertyInfo paused = engine?.GetType().GetProperty("IsPaused");
                if (paused == null)
                {
                    clockHeld = false;
                    return;
                }
                if (hold)
                {
                    // Checked every tick: this game restarts its clock when a fight of its own
                    // ends, even one called off while the other's goes on.
                    if ((bool)paused.GetValue(engine, null))
                    {
                        return;
                    }
                    paused.SetValue(engine, true, null);
                    if (!clockHeld)
                    {
                        log.LogInfo("Fight lock: the clock stops while " + holderName + " fights.");
                    }
                    clockHeld = true;
                }
                else
                {
                    paused.SetValue(engine, false, null);
                    clockHeld = false;
                    log.LogInfo("Fight lock: the clock runs again.");
                }
            }
            catch (Exception ex)
            {
                clockHeld = false;
                log.LogWarning("Fight lock: could not hold the clock: " + ex.Message);
            }
        }

        private static bool IsConnected(NetworkManager netcode, ulong clientId)
        {
            foreach (ulong connected in netcode.ConnectedClientsIds)
            {
                if (connected == clientId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Someone else was first: this player's fight, begun in the same moment, is called off.</summary>
        private static void CallOff(string name)
        {
            try
            {
                FightingGameController controller = LazySingleton<FightingGameController>.Instance;
                FightState state = controller.CurrentFightState;
                if (state == FightState.InPreFight)
                {
                    controller.CancelPreFight();
                }
                else if (state != FightState.Disabled)
                {
                    controller.Stop();
                }
                cancelled++;
                Refuse(name);
                log.LogInfo("Fight lock: " + name + " was fighting first; this fight was called off.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight lock: could not call the fight off: " + ex.Message);
            }
        }

        /// <summary>A line in the game's own notification strip.</summary>
        private static void Tell(string text)
        {
            try
            {
                UINotificator notificator = UINotificator.Instance;
                UISimpleTextNotification note = LazyPooler.GetObject<UISimpleTextNotification>();
                if (notificator != null && note != null)
                {
                    note.LocalizationKey = string.Empty;
                    note.Text = text;
                    AccessTools.Method(typeof(UINotificator), "ShowNotification").Invoke(notificator, new object[] { note });
                    return;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight lock: could not show the notice: " + ex.Message);
            }
            log.LogInfo("Fight lock: " + text);
        }

        // ---------------------------------------------------------------- host

        private static void Decide(NetworkManager netcode, ulong clientId, bool claim)
        {
            if (claim)
            {
                if (holder != Nobody && holder != clientId)
                {
                    // Someone was first: tell the one asking.
                    SendState(netcode, clientId);
                    if (clientId == netcode.LocalClientId && chosen)
                    {
                        CallOff(holderName);
                    }
                    return;
                }
                holder = clientId;
                holderName = clientId == netcode.LocalClientId ? CoopSession.LocalName : CoopSession.NameFor(clientId);
                SendState(netcode, null);
            }
            else if (holder == clientId)
            {
                holder = Nobody;
                holderName = string.Empty;
                SendState(netcode, null);
            }
        }

        private static void SendToHost(NetworkManager netcode, byte kind)
        {
            using (var writer = new FastBufferWriter(96, Allocator.Temp))
            {
                writer.WriteValueSafe(kind);
                writer.WriteValueSafe(Nobody);
                writer.WriteValueSafe(new FixedString64Bytes(string.Empty));
                netcode.CustomMessagingManager.SendNamedMessage(LockMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private static void SendState(NetworkManager netcode, ulong? only)
        {
            using (var writer = new FastBufferWriter(96, Allocator.Temp))
            {
                writer.WriteValueSafe(KindState);
                writer.WriteValueSafe(holder);
                writer.WriteValueSafe(new FixedString64Bytes(holderName ?? string.Empty));
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId && (only == null || only.Value == clientId))
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(LockMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
            }
        }

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
                reader.ReadValueSafe(out ulong clientId);
                reader.ReadValueSafe(out FixedString64Bytes name);
                if (netcode.IsHost)
                {
                    if (kind == KindClaim || kind == KindRelease)
                    {
                        Decide(netcode, sender, kind == KindClaim);
                    }
                    return;
                }
                if (kind != KindState)
                {
                    return;
                }
                if (clientId == Nobody)
                {
                    if (!claimed)
                    {
                        holder = Nobody;
                        holderName = string.Empty;
                    }
                    return;
                }
                holder = clientId;
                holderName = name.ToString();
                if (clientId != netcode.LocalClientId && claimed && chosen)
                {
                    claimed = false;
                    CallOff(holderName);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Fight lock: could not read a message from " + sender + ": " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- tests

        /// <summary>Tests: choose a level at a fight point, as the level list would: refused while someone else fights.</summary>
        internal static string ChooseForTest(string levelId, bool asIfAtOnce)
        {
            // asIfAtOnce: as if chosen in the same moment as the other's, before their claim arrived.
            if (!asIfAtOnce && BlockedBy(out string name))
            {
                Refuse(name);
                return "refused " + name + "; " + Describe();
            }
            chosen = true;
            LazySingleton<FightingGameController>.Instance.Play(levelId);
            return "started " + levelId + "; " + Describe();
        }
    }
}
