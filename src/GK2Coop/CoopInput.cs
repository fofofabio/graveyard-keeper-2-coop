using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Coop
{
    /// <summary>
    /// Controllers for every part of the mod. The game reads input as
    /// its own game keys (LazyInput, over Rewired); a controller button stands for several game
    /// keys depending on where the player is (Y: inventory, fold, plant…).
    ///
    /// Here: whether a controller is in use; a controller button pressed or held, read through
    /// every game key it is bound to; hiding buttons from the game while a part of the mod uses
    /// them (parts register a rule); the game's own icon for a button; and, for tests, a press
    /// fed into the game's input the way a real controller's would be.
    /// </summary>
    internal static class CoopInput
    {
        private static ManualLogSource log;
        private static bool bypass;
        private static readonly List<Func<GameKey, bool>> hiders = new List<Func<GameKey, bool>>();
        private static readonly Dictionary<int, HashSet<int>> keysOfButton = new Dictionary<int, HashSet<int>>();
        private static readonly List<GameKey> injected = new List<GameKey>();
        private static MethodInfo addPressed;
        private static MethodInfo addHolded;
        private static readonly List<GameKey> held = new List<GameKey>();
        private static float heldUntil;
        private static readonly List<Func<bool>> directionHiders = new List<Func<bool>>();

        internal static void Init(ManualLogSource source)
        {
            log = source;
        }

        internal static void Install(Harmony harmony)
        {
            try
            {
                var prefix = new HarmonyMethod(typeof(CoopInput), nameof(GameKeyPrefix));
                harmony.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.GetKeyDown), new[] { typeof(GameKey) }), prefix: prefix);
                harmony.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.GetKey), new[] { typeof(GameKey) }), prefix: prefix);
                harmony.Patch(AccessTools.Method(typeof(LazyInput), "Update"), postfix: new HarmonyMethod(typeof(CoopInput), nameof(UpdatePostfix)));
                harmony.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.GetDirection)), prefix: new HarmonyMethod(typeof(CoopInput), nameof(DirectionPrefix)));
                addPressed = AccessTools.Method(typeof(LazyInput), "AddPressed");
                addHolded = AccessTools.Method(typeof(LazyInput), "AddHolded");
            }
            catch (Exception ex)
            {
                log?.LogWarning("Controller input: buttons the mod uses may also reach the game: " + ex.Message);
            }
        }

        /// <summary>A rule: while it returns true the game does not see the stick (a window has it).</summary>
        internal static void HideDirection(Func<bool> rule)
        {
            directionHiders.Add(rule);
        }

        /// <summary>A rule: game keys the game should not see (while it returns true for them).</summary>
        internal static void Hide(Func<GameKey, bool> rule)
        {
            hiders.Add(rule);
        }

        internal static bool PadActive
        {
            get
            {
                try
                {
                    return LazyInput.IsGamepadActive;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The button went down this frame.</summary>
        internal static bool PadDown(GamepadButton button)
        {
            return PadActive && Read(button, true);
        }

        /// <summary>The button is held.</summary>
        internal static bool PadHeld(GamepadButton button)
        {
            return PadActive && Read(button, false);
        }

        /// <summary>A game key as the game reads it, even while it is hidden from the game.</summary>
        internal static bool KeyDown(GameKey key)
        {
            try
            {
                bypass = true;
                return LazyInput.GetKeyDown(key);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                bypass = false;
            }
        }

        /// <summary>The stick or pad direction (x right, y up), as the game reads it.</summary>
        internal static Vector2 Direction()
        {
            try
            {
                bypass = true;
                return LazyInput.GetDirection();
            }
            catch (Exception)
            {
                return Vector2.zero;
            }
            finally
            {
                bypass = false;
            }
        }

        private static bool Read(GamepadButton button, bool down)
        {
            try
            {
                bypass = true;
                foreach (int value in KeysOf(button))
                {
                    GameKey key = KeyByValue(value);
                    if ((object)key != null && (down ? LazyInput.GetKeyDown(key) : LazyInput.GetKey(key)))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                bypass = false;
            }
        }

        /// <summary>Every game key a controller button is bound to.</summary>
        internal static HashSet<int> KeysOf(GamepadButton button)
        {
            if (!keysOfButton.TryGetValue(button.value, out HashSet<int> keys))
            {
                keys = new HashSet<int>();
                try
                {
                    foreach (GamepadBinding binding in LazyInput.GameBindings.gamepadBindings)
                    {
                        if ((object)binding.gamepadButton != null && (object)binding.gameKey != null && binding.gamepadButton.value == button.value)
                        {
                            keys.Add(binding.gameKey.value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log?.LogWarning("Controller input: could not read the controller bindings: " + ex.Message);
                }
                if (keys.Count > 0)
                {
                    // Only once the game's bindings are loaded.
                    keysOfButton[button.value] = keys;
                }
            }
            return keys;
        }

        private static readonly Dictionary<int, GameKey> keyByValue = new Dictionary<int, GameKey>();

        private static GameKey KeyByValue(int value)
        {
            if (keyByValue.Count == 0)
            {
                foreach (FieldInfo field in typeof(GameKey).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.GetValue(null) is GameKey key && (object)key != null && !keyByValue.ContainsKey(key.value))
                    {
                        keyByValue[key.value] = key;
                    }
                }
            }
            keyByValue.TryGetValue(value, out GameKey found);
            return found;
        }

        /// <summary>
        /// The game's hint for a game key: with a controller its button icon (a TMP sprite tag),
        /// null when there is none.
        /// </summary>
        internal static string Icon(GameKey key)
        {
            try
            {
                string icon = ControllerIconLibrary.GetIconId(key, null, true);
                return string.IsNullOrEmpty(icon) ? null : icon;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- typing with a controller

        private static TMPro.TMP_InputField editing;
        private static Action<string> typed;

        /// <summary>
        /// Steam's controller keyboard for a line of text (chat): false when Steam does not offer
        /// it here (outside Big Picture or the Steam Deck).
        /// </summary>
        internal static bool TypeText(string description, int maxLength, Action<string> done)
        {
            try
            {
                if (dismissed == null)
                {
                    dismissed = Steamworks.Callback<Steamworks.GamepadTextInputDismissed_t>.Create(OnTextDismissed);
                }
                editing = null;
                typed = done;
                if (Steamworks.SteamUtils.ShowGamepadTextInput(Steamworks.EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                        Steamworks.EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, description ?? string.Empty, (uint)maxLength, string.Empty))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                log?.LogInfo("Controller input: Steam's keyboard is not available (" + ex.Message + ").");
            }
            typed = null;
            return false;
        }
        private static Steamworks.Callback<Steamworks.GamepadTextInputDismissed_t> dismissed;

        /// <summary>
        /// A text field chosen with a controller: Steam's controller keyboard when Steam offers
        /// it (Big Picture, Steam Deck), otherwise the field takes the keyboard.
        /// </summary>
        internal static void EditText(TMPro.TMP_InputField field, Action<string> onTyped)
        {
            try
            {
                if (dismissed == null)
                {
                    dismissed = Steamworks.Callback<Steamworks.GamepadTextInputDismissed_t>.Create(OnTextDismissed);
                }
                editing = field;
                typed = onTyped;
                if (Steamworks.SteamUtils.ShowGamepadTextInput(Steamworks.EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                        Steamworks.EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, string.Empty,
                        (uint)Math.Max(1, field.characterLimit > 0 ? field.characterLimit : 64), field.text ?? string.Empty))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                log?.LogInfo("Controller input: Steam's keyboard is not available (" + ex.Message + "); the field takes the keyboard.");
            }
            editing = null;
            typed = null;
            if (PadActive)
            {
                // No Steam keyboard here: the mod's own, on the game's buttons.
                CoopKeyboard.Open(string.Empty, field.text, field.characterLimit, onTyped);
                return;
            }
            field.ActivateInputField();
        }

        private static void OnTextDismissed(Steamworks.GamepadTextInputDismissed_t result)
        {
            try
            {
                if ((editing != null || typed != null) && result.m_bSubmitted)
                {
                    uint length = Steamworks.SteamUtils.GetEnteredGamepadTextLength();
                    if (Steamworks.SteamUtils.GetEnteredGamepadTextInput(out string text, length + 1))
                    {
                        if (editing != null) editing.text = text;
                        typed?.Invoke(text);
                    }
                }
                typed = null;
            }
            catch (Exception ex)
            {
                log?.LogWarning("Controller input: could not read the typed text: " + ex.Message);
            }
            editing = null;
        }

        // ---------------------------------------------------------------- the game's side

        private static bool GameKeyPrefix(GameKey __0, ref bool __result)
        {
            if (bypass || (object)__0 == null || hiders.Count == 0)
            {
                return true;
            }
            foreach (Func<GameKey, bool> rule in hiders)
            {
                bool hide;
                try
                {
                    hide = rule(__0);
                }
                catch (Exception)
                {
                    hide = false;
                }
                if (hide)
                {
                    __result = false;
                    return false;
                }
            }
            return true;
        }

        private static bool DirectionPrefix(ref Vector2 __result)
        {
            if (bypass)
            {
                return true;
            }
            if (Time.unscaledTime < stickUntil)
            {
                __result = stick;
                return false;
            }
            foreach (Func<bool> rule in directionHiders)
            {
                bool hide;
                try
                {
                    hide = rule();
                }
                catch (Exception)
                {
                    hide = false;
                }
                if (hide)
                {
                    __result = Vector2.zero;
                    return false;
                }
            }
            return true;
        }

        private static void UpdatePostfix(LazyInput __instance)
        {
            if (held.Count > 0 && addHolded != null)
            {
                if (Time.unscaledTime < heldUntil)
                {
                    foreach (GameKey key in held)
                    {
                        addHolded.Invoke(__instance, new object[] { key });
                    }
                }
                else
                {
                    held.Clear();
                }
            }
            if (injected.Count == 0 || addPressed == null)
            {
                return;
            }
            foreach (GameKey key in injected)
            {
                addPressed.Invoke(__instance, new object[] { key });
            }
            injected.Clear();
        }

        /// <summary>Tests: a controller button held for a while.</summary>
        private static Vector2 stick;
        private static float stickUntil;

        /// <summary>Tests: the stick held in a direction, so the player really walks (animation and all).</summary>
        internal static string StickForTest(float x, float y, float seconds)
        {
            stick = new Vector2(x, y);
            stickUntil = Time.unscaledTime + seconds;
            return "stick " + stick + " for " + seconds + " s";
        }

        internal static string HoldForTest(string button, float seconds)
        {
            FieldInfo field = typeof(GamepadButton).GetField(button, BindingFlags.Public | BindingFlags.Static);
            if (field == null)
            {
                return "no button " + button;
            }
            held.Clear();
            foreach (int value in KeysOf((GamepadButton)field.GetValue(null)))
            {
                GameKey key = KeyByValue(value);
                if ((object)key != null) held.Add(key);
            }
            heldUntil = Time.unscaledTime + seconds;
            return "holding " + button + " for " + seconds + " s (" + held.Count + " game keys)";
        }

        /// <summary>Tests: a controller press of a button (A, B, DDown…) for one frame.</summary>
        internal static string PressForTest(string button)
        {
            FieldInfo field = typeof(GamepadButton).GetField(button, BindingFlags.Public | BindingFlags.Static);
            if (field == null)
            {
                return "no button " + button;
            }
            var pad = (GamepadButton)field.GetValue(null);
            foreach (int value in KeysOf(pad))
            {
                GameKey key = KeyByValue(value);
                if ((object)key != null) injected.Add(key);
            }
            return "pressing " + button + " (" + injected.Count + " game keys)";
        }
    }
}
