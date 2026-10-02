using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;

namespace GK2Coop
{
    /// <summary>
    /// Sleeping in co-op: the night passes only when everyone sleeps.
    ///
    /// The game's sleep sets the whole simulation to fifty times speed
    /// (<c>UpdateManager.SetTimeSpeedMultiplier(50)</c>). On a joiner that raced its own world
    /// ahead — crops, crafts, schedules — while the host's clock sync kept pulling its time back.
    /// A host sleeping alone skipped the night under an awake joiner's feet.
    ///
    /// Rule: nobody's sleep speeds up time unless every player is asleep. A lone sleeper still
    /// sleeps — energy refills at normal speed (about a minute and a quarter for a full bar at 400
    /// energy per in-game day) and the sleep ends as usual. Joiners tell the host whether they are
    /// asleep; when the host and all joiners are, the host runs the night at the game's own speed
    /// and the joiners' clocks follow it.
    ///
    /// The shared night is the same for everyone: the host tells the joiners when it starts and
    /// ends. While it runs, a sleeping joiner sleeps at the game's speed too (every world runs at
    /// the same rate then, so none races ahead). When it ends, a joiner still asleep wakes with a
    /// full bar, as the night would have given it (found in play: a joiner who went to bed first
    /// lay there at normal speed, energy ticking up one by one, while the host was long up).
    /// </summary>
    internal static class CoopSleepSync
    {
        internal const string SleepMessage = "GK2Coop.Sleep.v1";
        internal const string NightMessage = "GK2Coop.Night.v1";

        // A joiner: the host's shared night runs, and this machine sleeps at its speed.
        private static bool hostNight;
        private static bool sleepingWithNight;
        private static bool wakeWithFullBar;

        private const float SleepSpeed = 50f;

        private static ManualLogSource log;
        private static readonly Dictionary<ulong, bool> clientsAsleep = new Dictionary<ulong, bool>();
        private static bool reportedAsleep;
        private static bool fastNight;
        private static bool settingSpeed;
        private static MethodInfo setSpeed;

        internal static bool Enabled { get; set; } = true;

        /// <summary>
        /// The host's setting (Network.NightPasses): "Everyone" (the default) or "Host" — the host's
        /// sleep passes the night for everyone. Awake joiners stay at normal speed (the game's night
        /// speed also runs walking, so an awake player cannot run along) and their clock jumps ahead
        /// with the host's; sleeping joiners sleep at the night's speed as always. Only the host can
        /// be the one: the host's world is the one that runs the night.
        /// </summary>
        internal static ConfigEntry<string> HostNightRule { get; set; }

        private static bool HostSleepPassesNight =>
            HostNightRule != null && string.Equals(HostNightRule.Value, "Host", StringComparison.OrdinalIgnoreCase);

        internal static string Describe()
        {
            return "sleep sync: " + (fastNight ? "night passing" : "normal speed") + ", joiners asleep=" + CountAsleep() + "/" + clientsAsleep.Count +
                   ", night passes when " + (HostSleepPassesNight ? "the host sleeps" : "everyone sleeps");
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
                setSpeed = AccessTools.Method(Plugin.FindGameType("UpdateManager"), "SetTimeSpeedMultiplier", new[] { typeof(float) });
                if (setSpeed == null)
                {
                    log.LogWarning("Sleep sync: UpdateManager.SetTimeSpeedMultiplier not found; sleeping speeds up time per machine.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(setSpeed, prefix: new HarmonyMethod(typeof(CoopSleepSync).GetMethod(nameof(SetSpeedPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Sleep sync: the night passes only when every player sleeps.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Sleep sync disabled: " + ex.Message);
            }
        }

        /// <summary>Holds sleep at normal speed unless this is the host's all-asleep night.</summary>
        private static void SetSpeedPrefix(ref float value)
        {
            if (!Enabled || settingSpeed || value <= 1f || !InSession())
            {
                return;
            }
            value = 1f;
        }

        private static bool InSession()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            return netcode != null && netcode.IsListening && (netcode.IsHost ? netcode.ConnectedClientsIds.Count > 1 : netcode.IsConnectedClient);
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                clientsAsleep.Clear();
                reportedAsleep = false;
                fastNight = false;
                hostNight = false;
                wakeWithFullBar = false;
                if (sleepingWithNight)
                {
                    sleepingWithNight = false;
                    SetSpeed(1f);
                }
                return;
            }
            bool asleep = LocalAsleep();
            if (!netcode.IsHost)
            {
                if (netcode.IsConnectedClient && asleep != reportedAsleep)
                {
                    reportedAsleep = asleep;
                    using (var writer = new FastBufferWriter(8, Allocator.Temp))
                    {
                        writer.WriteValueSafe(asleep);
                        netcode.CustomMessagingManager.SendNamedMessage(SleepMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
                // The shared night: sleep at its speed while it runs.
                bool withNight = hostNight && asleep;
                if (withNight != sleepingWithNight)
                {
                    sleepingWithNight = withNight;
                    SetSpeed(withNight ? SleepSpeed : 1f);
                    log.LogInfo(withNight ? "Sleep sync: the host's night runs; sleeping at its speed." : "Sleep sync: back to normal speed.");
                }
                // The night is over: still in bed, wake with the bar the night would have filled.
                if (wakeWithFullBar)
                {
                    wakeWithFullBar = false;
                    if (asleep)
                    {
                        try
                        {
                            PlayerEnergyGameResSystem energy = PlayerEnergyGameResSystem.GetSystem();
                            energy.Set(energy.Max);
                            log.LogInfo("Sleep sync: the night is over; waking with a full bar.");
                        }
                        catch (Exception ex)
                        {
                            log.LogWarning("Sleep sync: could not fill the bar: " + ex.Message);
                        }
                    }
                }
                return;
            }

            foreach (ulong id in new List<ulong>(clientsAsleep.Keys))
            {
                if (!System.Linq.Enumerable.Contains(netcode.ConnectedClientsIds, id))
                {
                    clientsAsleep.Remove(id);
                }
            }
            int joiners = netcode.ConnectedClientsIds.Count - 1;
            bool everyone = asleep && joiners > 0 && CountAsleep() >= joiners;
            bool night = everyone || (asleep && joiners > 0 && HostSleepPassesNight);
            if (night != fastNight)
            {
                fastNight = night;
                SetSpeed(night ? SleepSpeed : 1f);
                using (var writer = new FastBufferWriter(8, Allocator.Temp))
                {
                    writer.WriteValueSafe(night);
                    netcode.CustomMessagingManager.SendNamedMessageToAll(NightMessage, writer, NetworkDelivery.ReliableSequenced);
                }
                if (night)
                {
                    CoopStatus.Announce(everyone ? L.T("Everyone is asleep. The night passes.") : L.T("You sleep. The night passes for everyone."), false, 6f);
                }
                else if (asleep)
                {
                    // Someone got up while the host still sleeps.
                    CoopStatus.Announce(L.T("Waiting for everyone to sleep."), false, 6f);
                }
                log.LogInfo(night ? (everyone ? "Sleep sync: everyone asleep; night passes at the game's speed." : "Sleep sync: the host sleeps; the night passes for everyone (NightPasses=Host).") : "Sleep sync: back to normal speed.");
            }
            else if (asleep && !night && joiners > 0 && !announcedWaiting)
            {
                announcedWaiting = true;
                CoopStatus.Announce(L.T("You sleep and recover. The night only passes when everyone sleeps."), false, 8f);
            }
            if (!asleep)
            {
                announcedWaiting = false;
            }
        }

        private static bool announcedWaiting;

        /// <summary>
        /// The host, to a player who has just joined: a night passing now (it is announced only when it
        /// starts, so someone joining during it slept at normal speed and woke without the full bar).
        /// </summary>
        internal static void SendNightTo(ulong clientId)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || !fastNight || netcode == null || !netcode.IsHost)
            {
                return;
            }
            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(true);
                netcode.CustomMessagingManager.SendNamedMessage(NightMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsHost)
            {
                return;
            }
            reader.ReadValueSafe(out bool asleep);
            clientsAsleep[sender] = asleep;
            log.LogInfo($"Sleep sync: {CoopSession.NameFor(sender)} is {(asleep ? "asleep" : "awake")}.");
        }

        /// <summary>A joiner: the host's shared night starts or ends.</summary>
        internal static void ReceiveNight(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || netcode.IsHost || sender != NetworkManager.ServerClientId)
            {
                return;
            }
            reader.ReadValueSafe(out bool night);
            if (hostNight && !night)
            {
                wakeWithFullBar = true;
            }
            if (night && !hostNight && !LocalAsleep())
            {
                // Awake while the host's sleep passes the night (the host's NightPasses=Host).
                CoopStatus.Announce(L.F("{0} is asleep. The night passes.", CoopSession.NameFor(NetworkManager.ServerClientId)), false, 6f);
            }
            hostNight = night;
            log.LogInfo(night ? "Sleep sync: the host's night begins." : "Sleep sync: the host's night is over.");
        }

        private static int CountAsleep()
        {
            int count = 0;
            foreach (bool value in clientsAsleep.Values)
            {
                if (value) count++;
            }
            return count;
        }

        private static bool LocalAsleep()
        {
            object player = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "PlayerData");
            object energy = player == null ? null : CoopDiagnostics.GetMember(player, "energySystem");
            return energy != null && Convert.ToBoolean(CoopDiagnostics.GetMember(energy, "IsSleeping"));
        }

        private static void SetSpeed(float value)
        {
            object manager = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "UpdateManager");
            if (manager == null)
            {
                return;
            }
            settingSpeed = true;
            try
            {
                setSpeed.Invoke(manager, new object[] { value });
            }
            finally
            {
                settingSpeed = false;
            }
        }
    }
}
