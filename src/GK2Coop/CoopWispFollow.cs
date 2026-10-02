using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Another player's body carried a wisp. The wisp is the story's companion and belongs to the
    /// local player's controller, where the story switches it on and off and points it. The game's own
    /// (unfinished) multiplayer code gives every other player's body a player controller of its own
    /// and, in <c>PlayerPhysicalBody.InitNetworkPlayer</c>, switches that controller's wisp on
    /// unconditionally — so each other player came with a fairy of their own, whatever the story
    /// said. Only the local player's wisp is the story's: another player's wisp is switched off when
    /// their body is set up, and kept off. (0.62.1 pointed the wisps at the host instead, which gave
    /// the host a second one.)
    /// </summary>
    internal static class CoopWispFollow
    {
        private static ManualLogSource log;
        private static PropertyInfo wispOf;
        private static float nextTick;
        private static int switchedOff;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "remote wisps switched off=" + switchedOff;
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
                wispOf = AccessTools.Property(typeof(PlayerController), "WispController");
                harmony.Patch(AccessTools.Method(typeof(PlayerPhysicalBody), "InitNetworkPlayer"),
                    postfix: new HarmonyMethod(typeof(CoopWispFollow), nameof(InitNetworkPlayerPostfix)));
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Remote wisps: " + ex.Message);
            }
        }

        private static void InitNetworkPlayerPostfix(PlayerPhysicalBody __instance)
        {
            SwitchOff(__instance.GetComponent<PlayerController>());
        }

        /// <summary>From the plugin's Update: another player's wisp stays off.</summary>
        internal static void Tick()
        {
            // Before the game has loaded there is no MainGame (and no wisp): reading the local player
            // then threw at every game start.
            if (!Enabled || Time.unscaledTime < nextTick || MainGame.Instance == null)
            {
                return;
            }
            nextTick = Time.unscaledTime + 1f;
            try
            {
                foreach (PlayerController controller in OtherControllers())
                {
                    SwitchOff(controller);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Remote wisps: " + ex.Message);
                nextTick = Time.unscaledTime + 10f;
            }
        }

        private static IEnumerable<PlayerController> OtherControllers()
        {
            PlayerController local = MainGame.PlayerController;
            foreach (PlayerController controller in Resources.FindObjectsOfTypeAll<PlayerController>())
            {
                if (controller != null && controller != local && controller.gameObject.scene.IsValid())
                {
                    yield return controller;
                }
            }
        }

        private static void SwitchOff(PlayerController controller)
        {
            if (controller == null || controller == MainGame.PlayerController || wispOf == null)
            {
                return;
            }
            Component wisp = wispOf.GetValue(controller, null) as Component;
            if (wisp != null && wisp.gameObject.activeSelf)
            {
                wisp.gameObject.SetActive(false);
                switchedOff++;
                if (switchedOff == 1)
                {
                    log.LogInfo("Remote wisps: another player's body came with a wisp of its own; switched off (the story's wisp is the local player's).");
                }
            }
        }

        /// <summary>Tests: the local player's wisp (the story decides) and whether any other player's is on.</summary>
        internal static string DescribeForTest()
        {
            PlayerController local = MainGame.PlayerController;
            Component own = local == null || wispOf == null ? null : wispOf.GetValue(local, null) as Component;
            int othersOn = 0, others = 0;
            foreach (PlayerController controller in OtherControllers())
            {
                others++;
                Component wisp = wispOf == null ? null : wispOf.GetValue(controller, null) as Component;
                if (wisp != null && wisp.gameObject.activeInHierarchy) othersOn++;
            }
            return "own wisp active=" + (own != null && own.gameObject.activeInHierarchy) + "; other players=" + others + ", their wisps on=" + othersOn + "; " + Describe();
        }
    }
}
