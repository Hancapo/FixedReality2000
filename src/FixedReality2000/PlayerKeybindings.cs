using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
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

internal static partial class PlayerKeybindings
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
}
