using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Chooses the wire under the whole session: the game's own UDP transport, or Steam's.
    ///
    /// The game starts every session through <c>UNetworkManager.StartHostGame(ip, port)</c> and
    /// <c>ConnectToHost(ip, port)</c>, which put the address on its <c>UnityTransport</c> and start
    /// Netcode. In Steam mode these are taken over: <see cref="SteamSocketsTransport"/> becomes
    /// Netcode's transport and Netcode is started directly. A joiner's address <c>steam:&lt;id&gt;</c>
    /// connects to that Steam user through Valve's relay; a plain address connects through Steam's
    /// sockets over IP (LAN, local tests). IP mode leaves the game's path untouched, and Steam mode
    /// falls back to it when Steam is not available.
    /// </summary>
    internal static class CoopSteamTransportSwitch
    {
        internal const string SteamAddressPrefix = "steam:";

        private static ManualLogSource log;
        private static SteamSocketsTransport steam;
        private static NetworkTransport original;

        /// <summary>From <c>[Network] Transport</c>: true for Steam.</summary>
        internal static bool UseSteam { get; set; }

        /// <summary>For the status panel and tests: which wire the running session uses.</summary>
        internal static string ActiveName
        {
            get
            {
                NetworkTransport current = NetworkManager.Singleton?.NetworkConfig?.NetworkTransport;
                return current is SteamSocketsTransport ? "Steam" : current == null ? "none" : "IP";
            }
        }

        internal static string Describe()
        {
            return "transport: " + ActiveName + (steam != null && ActiveName == "Steam" ? " — " + steam.Describe() : string.Empty);
        }

        internal static void Install(Harmony harmony, ManualLogSource source)
        {
            log = source;
            SteamSocketsTransport.Log = source;
            try
            {
                Type manager = Plugin.FindGameType("UNetworkManager");
                MethodInfo host = AccessTools.Method(manager, "StartHostGame", new[] { typeof(string), typeof(ushort) });
                MethodInfo connect = AccessTools.Method(manager, "ConnectToHost", new[] { typeof(string), typeof(ushort) });
                if (host == null || connect == null)
                {
                    log.LogWarning("Steam transport: the game's host/connect methods were not found; only IP play is available.");
                    return;
                }
                harmony.Patch(host, prefix: new HarmonyMethod(typeof(CoopSteamTransportSwitch).GetMethod(nameof(HostPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(connect, prefix: new HarmonyMethod(typeof(CoopSteamTransportSwitch).GetMethod(nameof(ConnectPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                log.LogInfo("Steam transport: ready (" + (UseSteam ? "Steam" : "IP") + " mode).");
            }
            catch (Exception ex)
            {
                log.LogWarning("Steam transport not installed: " + ex.Message);
            }
        }

        /// <summary>Whether Steam is running and initialised by the game.</summary>
        internal static bool SteamAvailable()
        {
            try
            {
                Type manager = Plugin.FindGameType("SteamManager");
                PropertyInfo initialized = manager?.GetProperty("Initialized", BindingFlags.Static | BindingFlags.Public);
                return initialized != null && (bool)initialized.GetValue(null, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool WantsSteam(string address)
        {
            bool steamAddress = address != null && address.StartsWith(SteamAddressPrefix, StringComparison.OrdinalIgnoreCase);
            if (!UseSteam && !steamAddress)
            {
                return false;
            }
            if (!SteamAvailable())
            {
                log.LogWarning("Steam transport: Steam is not running; using the IP connection instead.");
                return false;
            }
            return true;
        }

        private static SteamSocketsTransport Select(ushort port)
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (steam == null)
            {
                steam = netcode.gameObject.GetComponent<SteamSocketsTransport>() ?? netcode.gameObject.AddComponent<SteamSocketsTransport>();
            }
            if (!(netcode.NetworkConfig.NetworkTransport is SteamSocketsTransport))
            {
                original = netcode.NetworkConfig.NetworkTransport;
            }
            steam.Port = port;
            netcode.NetworkConfig.NetworkTransport = steam;
            return steam;
        }

        /// <summary>Back to the game's own transport for an IP session.</summary>
        private static void Restore()
        {
            NetworkManager netcode = NetworkManager.Singleton;
            if (netcode != null && original != null && netcode.NetworkConfig.NetworkTransport is SteamSocketsTransport)
            {
                netcode.NetworkConfig.NetworkTransport = original;
            }
        }

        private static bool HostPrefix(object __instance, string ip, ushort port, ref bool __result)
        {
            if (!WantsSteam(null))
            {
                Restore();
                return true;
            }
            if (!(bool)(AccessTools.Field(__instance.GetType(), "isManagerInitedCorrectly")?.GetValue(__instance) ?? true))
            {
                __result = false;
                return false;
            }
            Select(port);
            __result = NetworkManager.Singleton.StartHost();
            AccessTools.Field(__instance.GetType(), "isCoopGame")?.SetValue(__instance, __result);
            log.LogInfo("Steam transport: host started=" + __result + ".");
            return false;
        }

        private static bool ConnectPrefix(object __instance, string ip, ushort port, ref bool __result)
        {
            if (!WantsSteam(ip))
            {
                Restore();
                return true;
            }
            SteamSocketsTransport transport = Select(port);
            if (ip != null && ip.StartsWith(SteamAddressPrefix, StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(ip.Substring(SteamAddressPrefix.Length), out ulong steamId))
            {
                transport.TargetSteamId = steamId;
            }
            else
            {
                transport.TargetSteamId = 0;
                transport.TargetAddress = string.IsNullOrEmpty(ip) ? "127.0.0.1" : ip;
            }
            __result = NetworkManager.Singleton.StartClient();
            // As the game's own ConnectToHost does: the rest of the game reads this flag.
            AccessTools.Field(__instance.GetType(), "isCoopGame")?.SetValue(__instance, __result);
            if (!__result)
            {
                Debug.LogError("NetworkManager StartClient failed");
            }
            return false;
        }
    }
}
