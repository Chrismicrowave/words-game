// Assets/-Scripts/Core/InputHandler.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.EventSystems;

public class InputHandler : MonoBehaviour
{
    public static InputHandler Instance { get; private set; }

    public event Action<KeyCode, bool> OnKeyAction; // key, isPressed
    public event Action OnBackspacePressed;
    public event Action OnEnterPressed;

    private Keyboard keyboard;

    // Map New Input System Key enum to legacy KeyCode for WordEngine compatibility
    private readonly Dictionary<Key, KeyCode> keyToKeyCode = new Dictionary<Key, KeyCode>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        BuildKeyMap();
    }

    void BuildKeyMap()
    {
        // Letters
        keyToKeyCode[Key.A] = KeyCode.A; keyToKeyCode[Key.B] = KeyCode.B;
        keyToKeyCode[Key.C] = KeyCode.C; keyToKeyCode[Key.D] = KeyCode.D;
        keyToKeyCode[Key.E] = KeyCode.E; keyToKeyCode[Key.F] = KeyCode.F;
        keyToKeyCode[Key.G] = KeyCode.G; keyToKeyCode[Key.H] = KeyCode.H;
        keyToKeyCode[Key.I] = KeyCode.I; keyToKeyCode[Key.J] = KeyCode.J;
        keyToKeyCode[Key.K] = KeyCode.K; keyToKeyCode[Key.L] = KeyCode.L;
        keyToKeyCode[Key.M] = KeyCode.M; keyToKeyCode[Key.N] = KeyCode.N;
        keyToKeyCode[Key.O] = KeyCode.O; keyToKeyCode[Key.P] = KeyCode.P;
        keyToKeyCode[Key.Q] = KeyCode.Q; keyToKeyCode[Key.R] = KeyCode.R;
        keyToKeyCode[Key.S] = KeyCode.S; keyToKeyCode[Key.T] = KeyCode.T;
        keyToKeyCode[Key.U] = KeyCode.U; keyToKeyCode[Key.V] = KeyCode.V;
        keyToKeyCode[Key.W] = KeyCode.W; keyToKeyCode[Key.X] = KeyCode.X;
        keyToKeyCode[Key.Y] = KeyCode.Y; keyToKeyCode[Key.Z] = KeyCode.Z;

        // Digits
        keyToKeyCode[Key.Digit0] = KeyCode.Alpha0;
        keyToKeyCode[Key.Digit1] = KeyCode.Alpha1;
        keyToKeyCode[Key.Digit2] = KeyCode.Alpha2;
        keyToKeyCode[Key.Digit3] = KeyCode.Alpha3;
        keyToKeyCode[Key.Digit4] = KeyCode.Alpha4;
        keyToKeyCode[Key.Digit5] = KeyCode.Alpha5;
        keyToKeyCode[Key.Digit6] = KeyCode.Alpha6;
        keyToKeyCode[Key.Digit7] = KeyCode.Alpha7;
        keyToKeyCode[Key.Digit8] = KeyCode.Alpha8;
        keyToKeyCode[Key.Digit9] = KeyCode.Alpha9;
    }

    private bool IsUIFocused()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null) return false;
        // Check for both TMP and legacy input fields
        return selected.GetComponent<TMPro.TMP_InputField>() != null
            || selected.GetComponent<UnityEngine.UI.InputField>() != null;
    }

    void Update()
    {
        keyboard = Keyboard.current;
        if (keyboard == null || IsUIFocused()) return;

        // Check backspace
        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            OnBackspacePressed?.Invoke();
            return;
        }

        // Check enter
        if (keyboard.enterKey.wasPressedThisFrame)
        {
            OnEnterPressed?.Invoke();
            return;
        }

        // Check all mapped keys for press and release
        foreach (var kvp in keyToKeyCode)
        {
            KeyControl keyControl = keyboard[kvp.Key];

            if (keyControl.wasPressedThisFrame)
            {
                OnKeyAction?.Invoke(kvp.Value, true);
                return; // process one key event per frame
            }

            if (keyControl.wasReleasedThisFrame)
            {
                OnKeyAction?.Invoke(kvp.Value, false);
                return;
            }
        }
    }
}
