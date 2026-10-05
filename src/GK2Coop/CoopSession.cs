using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Session identity and agreement: the handshake that rejects mismatched builds, the peer
    /// name registry behind the HUD, host-authoritative clock replication, and stable per-player
    /// GUIDs. None of this touches gameplay state; it is the foundation the later phases assume.
    /// </summary>
    internal static class CoopSession
    {
        /// <summary>
        /// Bumped whenever the wire format of any GK2Coop message changes. Two builds with
        /// different values must not play together, even if the plugin version happens to match.
        /// </summary>
        internal const int ProtocolVersion = 34;

        /// <summary>Tells a client that another client exists, so it can create a body for it.</summary>
        internal const string PeerMessage = "GK2Coop.Peer.v1";
        internal const string PeerLeftMessage = "GK2Coop.PeerLeft.v1";

        private const string HelloMessage = "GK2Coop.Hello.v1";
        private const string WelcomeMessage = "GK2Coop.Welcome.v1";
        private const string ClockMessage = "GK2Coop.Clock.v1";

        private const float ClockBroadcastInterval = 5f;
        // One day is five real minutes, so this is about a second of real time.
        private const float ClockCorrectionThreshold = 0.004f;

        private static ManualLogSource log;
        private static readonly Dictionary<ulong, string> peerNames = new Dictionary<ulong, string>();

        private static bool handlersRegistered;
        private static object registeredMessaging;
        private static bool helloSent;
        private static bool welcomeReceived;
        private static bool clientWasConnected;
        private static float nextHelloRetry;
        private static float nextClockBroadcast;

        private static string configuredName = string.Empty;

        /// <summary>
        /// Over Steam, the player's Steam name (friends know each other by it); otherwise the
        /// configured name, or a role-derived fallback. Resolved lazily rather than at startup
        /// because the role and the wire are not known until the session begins — and a fixed
        /// fallback would leave both players called the same thing.
        /// </summary>
        internal static string LocalName
        {
            get
            {
                string steamName = SteamName();
                if (!string.IsNullOrEmpty(steamName))
                {
                    return steamName;
                }
                if (!string.IsNullOrEmpty(configuredName))
                {
                    return configuredName;
                }
                NetworkManager netcode = NetworkManager.Singleton;
                if (netcode == null || !netcode.IsListening)
                {
                    return "Player";
                }
                return netcode.IsHost ? "Host" : "Player " + (netcode.LocalClientId + 1);
            }
        }
        internal static bool ClockSyncEnabled { get; set; }
        internal static bool SuppressHello { get; set; }
        internal static string HandshakeState { get; private set; } = "not started";
        internal static bool Rejected { get; private set; }

        /// <summary>This client has been accepted by the host in the current connection.</summary>
        internal static bool Welcomed => welcomeReceived;

        internal static void Init(ManualLogSource source, string localName)
        {
            log = source;
            SetLocalName(localName);
        }

        /// <summary>The Steam name, when this session runs over Steam.</summary>
        private static string SteamName()
        {
            try
            {
                if (CoopSteamTransportSwitch.ActiveName != "Steam" || !CoopSteamTransportSwitch.SteamAvailable())
                {
                    return null;
                }
                string name = Steamworks.SteamFriends.GetPersonaName();
                if (string.IsNullOrEmpty(name))
                {
                    return null;
                }
                name = name.Trim();
                return name.Length > 24 ? name.Substring(0, 24) : name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void SetLocalName(string localName)
        {
            configuredName = string.IsNullOrEmpty(localName) ? string.Empty : localName.Trim();
            if (configuredName.Length > 24)
            {
                configuredName = configuredName.Substring(0, 24);
            }
        }

        internal static void Reset()
        {
            peerNames.Clear();
            helloSent = false;
            welcomeReceived = false;
            clientWasConnected = false;
            nextHelloRetry = 0f;
            Rejected = false;
            HandshakeState = "not started";
            CoopPlayerProfiles.Reset();
        }

        /// <summary>Name for a peer, falling back to a role label until its handshake arrives.</summary>
        internal static string NameFor(ulong clientId)
        {
            string name;
            if (peerNames.TryGetValue(clientId, out name))
            {
                return name;
            }
            return clientId == NetworkManager.ServerClientId ? "Host" : "Player " + clientId;
        }

        internal static void ForgetPeer(ulong clientId)
        {
            peerNames.Remove(clientId);
        }

        internal static bool KnowsName(ulong clientId)
        {
            return peerNames.ContainsKey(clientId);
        }

        // ---------------------------------------------------------------- message plumbing

        internal static void EnsureHandlers()
        {
            if (NetworkManager.Singleton == null ||
                NetworkManager.Singleton.CustomMessagingManager == null)
            {
                return;
            }
            var messaging = NetworkManager.Singleton.CustomMessagingManager;
            // Netcode builds a new CustomMessagingManager each time it starts. A joiner that copies
            // the host's world connects twice in one process (menu, then in game), and handlers
            // registered on the first manager are gone on the second. Checking a flag alone left
            // that joiner deaf to every co-op message after Continue.
            if (handlersRegistered && ReferenceEquals(messaging, registeredMessaging))
            {
                return;
            }
            if (handlersRegistered)
            {
                log.LogInfo("Netcode restarted with a new message manager; registering co-op handlers again.");
            }
            registeredMessaging = messaging;
            messaging.RegisterNamedMessageHandler(HelloMessage, ReceiveHello);
            messaging.RegisterNamedMessageHandler(WelcomeMessage, ReceiveWelcome);
            messaging.RegisterNamedMessageHandler(ClockMessage, ReceiveClock);
            messaging.RegisterNamedMessageHandler(PeerMessage, ReceivePeer);
            messaging.RegisterNamedMessageHandler(PeerLeftMessage, ReceivePeerLeft);
            messaging.RegisterNamedMessageHandler(CoopLogRelay.LogMessage, CoopLogRelay.Receive);
            messaging.RegisterNamedMessageHandler(CoopToolSync.ToolUseMessage, CoopToolSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopWorldSync.DeathMessage, CoopWorldSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopQuestSync.QuestMessage, CoopQuestSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopDropSync.SpawnMessage, CoopDropSync.ReceiveSpawn);
            messaging.RegisterNamedMessageHandler(CoopDropSync.PickupRequestMessage, CoopDropSync.ReceivePickupRequest);
            messaging.RegisterNamedMessageHandler(CoopDropSync.PickupResultMessage, CoopDropSync.ReceivePickupResult);
            messaging.RegisterNamedMessageHandler(CoopDropSync.MergeMessage, CoopDropSync.ReceiveMerge);
            messaging.RegisterNamedMessageHandler(CoopDropSync.TakenMessage, CoopDropSync.ReceiveTaken);
            messaging.RegisterNamedMessageHandler(CoopTechPointSync.SpawnMessage, CoopTechPointSync.ReceiveSpawn);
            messaging.RegisterNamedMessageHandler(CoopTechPointSync.TakenMessage, CoopTechPointSync.ReceiveTaken);
            messaging.RegisterNamedMessageHandler(CoopContainerSync.RequestMessage, CoopContainerSync.ReceiveRequest);
            messaging.RegisterNamedMessageHandler(CoopContainerSync.StateMessage, CoopContainerSync.ReceiveState);
            messaging.RegisterNamedMessageHandler(CoopContainerSync.RichMessage, CoopContainerSync.ReceiveRich);
            messaging.RegisterNamedMessageHandler(CoopSaveBootstrap.RequestMessage, CoopSaveBootstrap.ReceiveRequest);
            messaging.RegisterNamedMessageHandler(CoopSaveBootstrap.MetaMessage, CoopSaveBootstrap.ReceiveMeta);
            messaging.RegisterNamedMessageHandler(CoopSaveBootstrap.ChunkMessage, CoopSaveBootstrap.ReceiveChunk);
            messaging.RegisterNamedMessageHandler(CoopSaveBootstrap.AckMessage, CoopSaveBootstrap.ReceiveAck);
            messaging.RegisterNamedMessageHandler(CoopSaveBootstrap.ErrorMessage, CoopSaveBootstrap.ReceiveError);
            messaging.RegisterNamedMessageHandler(CoopPlayerProfiles.HelloMessage, CoopPlayerProfiles.ReceiveHello);
            messaging.RegisterNamedMessageHandler(CoopPlayerProfiles.DataMessage, CoopPlayerProfiles.ReceiveData);
            messaging.RegisterNamedMessageHandler(CoopCraftSync.RequestMessage, CoopCraftSync.ReceiveRequest);
            messaging.RegisterNamedMessageHandler(CoopCraftSync.StateMessage, CoopCraftSync.ReceiveState);
            messaging.RegisterNamedMessageHandler(CoopCraftSync.InventoryMessage, CoopCraftSync.ReceiveInventory);
            messaging.RegisterNamedMessageHandler(CoopCraftSync.TakeMessage, CoopCraftSync.ReceiveTake);
            messaging.RegisterNamedMessageHandler(CoopCraftSync.TakeResultMessage, CoopCraftSync.ReceiveTakeResult);
            messaging.RegisterNamedMessageHandler(CoopCraftSync.ConsumeMessage, CoopCraftSync.ReceiveConsume);
            messaging.RegisterNamedMessageHandler(CoopGardenSync.PlantMessage, CoopGardenSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopCraftEndSync.EndMessage, CoopCraftEndSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopSleepSync.SleepMessage, CoopSleepSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopSleepSync.NightMessage, CoopSleepSync.ReceiveNight);
            messaging.RegisterNamedMessageHandler(CoopAppearanceSync.AppearanceMessage, CoopAppearanceSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopOverheadSync.StateMessage, CoopOverheadSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopOverheadSync.GiveMessage, CoopOverheadSync.ReceiveGive);
            messaging.RegisterNamedMessageHandler(CoopVendorSync.VendorMessage, CoopVendorSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopKnowledgeSync.KnowledgeMessage, CoopKnowledgeSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopSceneSync.SceneMessage, CoopSceneSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopWorldResSync.WorldResMessage, CoopWorldResSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopBuildSync.BuildMessage, CoopBuildSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopFightSync.FightMessage, CoopFightSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopZombieSync.PresenceMessage, CoopZombieSync.ReceivePresence);
            messaging.RegisterNamedMessageHandler(CoopZombieSync.MotionMessage, CoopZombieSync.ReceiveMotion);
            messaging.RegisterNamedMessageHandler(CoopWeatherSync.WeatherMessage, CoopWeatherSync.Receive);
            messaging.RegisterNamedMessageHandler(CoopSharedGems.GemsMessage, CoopSharedGems.Receive);
            messaging.RegisterNamedMessageHandler(CoopChat.ChatMessage, CoopChat.Receive);
            messaging.RegisterNamedMessageHandler(CoopSceneShare.ShareMessage, CoopSceneShare.Receive);
            messaging.RegisterNamedMessageHandler(CoopSpeechShare.SpeechMessage, CoopSpeechShare.Receive);
            messaging.RegisterNamedMessageHandler(CoopWorkLock.LockMessage, CoopWorkLock.Receive);
            messaging.RegisterNamedMessageHandler(CoopFightWatch.FightWatchMessage, CoopFightWatch.Receive);
            messaging.RegisterNamedMessageHandler(CoopFightLock.LockMessage, CoopFightLock.Receive);
            handlersRegistered = true;
            log.LogInfo("Session handshake handlers registered (protocol v" + ProtocolVersion + ").");
        }

        internal static void UnregisterHandlers()
        {
            if (!handlersRegistered || NetworkManager.Singleton == null)
            {
                return;
            }
            var messaging = NetworkManager.Singleton.CustomMessagingManager;
            if (messaging != null)
            {
                messaging.UnregisterNamedMessageHandler(HelloMessage);
                messaging.UnregisterNamedMessageHandler(WelcomeMessage);
                messaging.UnregisterNamedMessageHandler(ClockMessage);
                messaging.UnregisterNamedMessageHandler(PeerMessage);
                messaging.UnregisterNamedMessageHandler(PeerLeftMessage);
                messaging.UnregisterNamedMessageHandler(CoopLogRelay.LogMessage);
                messaging.UnregisterNamedMessageHandler(CoopToolSync.ToolUseMessage);
                messaging.UnregisterNamedMessageHandler(CoopWorldSync.DeathMessage);
                messaging.UnregisterNamedMessageHandler(CoopQuestSync.QuestMessage);
                messaging.UnregisterNamedMessageHandler(CoopDropSync.SpawnMessage);
                messaging.UnregisterNamedMessageHandler(CoopDropSync.PickupRequestMessage);
                messaging.UnregisterNamedMessageHandler(CoopDropSync.PickupResultMessage);
                messaging.UnregisterNamedMessageHandler(CoopDropSync.MergeMessage);
                messaging.UnregisterNamedMessageHandler(CoopDropSync.TakenMessage);
                messaging.UnregisterNamedMessageHandler(CoopTechPointSync.SpawnMessage);
                messaging.UnregisterNamedMessageHandler(CoopTechPointSync.TakenMessage);
                messaging.UnregisterNamedMessageHandler(CoopContainerSync.RequestMessage);
                messaging.UnregisterNamedMessageHandler(CoopContainerSync.StateMessage);
                messaging.UnregisterNamedMessageHandler(CoopContainerSync.RichMessage);
                messaging.UnregisterNamedMessageHandler(CoopSaveBootstrap.RequestMessage);
                messaging.UnregisterNamedMessageHandler(CoopSaveBootstrap.MetaMessage);
                messaging.UnregisterNamedMessageHandler(CoopSaveBootstrap.ChunkMessage);
                messaging.UnregisterNamedMessageHandler(CoopSaveBootstrap.AckMessage);
                messaging.UnregisterNamedMessageHandler(CoopSaveBootstrap.ErrorMessage);
                messaging.UnregisterNamedMessageHandler(CoopPlayerProfiles.HelloMessage);
                messaging.UnregisterNamedMessageHandler(CoopPlayerProfiles.DataMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftSync.RequestMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftSync.StateMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftSync.InventoryMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftSync.TakeMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftSync.TakeResultMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftSync.ConsumeMessage);
                messaging.UnregisterNamedMessageHandler(CoopGardenSync.PlantMessage);
                messaging.UnregisterNamedMessageHandler(CoopCraftEndSync.EndMessage);
                messaging.UnregisterNamedMessageHandler(CoopSleepSync.SleepMessage);
                messaging.UnregisterNamedMessageHandler(CoopSleepSync.NightMessage);
                messaging.UnregisterNamedMessageHandler(CoopAppearanceSync.AppearanceMessage);
                messaging.UnregisterNamedMessageHandler(CoopOverheadSync.StateMessage);
                messaging.UnregisterNamedMessageHandler(CoopOverheadSync.GiveMessage);
                messaging.UnregisterNamedMessageHandler(CoopVendorSync.VendorMessage);
                messaging.UnregisterNamedMessageHandler(CoopKnowledgeSync.KnowledgeMessage);
                messaging.UnregisterNamedMessageHandler(CoopSceneSync.SceneMessage);
                messaging.UnregisterNamedMessageHandler(CoopWorldResSync.WorldResMessage);
                messaging.UnregisterNamedMessageHandler(CoopBuildSync.BuildMessage);
                messaging.UnregisterNamedMessageHandler(CoopFightSync.FightMessage);
                messaging.UnregisterNamedMessageHandler(CoopZombieSync.PresenceMessage);
                messaging.UnregisterNamedMessageHandler(CoopZombieSync.MotionMessage);
                messaging.UnregisterNamedMessageHandler(CoopWeatherSync.WeatherMessage);
                messaging.UnregisterNamedMessageHandler(CoopSharedGems.GemsMessage);
                messaging.UnregisterNamedMessageHandler(CoopChat.ChatMessage);
                messaging.UnregisterNamedMessageHandler(CoopSceneShare.ShareMessage);
                messaging.UnregisterNamedMessageHandler(CoopSpeechShare.SpeechMessage);
                messaging.UnregisterNamedMessageHandler(CoopWorkLock.LockMessage);
                messaging.UnregisterNamedMessageHandler(CoopFightWatch.FightWatchMessage);
                messaging.UnregisterNamedMessageHandler(CoopFightLock.LockMessage);
            }
            handlersRegistered = false;
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsClient)
            {
                if (clientWasConnected) Reset();
                return;
            }
            EnsureHandlers();
            if (!handlersRegistered)
            {
                return;
            }

            if (netcode.IsHost)
            {
                BroadcastClock();
                return;
            }
            if (!netcode.IsConnectedClient && clientWasConnected)
            {
                // A menu-stage connection, a dropped link, or a same-process reconnect needs a
                // fresh Hello. Otherwise welcomeReceived suppresses it and the host never sends
                // the new peer its roster or join snapshot.
                Reset();
                log.LogInfo("Client session state reset after disconnect.");
            }
            if (netcode.IsConnectedClient) clientWasConnected = true;
            if (netcode.IsConnectedClient &&
                !SuppressHello &&
                !welcomeReceived &&
                Time.unscaledTime >= nextHelloRetry)
            {
                nextHelloRetry = Time.unscaledTime + 3f;
                SendHello();
            }
        }

        private static void SendHello()
        {
            HandshakeState = helloSent ? "hello resent" : "hello sent";
            helloSent = true;
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(ProtocolVersion);
                writer.WriteValueSafe(new FixedString64Bytes(Plugin.Version));
                writer.WriteValueSafe(new FixedString64Bytes(LocalName));
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    HelloMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
            }
            log.LogInfo("Sent handshake as '" + LocalName + "' (protocol v" + ProtocolVersion + ", plugin " + Plugin.Version + ").");
        }

        private static void ReceiveHello(ulong senderClientId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            {
                return;
            }
            try
            {
                int peerProtocol;
                FixedString64Bytes peerPlugin;
                FixedString64Bytes peerName;
                reader.ReadValueSafe(out peerProtocol);
                reader.ReadValueSafe(out peerPlugin);
                reader.ReadValueSafe(out peerName);

                bool accepted = peerProtocol == ProtocolVersion;
                string reason = accepted
                    ? string.Empty
                    : "protocol mismatch: host v" + ProtocolVersion + ", client v" + peerProtocol;

                if (accepted)
                {
                    peerNames[senderClientId] = peerName.ToString();
                    HandshakeState = "accepted " + peerName;
                    CoopStatus.Set(CoopPhase.Connected, L.F("{0} joined.", peerName));
                    CoopStatus.Announce(L.F("{0} joined.", peerName));
                    log.LogInfo("Client " + senderClientId + " joined as '" + peerName + "' (protocol v" +
                                peerProtocol + ", plugin " + peerPlugin + ").");
                }
                else
                {
                    HandshakeState = "rejected client " + senderClientId;
                    CoopStatus.Announce(L.F("{0} could not join: they have co-op mod {1}, you have {2}. You both need the same version.",
                        peerName, peerPlugin.ToString(), Plugin.Version), true, 20f);
                    log.LogWarning("Rejecting client " + senderClientId + ": " + reason +
                                   " (their plugin " + peerPlugin + ", ours " + Plugin.Version + ").");
                }

                SendWelcome(senderClientId, accepted, reason);

                if (accepted)
                {
                    // Before the world snapshot: the peers exist regardless of what the world
                    // looks like, and a client that does not know a peer cannot place its body.
                    ExchangePeerRoster(senderClientId);

                    // After the welcome, so the client has registered its handlers and knows it
                    // was accepted before world state starts arriving.
                    CoopJoinSnapshot.SendTo(senderClientId);
                    CoopCraftSync.SendAllTo(senderClientId);
                    CoopKnowledgeSync.SendAllTo(senderClientId);
                    CoopWorldResSync.SendAllTo(senderClientId);
                    CoopSharedGems.SendAllTo(senderClientId);
                }

                if (!accepted)
                {
                    // Give the rejection a frame to reach the client before tearing the link down,
                    // so it can show the reason instead of an unexplained disconnect.
                    CoopHud.NotePendingDisconnect(senderClientId, reason);
                }
            }
            catch (Exception ex)
            {
                log.LogError("Handshake from client " + senderClientId + " failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Introduces a joining client and the clients already present to each other. Each client
        /// builds records only for itself and the host, so without this a third player is invisible
        /// to everyone but the host — and the host's relay of their commands has nobody to apply
        /// them to.
        /// </summary>
        private static void ExchangePeerRoster(ulong joiningClientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsHost)
            {
                return;
            }
            int introduced = 0;
            foreach (ulong existing in netcode.ConnectedClientsIds)
            {
                if (existing == netcode.LocalClientId || existing == joiningClientId)
                {
                    continue;
                }
                SendPeer(joiningClientId, existing);
                SendPeer(existing, joiningClientId);
                introduced++;
            }
            if (introduced > 0)
            {
                log.LogInfo("Introduced client " + joiningClientId + " to " + introduced + " other client(s).");
            }
        }

        private static void SendPeer(ulong recipient, ulong peerClientId)
        {
            string peerName;
            if (!peerNames.TryGetValue(peerClientId, out peerName))
            {
                peerName = "Player " + peerClientId;
            }
            using (var writer = new FastBufferWriter(128, Allocator.Temp))
            {
                writer.WriteValueSafe((int)peerClientId);
                writer.WriteValueSafe(new FixedString64Bytes(peerName));
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    PeerMessage, recipient, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        internal static void ReceivePeer(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || netcode.IsHost || senderClientId != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                int peerClientId;
                FixedString64Bytes peerName;
                reader.ReadValueSafe(out peerClientId);
                reader.ReadValueSafe(out peerName);
                if (peerClientId == (int)netcode.LocalClientId || peerClientId == 0)
                {
                    return;
                }
                peerNames[(ulong)peerClientId] = peerName.ToString();
                Plugin.EnsureRemotePlayer(peerClientId, "peer '" + peerName + "'");
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read a peer introduction: " + ex.Message);
            }
        }

        /// <summary>
        /// The host: a joiner has left; the other joiners take their body away. Only the host sees
        /// joiners come and go, so without this, with three or four players, the one who left stayed
        /// standing on the others' screens — and a second body appeared when they came back.
        /// </summary>
        internal static void AnnouncePeerLeft(ulong leftClientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || !netcode.IsHost || netcode.CustomMessagingManager == null)
            {
                return;
            }
            foreach (ulong clientId in netcode.ConnectedClientsIds)
            {
                if (clientId == netcode.LocalClientId || clientId == leftClientId)
                {
                    continue;
                }
                using (var writer = new FastBufferWriter(16, Allocator.Temp))
                {
                    writer.WriteValueSafe((int)leftClientId);
                    netcode.CustomMessagingManager.SendNamedMessage(PeerLeftMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                }
            }
        }

        internal static void ReceivePeerLeft(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode == null || netcode.IsHost || senderClientId != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out int leftClientId);
                if (leftClientId <= 0 || (ulong)leftClientId == netcode.LocalClientId)
                {
                    return;
                }
                string name = NameFor((ulong)leftClientId);
                int bodies = CoopWatchdog.RemovePeer((ulong)leftClientId);
                ForgetPeer((ulong)leftClientId);
                CoopSceneSync.Forget((ulong)leftClientId);
                CoopStatus.Announce(L.F("{0} left.", name));
                log.LogInfo("Peer " + name + " (client " + leftClientId + ") left; removed their record and " + bodies + " body/bodies.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read a peer departure: " + ex.Message);
            }
        }

        private static void SendWelcome(ulong recipient, bool accepted, string reason)
        {
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(ProtocolVersion);
                writer.WriteValueSafe(accepted);
                writer.WriteValueSafe(new FixedString64Bytes(LocalName));
                writer.WriteValueSafe(new FixedString128Bytes(reason ?? string.Empty));
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    WelcomeMessage, recipient, writer, NetworkDelivery.Reliable);
            }
        }

        private static void ReceiveWelcome(ulong senderClientId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsHost)
            {
                return;
            }
            try
            {
                int hostProtocol;
                bool accepted;
                FixedString64Bytes hostName;
                FixedString128Bytes reason;
                reader.ReadValueSafe(out hostProtocol);
                reader.ReadValueSafe(out accepted);
                reader.ReadValueSafe(out hostName);
                reader.ReadValueSafe(out reason);

                welcomeReceived = true;
                peerNames[NetworkManager.ServerClientId] = hostName.ToString();
                if (accepted)
                {
                    Rejected = false;
                    HandshakeState = "accepted by " + hostName;
                    CoopStatus.Set(CoopPhase.Connected, L.F("You joined {0}'s game.", hostName));
                    CoopStatus.Announce(L.F("You joined {0}'s game.", hostName));
                    log.LogInfo("Host '" + hostName + "' accepted the connection (protocol v" + hostProtocol + ").");
                }
                else
                {
                    Rejected = true;
                    HandshakeState = "rejected: " + reason;
                    // A protocol number means nothing to a player, so the refusal is restated as
                    // the thing they can act on: the two builds differ.
                    string explained = hostProtocol == ProtocolVersion
                        ? reason.ToString()
                        : L.T("The host has a different version of the co-op mod. You both need the same version.");
                    CoopStatus.Set(CoopPhase.Failed, explained);
                    CoopStatus.Announce(L.F("The host turned you away. {0}", explained), true, 20f);
                    log.LogError("Host rejected the connection: " + reason);
                }
            }
            catch (Exception ex)
            {
                log.LogError("Welcome message could not be read: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- clock replication

        private static void BroadcastClock()
        {
            if (!ClockSyncEnabled ||
                Time.unscaledTime < nextClockBroadcast ||
                NetworkManager.Singleton.ConnectedClientsIds.Count <= 1)
            {
                return;
            }
            nextClockBroadcast = Time.unscaledTime + ClockBroadcastInterval;
            try
            {
                object environment = GetEnvironmentData();
                if (environment == null)
                {
                    return;
                }
                int day = Convert.ToInt32(CoopDiagnostics.GetMember(environment, "Day"));
                float timeOfDay = Convert.ToSingle(CoopDiagnostics.GetMember(environment, "TimeOfDay"));
                using (var writer = new FastBufferWriter(32, Allocator.Temp))
                {
                    writer.WriteValueSafe(day);
                    writer.WriteValueSafe(timeOfDay);
                    foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                    {
                        if (clientId != NetworkManager.Singleton.LocalClientId)
                        {
                            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                                ClockMessage, clientId, writer, NetworkDelivery.Unreliable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Clock broadcast failed: " + ex.Message);
                nextClockBroadcast = Time.unscaledTime + 30f;
            }
        }

        private static void ReceiveClock(ulong senderClientId, FastBufferReader reader)
        {
            if (!ClockSyncEnabled ||
                NetworkManager.Singleton == null ||
                NetworkManager.Singleton.IsHost ||
                senderClientId != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                int hostDay;
                float hostTimeOfDay;
                reader.ReadValueSafe(out hostDay);
                reader.ReadValueSafe(out hostTimeOfDay);

                object environment = GetEnvironmentData();
                if (environment == null)
                {
                    return;
                }
                int localDay = Convert.ToInt32(CoopDiagnostics.GetMember(environment, "Day"));
                float localTimeOfDay = Convert.ToSingle(CoopDiagnostics.GetMember(environment, "TimeOfDay"));

                // Midnight: the host can send day 18 at 1.0000 (the moment before it turns), and a
                // clock a breath ahead was at day 19 0.0014. Compared as day + time they are two game
                // minutes apart; compared by day they differed, and the correction set day 18 at
                // 1.0, which the game does not expect (its day read wrong: `chaos3`). So: one
                // number, and a time of 1.0 is the next day at 0.0.
                if (hostTimeOfDay >= 1f)
                {
                    hostDay += (int)hostTimeOfDay;
                    hostTimeOfDay -= (int)hostTimeOfDay;
                }
                double gap = Math.Abs((localDay + (double)localTimeOfDay) - (hostDay + (double)hostTimeOfDay));
                if (gap < ClockCorrectionThreshold)
                {
                    return;
                }

                if (localDay != hostDay)
                {
                    // Set the field directly. AddToDay() also runs vendor and town end-of-day
                    // updates, which are host-authoritative and must not be re-run here.
                    FieldInfo dayField = environment.GetType().GetField("day", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (dayField != null)
                    {
                        dayField.SetValue(environment, hostDay);
                    }
                }
                MethodInfo setTime = environment.GetType().GetMethod("SetTimeOfDay", BindingFlags.Instance | BindingFlags.Public);
                if (setTime != null)
                {
                    setTime.Invoke(environment, new object[] { hostTimeOfDay });
                }
                ApplyTimeToEnvironmentEngine(hostTimeOfDay);
                log.LogInfo("Clock corrected from day " + localDay + " " + localTimeOfDay.ToString("F4") +
                            " to host day " + hostDay + " " + hostTimeOfDay.ToString("F4") + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Clock correction failed: " + ex.Message);
            }
        }

        private static void ApplyTimeToEnvironmentEngine(float timeOfDay)
        {
            Type engineType = Plugin.FindGameType("EnvironmentEngine");
            object engine = CoopDiagnostics.GetStatic(engineType, "Instance");
            if (engine == null)
            {
                return;
            }
            MethodInfo setTime = engineType.GetMethod("SetTimeOfDay", BindingFlags.Instance | BindingFlags.Public);
            if (setTime != null)
            {
                setTime.Invoke(engine, new object[] { timeOfDay });
            }
        }

        private static object GetEnvironmentData()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            return save == null ? null : CoopDiagnostics.GetMember(save, "environmentData");
        }

        // ---------------------------------------------------------------- stable player identity

        /// <summary>
        /// Every <c>PlayerData</c> ships with the same hardcoded GUID
        /// (<c>49042eb8-eda7-4612-80c8-6fbfc39b52ac</c>), which collides as soon as there are two
        /// players. Derive one from the client ID instead: unique per player, and identical on
        /// both machines without needing to be replicated.
        /// </summary>
        internal static void AssignStablePlayerGuid(object networkPlayer)
        {
            if (networkPlayer == null)
            {
                return;
            }
            try
            {
                object playerData = CoopDiagnostics.GetMember(networkPlayer, "playerData");
                if (playerData == null)
                {
                    return;
                }
                int clientId = Convert.ToInt32(CoopDiagnostics.GetMember(networkPlayer, "clientId"));
                Guid stable = BuildPlayerGuid(clientId);

                Type sguidType = Plugin.FindGameType("SGuid");
                object sguid = Activator.CreateInstance(sguidType, stable);
                FieldInfo guidField = playerData.GetType().GetField("guid", BindingFlags.Instance | BindingFlags.NonPublic);
                if (guidField == null)
                {
                    log.LogWarning("PlayerData.guid not found; player GUIDs remain colliding.");
                    return;
                }
                guidField.SetValue(playerData, sguid);
                log.LogInfo("Assigned stable GUID " + stable + " to network player " + clientId + ".");
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not assign a stable player GUID: " + ex.Message);
            }
        }

        private static Guid BuildPlayerGuid(int clientId)
        {
            // Fixed namespace with the client id in the last four bytes: deterministic, so both
            // machines derive the same GUID for the same player without exchanging it.
            var bytes = new byte[16]
            {
                0x9C, 0x2C, 0x00, 0x9F, 0xC0, 0x0F, 0x4E, 0x1A,
                0xB7, 0x55, 0x11, 0x22, 0x00, 0x00, 0x00, 0x00
            };
            bytes[12] = (byte)(clientId & 0xFF);
            bytes[13] = (byte)((clientId >> 8) & 0xFF);
            bytes[14] = (byte)((clientId >> 16) & 0xFF);
            bytes[15] = (byte)((clientId >> 24) & 0xFF);
            return new Guid(bytes);
        }
    }
}
