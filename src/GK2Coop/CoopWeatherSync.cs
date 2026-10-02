using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// One sky for everyone.
    ///
    /// The weather is a state machine (<c>WeatherSystem</c>) that picks the next state at random when
    /// a phase ends (<c>RollNextWeatherState</c>), so each machine rolled its own: rain for one player,
    /// sunshine for the other. The shared clock kept the phases in step but not the outcome.
    ///
    /// Only the host rolls. It sends its weather state whenever it changes, and to anyone who joins;
    /// joiners enter that state through the game's own <c>WeatherSystem.SetWeatherState</c>, which
    /// also switches the state's rain, fog and wind effects.
    /// </summary>
    internal static class CoopWeatherSync
    {
        internal const string WeatherMessage = "GK2Coop.Weather.v1";

        private static ManualLogSource log;
        private static string lastSent;
        private static string lastApplied;
        private static int lastPeers = -1;
        private static int applied;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "weather sync: " + (CurrentState() ?? "?") + ", applied from host=" + applied;
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
                MethodInfo roll = AccessTools.Method(Plugin.FindGameType("WeatherSystem"), "RollNextWeatherState");
                if (roll == null)
                {
                    log.LogWarning("Weather sync: WeatherSystem.RollNextWeatherState not found; weather stays local.");
                    Enabled = false;
                    return;
                }
                harmony.Patch(roll, prefix: new HarmonyMethod(typeof(CoopWeatherSync).GetMethod(nameof(RollPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Weather sync: the host's weather is everyone's.");
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Weather sync disabled: " + ex.Message);
            }
        }

        /// <summary>Joiners do not roll weather of their own; the host's arrives instead.</summary>
        private static bool RollPrefix()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            return !Enabled || netcode == null || !netcode.IsListening || netcode.IsHost;
        }

        /// <summary>Driven from the plugin's one-second poll.</summary>
        internal static void Tick()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || !netcode.IsListening)
            {
                lastSent = null;
                lastApplied = null;
                lastPeers = -1;
                return;
            }
            if (!netcode.IsHost)
            {
                return;
            }
            try
            {
                string state = CurrentState();
                int peers = netcode.ConnectedClientsIds.Count;
                if (!string.IsNullOrEmpty(state) && (state != lastSent || peers != lastPeers))
                {
                    lastSent = state;
                    lastPeers = peers;
                    Send(netcode, state);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Weather sync: " + ex.Message);
            }
        }

        private static void Send(NetworkManager netcode, string state)
        {
            using (var writer = new FastBufferWriter(160, Allocator.Temp))
            {
                writer.WriteValueSafe(new FixedString128Bytes(state));
                foreach (ulong clientId in netcode.ConnectedClientsIds)
                {
                    if (clientId != netcode.LocalClientId)
                    {
                        netcode.CustomMessagingManager.SendNamedMessage(WeatherMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
                    }
                }
            }
        }

        internal static void Receive(ulong sender, FastBufferReader reader)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (!Enabled || netcode == null || netcode.IsHost || sender != NetworkManager.ServerClientId)
            {
                return;
            }
            try
            {
                reader.ReadValueSafe(out FixedString128Bytes stateText);
                string state = stateText.ToString();
                if (state == CurrentState())
                {
                    lastApplied = state;
                    return;
                }
                object weather = WeatherSystemInstance();
                if (weather == null)
                {
                    return;
                }
                weather.GetType().GetMethod("SetWeatherState", new[] { typeof(string), typeof(bool) }).Invoke(weather, new object[] { state, false });
                lastApplied = state;
                applied++;
                log.LogInfo("Weather sync: now " + state + ", as on the host.");
            }
            catch (Exception ex)
            {
                log.LogWarning("Weather sync: could not apply the host's weather: " + ex.GetBaseException().Message);
            }
        }

        internal static string CurrentState()
        {
            object mainGame = CoopDiagnostics.GetStatic(Plugin.FindGameType("MainGame"), "Instance");
            object save = mainGame == null ? null : CoopDiagnostics.GetMember(mainGame, "GameSave");
            object data = save == null ? null : CoopDiagnostics.GetMember(save, "weatherData");
            return data == null ? null : Convert.ToString(CoopDiagnostics.GetMember(data, "stateName"));
        }

        private static object WeatherSystemInstance()
        {
            Type type = Plugin.FindGameType("WeatherSystem");
            object instance = type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null, null);
            return instance ?? UnityEngine.Object.FindAnyObjectByType(type);
        }
    }
}
