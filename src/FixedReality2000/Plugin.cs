using System;
using System.Collections;
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
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "FixedReality2000";
    public const string PluginName = "Fixed Reality 2000";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> ReloadConfigHotkey { get; private set; } = null!;
    internal static ConfigEntry<bool> ShowReloadNotification { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> ToggleSecondaryCameraHotkey { get; private set; } = null!;
    internal static ConfigEntry<bool> DisableUnusedStoreCamera { get; private set; } = null!;
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
    private StoreCameraOptimization? _storeCameraOptimization;
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

    internal static void RecordPlayerMovement(
        com.DMT.BrokenReality2000.BrokenPlayer player,
        float horizontalDistance,
        bool sprinting)
    {
        Instance?._playerMotionEffects?.RecordMovement(player, horizontalDistance, sprinting);
    }

    internal static void SetTextureFilteringFromUi(bool nearest)
    {
        ForceNearestTextureFiltering = nearest;
        PlayerPrefs.SetInt(TextureFilteringPreference, nearest ? 1 : 0);
        PlayerPrefs.Save();
        ApplyConfiguredTextureFiltering();
    }

    internal static void ApplyConfiguredTextureFiltering()
    {
        if (ForceNearestTextureFiltering)
        {
            TextureFiltering.ApplyNearestToLoadedTextures();
        }
        else
        {
            TextureFiltering.ApplyOriginalToLoadedTextures();
        }
    }

    internal static void SetMsaaFromUi(int sampleCount)
    {
        MsaaSampleCount = NormalizeMsaa(sampleCount);
        PlayerPrefs.SetInt(MsaaPreference, MsaaSampleCount);
        PlayerPrefs.Save();
        ApplyConfiguredMsaa();
    }

    internal static void ApplyConfiguredMsaa()
    {
        int samples = NormalizeMsaa(MsaaSampleCount);
        QualitySettings.antiAliasing = samples;

        UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset? asset =
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (asset != null)
        {
            asset.msaaSampleCount = samples == 0 ? 1 : samples;
        }

        Camera[] cameras =
            UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            camera.allowMSAA = samples > 0;
        }

        Log.LogInfo($"MSAA applied: {(samples == 0 ? "off" : $"{samples}x")}.");
    }

    internal static void SetPostProcessAaFromUi(int mode)
    {
        PostProcessAaMode = NormalizePostProcessAa(mode);
        PlayerPrefs.SetInt(PostProcessAaPreference, PostProcessAaMode);
        PlayerPrefs.Save();
        ApplyConfiguredPostProcessAa();
    }

    internal static void ApplyConfiguredPostProcessAa()
    {
        int normalizedMode = NormalizePostProcessAa(PostProcessAaMode);
        var mode =
            (UnityEngine.Rendering.Universal.AntialiasingMode)normalizedMode;
        UnityEngine.Rendering.Universal.UniversalAdditionalCameraData[] cameras =
            UnityEngine.Object.FindObjectsByType<
                UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
        int applied = 0;
        foreach (
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData
            in cameras)
        {
            if (cameraData.renderType !=
                UnityEngine.Rendering.Universal.CameraRenderType.Base)
            {
                continue;
            }

            cameraData.antialiasing = mode;
            if (mode ==
                UnityEngine.Rendering.Universal.AntialiasingMode
                    .SubpixelMorphologicalAntiAliasing)
            {
                cameraData.antialiasingQuality =
                    UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            }

            applied++;
        }

        Log.LogInfo(
            $"Post-process AA applied: {mode} on {applied} base cameras.");
    }

    private static int NormalizeMsaa(int sampleCount)
    {
        return sampleCount switch
        {
            2 => 2,
            4 => 4,
            8 => 8,
            _ => 0
        };
    }

    private static int NormalizePostProcessAa(int mode)
    {
        return mode is >= 0 and <= 2 ? mode : 1;
    }

    internal static void SetFrameRateFromUi(int targetFrameRate)
    {
        TargetFrameRate = NormalizeFrameRate(targetFrameRate);
        PlayerPrefs.SetInt(TargetFrameRatePreference, TargetFrameRate);
        PlayerPrefs.Save();
        ApplyConfiguredFramePacing();
    }

    internal static void SetVSyncFromUi(bool enabled)
    {
        VSyncEnabled = enabled;
        PlayerPrefs.SetInt(VSyncPreference, enabled ? 1 : 0);
        PlayerPrefs.Save();
        ApplyConfiguredFramePacing();
    }

    internal static void ApplyConfiguredFramePacing()
    {
        QualitySettings.vSyncCount = VSyncEnabled ? 1 : 0;
        Application.targetFrameRate =
            VSyncEnabled ? -1 : TargetFrameRate;
        Log.LogInfo(
            VSyncEnabled
                ? "Frame pacing applied: V-Sync on."
                : TargetFrameRate < 0
                    ? "Frame pacing applied: V-Sync off, unlimited."
                    : $"Frame pacing applied: V-Sync off, {TargetFrameRate} FPS.");
    }

    internal static void ApplyScreenMode(
        int width,
        int height,
        FullScreenMode mode,
        int refreshRate,
        int monitorIndex = -1)
    {
        Instance?._screenModeApplier?.Request(
            width,
            height,
            mode,
            refreshRate,
            monitorIndex);
    }

    private static int NormalizeFrameRate(int targetFrameRate)
    {
        return targetFrameRate is 60 or 120 or 144 or 165 or 240 or 360
            ? targetFrameRate
            : -1;
    }

    private static void LoadInGameGraphicsPreferences()
    {
        TargetFrameRate = NormalizeFrameRate(
            PlayerPrefs.GetInt(TargetFrameRatePreference, -1));
        VSyncEnabled =
            PlayerPrefs.GetInt(VSyncPreference, 0) != 0;
        ForceNearestTextureFiltering =
            PlayerPrefs.GetInt(TextureFilteringPreference, 0) != 0;
        MsaaSampleCount = NormalizeMsaa(
            PlayerPrefs.GetInt(MsaaPreference, 4));
        PostProcessAaMode = NormalizePostProcessAa(
            PlayerPrefs.GetInt(PostProcessAaPreference, 1));
    }

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

        ToggleSecondaryCameraHotkey = Config.Bind(
            "Performance",
            "ToggleSecondaryCameraHotkey",
            new KeyboardShortcut(KeyCode.F8),
            "Restores or disables the store camera for the current scene.");

        DisableUnusedStoreCamera = Config.Bind(
            "Performance",
            "DisableUnusedStoreCamera",
            true,
            "Disables player_storecamera until its gameplay mechanic is needed. Press the camera hotkey to restore it for the current scene.");

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

        PlayerKeybindings.Initialize();
        ControllerSettings.Initialize();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        _storeCameraOptimization = new StoreCameraOptimization();
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
        _storeCameraOptimization?.Dispose();
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

        if (ToggleSecondaryCameraHotkey.Value.IsDown())
        {
            string result = _storeCameraOptimization?.Toggle()
                ?? "Store camera optimization is not initialized";
            ShowNotification(result);
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
        _storeCameraOptimization?.OnSceneLoaded();
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

        Instance?._storeCameraOptimization?.ApplyConfiguration();
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
            _storeCameraOptimization?.ReloadConfiguration();
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
}
