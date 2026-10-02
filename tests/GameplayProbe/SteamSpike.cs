using System;
using System.Runtime.InteropServices;
using System.Text;
using Steamworks;

// Phase 0 spike for Steam multiplayer: can two local game processes talk through
// SteamNetworkingSockets? Uses the IP mode (no relay, no second account needed) — the same library
// and message path the P2P transport will use.
internal static class SteamSpike
{
    private static HSteamListenSocket listen = HSteamListenSocket.Invalid;
    private static HSteamNetConnection connection = HSteamNetConnection.Invalid;
    private static Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
    private static readonly StringBuilder events = new StringBuilder();

    internal static string Run(string[] args)
    {
        switch (args[0])
        {
            case "steam-status":
                return Status();
            case "steam-listen":
                return Listen(ushort.Parse(args[1]));
            case "steam-connect":
                return Connect(ushort.Parse(args[1]));
            case "steam-send":
                return Send(args[1]);
            case "steam-poll":
                return Poll();
            case "steam-close":
                return Close();
            case "steam-lobby":
                return Lobby();
            case "steam-join-lobby":
                return JoinLobby(ulong.Parse(args[1]));
            case "steam-lobby-set":
                return SetLobbyData(args[1], args[2]);
            case "steam-lobby-drop":
            {
                // As Steam closing the lobby under the host: the host leaves it on Steam's side only.
                Type lobbyType = Type.GetType("GK2Coop.CoopSteamLobby, GK2Coop");
                ulong id = (ulong)lobbyType.GetProperty("HostedLobbyId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
                SteamMatchmaking.LeaveLobby(new CSteamID(id));
                return "STEAM-LOBBY-DROP " + id;
            }
            case "steam-lobby-outcome":
                return "STEAM-LOBBY-OUTCOME " + Type.GetType("GK2Coop.CoopSteamLobby, GK2Coop")
                    .GetProperty("LastOutcome", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
            default:
                throw new Exception("Unknown steam spike command");
        }
    }

    private static string Status()
    {
        bool running = SteamAPI.IsSteamRunning();
        string id = "-";
        string name = "-";
        try
        {
            id = SteamUser.GetSteamID().ToString();
            name = SteamFriends.GetPersonaName();
        }
        catch (Exception ex)
        {
            id = "unavailable (" + ex.GetType().Name + ")";
        }
        return "STEAM-STATUS running=" + running + " id=" + id + " name=" + name;
    }

    private static void EnsureCallback()
    {
        if (statusCallback == null)
        {
            statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
            SteamNetworkingUtils.InitRelayNetworkAccess();
        }
    }

    private static SteamNetworkingConfigValue_t[] Options()
    {
        // Local test: both processes are the same Steam account, so allow unauthenticated IP.
        var allow = new SteamNetworkingConfigValue_t
        {
            m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_IP_AllowWithoutAuth,
            m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32
        };
        allow.m_val.m_int32 = 2;
        return new[] { allow };
    }

    private static SteamNetworkingIPAddr Loopback(ushort port)
    {
        var address = new SteamNetworkingIPAddr();
        address.Clear();
        address.SetIPv4(0x7f000001, port);
        return address;
    }

    private static string Listen(ushort port)
    {
        EnsureCallback();
        SteamNetworkingIPAddr address = Loopback(port);
        address.SetIPv4(0, port);
        SteamNetworkingConfigValue_t[] options = Options();
        listen = SteamNetworkingSockets.CreateListenSocketIP(ref address, options.Length, options);
        return "STEAM-LISTEN port=" + port + " socket=" + listen.m_HSteamListenSocket + " ok=" + (listen != HSteamListenSocket.Invalid);
    }

    private static string Connect(ushort port)
    {
        EnsureCallback();
        SteamNetworkingIPAddr address = Loopback(port);
        SteamNetworkingConfigValue_t[] options = Options();
        connection = SteamNetworkingSockets.ConnectByIPAddress(ref address, options.Length, options);
        return "STEAM-CONNECT port=" + port + " connection=" + connection.m_HSteamNetConnection + " ok=" + (connection != HSteamNetConnection.Invalid);
    }

    private static void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t update)
    {
        ESteamNetworkingConnectionState state = update.m_info.m_eState;
        events.Append(state).Append("(").Append(update.m_info.m_eEndReason).Append(") ");
        if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting &&
            update.m_info.m_hListenSocket != HSteamListenSocket.Invalid)
        {
            EResult accepted = SteamNetworkingSockets.AcceptConnection(update.m_hConn);
            events.Append("accept=").Append(accepted).Append(" ");
            connection = update.m_hConn;
        }
        else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                 state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
        {
            SteamNetworkingSockets.CloseConnection(update.m_hConn, 0, "closed", false);
        }
    }

    private static string Send(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            EResult result = SteamNetworkingSockets.SendMessageToConnection(connection, buffer, (uint)bytes.Length,
                Constants.k_nSteamNetworkingSend_Reliable, out long number);
            return "STEAM-SEND " + result + " number=" + number;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Poll()
    {
        var received = new StringBuilder();
        if (connection != HSteamNetConnection.Invalid)
        {
            var messages = new IntPtr[16];
            int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, messages, messages.Length);
            for (int i = 0; i < count; i++)
            {
                SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(messages[i]);
                byte[] data = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, data, 0, data.Length);
                received.Append("[").Append(Encoding.UTF8.GetString(data)).Append("]");
                SteamNetworkingMessage_t.Release(messages[i]);
            }
        }
        string state = "none";
        long ping = -1;
        if (connection != HSteamNetConnection.Invalid && SteamNetworkingSockets.GetConnectionInfo(connection, out SteamNetConnectionInfo_t info))
        {
            state = info.m_eState.ToString();
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK)
            {
                ping = status.m_nPing;
            }
        }
        string eventText = events.ToString();
        events.Length = 0;
        return "STEAM-POLL state=" + state + " ping=" + ping + " received=" + received + " events=" + eventText;
    }

    // Lobby checks: what the host's lobby tells friends, and a join request as Steam would deliver it.
    private static string Lobby()
    {
        Type lobbyType = Type.GetType("GK2Coop.CoopSteamLobby, GK2Coop");
        ulong id = (ulong)lobbyType.GetProperty("HostedLobbyId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
        if (id == 0)
        {
            return "STEAM-LOBBY none";
        }
        var lobby = new CSteamID(id);
        string connect = SteamFriends.GetFriendRichPresence(SteamUser.GetSteamID(), "connect");
        return "STEAM-LOBBY " + id + " marker=" + SteamMatchmaking.GetLobbyData(lobby, "gk2coop") +
               " version=" + SteamMatchmaking.GetLobbyData(lobby, "version") +
               " protocol=" + SteamMatchmaking.GetLobbyData(lobby, "protocol") +
               " limit=" + SteamMatchmaking.GetLobbyMemberLimit(lobby) +
               " owner=" + SteamMatchmaking.GetLobbyOwner(lobby) + " connect=" + connect;
    }

    private static string SetLobbyData(string key, string value)
    {
        Type lobbyType = Type.GetType("GK2Coop.CoopSteamLobby, GK2Coop");
        ulong id = (ulong)lobbyType.GetProperty("HostedLobbyId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
        bool ok = SteamMatchmaking.SetLobbyData(new CSteamID(id), key, value);
        return "STEAM-LOBBY-SET " + key + "=" + value + " ok=" + ok;
    }

    private static string JoinLobby(ulong id)
    {
        Type menu = Type.GetType("GK2Coop.CoopMenu, GK2Coop");
        menu.GetField("nameField", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, "BootstrapGuest");
        Type lobbyType = Type.GetType("GK2Coop.CoopSteamLobby, GK2Coop");
        lobbyType.GetMethod("RequestJoin", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { new CSteamID(id), "the test probe" });
        return "STEAM-JOIN-LOBBY requested " + id;
    }

    private static string Close()
    {
        if (connection != HSteamNetConnection.Invalid)
        {
            SteamNetworkingSockets.CloseConnection(connection, 0, "spike done", false);
            connection = HSteamNetConnection.Invalid;
        }
        if (listen != HSteamListenSocket.Invalid)
        {
            SteamNetworkingSockets.CloseListenSocket(listen);
            listen = HSteamListenSocket.Invalid;
        }
        return "STEAM-CLOSE";
    }
}
