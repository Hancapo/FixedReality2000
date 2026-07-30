using System;
using BepInEx.Configuration;
using UnityEngine;
using InputKey = UnityEngine.InputSystem.Key;

namespace FixedReality2000;

internal static partial class PlayerKeybindings
{
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
