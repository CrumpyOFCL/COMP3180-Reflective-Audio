using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerInputManager : MonoBehaviour
{
    [HideInInspector]
    public Gamepad currentController;

    public enum playerInputType
    {
        KeyboardMouse,
        Controller
    }

    [HideInInspector]
    public playerInputType inputType;

    public enum GamepadFamily
    {
        None,
        Xbox,
        PlayStation,
        Switch,
        Generic
    }

    // Which kind of pad is in use, for labelling prompts
    // A vs Cross vs B.
    public GamepadFamily Family { get; private set; } =
        GamepadFamily.None;

    // Raised when the active input scheme or controller
    // family changes.
    public static event System.Action OnInputChanged;

    // Raised specifically when input bindings are rebound
    // or reset.
    public static event System.Action OnBindingsChanged;

    // Statics survive a play session when Reload Domain is off.
    // Clear them when the subsystem starts again so destroyed
    // UI objects do not remain subscribed.
    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnInputChanged = null;
        OnBindingsChanged = null;
    }

    // Call this after a rebind is applied or bindings
    // are reset to defaults.
    public static void NotifyBindingsChanged()
    {
        OnBindingsChanged?.Invoke();
    }

    private PlayerInput playerInput;

    private string lastScheme;

    public InputAction moveAction
    {
        get;
        private set;
    }

    public InputAction jumpAction
    {
        get;
        private set;
    }

    public InputAction dashAction
    {
        get;
        private set;
    }

    public InputAction aimAction
    {
        get;
        private set;
    }

    public InputAction shootAction
    {
        get;
        private set;
    }

    public InputAction switchAction
    {
        get;
        private set;
    }

    public InputAction switchBackAction
    {
        get;
        private set;
    }

    public InputAction interactAction
    {
        get;
        private set;
    }

    public InputAction dropAction
    {
        get;
        private set;
    }

    public InputAction boxPushAction
    {
        get;
        private set;
    }

    // Optional actions.
    // Null until these exist in the input asset.
    public InputAction cycleCrystalNextAction
    {
        get;
        private set;
    }

    public InputAction cycleCrystalPrevAction
    {
        get;
        private set;
    }

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();

        // Use PlayerInput's own runtime InputActionAsset instance.
        //
        // Do NOT create a separate new PlayerInputs() here,
        // because rebinding overrides applied to PlayerInput
        // would not automatically reach that separate instance.
        InputActionAsset actions =
            playerInput.actions;

        moveAction =
            actions.FindAction(
                "Player/Move",
                true
            );

        jumpAction =
            actions.FindAction(
                "Player/Jump",
                true
            );

        dashAction =
            actions.FindAction(
                "Player/Dash",
                true
            );

        aimAction =
            actions.FindAction(
                "Player/AimBeam",
                true
            );

        shootAction =
            actions.FindAction(
                "Player/ShootBeam",
                true
            );

        switchAction =
            actions.FindAction(
                "Player/SwitchTimeForward",
                true
            );

        switchBackAction =
            actions.FindAction(
                "Player/SwitchTimeBack",
                true
            );

        interactAction =
            actions.FindAction(
                "Player/Interact",
                true
            );

        dropAction =
            actions.FindAction(
                "Player/Drop",
                true
            );

        boxPushAction =
            actions.FindAction(
                "Player/PushBox",
                true
            );

        // Optional actions.
        // throwIfNotFound = false prevents a missing optional
        // action from breaking the whole input manager.
        cycleCrystalNextAction =
            actions.FindAction(
                "Player/CycleCrystalNext",
                false
            );

        cycleCrystalPrevAction =
            actions.FindAction(
                "Player/CycleCrystalPrev",
                false
            );
    }

    // PlayerInput controls the action-map enable state,
    // so this deliberately does not enable/disable actions.
    private void OnEnable()
    {
        RefreshControls();
    }

    // Watch currentControlScheme directly so this works
    // regardless of whether PlayerInput Behaviour is set to
    // Send Messages or Invoke C# Events.
    private void Update()
    {
        if (playerInput == null)
        {
            return;
        }

        if (playerInput.currentControlScheme != lastScheme)
        {
            RefreshControls();
        }
    }

    // Also works as a SendMessage callback from PlayerInput.
    private void OnControlsChanged(PlayerInput input)
    {
        // PlayerInput can send this before our own fields
        // have finished initialising.
        if (playerInput == null)
        {
            playerInput = input;
        }

        RefreshControls();
    }

    private void RefreshControls()
    {
        if (playerInput == null)
        {
            return;
        }

        playerInputType previousType =
            inputType;

        GamepadFamily previousFamily =
            Family;

        string scheme =
            playerInput.currentControlScheme;

        lastScheme =
            scheme;

        if (scheme == "KeyboardMouse")
        {
            inputType =
                playerInputType.KeyboardMouse;

            currentController =
                null;

            Family =
                GamepadFamily.None;
        }
        else if (scheme == "Controller")
        {
            inputType =
                playerInputType.Controller;

            currentController =
                FindActiveGamepad(playerInput);

            Family =
                ResolveFamily(playerInput);
        }
        else
        {
            // currentControlScheme can be null if Default Scheme
            // is <Any>, or unrecognised if scheme names change.
            //
            // Fall back to checking PlayerInput's paired devices.
            Gamepad pairedGamepad =
                FindActiveGamepad(playerInput);

            bool hasPad =
                pairedGamepad != null;

            inputType =
                hasPad
                    ? playerInputType.Controller
                    : playerInputType.KeyboardMouse;

            currentController =
                pairedGamepad;

            Family =
                hasPad
                    ? ResolveFamily(playerInput)
                    : GamepadFamily.None;

            Debug.LogWarning(
                $"From PlayerInputManager: control scheme \"{scheme}\" " +
                "not recognised. Set the Default Scheme on PlayerInput " +
                "to KeyboardMouse and check that your Input Actions " +
                "control scheme names are exactly KeyboardMouse and Controller."
            );
        }

        // Changing controller family can change displayed prompts
        // even if the input scheme itself remains Controller.
        if (inputType != previousType ||
            Family != previousFamily)
        {
            OnInputChanged?.Invoke();
        }
    }

    private static Gamepad FindActiveGamepad(
        PlayerInput input)
    {
        if (input == null)
        {
            return Gamepad.current;
        }

        // Prefer the gamepad actually paired to this PlayerInput.
        foreach (InputDevice device in input.devices)
        {
            if (device is Gamepad gamepad)
            {
                return gamepad;
            }
        }

        return Gamepad.current;
    }

    // Layout inheritance rather than concrete controller
    // classes keeps this compatible with derived device layouts.
    private static GamepadFamily ResolveFamily(
        PlayerInput input)
    {
        if (input == null)
        {
            return GamepadFamily.None;
        }

        foreach (InputDevice device in input.devices)
        {
            if (!(device is Gamepad))
            {
                continue;
            }

            if (
                IsLayout(
                    device,
                    "DualShockGamepad"
                ) ||
                IsLayout(
                    device,
                    "DualSenseGamepadHID"
                ))
            {
                return GamepadFamily.PlayStation;
            }

            if (IsLayout(
                device,
                "XInputController"))
            {
                return GamepadFamily.Xbox;
            }

            if (IsLayout(
                device,
                "SwitchProControllerHID"))
            {
                return GamepadFamily.Switch;
            }

            return GamepadFamily.Generic;
        }

        return GamepadFamily.None;
    }

    private static bool IsLayout(
        InputDevice device,
        string layoutName)
    {
        return InputSystem
            .IsFirstLayoutBasedOnSecond(
                device.layout,
                layoutName
            );
    }
}
