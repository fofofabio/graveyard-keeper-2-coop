using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GK2Coop
{
    /// <summary>
    /// The player bodies in the loaded scenes, kept as the game makes them. Looking them up with
    /// <c>Resources.FindObjectsOfTypeAll</c> goes through every object in memory: 8 to 11 ms each
    /// time in a day-18 world, and five parts of the mod did it every second (a hitch every
    /// second). The game prepares every body it uses through <c>PlayerPhysicalBody.Init</c> /
    /// <c>PrepareForGame</c> / <c>InitNetworkPlayer</c>; a body is noted there. The full search
    /// is still made once after each scene load, in case a body came another way, and every
    /// two seconds if the game no longer has those methods.
    /// </summary>
    internal static class CoopBodies
    {
        private static ManualLogSource log;
        private static readonly List<PlayerPhysicalBody> known = new List<PlayerPhysicalBody>();
        private static bool hooked;
        private static bool searchDue = true;
        private static float nextSearch;
        private static int searches;

        internal static void Install(Harmony harmony, ManualLogSource source)
        {
            log = source;
            SceneManager.sceneLoaded += (scene, mode) => searchDue = true;
            try
            {
                var postfix = new HarmonyMethod(typeof(CoopBodies), nameof(Seen));
                harmony.Patch(AccessTools.Method(typeof(PlayerPhysicalBody), nameof(PlayerPhysicalBody.Init)), postfix: postfix);
                harmony.Patch(AccessTools.Method(typeof(PlayerPhysicalBody), nameof(PlayerPhysicalBody.PrepareForGame)), postfix: postfix);
                harmony.Patch(AccessTools.Method(typeof(PlayerPhysicalBody), nameof(PlayerPhysicalBody.InitNetworkPlayer)), postfix: postfix);
                hooked = true;
                log.LogInfo("Player bodies: followed as the game prepares them.");
            }
            catch (Exception ex)
            {
                hooked = false;
                log.LogWarning("Player bodies: could not follow the game's bodies (" + ex.Message + "); searching for them every 2 s instead.");
            }
        }

        private static void Seen(PlayerPhysicalBody __instance)
        {
            if ((object)__instance != null && !known.Contains(__instance))
            {
                known.Add(__instance);
            }
        }

        /// <summary>
        /// The live player bodies in the loaded scenes, active or not (a new array: callers may
        /// destroy bodies while they go through it).
        /// </summary>
        internal static Component[] All()
        {
            float now = Time.unscaledTime;
            if (searchDue || (!hooked && now >= nextSearch))
            {
                searchDue = false;
                nextSearch = now + 2f;
                Search();
            }
            known.RemoveAll(body => body == null || !body.gameObject.scene.IsValid());
            var bodies = new Component[known.Count];
            for (int i = 0; i < known.Count; i++)
            {
                bodies[i] = known[i];
            }
            return bodies;
        }

        private static void Search()
        {
            searches++;
            foreach (PlayerPhysicalBody body in Resources.FindObjectsOfTypeAll<PlayerPhysicalBody>())
            {
                if (body != null && body.gameObject.scene.IsValid() && !known.Contains(body))
                {
                    known.Add(body);
                }
            }
        }

        internal static string Describe()
        {
            return "bodies: known=" + known.Count + ", searches=" + searches + ", followed=" + hooked;
        }
    }
}
