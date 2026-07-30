using BepInEx;
using UnityEngine;

namespace FixedReality2000;

public sealed partial class Plugin : BaseUnityPlugin
{
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
}
