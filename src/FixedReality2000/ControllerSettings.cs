using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FixedReality2000;

internal enum ControllerAction
{
    Primary,
    Secondary,
    Utility,
    PreviousTool,
    NextTool,
    ToggleToolbar,
    Sprint
}

internal enum ControllerBinding
{
    None,
    South,
    East,
    West,
    North,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    LeftStick,
    RightStick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Start,
    Select
}

internal enum ControllerResponseCurve
{
    Linear,
    Standard,
    Dynamic
}

internal enum ControllerSprintMode
{
    Hold,
    Toggle
}

internal enum ControllerGlyphLayout
{
    Xbox,
    PlayStation,
    Nintendo
}

internal enum ControllerStickLayout
{
    Standard,
    Southpaw
}

internal static class ControllerSettings
{
    private const string ConfigFileName = "FixedReality2000.controller.cfg";
    private const string SettingsSection = "Gamepad";
    private const string BindingsSection = "Bindings";

    private static readonly Dictionary<ControllerAction, ControllerBinding>
        Defaults = new()
        {
            [ControllerAction.Primary] = ControllerBinding.RightTrigger,
            [ControllerAction.Secondary] = ControllerBinding.LeftTrigger,
            [ControllerAction.Utility] = ControllerBinding.North,
            [ControllerAction.PreviousTool] = ControllerBinding.LeftShoulder,
            [ControllerAction.NextTool] = ControllerBinding.RightShoulder,
            [ControllerAction.ToggleToolbar] = ControllerBinding.DpadDown,
            [ControllerAction.Sprint] = ControllerBinding.LeftStick
        };

    private static readonly Dictionary<ControllerAction, ConfigEntry<string>>
        BindingEntries = new();
    private static readonly Dictionary<ControllerAction, bool> CurrentState = new();
    private static readonly Dictionary<ControllerAction, bool> PreviousState = new();
    private static readonly Dictionary<ControllerAction, bool> PressedState = new();
    private static readonly Dictionary<ControllerAction, bool> ReleasedState = new();

    private static ConfigFile? _config;
    private static ConfigEntry<float>? _lookSensitivity;
    private static ConfigEntry<bool>? _invertX;
    private static ConfigEntry<bool>? _invertY;
    private static ConfigEntry<float>? _moveDeadzone;
    private static ConfigEntry<float>? _lookDeadzone;
    private static ConfigEntry<float>? _cursorSpeed;
    private static ConfigEntry<float>? _triggerThreshold;
    private static ConfigEntry<string>? _responseCurve;
    private static ConfigEntry<string>? _sprintMode;
    private static ConfigEntry<string>? _stickLayout;
    private static ConfigEntry<bool>? _vibrationEnabled;
    private static ConfigEntry<float>? _vibrationIntensity;
    private static int _inputFrame = -1;
    private static bool _sprintToggle;
    private static float _rumbleEndsAt;
    private static float _rumbleLow;
    private static float _rumbleHigh;

    internal static readonly ControllerAction[] MenuOrder =
    {
        ControllerAction.Primary,
        ControllerAction.Secondary,
        ControllerAction.Utility,
        ControllerAction.PreviousTool,
        ControllerAction.NextTool,
        ControllerAction.ToggleToolbar,
        ControllerAction.Sprint
    };

    internal static float LookSensitivity
    {
        get => _lookSensitivity?.Value ?? 1f;
        set => SetClamped(_lookSensitivity, value, 0.25f, 3f);
    }

    internal static bool InvertX
    {
        get => _invertX?.Value ?? false;
        set => SetValue(_invertX, value);
    }

    internal static bool InvertY
    {
        get => _invertY?.Value ?? false;
        set => SetValue(_invertY, value);
    }

    internal static float MoveDeadzone
    {
        get => _moveDeadzone?.Value ?? 0.16f;
        set => SetClamped(_moveDeadzone, value, 0f, 0.4f);
    }

    internal static float LookDeadzone
    {
        get => _lookDeadzone?.Value ?? 0.12f;
        set => SetClamped(_lookDeadzone, value, 0f, 0.4f);
    }

    internal static float CursorSpeed
    {
        get => _cursorSpeed?.Value ?? 900f;
        set => SetClamped(_cursorSpeed, value, 300f, 1800f);
    }

    internal static float TriggerThreshold
    {
        get => _triggerThreshold?.Value ?? 0.15f;
        set => SetClamped(_triggerThreshold, value, 0.05f, 0.9f);
    }

    internal static float VibrationIntensity
    {
        get => _vibrationIntensity?.Value ?? 1f;
        set => SetClamped(_vibrationIntensity, value, 0f, 1f);
    }

    internal static bool VibrationEnabled
    {
        get => _vibrationEnabled?.Value ?? true;
        set
        {
            SetValue(_vibrationEnabled, value);
            if (!value)
            {
                StopRumble();
            }
        }
    }

    internal static ControllerResponseCurve ResponseCurve
    {
        get => Parse(_responseCurve, ControllerResponseCurve.Standard);
        set => SetEnum(_responseCurve, value);
    }

    internal static ControllerSprintMode SprintMode
    {
        get => Parse(_sprintMode, ControllerSprintMode.Hold);
        set
        {
            SetEnum(_sprintMode, value);
            _sprintToggle = false;
        }
    }

    internal static ControllerStickLayout StickLayout
    {
        get => Parse(_stickLayout, ControllerStickLayout.Standard);
        set => SetEnum(_stickLayout, value);
    }

    internal static void Initialize()
    {
        string path = Path.Combine(Paths.ConfigPath, ConfigFileName);
        _config = new ConfigFile(path, saveOnInit: false)
        {
            SaveOnConfigSet = false
        };
        BindEntries();
        ValidateAll();
        _config.Save();
        _config.SaveOnConfigSet = true;
        ResetRuntimeState();
    }

    internal static void Reload()
    {
        if (_config == null)
        {
            Initialize();
            return;
        }

        _config.Reload();
        _config.SaveOnConfigSet = false;
        BindEntries();
        ValidateAll();
        _config.Save();
        _config.SaveOnConfigSet = true;
        ResetRuntimeState();
    }

    private static void BindEntries()
    {
        if (_config == null)
        {
            return;
        }

        _lookSensitivity = _config.Bind(
            SettingsSection,
            "LookSensitivity",
            1f,
            new ConfigDescription(
                "Controller camera sensitivity.",
                new AcceptableValueRange<float>(0.25f, 3f)));
        _invertX = _config.Bind(
            SettingsSection, "InvertX", false, "Invert horizontal camera input.");
        _invertY = _config.Bind(
            SettingsSection, "InvertY", false, "Invert vertical camera input.");
        _moveDeadzone = _config.Bind(
            SettingsSection,
            "MoveDeadzone",
            0.16f,
            new ConfigDescription(
                "Radial movement-stick deadzone.",
                new AcceptableValueRange<float>(0f, 0.4f)));
        _lookDeadzone = _config.Bind(
            SettingsSection,
            "LookDeadzone",
            0.12f,
            new ConfigDescription(
                "Radial camera-stick deadzone.",
                new AcceptableValueRange<float>(0f, 0.4f)));
        _cursorSpeed = _config.Bind(
            SettingsSection,
            "MenuCursorSpeed",
            900f,
            new ConfigDescription(
                "Virtual cursor speed in menus.",
                new AcceptableValueRange<float>(300f, 1800f)));
        _triggerThreshold = _config.Bind(
            SettingsSection,
            "TriggerThreshold",
            0.15f,
            new ConfigDescription(
                "Trigger press threshold.",
                new AcceptableValueRange<float>(0.05f, 0.9f)));
        _responseCurve = _config.Bind(
            SettingsSection,
            "ResponseCurve",
            ControllerResponseCurve.Standard.ToString(),
            "Stick response curve: Linear, Standard, or Dynamic.");
        _sprintMode = _config.Bind(
            SettingsSection,
            "SprintMode",
            ControllerSprintMode.Hold.ToString(),
            "Controller sprint behavior: Hold or Toggle.");
        _stickLayout = _config.Bind(
            SettingsSection,
            "StickLayout",
            ControllerStickLayout.Standard.ToString(),
            "Stick layout: Standard or Southpaw.");
        _vibrationEnabled = _config.Bind(
            SettingsSection, "Vibration", true, "Enable controller vibration.");
        _vibrationIntensity = _config.Bind(
            SettingsSection,
            "VibrationIntensity",
            1f,
            new ConfigDescription(
                "Controller vibration strength.",
                new AcceptableValueRange<float>(0f, 1f)));
        ConfigDefinition obsoleteGlyphLayout =
            new(SettingsSection, "GlyphLayout");
        _config.Bind(
            obsoleteGlyphLayout,
            "Auto",
            new ConfigDescription(
                "Obsolete setting retained only for automatic migration."));
        _config.Remove(obsoleteGlyphLayout);

        BindingEntries.Clear();
        foreach (ControllerAction action in MenuOrder)
        {
            BindingEntries[action] = _config.Bind(
                BindingsSection,
                action.ToString(),
                Defaults[action].ToString(),
                $"{GetActionLabel(action)} controller binding.");
        }
    }

    private static void ValidateAll()
    {
        ValidateEnum(_responseCurve, ControllerResponseCurve.Standard);
        ValidateEnum(_sprintMode, ControllerSprintMode.Hold);
        ValidateEnum(_stickLayout, ControllerStickLayout.Standard);
        foreach (ControllerAction action in MenuOrder)
        {
            ConfigEntry<string> entry = BindingEntries[action];
            if (!Enum.TryParse(entry.Value, true, out ControllerBinding binding) ||
                binding == ControllerBinding.None)
            {
                Plugin.Log.LogWarning(
                    $"Invalid controller binding '{action} = {entry.Value}' was reset.");
                entry.Value = Defaults[action].ToString();
            }
            else
            {
                entry.Value = binding.ToString();
            }
        }
    }

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

    internal static void UpdateInputState(Gamepad? gamepad)
    {
        if (_inputFrame == Time.frameCount)
        {
            return;
        }

        _inputFrame = Time.frameCount;
        foreach (ControllerAction action in MenuOrder)
        {
            bool previous = CurrentState.TryGetValue(action, out bool old) && old;
            bool current =
                gamepad != null &&
                IsBindingHeld(gamepad, GetBinding(action));
            PreviousState[action] = previous;
            CurrentState[action] = current;
            PressedState[action] = current && !previous;
            ReleasedState[action] = !current && previous;
        }

        if (SprintMode == ControllerSprintMode.Toggle &&
            Pressed(ControllerAction.Sprint))
        {
            _sprintToggle = !_sprintToggle;
        }
    }

    internal static bool Held(ControllerAction action)
    {
        UpdateInputState(Gamepad.current);
        return CurrentState.TryGetValue(action, out bool value) && value;
    }

    internal static bool Pressed(ControllerAction action)
    {
        UpdateInputState(Gamepad.current);
        return PressedState.TryGetValue(action, out bool value) && value;
    }

    internal static bool Released(ControllerAction action)
    {
        UpdateInputState(Gamepad.current);
        return ReleasedState.TryGetValue(action, out bool value) && value;
    }

    internal static bool SprintActive()
    {
        UpdateInputState(Gamepad.current);
        return SprintMode == ControllerSprintMode.Toggle
            ? _sprintToggle
            : Held(ControllerAction.Sprint);
    }

    internal static void CancelSprintToggle()
    {
        _sprintToggle = false;
    }

    internal static Vector2 ApplyCurve(Vector2 value)
    {
        float magnitude = Mathf.Clamp01(value.magnitude);
        if (magnitude <= 0f)
        {
            return Vector2.zero;
        }

        float curved = ResponseCurve switch
        {
            ControllerResponseCurve.Linear => magnitude,
            ControllerResponseCurve.Dynamic =>
                Mathf.SmoothStep(0f, 1f, magnitude),
            _ => magnitude * magnitude
        };
        return value / magnitude * curved;
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

    internal static bool HasActivity(Gamepad gamepad)
    {
        return
            gamepad.leftStick.ReadUnprocessedValue().magnitude > MoveDeadzone ||
            gamepad.rightStick.ReadUnprocessedValue().magnitude > LookDeadzone ||
            gamepad.leftTrigger.ReadValue() > TriggerThreshold ||
            gamepad.rightTrigger.ReadValue() > TriggerThreshold ||
            gamepad.buttonSouth.isPressed ||
            gamepad.buttonNorth.isPressed ||
            gamepad.buttonEast.isPressed ||
            gamepad.buttonWest.isPressed ||
            gamepad.leftShoulder.isPressed ||
            gamepad.rightShoulder.isPressed ||
            gamepad.leftStickButton.isPressed ||
            gamepad.rightStickButton.isPressed ||
            gamepad.startButton.isPressed ||
            gamepad.selectButton.isPressed ||
            gamepad.dpad.IsPressed();
    }

    internal static void PulseRumble(
        float lowFrequency,
        float highFrequency,
        float duration)
    {
        if (!VibrationEnabled || VibrationIntensity <= 0f)
        {
            return;
        }

        _rumbleLow = Mathf.Clamp01(lowFrequency) * VibrationIntensity;
        _rumbleHigh = Mathf.Clamp01(highFrequency) * VibrationIntensity;
        _rumbleEndsAt = Time.unscaledTime + Mathf.Max(0.01f, duration);
    }

    internal static void TickRumble()
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return;
        }

        if (!VibrationEnabled || Time.unscaledTime >= _rumbleEndsAt)
        {
            gamepad.SetMotorSpeeds(0f, 0f);
            return;
        }

        gamepad.SetMotorSpeeds(_rumbleLow, _rumbleHigh);
    }

    internal static void StopRumble()
    {
        _rumbleEndsAt = 0f;
        Gamepad.current?.SetMotorSpeeds(0f, 0f);
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

    private static void ResetRuntimeState()
    {
        _inputFrame = -1;
        _sprintToggle = false;
        CurrentState.Clear();
        PreviousState.Clear();
        PressedState.Clear();
        ReleasedState.Clear();
        StopRumble();
    }

    private static T Parse<T>(ConfigEntry<string>? entry, T fallback)
        where T : struct, Enum
    {
        return entry != null &&
               Enum.TryParse(entry.Value, true, out T value)
            ? value
            : fallback;
    }

    private static void ValidateEnum<T>(ConfigEntry<string>? entry, T fallback)
        where T : struct, Enum
    {
        if (entry == null)
        {
            return;
        }

        if (!Enum.TryParse(entry.Value, true, out T value))
        {
            entry.Value = fallback.ToString();
            return;
        }

        entry.Value = value.ToString();
    }

    private static void SetEnum<T>(ConfigEntry<string>? entry, T value)
        where T : struct, Enum
    {
        if (entry != null)
        {
            entry.Value = value.ToString();
        }
    }

    private static void SetValue<T>(ConfigEntry<T>? entry, T value)
    {
        if (entry != null)
        {
            entry.Value = value;
        }
    }

    private static void SetClamped(
        ConfigEntry<float>? entry,
        float value,
        float minimum,
        float maximum)
    {
        if (entry != null)
        {
            entry.Value = Mathf.Clamp(value, minimum, maximum);
        }
    }
}
