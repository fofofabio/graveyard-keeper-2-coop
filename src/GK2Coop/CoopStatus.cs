using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// What the player is told about the session, as opposed to what the log records.
    ///
    /// The failure this exists to fix: a connection that never succeeds used to leave the player
    /// in their own world with no indication at all. The game loaded, everything looked normal,
    /// and they were alone. Silence that looks like success is the worst failure mode a co-op
    /// mod can have, so every outcome now says something, and every failure says what to do.
    /// </summary>
    internal enum CoopPhase
    {
        Idle,
        Hosting,
        Connecting,
        Connected,
        Failed
    }

    /// <summary>The steps of joining that the status window names.</summary>
    internal enum JoinStep
    {
        None,
        Connecting,
        Copying,
        Loading
    }

    internal static class CoopStatus
    {
        private sealed class Toast
        {
            public string Text;
            public float Until;
            public bool IsProblem;
        }

        private const int MaxToasts = 4;
        private static readonly List<Toast> toasts = new List<Toast>();
        private static ManualLogSource log;

        internal static CoopPhase Phase { get; private set; } = CoopPhase.Idle;

        /// <summary>One line naming what went wrong and what to do about it. Never a stack trace.</summary>
        internal static string Detail { get; private set; } = string.Empty;

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        /// <summary>While joining: which step is under way, for the status window.</summary>
        internal static JoinStep Step { get; private set; } = JoinStep.None;

        /// <summary>How far the current step is, 0 to 1, or below 0 when unknown.</summary>
        internal static float Progress { get; private set; } = -1f;

        internal static void SetStep(JoinStep step, float progress = -1f)
        {
            Step = step;
            Progress = progress;
        }

        internal static void Set(CoopPhase phase, string detail)
        {
            bool changed = Phase != phase || !string.Equals(Detail, detail, StringComparison.Ordinal);
            if (phase != CoopPhase.Connecting)
            {
                Step = JoinStep.None;
                Progress = -1f;
            }
            Phase = phase;
            Detail = detail ?? string.Empty;
            if (changed && log != null && !string.IsNullOrEmpty(Detail))
            {
                log.LogInfo("Session status: " + phase + " — " + Detail);
            }
        }

        /// <summary>A short message shown for a few seconds: someone joined, left, or a retry.</summary>
        internal static void Announce(string text, bool isProblem = false, float seconds = 6f)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            toasts.Add(new Toast { Text = text, Until = Time.unscaledTime + seconds, IsProblem = isProblem });
            while (toasts.Count > MaxToasts)
            {
                toasts.RemoveAt(0);
            }
            if (log != null)
            {
                log.LogInfo("Told the player: " + text);
            }
        }

        internal static IEnumerable<KeyValuePair<string, bool>> ActiveToasts()
        {
            float now = Time.unscaledTime;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                if (toasts[i].Until <= now)
                {
                    toasts.RemoveAt(i);
                }
            }
            var active = new List<KeyValuePair<string, bool>>(toasts.Count);
            foreach (Toast toast in toasts)
            {
                active.Add(new KeyValuePair<string, bool>(toast.Text, toast.IsProblem));
            }
            return active;
        }

        /// <summary>
        /// Turns a connection failure into something the player can act on. The address is
        /// included because "check the host is running" is useless without knowing which host was
        /// tried, and forwarding is named because it is the usual cause over the internet.
        /// </summary>
        internal static string DescribeUnreachable(string address, int port, int attempts)
        {
            return SteamFriendName(address, out string name)
                ? L.F("Could not reach {0} over Steam. Make sure they are hosting and already in their world, then try again.", name)
                : L.F("Could not reach {0}:{1}. Make sure the host is hosting. Over the internet, port {1} has to be forwarded to their PC.", address, port);
        }

        /// <summary>While copying the host's world: the same, with "already in their world".</summary>
        internal static string DescribeUnreachableHost(string address, int port)
        {
            return SteamFriendName(address, out string name)
                ? L.F("Could not reach {0} over Steam. Make sure they are hosting and already in their world, then try again.", name)
                : L.F("Could not reach the host at {0}. Make sure they are hosting and already in their world. Over the internet, their router has to forward port {1}.", address + ":" + port, port);
        }

        /// <summary>
        /// A "steam:&lt;id&gt;" address names a friend: their Steam name (the address and a port mean
        /// nothing to a player, and Steam needs no port forwarding).
        /// </summary>
        private static bool SteamFriendName(string address, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(address) || !address.StartsWith(CoopSteamTransportSwitch.SteamAddressPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string id = address.Substring(CoopSteamTransportSwitch.SteamAddressPrefix.Length);
            int colon = id.IndexOf(':');
            if (colon >= 0) id = id.Substring(0, colon);
            name = L.T("your friend");
            try
            {
                if (ulong.TryParse(id, out ulong steamId) && CoopSteamTransportSwitch.SteamAvailable())
                {
                    string persona = Steamworks.SteamFriends.GetFriendPersonaName(new Steamworks.CSteamID(steamId));
                    if (!string.IsNullOrEmpty(persona) && persona != "[unknown]") name = persona;
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        internal static string DescribeVersionMismatch(string theirVersion, string ourVersion)
        {
            return "Version mismatch: the host is running " + theirVersion + ", you have " + ourVersion +
                   ". Both players need the same build.";
        }
    }
}
