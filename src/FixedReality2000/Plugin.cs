using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using FixedReality2000.Patches;

namespace FixedReality2000;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[DefaultExecutionOrder(10000)]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "FixedReality2000";
    public const string PluginName = "Fixed Reality 2000";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> ReloadConfigHotkey { get; private set; } = null!;
    internal static ConfigEntry<bool> ShowReloadNotification { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableRenderBatchingOptimizations { get; private set; } = null!;
    internal static ConfigEntry<bool> OptimizePerFrameLookups { get; private set; } = null!;
    internal static ConfigEntry<bool> FixLowQualityFpsCap { get; private set; } = null!;
    internal static int TargetFrameRate { get; private set; } = -1;
    internal static bool VSyncEnabled { get; private set; }
    internal static bool ForceNearestTextureFiltering { get; private set; }
    internal static int MsaaSampleCount { get; private set; } = 4;
    internal static int PostProcessAaMode { get; private set; } = 1;
    internal static ConfigEntry<bool> EnableSprint { get; private set; } = null!;
    internal static ConfigEntry<float> SprintMultiplier { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableHeadBobbing { get; private set; } = null!;
    internal static ConfigEntry<float> HeadBobAmplitude { get; private set; } = null!;
    internal static ConfigEntry<float> HeadBobFrequency { get; private set; } = null!;

    private Harmony? _harmony;
    private string? _notificationMessage;
    private float _notificationExpiresAt;
    private GUIStyle? _notificationStyle;
    private PlayerMotionEffects? _playerMotionEffects;
    private ViewmodelFovCompensation? _viewmodelFovCompensation;
    private UltrawideSupport? _ultrawideSupport;
    private RuntimeInspectorBridge? _runtimeInspectorBridge;
    private ScreenModeApplier? _screenModeApplier;

    private static Plugin? Instance { get; set; }

    private const string TargetFrameRatePreference =
        "FixedReality2000.TargetFrameRate";
    private const string VSyncPreference =
        "FixedReality2000.VSync";
    private const string TextureFilteringPreference =
        "FixedReality2000.NearestTextureFiltering";
    private const string MsaaPreference =
        "FixedReality2000.MsaaSampleCount";
    private const string PostProcessAaPreference =
        "FixedReality2000.PostProcessAaMode";

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        LoadInGameGraphicsPreferences();

        ReloadConfigHotkey = Config.Bind(
            "General",
            "ReloadConfigHotkey",
            new KeyboardShortcut(KeyCode.F5),
            "Reloads this configuration file and applies its settings while the game is running.");

        ShowReloadNotification = Config.Bind(
            "General",
            "ShowReloadNotification",
            true,
            "Shows an on-screen confirmation after attempting to reload the configuration.");

        EnableRenderBatchingOptimizations = Config.Bind(
            "Performance",
            "EnableRenderBatchingOptimizations",
            true,
            "Enables Unity's SRP Batcher and URP dynamic batching without replacing renderers or changing materials.");

        OptimizePerFrameLookups = Config.Bind(
            "Performance",
            "OptimizePerFrameLookups",
            true,
            "Caches repeated Camera.main and GameObject.Find calls made by selected game scripts.");

        FixLowQualityFpsCap = Config.Bind(
            "Fixes",
            "FixLowQualityFpsCap",
            true,
            "Prevents BrokenPlayer.Prepare from forcing Application.targetFrameRate to 60.");

        EnableSprint = Config.Bind(
            "Movement",
            "EnableSprint",
            true,
            "Enables sprinting while either Shift key is held.");

        SprintMultiplier = Config.Bind(
            "Movement",
            "SprintMultiplier",
            1.65f,
            new ConfigDescription(
                "Horizontal movement speed multiplier while sprinting.",
                new AcceptableValueRange<float>(1f, 4f)));

        EnableHeadBobbing = Config.Bind(
            "Movement",
            "EnableHeadBobbing",
            true,
            "Adds subtle camera head bob while the player is walking or sprinting.");

        HeadBobAmplitude = Config.Bind(
            "Movement",
            "HeadBobAmplitude",
            0.03f,
            new ConfigDescription(
                "Maximum vertical head-bob displacement in local Unity units.",
                new AcceptableValueRange<float>(0f, 0.2f)));

        HeadBobFrequency = Config.Bind(
            "Movement",
            "HeadBobFrequency",
            9f,
            new ConfigDescription(
                "Head-bob cycle speed while walking. Sprinting is slightly faster.",
                new AcceptableValueRange<float>(1f, 20f)));

        RemoveObsoleteConfigurationEntries();

        PlayerKeybindings.Initialize();
        ControllerSettings.Initialize();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        _playerMotionEffects = new PlayerMotionEffects();
        _viewmodelFovCompensation = new ViewmodelFovCompensation();
        _ultrawideSupport = new UltrawideSupport();
        _screenModeApplier = new ScreenModeApplier(this);
        _runtimeInspectorBridge = new RuntimeInspectorBridge();
        _runtimeInspectorBridge.Start();

        SceneManager.sceneLoaded += OnSceneLoaded;
        StartCoroutine(ApplyGraphicsSettingsAfterLoad());

        Log.LogInfo(
            $"{PluginName} {PluginVersion} loaded. Press {ReloadConfigHotkey.Value} to reload the config.");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        TextureFiltering.RestoreOriginalFiltering();
        TextureFiltering.RestoreOriginalAnisotropicFiltering();
        GraphicsQualityLighting.RestoreOriginalShadowDarkness();
        RenderBatchingOptimizations.RestoreOriginalSettings();
        _playerMotionEffects?.Dispose();
        _viewmodelFovCompensation?.Dispose();
        _ultrawideSupport?.Dispose();
        _screenModeApplier?.Dispose();
        _runtimeInspectorBridge?.Dispose();
        ControllerSettings.StopRumble();
        _harmony?.UnpatchSelf();
        Instance = null;
    }

    private void Update()
    {
        _runtimeInspectorBridge?.Tick();
        _ultrawideSupport?.Tick();
        ControllerSettings.TickRumble();

        if (ReloadConfigHotkey.Value.IsDown())
        {
            Log.LogInfo($"Reload hotkey pressed: {ReloadConfigHotkey.Value}.");
            ReloadConfiguration();
        }

    }

    private void LateUpdate()
    {
        _playerMotionEffects?.LateTick();
        _viewmodelFovCompensation?.LateTick();
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_notificationMessage) ||
            Time.realtimeSinceStartup >= _notificationExpiresAt)
        {
            return;
        }

        _notificationStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };

        const float width = 420f;
        const float height = 42f;
        Rect area = new Rect((Screen.width - width) / 2f, 28f, width, height);
        GUI.Box(area, _notificationMessage, _notificationStyle);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _playerMotionEffects?.OnSceneLoaded();
        _viewmodelFovCompensation?.OnSceneLoaded();
        _ultrawideSupport?.OnSceneLoaded();
        SceneObjectCache.Clear();
        StartCoroutine(ApplyGraphicsSettingsAfterLoad());
    }

    private static IEnumerator ApplyGraphicsSettingsAfterLoad()
    {
        // Wait until Unity has finished creating and binding the scene resources.
        yield return new WaitForEndOfFrame();

        ApplyConfiguredFramePacing();

        // The DMT intro creates fading MeshTrail copies. Reloading the render
        // pipeline while those temporary meshes exist corrupts their transforms
        // and produces giant overexposed polygons across the screen.
        if (string.Equals(
                SceneManager.GetActiveScene().name,
                "00_room",
                StringComparison.OrdinalIgnoreCase))
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (IsDmtSplashActive() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (IsDmtSplashActive())
            {
                Log.LogWarning(
                    "The DMT splash remained active for 30 seconds; graphics " +
                    "overrides were skipped to preserve its mesh trails.");
                yield break;
            }
        }

        GraphicsQualityLighting.ApplySaved();

        ApplyConfiguredTextureFiltering();

        ApplyConfiguredMsaa();

        ApplyConfiguredPostProcessAa();

    }

    internal static bool IsDmtSplashActive()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().name,
                "00_room",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!string.Equals(
                    root.name,
                    "SPLASH",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Transform logo = root.transform.Find("Logo");
            return root.activeInHierarchy &&
                   logo != null &&
                   logo.gameObject.activeInHierarchy;
        }

        return false;
    }

    private void ReloadConfiguration()
    {
        try
        {
            Config.Reload();
            PlayerKeybindings.Reload();
            ControllerSettings.Reload();

            ApplyConfiguredTextureFiltering();

            ApplyConfiguredMsaa();

            ApplyConfiguredPostProcessAa();

            if (EnableRenderBatchingOptimizations.Value)
            {
                if (string.Equals(
                        SceneManager.GetActiveScene().name,
                        "00_room",
                        StringComparison.OrdinalIgnoreCase))
                {
                    RenderBatchingOptimizations.RestoreOriginalSettings();
                }
                else
                {
                    RenderBatchingOptimizations.Apply();
                }
            }
            else
            {
                RenderBatchingOptimizations.RestoreOriginalSettings();
            }

            SceneObjectCache.Clear();
            _playerMotionEffects?.ApplyConfiguration();

            if (FixLowQualityFpsCap.Value)
            {
                ApplyConfiguredFramePacing();
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
            }

            Log.LogInfo("Configuration reloaded and applied.");
            ShowNotification("Fixed Reality 2000: config reloaded");
        }
        catch (Exception exception)
        {
            Log.LogError($"Unable to reload configuration: {exception}");
            ShowNotification("Fixed Reality 2000: config reload failed");
        }
    }

    private void ShowNotification(string message)
    {
        if (!ShowReloadNotification.Value)
        {
            return;
        }

        _notificationMessage = message;
        _notificationExpiresAt = Time.realtimeSinceStartup + 2.5f;
    }

    private void RemoveObsoleteConfigurationEntries()
    {
        const string hotkeyName = "ToggleSecondaryCameraHotkey";
        const string toggleName = "DisableUnusedStoreCamera";
        bool saveOnConfigSet = Config.SaveOnConfigSet;
        try
        {
            if (!File.Exists(Config.ConfigFilePath))
            {
                return;
            }

            string contents = File.ReadAllText(Config.ConfigFilePath);
            if (!contents.Contains(hotkeyName, StringComparison.Ordinal) &&
                !contents.Contains(toggleName, StringComparison.Ordinal))
            {
                return;
            }

            Config.SaveOnConfigSet = false;
            Config.Bind(
                "Performance",
                hotkeyName,
                new KeyboardShortcut(KeyCode.F8),
                "Obsolete setting pending removal.");
            Config.Bind(
                "Performance",
                toggleName,
                false,
                "Obsolete setting pending removal.");
            Config.Remove(new ConfigDefinition("Performance", hotkeyName));
            Config.Remove(new ConfigDefinition("Performance", toggleName));
            Config.Save();
            Log.LogInfo("Removed obsolete store-camera settings from the configuration file.");
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Could not remove obsolete store-camera settings: {exception.Message}");
        }
        finally
        {
            Config.SaveOnConfigSet = saveOnConfigSet;
        }
    }
}
