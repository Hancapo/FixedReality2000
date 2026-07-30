using System;
using BepInEx.Configuration;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FixedReality2000;

internal static partial class ControllerSettings
{
    internal static ControllerBinding GetBinding(ControllerAction action)
    {
        return BindingEntries.TryGetValue(action, out ConfigEntry<string>? entry) &&
               Enum.TryParse(entry.Value, true, out ControllerBinding binding)
            ? binding
            : Defaults[action];
    }

    internal static void SetBinding(
        ControllerAction action,
        ControllerBinding binding)
    {
        if (binding == ControllerBinding.None)
        {
            binding = Defaults[action];
        }

        if (binding != ControllerBinding.None)
        {
            foreach (ControllerAction other in MenuOrder)
            {
                if (other != action && GetBinding(other) == binding)
                {
                    BindingEntries[other].Value =
                        GetBinding(action).ToString();
                    break;
                }
            }
        }

        BindingEntries[action].Value = binding.ToString();
        ResetRuntimeState();
    }

    internal static void Unbind(ControllerAction action) =>
        SetBinding(action, Defaults[action]);

    internal static ButtonControl GetButtonControl(
        Gamepad gamepad,
        ControllerAction action)
    {
        ControllerBinding binding = GetBinding(action);
        ButtonControl control = binding switch
        {
            ControllerBinding.South => gamepad.buttonSouth,
            ControllerBinding.East => gamepad.buttonEast,
            ControllerBinding.West => gamepad.buttonWest,
            ControllerBinding.North => gamepad.buttonNorth,
            ControllerBinding.LeftShoulder => gamepad.leftShoulder,
            ControllerBinding.RightShoulder => gamepad.rightShoulder,
            ControllerBinding.LeftTrigger => gamepad.leftTrigger,
            ControllerBinding.RightTrigger => gamepad.rightTrigger,
            ControllerBinding.LeftStick => gamepad.leftStickButton,
            ControllerBinding.RightStick => gamepad.rightStickButton,
            ControllerBinding.DpadUp => gamepad.dpad.up,
            ControllerBinding.DpadDown => gamepad.dpad.down,
            ControllerBinding.DpadLeft => gamepad.dpad.left,
            ControllerBinding.DpadRight => gamepad.dpad.right,
            ControllerBinding.Start => gamepad.startButton,
            ControllerBinding.Select => gamepad.selectButton,
            _ => GetDefaultButtonControl(gamepad, action)
        };

        if (binding is ControllerBinding.LeftTrigger or
            ControllerBinding.RightTrigger)
        {
            control.pressPoint = TriggerThreshold;
        }

        return control;
    }

    internal static void ResetAll()
    {
        if (_config == null)
        {
            return;
        }

        _config.SaveOnConfigSet = false;
        LookSensitivity = 1f;
        InvertX = false;
        InvertY = false;
        MoveDeadzone = 0.16f;
        LookDeadzone = 0.12f;
        CursorSpeed = 900f;
        TriggerThreshold = 0.15f;
        ResponseCurve = ControllerResponseCurve.Standard;
        SprintMode = ControllerSprintMode.Hold;
        StickLayout = ControllerStickLayout.Standard;
        VibrationEnabled = true;
        VibrationIntensity = 1f;
        foreach (ControllerAction action in MenuOrder)
        {
            BindingEntries[action].Value = Defaults[action].ToString();
        }

        _config.Save();
        _config.SaveOnConfigSet = true;
        ResetRuntimeState();
    }


    internal static string GetActionLabel(ControllerAction action)
    {
        return action switch
        {
            ControllerAction.Primary => "PRIMARY ACTION",
            ControllerAction.Secondary => "SECONDARY ACTION",
            ControllerAction.Utility => "UTILITY",
            ControllerAction.PreviousTool => "PREVIOUS TOOL",
            ControllerAction.NextTool => "NEXT TOOL",
            ControllerAction.ToggleToolbar => "HIDE TOOLBAR",
            ControllerAction.Sprint => "SPRINT",
            _ => action.ToString().ToUpperInvariant()
        };
    }

    internal static string GetBindingLabel(ControllerAction action) =>
        FormatBinding(GetBinding(action));

    internal static string FormatBinding(ControllerBinding binding)
    {
        ControllerGlyphLayout layout = ResolveGlyphLayout();
        return binding switch
        {
            ControllerBinding.None => "UNBOUND",
            ControllerBinding.South => layout switch
            {
                ControllerGlyphLayout.PlayStation => "CROSS",
                ControllerGlyphLayout.Nintendo => "B",
                _ => "A"
            },
            ControllerBinding.East => layout switch
            {
                ControllerGlyphLayout.PlayStation => "CIRCLE",
                ControllerGlyphLayout.Nintendo => "A",
                _ => "B"
            },
            ControllerBinding.West => layout switch
            {
                ControllerGlyphLayout.PlayStation => "SQUARE",
                ControllerGlyphLayout.Nintendo => "Y",
                _ => "X"
            },
            ControllerBinding.North => layout switch
            {
                ControllerGlyphLayout.PlayStation => "TRIANGLE",
                ControllerGlyphLayout.Nintendo => "X",
                _ => "Y"
            },
            ControllerBinding.LeftShoulder => "LB / L1",
            ControllerBinding.RightShoulder => "RB / R1",
            ControllerBinding.LeftTrigger => "LT / L2",
            ControllerBinding.RightTrigger => "RT / R2",
            ControllerBinding.LeftStick => "L3",
            ControllerBinding.RightStick => "R3",
            ControllerBinding.DpadUp => "DPAD UP",
            ControllerBinding.DpadDown => "DPAD DOWN",
            ControllerBinding.DpadLeft => "DPAD LEFT",
            ControllerBinding.DpadRight => "DPAD RIGHT",
            ControllerBinding.Start => "START",
            ControllerBinding.Select => "SELECT",
            _ => binding.ToString().ToUpperInvariant()
        };
    }

    internal static ControllerBinding DetectPressedBinding(Gamepad gamepad)
    {
        if (gamepad.buttonSouth.wasPressedThisFrame) return ControllerBinding.South;
        if (gamepad.buttonEast.wasPressedThisFrame) return ControllerBinding.East;
        if (gamepad.buttonWest.wasPressedThisFrame) return ControllerBinding.West;
        if (gamepad.buttonNorth.wasPressedThisFrame) return ControllerBinding.North;
        if (gamepad.leftShoulder.wasPressedThisFrame) return ControllerBinding.LeftShoulder;
        if (gamepad.rightShoulder.wasPressedThisFrame) return ControllerBinding.RightShoulder;
        if (gamepad.leftStickButton.wasPressedThisFrame) return ControllerBinding.LeftStick;
        if (gamepad.rightStickButton.wasPressedThisFrame) return ControllerBinding.RightStick;
        if (gamepad.dpad.up.wasPressedThisFrame) return ControllerBinding.DpadUp;
        if (gamepad.dpad.down.wasPressedThisFrame) return ControllerBinding.DpadDown;
        if (gamepad.dpad.left.wasPressedThisFrame) return ControllerBinding.DpadLeft;
        if (gamepad.dpad.right.wasPressedThisFrame) return ControllerBinding.DpadRight;
        if (gamepad.startButton.wasPressedThisFrame) return ControllerBinding.Start;
        if (gamepad.selectButton.wasPressedThisFrame) return ControllerBinding.Select;
        if (gamepad.leftTrigger.wasPressedThisFrame) return ControllerBinding.LeftTrigger;
        if (gamepad.rightTrigger.wasPressedThisFrame) return ControllerBinding.RightTrigger;

        return ControllerBinding.None;
    }


    private static bool IsBindingHeld(
        Gamepad gamepad,
        ControllerBinding binding)
    {
        ButtonControl? button = binding switch
        {
            ControllerBinding.South => gamepad.buttonSouth,
            ControllerBinding.East => gamepad.buttonEast,
            ControllerBinding.West => gamepad.buttonWest,
            ControllerBinding.North => gamepad.buttonNorth,
            ControllerBinding.LeftShoulder => gamepad.leftShoulder,
            ControllerBinding.RightShoulder => gamepad.rightShoulder,
            ControllerBinding.LeftStick => gamepad.leftStickButton,
            ControllerBinding.RightStick => gamepad.rightStickButton,
            ControllerBinding.DpadUp => gamepad.dpad.up,
            ControllerBinding.DpadDown => gamepad.dpad.down,
            ControllerBinding.DpadLeft => gamepad.dpad.left,
            ControllerBinding.DpadRight => gamepad.dpad.right,
            ControllerBinding.Start => gamepad.startButton,
            ControllerBinding.Select => gamepad.selectButton,
            _ => null
        };
        if (button != null)
        {
            return button.isPressed;
        }

        return binding switch
        {
            ControllerBinding.LeftTrigger =>
                gamepad.leftTrigger.ReadValue() >= TriggerThreshold,
            ControllerBinding.RightTrigger =>
                gamepad.rightTrigger.ReadValue() >= TriggerThreshold,
            _ => false
        };
    }

    private static ButtonControl GetDefaultButtonControl(
        Gamepad gamepad,
        ControllerAction action)
    {
        return Defaults[action] switch
        {
            ControllerBinding.RightTrigger => gamepad.rightTrigger,
            ControllerBinding.LeftTrigger => gamepad.leftTrigger,
            ControllerBinding.North => gamepad.buttonNorth,
            ControllerBinding.LeftShoulder => gamepad.leftShoulder,
            ControllerBinding.RightShoulder => gamepad.rightShoulder,
            ControllerBinding.DpadDown => gamepad.dpad.down,
            ControllerBinding.LeftStick => gamepad.leftStickButton,
            _ => gamepad.buttonSouth
        };
    }

    private static ControllerGlyphLayout ResolveGlyphLayout()
    {
        string identity =
            $"{Gamepad.current?.displayName} {Gamepad.current?.layout} " +
            $"{Gamepad.current?.name}".ToLowerInvariant();
        if (identity.Contains("dual") ||
            identity.Contains("playstation") ||
            identity.Contains("sony"))
        {
            return ControllerGlyphLayout.PlayStation;
        }

        if (identity.Contains("switch") ||
            identity.Contains("nintendo") ||
            identity.Contains("joy"))
        {
            return ControllerGlyphLayout.Nintendo;
        }

        return ControllerGlyphLayout.Xbox;
    }


}
