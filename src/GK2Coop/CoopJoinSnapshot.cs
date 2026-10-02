using System;
using BepInEx.Logging;

namespace GK2Coop
{
    /// <summary>
    /// Phase 5: bringing a joining client level with a world that has already progressed.
    ///
    /// The documents long recorded rejoin as blocked by a failure inside the navigation graph
    /// rebuild. That failure is real but belongs to the legacy lobby-style startup. The shipped
    /// normal menu/intro flow never transfers a world at all: each peer starts its own new game,
    /// and the two agree only because both begin identical.
    ///
    /// So rather than revive a world transfer — which would fight the requirement to preserve the
    /// normal flow, and would need a second representation of world state — the client keeps
    /// starting its own world, and the host replays what has happened since this session began.
    ///
    /// Every message sent here is one the peer already handles for live play: a world-object
    /// death, a canonical container state, a drop spawn, a quest transition. Nothing new goes on
    /// the wire, so the receiving side runs the paths verified in 0.14.0 rather than a second,
    /// less-travelled one. That is the whole point of doing it this way.
    ///
    /// **What this does not cover.** Per-player inventory and energy, time of day beyond the
    /// existing clock sync, crafting in progress, and anything no sync system owns yet. A client
    /// joining a long-running world will match its structure, not every detail. Growing this is a
    /// matter of each system gaining a snapshot, not of changing the approach.
    /// </summary>
    internal static class CoopJoinSnapshot
    {
        private static ManualLogSource log;

        internal static bool Enabled { get; set; }

        internal static string LastSummary { get; private set; } = "not sent";

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static string Describe()
        {
            return "join snapshot: " + LastSummary;
        }

        /// <summary>
        /// Sends the accumulated world state to one accepted client. Called once per join, after
        /// the handshake is accepted, so a rejected peer never receives world data.
        /// </summary>
        internal static void SendTo(ulong clientId)
        {
            if (!Enabled)
            {
                LastSummary = "disabled";
                return;
            }
            int deaths = 0;
            int containers = 0;
            int drops = 0;
            int quests = 0;
            int builds = 0;
            int zombies = 0;
            try
            {
                // Deaths first: a container or drop belonging to an object that is already gone
                // should arrive after the removal, not before it.
                deaths = CoopWorldSync.SendSnapshotTo(clientId);
                // Buildings before contents: a chest built while they were joining has to exist
                // before its contents arrive.
                builds = CoopBuildSync.SendSnapshotTo(clientId);
                zombies = CoopZombieSync.SendSnapshotTo(clientId);
                containers = CoopContainerSync.SendSnapshotTo(clientId);
                drops = CoopDropSync.SendSnapshotTo(clientId);
                quests = CoopQuestSync.SendSnapshotTo(clientId);
                // A night already passing: the message goes out only when it starts.
                CoopSleepSync.SendNightTo(clientId);
            }
            catch (Exception ex)
            {
                LastSummary = "failed: " + ex.Message;
                log.LogError("Join snapshot for client " + clientId + " failed: " + ex.Message);
                return;
            }

            LastSummary = deaths + " deaths, " + containers + " containers, " +
                          drops + " drops, " + quests + " quest transitions, " + builds + " building changes, " + zombies + " zombie changes";
            log.LogInfo("Sent the join snapshot to client " + clientId + ": " + LastSummary + ".");
        }
    }
}
