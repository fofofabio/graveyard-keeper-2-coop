using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Netcode transport over Steam's networking (<c>SteamNetworkingSockets</c>).
    ///
    /// Everything the game and the mod send goes through Netcode's transport, so this swap alone
    /// moves the whole session onto Steam: connections by SteamID through Valve's relay (no port
    /// forwarding, no public address), or — for local tests and LAN — Steam's sockets over plain IP.
    ///
    /// The host listens both ways. A joiner connects to <see cref="TargetSteamId"/> when set, else to
    /// <see cref="TargetAddress"/>/<see cref="Port"/>. Steam's connection-status callback becomes
    /// Netcode's Connect/Disconnect events; received messages are drained into
    /// <see cref="PollEvent"/> each frame. Reliable Netcode deliveries map to Steam's reliable
    /// channel (which fragments up to 512 KB per message), unreliable ones to unreliable.
    /// </summary>
    internal sealed class SteamSocketsTransport : NetworkTransport
    {
        private const int MaxMessagesPerPoll = 256;
        private const int VirtualPort = 0;

        internal static ManualLogSource Log;

        internal ulong TargetSteamId;
        internal string TargetAddress = "127.0.0.1";
        internal ushort Port = 8889;

        private readonly Queue<Pending> events = new Queue<Pending>();
        private readonly Dictionary<uint, ulong> clientByConnection = new Dictionary<uint, ulong>();
        private readonly Dictionary<ulong, HSteamNetConnection> connectionByClient = new Dictionary<ulong, HSteamNetConnection>();
        private readonly IntPtr[] incoming = new IntPtr[MaxMessagesPerPoll];
        private Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
        private HSteamListenSocket listenP2P = HSteamListenSocket.Invalid;
        private HSteamListenSocket listenIP = HSteamListenSocket.Invalid;
        private HSteamNetPollGroup pollGroup = HSteamNetPollGroup.Invalid;
        private HSteamNetConnection serverConnection = HSteamNetConnection.Invalid;
        private bool isServer;
        private ulong nextClientId = 1;
        private IntPtr sendBuffer = IntPtr.Zero;
        private int sendBufferSize;

        private struct Pending
        {
            internal NetworkEvent Type;
            internal ulong ClientId;
            internal ArraySegment<byte> Payload;
        }

        public override ulong ServerClientId => 0;

        internal string Describe()
        {
            return "steam transport: " + (isServer ? "host, " + connectionByClient.Count + " joiner(s)" : "joiner, " + (serverConnection != HSteamNetConnection.Invalid ? "connected" : "not connected"));
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
        }

        private void EnsureCallback()
        {
            if (statusCallback == null)
            {
                statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
            }
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }

        private static SteamNetworkingConfigValue_t[] Options()
        {
            // Plain-IP connections (LAN and local tests) are allowed without Steam authentication;
            // P2P connections are always authenticated by Steam.
            var allow = new SteamNetworkingConfigValue_t
            {
                m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_IP_AllowWithoutAuth,
                m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32
            };
            allow.m_val.m_int32 = 2;
            return new[] { allow };
        }

        public override bool StartServer()
        {
            try
            {
                EnsureCallback();
                isServer = true;
                SteamNetworkingConfigValue_t[] options = Options();
                pollGroup = SteamNetworkingSockets.CreatePollGroup();
                listenP2P = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, options.Length, options);
                var address = new SteamNetworkingIPAddr();
                address.Clear();
                address.SetIPv4(0, Port);
                listenIP = SteamNetworkingSockets.CreateListenSocketIP(ref address, options.Length, options);
                Log?.LogInfo("Steam transport: hosting as " + SteamUser.GetSteamID() + " (P2P " + (listenP2P != HSteamListenSocket.Invalid ? "on" : "off") +
                             ", IP port " + Port + " " + (listenIP != HSteamListenSocket.Invalid ? "on" : "off") + ").");
                return listenP2P != HSteamListenSocket.Invalid || listenIP != HSteamListenSocket.Invalid;
            }
            catch (Exception ex)
            {
                Log?.LogError("Steam transport: could not start hosting: " + ex.Message);
                return false;
            }
        }

        public override bool StartClient()
        {
            try
            {
                EnsureCallback();
                isServer = false;
                SteamNetworkingConfigValue_t[] options = Options();
                if (TargetSteamId != 0)
                {
                    var identity = new SteamNetworkingIdentity();
                    identity.SetSteamID(new CSteamID(TargetSteamId));
                    serverConnection = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, options.Length, options);
                    Log?.LogInfo("Steam transport: connecting to Steam user " + TargetSteamId + " through Steam.");
                }
                else
                {
                    var address = new SteamNetworkingIPAddr();
                    address.Clear();
                    if (!address.ParseString(TargetAddress + ":" + Port))
                    {
                        Log?.LogError("Steam transport: not an address: " + TargetAddress);
                        return false;
                    }
                    serverConnection = SteamNetworkingSockets.ConnectByIPAddress(ref address, options.Length, options);
                    Log?.LogInfo("Steam transport: connecting to " + TargetAddress + ":" + Port + " through Steam's sockets.");
                }
                return serverConnection != HSteamNetConnection.Invalid;
            }
            catch (Exception ex)
            {
                Log?.LogError("Steam transport: could not connect: " + ex.Message);
                return false;
            }
        }

        private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t update)
        {
            HSteamNetConnection connection = update.m_hConn;
            ESteamNetworkingConnectionState state = update.m_info.m_eState;
            bool ours = isServer
                ? update.m_info.m_hListenSocket != HSteamListenSocket.Invalid &&
                  (update.m_info.m_hListenSocket == listenP2P || update.m_info.m_hListenSocket == listenIP)
                : connection == serverConnection;
            if (!ours)
            {
                return;
            }
            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (isServer)
                    {
                        EResult accepted = SteamNetworkingSockets.AcceptConnection(connection);
                        if (accepted != EResult.k_EResultOK)
                        {
                            SteamNetworkingSockets.CloseConnection(connection, 0, "not accepted", false);
                            Log?.LogWarning("Steam transport: could not accept a connection: " + accepted);
                            return;
                        }
                        SteamNetworkingSockets.SetConnectionPollGroup(connection, pollGroup);
                    }
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (isServer)
                    {
                        ulong clientId = nextClientId++;
                        clientByConnection[connection.m_HSteamNetConnection] = clientId;
                        connectionByClient[clientId] = connection;
                        events.Enqueue(new Pending { Type = NetworkEvent.Connect, ClientId = clientId });
                        Log?.LogInfo("Steam transport: player " + clientId + " connected (" + update.m_info.m_identityRemote.GetSteamID() + ").");
                    }
                    else
                    {
                        events.Enqueue(new Pending { Type = NetworkEvent.Connect, ClientId = ServerClientId });
                        Log?.LogInfo("Steam transport: connected to the host.");
                    }
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    Log?.LogInfo("Steam transport: connection ended (" + state + ", " + update.m_info.m_eEndReason + " " + update.m_info.m_szEndDebug + ").");
                    if (isServer)
                    {
                        if (clientByConnection.TryGetValue(connection.m_HSteamNetConnection, out ulong gone))
                        {
                            clientByConnection.Remove(connection.m_HSteamNetConnection);
                            connectionByClient.Remove(gone);
                            events.Enqueue(new Pending { Type = NetworkEvent.Disconnect, ClientId = gone });
                        }
                    }
                    else
                    {
                        serverConnection = HSteamNetConnection.Invalid;
                        events.Enqueue(new Pending { Type = NetworkEvent.Disconnect, ClientId = ServerClientId });
                    }
                    SteamNetworkingSockets.CloseConnection(connection, 0, "closed", false);
                    break;
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            HSteamNetConnection connection = isServer
                ? (connectionByClient.TryGetValue(clientId, out HSteamNetConnection found) ? found : HSteamNetConnection.Invalid)
                : serverConnection;
            if (connection == HSteamNetConnection.Invalid || payload.Count == 0)
            {
                return;
            }
            int flags = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced
                ? Constants.k_nSteamNetworkingSend_UnreliableNoNagle
                : Constants.k_nSteamNetworkingSend_ReliableNoNagle;
            if (sendBufferSize < payload.Count)
            {
                if (sendBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(sendBuffer);
                }
                sendBufferSize = Math.Max(payload.Count, 4096);
                sendBuffer = Marshal.AllocHGlobal(sendBufferSize);
            }
            Marshal.Copy(payload.Array, payload.Offset, sendBuffer, payload.Count);
            EResult result = SteamNetworkingSockets.SendMessageToConnection(connection, sendBuffer, (uint)payload.Count, flags, out long _);
            if (result != EResult.k_EResultOK && result != EResult.k_EResultIgnored)
            {
                Log?.LogWarning("Steam transport: a message of " + payload.Count + " bytes to player " + clientId + " was not sent (" + result + ").");
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            if (events.Count == 0)
            {
                Drain();
            }
            receiveTime = Time.realtimeSinceStartup;
            if (events.Count == 0)
            {
                clientId = 0;
                payload = default(ArraySegment<byte>);
                return NetworkEvent.Nothing;
            }
            Pending next = events.Dequeue();
            clientId = next.ClientId;
            payload = next.Payload;
            return next.Type;
        }

        private void Drain()
        {
            int count;
            if (isServer)
            {
                if (pollGroup == HSteamNetPollGroup.Invalid)
                {
                    return;
                }
                count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(pollGroup, incoming, incoming.Length);
            }
            else
            {
                if (serverConnection == HSteamNetConnection.Invalid)
                {
                    return;
                }
                count = SteamNetworkingSockets.ReceiveMessagesOnConnection(serverConnection, incoming, incoming.Length);
            }
            for (int i = 0; i < count; i++)
            {
                SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(incoming[i]);
                byte[] data = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, data, 0, data.Length);
                ulong from = ServerClientId;
                if (isServer && !clientByConnection.TryGetValue(message.m_conn.m_HSteamNetConnection, out from))
                {
                    SteamNetworkingMessage_t.Release(incoming[i]);
                    continue;
                }
                SteamNetworkingMessage_t.Release(incoming[i]);
                events.Enqueue(new Pending { Type = NetworkEvent.Data, ClientId = from, Payload = new ArraySegment<byte>(data) });
            }
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (connectionByClient.TryGetValue(clientId, out HSteamNetConnection connection))
            {
                SteamNetworkingSockets.CloseConnection(connection, 0, "disconnected by host", true);
                connectionByClient.Remove(clientId);
                clientByConnection.Remove(connection.m_HSteamNetConnection);
            }
        }

        public override void DisconnectLocalClient()
        {
            if (serverConnection != HSteamNetConnection.Invalid)
            {
                SteamNetworkingSockets.CloseConnection(serverConnection, 0, "left", true);
                serverConnection = HSteamNetConnection.Invalid;
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            HSteamNetConnection connection = isServer
                ? (connectionByClient.TryGetValue(clientId, out HSteamNetConnection found) ? found : HSteamNetConnection.Invalid)
                : serverConnection;
            if (connection == HSteamNetConnection.Invalid)
            {
                return 0;
            }
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK
                ? (ulong)Math.Max(0, status.m_nPing)
                : 0;
        }

        public override void Shutdown()
        {
            try
            {
                foreach (HSteamNetConnection connection in connectionByClient.Values)
                {
                    SteamNetworkingSockets.CloseConnection(connection, 0, "host closed", true);
                }
                if (serverConnection != HSteamNetConnection.Invalid)
                {
                    SteamNetworkingSockets.CloseConnection(serverConnection, 0, "left", true);
                }
                if (listenP2P != HSteamListenSocket.Invalid)
                {
                    SteamNetworkingSockets.CloseListenSocket(listenP2P);
                }
                if (listenIP != HSteamListenSocket.Invalid)
                {
                    SteamNetworkingSockets.CloseListenSocket(listenIP);
                }
                if (pollGroup != HSteamNetPollGroup.Invalid)
                {
                    SteamNetworkingSockets.DestroyPollGroup(pollGroup);
                }
            }
            catch (Exception ex)
            {
                Log?.LogWarning("Steam transport: shutdown: " + ex.Message);
            }
            connectionByClient.Clear();
            clientByConnection.Clear();
            events.Clear();
            listenP2P = HSteamListenSocket.Invalid;
            listenIP = HSteamListenSocket.Invalid;
            pollGroup = HSteamNetPollGroup.Invalid;
            serverConnection = HSteamNetConnection.Invalid;
            nextClientId = 1;
            if (sendBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(sendBuffer);
                sendBuffer = IntPtr.Zero;
                sendBufferSize = 0;
            }
        }
    }
}
