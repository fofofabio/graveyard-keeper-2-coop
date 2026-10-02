using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Makes the game's two "the player" statics point at a chosen player for the duration of one
    /// action, so a remote player's action can be executed by the game's own code.
    ///
    /// The game reads <c>MainGame.PlayerData</c> at 422 sites and
    /// <c>MainGame.PlayerController</c> at 334, every one of them meaning "the local player".
    /// Refactoring them is not an option, so the values are swapped instead.
    ///
    /// This swaps the backing fields rather than patching the getters. <c>MainGame.PlayerData</c>
    /// is a static auto-property and <c>MainGame.PlayerController</c> returns
    /// <c>Instance.playerController</c> — both are exactly the kind of trivial getter a JIT
    /// inlines, and Harmony cannot reach a call site that has already been inlined. Writing the
    /// field is immune to that: every reader sees it, inlined or not.
    ///
    /// The scope is strictly synchronous. Anything that captures a player reference inside it and
    /// uses it later — a cached field, a coroutine, an async continuation, an event subscription —
    /// escapes and will operate on the wrong player. That is the known risk of this whole approach
    /// and the reason for the leak check.
    /// </summary>
    internal static class CoopPlayerContext
    {
        private static ManualLogSource log;
        private static FieldInfo playerDataField;
        private static FieldInfo playerControllerField;
        private static Type mainGameType;
        private static bool resolved;

        private static int depth;
        private static object savedPlayerData;
        private static object savedPlayerController;
        private static string activeLabel;
        private static int enterCount;
        private static int failureCount;

        internal static bool IsActive
        {
            get { return depth > 0; }
        }

        internal static string ActiveLabel
        {
            get { return depth > 0 ? activeLabel : "none"; }
        }

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        private static bool Resolve()
        {
            if (resolved)
            {
                return playerDataField != null && playerControllerField != null;
            }
            resolved = true;
            mainGameType = Plugin.FindGameType("MainGame");
            if (mainGameType == null)
            {
                return false;
            }
            // Static auto-property: the compiler-generated backing field is the real storage.
            playerDataField = mainGameType.GetField("<PlayerData>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            // Instance field behind the static PlayerController => Instance.playerController.
            playerControllerField = mainGameType.GetField("playerController",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (playerDataField == null || playerControllerField == null)
            {
                log.LogError("Player context unavailable: " +
                             (playerDataField == null ? "MainGame.<PlayerData>k__BackingField" : "MainGame.playerController") +
                             " not found. Remote actions cannot be attributed.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Runs <paramref name="action"/> with the statics pointing at the given player. Returns
        /// false when the context could not be established, in which case the action is not run —
        /// executing it against the local player would corrupt the wrong save.
        /// </summary>
        internal static bool Run(object playerData, object playerController, string label, Action action)
        {
            if (action == null)
            {
                return false;
            }
            if (!Resolve() || playerData == null)
            {
                failureCount++;
                return false;
            }
            if (depth > 0)
            {
                // Nesting would mean a remote action triggering another remote action; the restore
                // bookkeeping here only handles one level, so refuse rather than corrupt it.
                log.LogWarning("Refusing to nest player context (" + activeLabel + " -> " + label + ").");
                failureCount++;
                return false;
            }

            object mainGame = CoopDiagnostics.GetStatic(mainGameType, "Instance");
            if (mainGame == null)
            {
                failureCount++;
                return false;
            }

            savedPlayerData = playerDataField.GetValue(null);
            savedPlayerController = playerControllerField.GetValue(mainGame);
            activeLabel = label;
            depth = 1;
            enterCount++;

            try
            {
                playerDataField.SetValue(null, playerData);
                if (playerController != null)
                {
                    playerControllerField.SetValue(mainGame, playerController);
                }
                action();
                return true;
            }
            catch (Exception ex)
            {
                failureCount++;
                log.LogError("Action inside player context '" + label + "' failed: " + Unwrap(ex));
                return false;
            }
            finally
            {
                playerDataField.SetValue(null, savedPlayerData);
                playerControllerField.SetValue(mainGame, savedPlayerController);
                savedPlayerData = null;
                savedPlayerController = null;
                depth = 0;
                activeLabel = null;
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
            {
                exception = exception.InnerException;
            }
            return exception;
        }

        /// <summary>
        /// Called once per frame. The scope is synchronous, so finding it still open here means
        /// something escaped and every subsequent read of "the player" is wrong — worth shouting
        /// about rather than letting it corrupt state quietly.
        /// </summary>
        internal static void VerifyNotLeaked()
        {
            if (depth <= 0)
            {
                return;
            }
            log.LogError("Player context '" + activeLabel + "' was still open at end of frame; restoring the local player.");
            try
            {
                object mainGame = CoopDiagnostics.GetStatic(mainGameType, "Instance");
                playerDataField.SetValue(null, savedPlayerData);
                if (mainGame != null)
                {
                    playerControllerField.SetValue(mainGame, savedPlayerController);
                }
            }
            catch (Exception ex)
            {
                log.LogError("Could not restore the local player context: " + ex.Message);
            }
            depth = 0;
            activeLabel = null;
            failureCount++;
        }

        internal static string Describe()
        {
            return "player context: entered=" + enterCount + ", failures=" + failureCount +
                   ", active=" + ActiveLabel +
                   ", resolved=" + (Resolve() ? "yes" : "no");
        }

        /// <summary>
        /// Proves the mechanism on real data before anything depends on it: enter the context with
        /// a remote player and confirm the statics actually report that player, then confirm they
        /// are restored. Runs once per session.
        /// </summary>
        internal static void RunSelfTest(object remotePlayer)
        {
            if (selfTested || remotePlayer == null || !Resolve())
            {
                return;
            }
            selfTested = true;

            object remoteData = CoopDiagnostics.GetMember(remotePlayer, "playerData");
            object beforeData = CoopDiagnostics.GetStatic(mainGameType, "PlayerData");
            object insideData = null;
            object insideController = null;

            bool ran = Run(remoteData, null, "self-test", delegate
            {
                insideData = CoopDiagnostics.GetStatic(mainGameType, "PlayerData");
                insideController = CoopDiagnostics.GetStatic(mainGameType, "PlayerController");
            });

            object afterData = CoopDiagnostics.GetStatic(mainGameType, "PlayerData");
            bool swapped = ran && ReferenceEquals(insideData, remoteData) && !ReferenceEquals(insideData, beforeData);
            bool restored = ReferenceEquals(afterData, beforeData);

            log.LogInfo("Player context self-test: ran=" + ran + "; swapped=" + swapped + "; restored=" + restored +
                        "; localHash=" + Hash(beforeData) + "; remoteHash=" + Hash(remoteData) +
                        "; insideHash=" + Hash(insideData) + "; controllerInside=" + (insideController == null ? "null" : "present"));
            if (!swapped || !restored)
            {
                log.LogError("Player context self-test FAILED. Remote actions must not be attributed until this passes.");
            }
        }

        private static bool selfTested;

        private static int Hash(object value)
        {
            return value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
