using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ReflectiveAudio2D
{
    /// <summary>
    /// Drop-in replacement for Input.GetKeyDown / Input.GetKey that works when the
    /// project uses the Input System package only. The old Input class throws
    /// InvalidOperationException in that case.
    ///
    /// The study tools keep their KeyCode fields, so the keys set in the Inspector
    /// stay as they are; only the call changes:
    ///     Input.GetKeyDown(startKey)  ->  KeyInput.Down(startKey)
    /// </summary>
    public static class KeyInput
    {
        public static bool Down(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl button = Control(key);
            return button != null && button.wasPressedThisFrame;
#else
            return Input.GetKeyDown(key);
#endif
        }

        public static bool Held(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl button = Control(key);
            return button != null && button.isPressed;
#else
            return Input.GetKey(key);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static readonly Dictionary<KeyCode, Key> Cache = new Dictionary<KeyCode, Key>();

        private static ButtonControl Control(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.None: return null;
                case KeyCode.Mouse0: return Mouse.current?.leftButton;
                case KeyCode.Mouse1: return Mouse.current?.rightButton;
                case KeyCode.Mouse2: return Mouse.current?.middleButton;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return null;

            Key key = ToKey(code);
            return key == Key.None ? null : keyboard[key];
        }

        // Most KeyCode names match Key names. These are the ones that don't.
        private static Key ToKey(KeyCode code)
        {
            if (Cache.TryGetValue(code, out Key cached)) return cached;

            string name = code.ToString();
            if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
            else if (name.StartsWith("Keypad") && name.Length == 7 && char.IsDigit(name[6])) name = "Numpad" + name[6];
            else
            {
                switch (code)
                {
                    case KeyCode.Return: name = "Enter"; break;
                    case KeyCode.KeypadEnter: name = "NumpadEnter"; break;
                    case KeyCode.LeftControl: name = "LeftCtrl"; break;
                    case KeyCode.RightControl: name = "RightCtrl"; break;
                    case KeyCode.BackQuote: name = "Backquote"; break;
                }
            }

            if (!Enum.TryParse(name, out Key key) || int.TryParse(name, out _))
            {
                Debug.LogWarning($"[KeyInput] No Input System key for KeyCode.{code}. Pick another key in the Inspector.");
                key = Key.None;
            }

            Cache[code] = key;
            return key;
        }
#endif
    }
}
