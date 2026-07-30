using System;
using BepInEx.Configuration;
using UnityEngine.InputSystem;
using InputKey = UnityEngine.InputSystem.Key;

namespace FixedReality2000;

internal static partial class PlayerKeybindings
{
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

}
