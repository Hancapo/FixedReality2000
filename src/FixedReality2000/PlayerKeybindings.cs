using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using com.DMT.BrokenReality2000;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using InputKey = UnityEngine.InputSystem.Key;

namespace FixedReality2000;

internal enum PlayerBinding
{
    MoveForward,
    MoveBackward,
    MoveLeft,
    MoveRight,
    Sprint,
    PreviousTool,
    NextTool,
    Utility,
    ToggleToolbar
}

internal static class PlayerKeybindings
{
    private const string PreferencePrefix = "FixedReality2000.Binding.";
    private const string ConfigSection = "Keyboard";
    private const string ConfigFileName =
        "FixedReality2000.keybindings.cfg";

    private static readonly Dictionary<PlayerBinding, ConfigEntry<string>>
        Entries = new();

    private static ConfigFile? _config;

    private static readonly IReadOnlyDictionary<PlayerBinding, InputKey> Defaults =
        new Dictionary<PlayerBinding, InputKey>
        {
            [PlayerBinding.MoveForward] = InputKey.W,
            [PlayerBinding.MoveBackward] = InputKey.S,
            [PlayerBinding.MoveLeft] = InputKey.A,
            [PlayerBinding.MoveRight] = InputKey.D,
            [PlayerBinding.Sprint] = InputKey.LeftShift,
            [PlayerBinding.PreviousTool] = InputKey.Q,
            [PlayerBinding.NextTool] = InputKey.E,
            [PlayerBinding.Utility] = InputKey.F,
            [PlayerBinding.ToggleToolbar] = InputKey.DownArrow
        };

    internal static readonly PlayerBinding[] MenuOrder =
    {
        PlayerBinding.MoveForward,
        PlayerBinding.MoveBackward,
        PlayerBinding.MoveLeft,
        PlayerBinding.MoveRight,
        PlayerBinding.Sprint,
        PlayerBinding.PreviousTool,
        PlayerBinding.NextTool,
        PlayerBinding.Utility,
        PlayerBinding.ToggleToolbar
    };

    internal static void Initialize()
    {
        string path = Path.Combine(Paths.ConfigPath, ConfigFileName);
        _config = new ConfigFile(path, saveOnInit: false)
        {
            SaveOnConfigSet = false
        };
        Entries.Clear();

        bool removedLegacyEntry = false;
        foreach (PlayerBinding binding in MenuOrder)
        {
            var definition =
                new ConfigDefinition(ConfigSection, binding.ToString());
            bool alreadyConfigured = _config.ContainsKey(definition);
            InputKey initial = Defaults[binding];

            if (!alreadyConfigured &&
                TryReadLegacy(binding, out InputKey legacy))
            {
                initial =
                    IsEssentialMovement(binding) &&
                    legacy == InputKey.None
                        ? Defaults[binding]
                        : legacy;
            }

            ConfigEntry<string> entry = _config.Bind(
                definition,
                initial.ToString(),
                new ConfigDescription(GetDescription(binding)));
            Entries[binding] = entry;
            ValidateEntry(binding, entry);

            string legacyKey = PreferencePrefix + binding;
            if (PlayerPrefs.HasKey(legacyKey))
            {
                PlayerPrefs.DeleteKey(legacyKey);
                removedLegacyEntry = true;
            }
        }

        _config.Save();
        _config.SaveOnConfigSet = true;
        if (removedLegacyEntry)
        {
            PlayerPrefs.Save();
            Plugin.Log.LogInfo(
                $"Migrated keybindings to {ConfigFileName} and removed " +
                "the legacy PlayerPrefs entries.");
        }
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
        foreach (PlayerBinding binding in MenuOrder)
        {
            var definition =
                new ConfigDefinition(ConfigSection, binding.ToString());
            ConfigEntry<string> entry;
            if (!_config.TryGetEntry(definition, out entry))
            {
                entry = _config.Bind(
                    definition,
                    Defaults[binding].ToString(),
                    new ConfigDescription(GetDescription(binding)));
            }

            Entries[binding] = entry;
            ValidateEntry(binding, entry);
        }

        _config.Save();
        _config.SaveOnConfigSet = true;
    }

    internal static InputKey Get(PlayerBinding binding)
    {
        return Entries.TryGetValue(binding, out ConfigEntry<string>? entry) &&
               Enum.TryParse(
                   entry.Value,
                   ignoreCase: true,
                   out InputKey key)
            ? key
            : Defaults[binding];
    }

    internal static void Set(PlayerBinding binding, InputKey key)
    {
        InputKey previous = Get(binding);
        if (key != InputKey.None)
        {
            foreach (PlayerBinding other in MenuOrder)
            {
                if (other != binding && Get(other) == key)
                {
                    Save(other, previous);
                    break;
                }
            }
        }

        Save(binding, key);
    }

    internal static void Unbind(PlayerBinding binding)
    {
        Set(binding, InputKey.None);
    }

    internal static void Reset(PlayerBinding binding)
    {
        Save(binding, Defaults[binding]);
    }

    internal static void ResetAll()
    {
        if (_config == null)
        {
            return;
        }

        _config.SaveOnConfigSet = false;
        foreach (PlayerBinding binding in MenuOrder)
        {
            Save(binding, Defaults[binding]);
        }

        _config.Save();
        _config.SaveOnConfigSet = true;
    }

    internal static bool IsPressed(PlayerBinding binding)
    {
        Keyboard? keyboard = Keyboard.current;
        return keyboard != null && IsPressed(keyboard, binding);
    }

    internal static string GetLabel(PlayerBinding binding)
    {
        return FormatKey(Get(binding));
    }

    internal static string GetPreviousToolPrompt() =>
        GetLabel(PlayerBinding.PreviousTool);

    internal static string GetNextToolPrompt() =>
        GetLabel(PlayerBinding.NextTool);

    internal static string GetLensPrompt() =>
        $"{GetLabel(PlayerBinding.Utility)}: CHANGE LENS";

    internal static string GetFilterPrompt() =>
        $"{GetLabel(PlayerBinding.NextTool)}: CHANGE FILTER";

    internal static string GetActionLabel(PlayerBinding binding)
    {
        return binding switch
        {
            PlayerBinding.MoveForward => "MOVE FORWARD",
            PlayerBinding.MoveBackward => "MOVE BACKWARD",
            PlayerBinding.MoveLeft => "MOVE LEFT",
            PlayerBinding.MoveRight => "MOVE RIGHT",
            PlayerBinding.Sprint => "SPRINT",
            PlayerBinding.PreviousTool => "PREVIOUS TOOL",
            PlayerBinding.NextTool => "NEXT TOOL",
            PlayerBinding.Utility => "UTILITY",
            PlayerBinding.ToggleToolbar => "HIDE TOOLBAR",
            _ => binding.ToString().ToUpperInvariant()
        };
    }

    internal static string FormatKey(InputKey key)
    {
        return key switch
        {
            InputKey.None => "UNBOUND",
            InputKey.LeftShift => "LEFT SHIFT",
            InputKey.RightShift => "RIGHT SHIFT",
            InputKey.LeftCtrl => "LEFT CTRL",
            InputKey.RightCtrl => "RIGHT CTRL",
            InputKey.LeftAlt => "LEFT ALT",
            InputKey.RightAlt => "RIGHT ALT",
            InputKey.DownArrow => "DOWN ARROW",
            InputKey.UpArrow => "UP ARROW",
            InputKey.LeftArrow => "LEFT ARROW",
            InputKey.RightArrow => "RIGHT ARROW",
            InputKey.Backspace => "BACKSPACE",
            _ => key.ToString().ToUpperInvariant()
        };
    }

    internal static bool Forward(Keyboard keyboard) =>
        IsPressed(keyboard, PlayerBinding.MoveForward);

    internal static bool Backward(Keyboard keyboard) =>
        IsPressed(keyboard, PlayerBinding.MoveBackward);

    internal static bool Left(Keyboard keyboard) =>
        IsPressed(keyboard, PlayerBinding.MoveLeft);

    internal static bool Right(Keyboard keyboard) =>
        IsPressed(keyboard, PlayerBinding.MoveRight);

    internal static bool PreviousTool(Keyboard keyboard) =>
        WasPressedThisFrame(keyboard, PlayerBinding.PreviousTool);

    internal static bool NextTool(Keyboard keyboard) =>
        WasPressedThisFrame(keyboard, PlayerBinding.NextTool);

    internal static bool Utility(Keyboard keyboard) =>
        WasPressedThisFrame(keyboard, PlayerBinding.Utility);

    internal static bool ToggleToolbar(Keyboard keyboard) =>
        WasPressedThisFrame(keyboard, PlayerBinding.ToggleToolbar);

    private static bool IsPressed(
        Keyboard keyboard,
        PlayerBinding binding)
    {
        InputKey key = Get(binding);
        return key != InputKey.None && keyboard[key].isPressed;
    }

    private static bool WasPressedThisFrame(
        Keyboard keyboard,
        PlayerBinding binding)
    {
        InputKey key = Get(binding);
        return key != InputKey.None &&
               keyboard[key].wasPressedThisFrame;
    }

    private static void Save(PlayerBinding binding, InputKey key)
    {
        if (Entries.TryGetValue(
                binding,
                out ConfigEntry<string>? entry))
        {
            entry.Value = key.ToString();
        }
    }

    private static bool TryReadLegacy(
        PlayerBinding binding,
        out InputKey key)
    {
        string preference = PreferencePrefix + binding;
        if (!PlayerPrefs.HasKey(preference))
        {
            key = default;
            return false;
        }

        return Enum.TryParse(
            PlayerPrefs.GetString(preference, string.Empty),
            ignoreCase: true,
            out key);
    }

    private static void ValidateEntry(
        PlayerBinding binding,
        ConfigEntry<string> entry)
    {
        if (Enum.TryParse(
                entry.Value,
                ignoreCase: true,
                out InputKey key))
        {
            entry.Value = key.ToString();
            return;
        }

        string invalidValue = entry.Value;
        entry.Value = Defaults[binding].ToString();
        Plugin.Log.LogWarning(
            $"Invalid keybinding '{binding} = {invalidValue}' was reset " +
            "to its default.");
    }

    private static bool IsEssentialMovement(PlayerBinding binding)
    {
        return binding is
            PlayerBinding.MoveForward or
            PlayerBinding.MoveBackward or
            PlayerBinding.MoveLeft or
            PlayerBinding.MoveRight;
    }

    private static string GetDescription(PlayerBinding binding)
    {
        return
            $"{GetActionLabel(binding)} binding. Use a Unity Input System " +
            "key name or None to leave it unbound.";
    }
}

[HarmonyPatch(typeof(BrokenPlayer), "HandleMovement")]
internal static class BrokenPlayerKeybindingsPatch
{
    private static readonly IReadOnlyDictionary<MethodInfo, MethodInfo>
        GetterReplacements = BuildReplacements();

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReplaceHardcodedKeys(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> patched = instructions.ToList();
        for (int index = 0; index < patched.Count; index++)
        {
            CodeInstruction instruction = patched[index];
            if (instruction.opcode == OpCodes.Ldstr &&
                instruction.operand is string prompt &&
                TryGetPromptReplacement(prompt, out MethodInfo? promptGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = promptGetter;
                continue;
            }

            if (index < patched.Count - 1 &&
                instruction.operand is MethodInfo called &&
                GetterReplacements.TryGetValue(
                    called,
                    out MethodInfo? replacement) &&
                patched[index + 1].operand is MethodInfo stateGetter &&
                IsButtonStateGetter(stateGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                patched[index + 1].opcode = OpCodes.Nop;
                patched[index + 1].operand = null;
            }
        }

        return patched;
    }

    private static bool TryGetPromptReplacement(
        string prompt,
        out MethodInfo? replacement)
    {
        string? methodName = prompt switch
        {
            "Q" => nameof(PlayerKeybindings.GetPreviousToolPrompt),
            "E" => nameof(PlayerKeybindings.GetNextToolPrompt),
            "F: CHANGE LENS" => nameof(PlayerKeybindings.GetLensPrompt),
            "E: CHANGE FILTER" => nameof(PlayerKeybindings.GetFilterPrompt),
            _ => null
        };
        replacement = methodName == null
            ? null
            : AccessTools.Method(typeof(PlayerKeybindings), methodName);
        return replacement != null;
    }

    private static bool IsButtonStateGetter(MethodInfo method)
    {
        return method.DeclaringType == typeof(ButtonControl) &&
               method.Name is
                   "get_isPressed" or
                   "get_wasPressedThisFrame";
    }

    private static IReadOnlyDictionary<MethodInfo, MethodInfo>
        BuildReplacements()
    {
        return new Dictionary<MethodInfo, MethodInfo>
        {
            [AccessTools.PropertyGetter(typeof(Keyboard), "wKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Forward)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "sKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Backward)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "aKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Left)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "dKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Right)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "qKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.PreviousTool)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "eKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.NextTool)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "fKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Utility)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "downArrowKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.ToggleToolbar))
        };
    }
}

/// <summary>
/// ToolPromptChanger refreshes its helper labels every half second using
/// private retail defaults. Keep that refresh path synchronized with the
/// remappable bindings as well.
/// </summary>
[HarmonyPatch(typeof(ToolPromptChanger), "ChangeUI")]
internal static class ToolPromptChangerKeybindingsPatch
{
    [HarmonyPrefix]
    private static void ApplyCurrentBindings(
        ref string ___leftKBBH,
        ref string ___rightKBBH)
    {
        ___leftKBBH =
            PlayerKeybindings.GetLabel(PlayerBinding.PreviousTool);
        ___rightKBBH =
            PlayerKeybindings.GetLabel(PlayerBinding.NextTool);
    }
}
