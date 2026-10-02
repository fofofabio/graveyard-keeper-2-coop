using System;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Coop
{
    /// <summary>
    /// The game's player code knows one player. When a keeper body finishes its death animation
    /// (<c>PlayerAnimation.OnDeathAnimationFinished</c>), the game opens "you're dead" and, on OK,
    /// takes control from the local player — whichever body it was. So when another player died
    /// (in a fight the others were watching), their body's death animation told the watcher they
    /// were dead and froze the watcher's controls. Only the local player's own body does that now;
    /// another player's body just plays the animation, and their own game handles their death.
    /// </summary>
    internal static class CoopRemoteBodyGuard
    {
        private static ManualLogSource log;
        private static int skipped;

        internal static bool Enabled { get; set; } = true;

        internal static string Describe()
        {
            return "remote body guard: death windows skipped=" + skipped;
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
                harmony.Patch(AccessTools.Method(typeof(PlayerAnimation), nameof(PlayerAnimation.OnDeathAnimationFinished)),
                    prefix: new HarmonyMethod(typeof(CoopRemoteBodyGuard), nameof(DeathFinishedPrefix)));
            }
            catch (Exception ex)
            {
                Enabled = false;
                log.LogWarning("Remote body guard disabled: " + ex.Message);
            }
        }

        private static bool DeathFinishedPrefix(PlayerAnimation __instance)
        {
            try
            {
                PlayerAnimation local = MainGame.PlayerController == null || MainGame.PlayerController.View == null ? null : MainGame.PlayerController.View.PlayerAnimation;
                if (local == null || ReferenceEquals(__instance, local))
                {
                    return true;
                }
                skipped++;
                log.LogInfo("Remote body guard: another player's body finished dying; no death window here.");
                return false;
            }
            catch (Exception ex)
            {
                log.LogWarning("Remote body guard: " + ex.Message);
                return true;
            }
        }
    }
}
