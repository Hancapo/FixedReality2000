using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

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

internal static partial class ControllerSettings
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
