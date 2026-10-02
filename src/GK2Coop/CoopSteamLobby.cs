using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Steamworks;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// Steam lobbies, invites and "Join game".
    ///
    /// A host playing over Steam opens a friends-visible lobby sized for the session (2–4 players)
    /// that carries the mod version and protocol, and sets rich presence so friends see the game and
    /// can click "Join game". <see cref="OpenInviteDialog"/> opens Steam's invite overlay.
    ///
    /// A friend's request to join — accepting an invite or clicking "Join game"
    /// (<c>GameLobbyJoinRequested_t</c>), a rich-presence join
    /// (<c>GameRichPresenceJoinRequested_t</c>), or a game launched by Steam with
    /// <c>+connect_lobby &lt;id&gt;</c> — enters that lobby, checks the host runs the same mod
    /// version and protocol (a clear message if not, before anything is copied), and then runs the
    /// usual "Copy host world and join" to the host's SteamID. A request that arrives while the
    /// player is in a world waits until they are back on the main menu.
    /// </summary>
    internal static class CoopSteamLobby
    {
        private const string MarkerKey = "gk2coop";
        private const string VersionKey = "version";
        private const string ProtocolKey = "protocol";
        private const string NameKey = "host_name";

        private static ManualLogSource log;
        private static CallResult<LobbyCreated_t> created;
        private static CallResult<LobbyEnter_t> entered;
        private static Callback<GameLobbyJoinRequested_t> lobbyJoinRequested;
        private static Callback<GameRichPresenceJoinRequested_t> presenceJoinRequested;
        private static CSteamID hostedLobby = CSteamID.Nil;
        private static CSteamID joinedLobby = CSteamID.Nil;
        private static CSteamID pendingLobby = CSteamID.Nil;
        private static bool creating;
        private static bool launchArgumentChecked;

        /// <summary>[Network] MaxPlayers: 2, 3 or 4.</summary>
        internal static int MaxPlayers { get; set; } = 4;

        /// <summary>Tests only: join the lobby's host over local IP instead of its SteamID (one PC, one account).</summary>
        internal static bool TestJoinOverLocalIp { get; set; }

        internal static string LastOutcome { get; private set; } = "none";

        internal static ulong HostedLobbyId => hostedLobby.m_SteamID;

        internal static string Describe()
        {
            return "steam lobby: hosted=" + (hostedLobby.IsValid() ? hostedLobby.m_SteamID.ToString() : "-") +
                   ", joined=" + (joinedLobby.IsValid() ? joinedLobby.m_SteamID.ToString() : "-") + ", last=" + LastOutcome;
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        private static bool Ready()
        {
            return CoopSteamTransportSwitch.SteamAvailable();
        }

        private static void EnsureCallbacks()
        {
            if (lobbyJoinRequested != null)
            {
                return;
            }
            created = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(request => RequestJoin(request.m_steamIDLobby, "a Steam invite or \"Join game\""));
            presenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(request =>
            {
                CSteamID lobby = ParseConnect(request.m_rgchConnect);
                if (lobby.IsValid())
                {
                    RequestJoin(lobby, "a friend's \"Join game\"");
                }
            });
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            if (!Ready())
            {
                return;
            }
            try
            {
                EnsureCallbacks();
                NetworkManager netcode = NetworkManager.Singleton;
                bool hostingOverSteam = netcode != null && netcode.IsListening && netcode.IsHost && CoopSteamTransportSwitch.ActiveName == "Steam";
                if (hostingOverSteam && hostedLobby.IsValid() && !creating && !StillIn(hostedLobby))
                {
                    // Steam can close a lobby under us (connection hiccup, or in a one-account test
                    // the "joiner" leaving it). Open a fresh one so friends can still join.
                    log.LogInfo("Steam lobby: " + hostedLobby.m_SteamID + " is gone; opening a new one.");
                    hostedLobby = CSteamID.Nil;
                }
                if (hostingOverSteam && !hostedLobby.IsValid() && !creating)
                {
                    creating = true;
                    created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Math.Max(2, Math.Min(4, MaxPlayers))));
                }
                else if (!hostingOverSteam && hostedLobby.IsValid())
                {
                    SteamMatchmaking.LeaveLobby(hostedLobby);
                    SteamFriends.ClearRichPresence();
                    log.LogInfo("Steam lobby: closed (hosting ended).");
                    hostedLobby = CSteamID.Nil;
                }
                if (!launchArgumentChecked)
                {
                    launchArgumentChecked = true;
                    CSteamID fromLaunch = ParseConnect(string.Join(" ", Environment.GetCommandLineArgs()));
                    if (fromLaunch.IsValid())
                    {
                        RequestJoin(fromLaunch, "the game's launch from a Steam invite");
                    }
                }
                if (pendingLobby.IsValid() && OnMainMenu() && (netcode == null || !netcode.IsListening))
                {
                    CSteamID lobby = pendingLobby;
                    pendingLobby = CSteamID.Nil;
                    entered.Set(SteamMatchmaking.JoinLobby(lobby));
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Steam lobby: " + ex.Message);
            }
        }

        private static bool StillIn(CSteamID lobby)
        {
            CSteamID me = SteamUser.GetSteamID();
            int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < members; i++)
            {
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i) == me)
                {
                    return true;
                }
            }
            return false;
        }

        private static void OnLobbyCreated(LobbyCreated_t result, bool failed)
        {
            creating = false;
            if (failed || result.m_eResult != EResult.k_EResultOK)
            {
                LastOutcome = "lobby not created (" + result.m_eResult + ")";
                log.LogWarning("Steam lobby: could not create one (" + result.m_eResult + "); friends can still join by SteamID.");
                return;
            }
            hostedLobby = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(hostedLobby, MarkerKey, "1");
            SteamMatchmaking.SetLobbyData(hostedLobby, VersionKey, Plugin.Version);
            SteamMatchmaking.SetLobbyData(hostedLobby, ProtocolKey, CoopSession.ProtocolVersion.ToString());
            SteamMatchmaking.SetLobbyData(hostedLobby, NameKey, CoopSession.LocalName ?? string.Empty);
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + hostedLobby.m_SteamID);
            // What friends see next to the host's name in their Steam friends list.
            SteamFriends.SetRichPresence("status", L.T("Hosting co-op in Graveyard Keeper 2"));
            LastOutcome = "hosting lobby " + hostedLobby.m_SteamID;
            log.LogInfo("Steam lobby: " + hostedLobby.m_SteamID + " open for friends (" + Math.Max(2, Math.Min(4, MaxPlayers)) + " players).");
        }

        /// <summary>Hosting a Steam lobby that friends can be invited to.</summary>
        internal static bool CanInvite
        {
            get
            {
                try
                {
                    return Ready() && hostedLobby.IsValid();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Opens Steam's overlay to invite friends into the hosted lobby.</summary>
        internal static bool OpenInviteDialog(string from)
        {
            if (!Ready() || !hostedLobby.IsValid())
            {
                return false;
            }
            log.LogInfo("Steam lobby: invite overlay opened from " + from + ".");
            SteamFriends.ActivateGameOverlayInviteDialog(hostedLobby);
            return true;
        }

        /// <summary>A join request from Steam (invite, friends list, launch) or from the menu.</summary>
        internal static void RequestJoin(CSteamID lobby, string source)
        {
            if (!lobby.IsValid())
            {
                return;
            }
            log.LogInfo("Steam lobby: join requested through " + source + " (" + lobby.m_SteamID + ").");
            if (!OnMainMenu())
            {
                pendingLobby = lobby;
                CoopStatus.Announce(L.T("A friend invited you. Go back to the main menu to join."), false, 10f);
                return;
            }
            EnsureCallbacks();
            entered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private static void OnLobbyEntered(LobbyEnter_t result, bool failed)
        {
            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (failed || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                var response = (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
                // A lobby Steam has closed (seen once after a joiner's game was killed: the host was
                // still in it by its own count). The host opens a new one within seconds, and
                // "Join game" again finds it — say so rather than show Steam's code.
                bool gone = failed || response == EChatRoomEnterResponse.k_EChatRoomEnterResponseError || response == EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist;
                Fail(gone ? L.T("Your friend's game is not open to join right now. If they are hosting, try again in a moment.") : L.F("Could not enter your friend's game ({0}).", response));
                return;
            }
            if (lobby == hostedLobby)
            {
                return;
            }
            joinedLobby = lobby;
            if (SteamMatchmaking.GetLobbyData(lobby, MarkerKey) != "1")
            {
                Leave();
                Fail(L.T("That Steam game is not a co-op session."));
                return;
            }
            string version = SteamMatchmaking.GetLobbyData(lobby, VersionKey);
            string protocol = SteamMatchmaking.GetLobbyData(lobby, ProtocolKey);
            if (protocol != CoopSession.ProtocolVersion.ToString())
            {
                Leave();
                Fail(version == Plugin.Version
                    ? L.T("Your friend's co-op mod does not match yours. You both need the same release.")
                    : L.F("Your friend has co-op mod {0}, you have {1}. You both need the same version.", version, Plugin.Version));
                return;
            }
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (!TestJoinOverLocalIp && owner == SteamUser.GetSteamID())
            {
                // Entered a lobby its host had already left: we would be joining ourselves. (The
                // one-account test plays both sides, so there the owner is always "us".)
                Leave();
                Fail(L.T("Your friend's game is not open to join right now. If they are hosting, try again in a moment."));
                return;
            }
            string address = TestJoinOverLocalIp ? "127.0.0.1" : CoopSteamTransportSwitch.SteamAddressPrefix + owner.m_SteamID;
            string host = SteamMatchmaking.GetLobbyData(lobby, NameKey);
            try
            {
                CoopMenu.CopyAndJoin(address);
                LastOutcome = "joining " + owner.m_SteamID;
                CoopStatus.Announce(string.IsNullOrEmpty(host) ? L.T("Joining your friend…") : L.F("Joining {0}…", host), false, 8f);
                log.LogInfo("Steam lobby: joining host " + owner.m_SteamID + " at " + address + ".");
            }
            catch (Exception ex)
            {
                Leave();
                Fail(ex.GetBaseException().Message);
            }
        }

        /// <summary>A joiner leaving the session: out of the host's lobby too, so joining again enters it afresh.</summary>
        internal static void LeaveJoinedLobby()
        {
            try
            {
                Leave();
            }
            catch (Exception ex)
            {
                log.LogWarning("Steam lobby: could not leave: " + ex.Message);
            }
        }

        private static void Leave()
        {
            if (joinedLobby.IsValid())
            {
                SteamMatchmaking.LeaveLobby(joinedLobby);
                joinedLobby = CSteamID.Nil;
            }
        }

        private static void Fail(string message)
        {
            LastOutcome = "failed: " + message;
            CoopStatus.Announce(message, true, 12f);
            log.LogWarning("Steam lobby: " + message);
        }

        /// <summary>"+connect_lobby 1234" anywhere in a string.</summary>
        internal static CSteamID ParseConnect(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return CSteamID.Nil;
            }
            string[] parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
            {
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out ulong id))
                {
                    return new CSteamID(id);
                }
            }
            return CSteamID.Nil;
        }

        /// <summary>
        /// Friends currently hosting a co-op game: their name and lobby, from Steam's friends list.
        /// </summary>
        internal static List<KeyValuePair<string, CSteamID>> FriendsHosting()
        {
            var hosting = new List<KeyValuePair<string, CSteamID>>();
            if (!Ready())
            {
                return hosting;
            }
            AppId_t ourApp = SteamUtils.GetAppID();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t game) &&
                    game.m_gameID.AppID() == ourApp && game.m_steamIDLobby.IsValid())
                {
                    hosting.Add(new KeyValuePair<string, CSteamID>(SteamFriends.GetFriendPersonaName(friend), game.m_steamIDLobby));
                }
            }
            return hosting;
        }

        private static bool OnMainMenu()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            return mainGame != null && Convert.ToString(CoopDiagnostics.GetMember(mainGame, "gameState")) == "MainMenu";
        }
    }
}
