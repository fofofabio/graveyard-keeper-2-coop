using System;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// The end of a session, for both sides.
    ///
    /// A joiner whose host is gone: the game's own code only switches to offline when the
    /// connection ends, so a joiner whose host quit (or whose connection dropped) went on playing
    /// alone in the copy of the host's world, where nothing is saved — everything done from then on
    /// was lost without a word. Now the status window says the session has ended and OK takes the
    /// joiner back to the main menu, where Continue opens their own save and they can join again.
    /// Not when the joiner leaves themselves (going to the main menu or quitting the game).
    ///
    /// Going back to the main menu leaves the session: the game's main menu kept the connection,
    /// so a joiner in the menu stayed a player in the host's world (and could not join again), and a
    /// host in the menu went on hosting a world that was no longer loaded. Now a joiner who goes to
    /// the menu disconnects (and leaves the host's Steam lobby), and a host who goes to the menu
    /// stops hosting, which tells the joiners at once.
    /// </summary>
    internal static class CoopHostLeft
    {
        // A player's own way out ends the connection within moments; a drop after that is theirs.
        private const float OwnLeaveSeconds = 15f;

        private static ManualLogSource log;
        private static bool inSession;
        private static bool hostingWorld;
        private static string hostName;
        private static float ownLeaveAt = -100f;
        private static int ended;
        private static int left;

        internal static bool Enabled { get; set; } = true;

        /// <summary>The status window shows the end of the session (its heading and its OK).</summary>
        internal static bool Pending { get; private set; }

        internal static string Describe()
        {
            return "session end: joined=" + inSession + " hosting=" + hostingWorld + " pending=" + Pending + " ended=" + ended + " left=" + left;
        }

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
                harmony.Patch(AccessTools.Method(typeof(MainGame), nameof(MainGame.GoToMenu)),
                    prefix: new HarmonyMethod(typeof(CoopHostLeft), nameof(GoToMenuPrefix)));
                Application.quitting += () => ownLeaveAt = Time.unscaledTime;
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Session end handling disabled: " + ex.Message);
            }
        }

        private static void GoToMenuPrefix()
        {
            ownLeaveAt = Time.unscaledTime;
        }

        private static bool InGame()
        {
            return MainGame.Instance != null && MainGame.Instance.gameState == MainGame.GameState.InGame;
        }

        /// <summary>From the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            if (!Enabled)
            {
                return;
            }
            if (Pending && CoopStatus.Phase != CoopPhase.Failed)
            {
                // Moved on some other way (a new join, the menu): the window no longer shows it.
                Pending = false;
            }
            NetworkManager netcode = NetworkManager.Singleton;
            bool inGame = InGame();

            // The host: from its world back to the main menu ends the hosting.
            bool hosting = netcode != null && netcode.IsListening && netcode.IsHost;
            if (hosting && inGame)
            {
                hostingWorld = true;
            }
            else if (hostingWorld)
            {
                hostingWorld = false;
                if (hosting)
                {
                    left++;
                    log.LogInfo("Session end: the host went back to the main menu; hosting ends.");
                    Shutdown(netcode);
                }
                return;
            }

            // A joiner.
            bool connected = netcode != null && netcode.IsConnectedClient && !netcode.IsHost;
            if (connected && CoopSession.Welcomed && inGame)
            {
                inSession = true;
                hostName = CoopSession.NameFor(NetworkManager.ServerClientId);
                return;
            }
            if (!inSession)
            {
                return;
            }
            inSession = false;
            if (connected)
            {
                // Still connected, but back in the main menu: leaving is theirs.
                left++;
                log.LogInfo("Session end: back in the main menu; leaving " + hostName + "'s game.");
                CoopSteamLobby.LeaveJoinedLobby();
                Shutdown(netcode);
                return;
            }
            if (!inGame || Time.unscaledTime - ownLeaveAt < OwnLeaveSeconds)
            {
                return;
            }
            ended++;
            Pending = true;
            // The status window says it all (the plain look's HUD shows the same status); a notice
            // as well would repeat it, and the notice box is too narrow for this much text.
            CoopStatus.Set(CoopPhase.Failed, L.F("The connection to {0}'s game has ended. Nothing you do from here on is saved. OK takes you back to the main menu, where you can join again.", hostName ?? "Host"));
            log.LogWarning("Session end: the connection to " + hostName + " ended while in their world; offering the main menu.");
        }

        private static void Shutdown(NetworkManager netcode)
        {
            try
            {
                netcode.Shutdown();
            }
            catch (Exception ex)
            {
                log.LogWarning("Session end: could not close the connection: " + ex.Message);
            }
        }

        /// <summary>The status window's OK: back to the main menu.</summary>
        internal static void Dismissed()
        {
            if (!Pending)
            {
                return;
            }
            Pending = false;
            try
            {
                if (InGame())
                {
                    log.LogInfo("Session end: going to the main menu.");
                    MainGame.Instance.GoToMenu();
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Session end: could not go to the main menu: " + ex.Message);
            }
        }
    }
}
