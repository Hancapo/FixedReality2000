using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FixedReality2000.Patches;

[HarmonyPatch(typeof(NewOptionsScript), "ChangeQuality")]
internal static class GraphicsQualityLightingPatch
{
    [HarmonyPrefix]
    private static bool ApplyCorrectQualityLevel(int __0)
    {
        GraphicsQualityLighting.Apply(__0);
        return false;
    }
}

internal static class GraphicsQualityLighting
{
    private const float ShadowAmbientMultiplier = 0.7f;

    private static RenderPipelineAsset? _shadowCapablePipeline;
    private static readonly Dictionary<int, float> OriginalShadowStrengths =
        new();
    private static int _shadowStateSceneHandle = int.MinValue;
    private static float _originalAmbientIntensity;
    private static bool _ambientIntensityCaptured;

    private static readonly MethodInfo SetMainLightRenderingMode =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.mainLightRenderingMode));

    private static readonly MethodInfo SetAdditionalLightsRenderingMode =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.additionalLightsRenderingMode));

    private static readonly MethodInfo SetSupportsMainLightShadows =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.supportsMainLightShadows));

    private static readonly MethodInfo SetMainLightShadowmapResolution =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.mainLightShadowmapResolution));

    private static readonly MethodInfo SetSupportsAdditionalLightShadows =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.supportsAdditionalLightShadows));

    private static readonly MethodInfo SetAdditionalLightsShadowmapResolution =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.additionalLightsShadowmapResolution));

    private static readonly MethodInfo SetSupportsSoftShadows =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.supportsSoftShadows));

    private static readonly MethodInfo SetAdditionalShadowTierLow =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.additionalLightsShadowResolutionTierLow));

    private static readonly MethodInfo SetAdditionalShadowTierMedium =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.additionalLightsShadowResolutionTierMedium));

    private static readonly MethodInfo SetAdditionalShadowTierHigh =
        AccessTools.PropertySetter(
            typeof(UniversalRenderPipelineAsset),
            nameof(UniversalRenderPipelineAsset.additionalLightsShadowResolutionTierHigh));

    private static readonly FieldInfo AdditionalLightShadowTier =
        AccessTools.Field(
            typeof(UniversalAdditionalLightData),
            "m_AdditionalLightsShadowResolutionTier");

    internal static void ApplySaved()
    {
        int fallbackQuality = QualitySettings.GetQualityLevel() switch
        {
            >= 3 => 1,
            2 => 2,
            _ => 3
        };
        int savedQuality =
            PlayerPrefs.GetInt("FixedReality2000.Quality", fallbackQuality);
        Apply(savedQuality);
    }

    internal static void Apply(int uiQualityIndex)
    {
        int quality = Mathf.Clamp(uiQualityIndex, 0, 3);
        int baseQualityLevel = quality switch
        {
            0 => 3,
            1 => 3,
            2 => 2,
            _ => 1
        };

        // Changing the quality level while the animated DMT splash is creating
        // MeshTrail copies reloads their render pipeline mid-animation. Store
        // the user's choice now; the normal scene-load coroutine applies it
        // after the splash has finished.
        if (Plugin.IsDmtSplashActive())
        {
            PlayerPrefs.SetInt("FixedReality2000.Quality", quality);
            PlayerPrefs.SetInt("qualitySetting", baseQualityLevel);
            PlayerPrefs.Save();

            Plugin.Log.LogInfo(
                $"Graphics quality {quality} stored while the DMT splash is " +
                "active; rendering changes were deferred.");
            return;
        }

        // The game's Medium URP asset is the only original preset with a
        // correctly authored shadow pipeline. Keep the non-render-pipeline
        // settings from High, but use that known-good pipeline as the base
        // for High and Very High before increasing their shadow budgets.
        if (quality < 3 && _shadowCapablePipeline == null)
        {
            QualitySettings.SetQualityLevel(2, applyExpensiveChanges: true);
            _shadowCapablePipeline = QualitySettings.renderPipeline;
        }

        QualitySettings.SetQualityLevel(baseQualityLevel, applyExpensiveChanges: true);
        if (quality <= 1 && _shadowCapablePipeline != null)
        {
            QualitySettings.renderPipeline = _shadowCapablePipeline;
        }

        bool veryHigh = quality == 0;
        bool shadowsEnabled = quality < 3;
        int shadowResolution = quality switch
        {
            0 => 8192,
            1 => 4096,
            _ => 2048
        };
        float shadowDistance = quality switch
        {
            0 => 250f,
            1 => 150f,
            _ => 100f
        };
        int shadowCascades = quality == 0 ? 4 : 2;

        UniversalRenderPipelineAsset? asset =
            QualitySettings.renderPipeline as UniversalRenderPipelineAsset ??
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset != null)
        {
            SetMainLightRenderingMode.Invoke(
                asset,
                new object[] { LightRenderingMode.PerPixel });
            SetAdditionalLightsRenderingMode.Invoke(
                asset,
                new object[]
                {
                    quality < 3
                        ? LightRenderingMode.PerPixel
                        : LightRenderingMode.PerVertex
                });

            asset.maxAdditionalLightsCount =
                quality switch
                {
                    0 => 8,
                    1 => 4,
                    _ => 2
                };
            SetSupportsMainLightShadows.Invoke(
                asset,
                new object[] { shadowsEnabled });
            SetSupportsAdditionalLightShadows.Invoke(
                asset,
                new object[] { shadowsEnabled });
            SetSupportsSoftShadows.Invoke(
                asset,
                new object[] { shadowsEnabled });
            if (shadowsEnabled)
            {
                SetMainLightShadowmapResolution.Invoke(
                    asset,
                    new object[] { shadowResolution });
                SetAdditionalLightsShadowmapResolution.Invoke(
                    asset,
                    new object[] { shadowResolution });
                SetAdditionalShadowTierLow.Invoke(
                    asset,
                    new object[] { Mathf.Max(512, shadowResolution / 4) });
                SetAdditionalShadowTierMedium.Invoke(
                    asset,
                    new object[] { Mathf.Max(1024, shadowResolution / 2) });
                SetAdditionalShadowTierHigh.Invoke(
                    asset,
                    new object[] { shadowResolution });

                asset.shadowDistance = shadowDistance;
                asset.shadowCascadeCount = shadowCascades;
            }
        }

        QualitySettings.shadows =
            shadowsEnabled
                ? UnityEngine.ShadowQuality.All
                : UnityEngine.ShadowQuality.Disable;
        if (shadowsEnabled)
        {
            QualitySettings.shadowResolution =
                veryHigh
                    ? UnityEngine.ShadowResolution.VeryHigh
                    : quality == 1
                        ? UnityEngine.ShadowResolution.High
                        : UnityEngine.ShadowResolution.Medium;
            QualitySettings.shadowDistance = shadowDistance;
            QualitySettings.shadowCascades = shadowCascades;
        }

        UniversalAdditionalCameraData[] cameras =
            Object.FindObjectsByType<UniversalAdditionalCameraData>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (UniversalAdditionalCameraData cameraData in cameras)
        {
            cameraData.renderShadows = shadowsEnabled;
        }

        Light[] lights =
            Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        PrepareShadowDarknessState();
        RenderSettings.ambientIntensity =
            shadowsEnabled
                ? _originalAmbientIntensity * ShadowAmbientMultiplier
                : _originalAmbientIntensity;
        foreach (Light light in lights)
        {
            if (!light.enabled ||
                !light.gameObject.activeInHierarchy ||
                light.type != LightType.Directional)
            {
                continue;
            }

            if (!OriginalShadowStrengths.TryGetValue(
                    light.GetInstanceID(),
                    out float originalShadowStrength))
            {
                originalShadowStrength = light.shadowStrength;
                OriginalShadowStrengths.Add(
                    light.GetInstanceID(),
                    originalShadowStrength);
            }

            light.shadows =
                shadowsEnabled ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength =
                shadowsEnabled
                    ? Mathf.Clamp01(originalShadowStrength * 1.3f)
                    : originalShadowStrength;
            if (shadowsEnabled)
            {
                light.shadowCustomResolution = shadowResolution;
                light.shadowResolution =
                    veryHigh
                        ? UnityEngine.Rendering.LightShadowResolution.VeryHigh
                        : quality == 1
                            ? UnityEngine.Rendering.LightShadowResolution.High
                            : UnityEngine.Rendering.LightShadowResolution.Medium;

                UniversalAdditionalLightData additionalData =
                    light.GetComponent<UniversalAdditionalLightData>();
                if (additionalData != null)
                {
                    AdditionalLightShadowTier.SetValue(
                        additionalData,
                        UniversalAdditionalLightData
                            .AdditionalLightsShadowResolutionTierCustom);
                }
            }
        }

        PlayerPrefs.SetInt("FixedReality2000.Quality", quality);
        PlayerPrefs.SetInt("qualitySetting", baseQualityLevel);
        PlayerPrefs.Save();
        Plugin.ApplyConfiguredTextureFiltering();
        Plugin.ApplyConfiguredMsaa();
        Plugin.ApplyConfiguredPostProcessAa();
        if (Plugin.EnableRenderBatchingOptimizations.Value)
        {
            RenderBatchingOptimizations.Apply();
        }

        Plugin.Log.LogInfo(
            $"Graphics quality {quality} applied using base level {baseQualityLevel}: " +
            $"main light on, shadows {(shadowsEnabled ? $"{shadowResolution}px, 30% darker" : "off")}.");
    }

    internal static void RestoreOriginalShadowDarkness()
    {
        if (!_ambientIntensityCaptured ||
            SceneManager.GetActiveScene().handle != _shadowStateSceneHandle)
        {
            return;
        }

        RenderSettings.ambientIntensity = _originalAmbientIntensity;
        Light[] lights =
            Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (Light light in lights)
        {
            if (OriginalShadowStrengths.TryGetValue(
                    light.GetInstanceID(),
                    out float originalShadowStrength))
            {
                light.shadowStrength = originalShadowStrength;
            }
        }

        OriginalShadowStrengths.Clear();
        _ambientIntensityCaptured = false;
        _shadowStateSceneHandle = int.MinValue;
    }

    private static void PrepareShadowDarknessState()
    {
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (_ambientIntensityCaptured &&
            _shadowStateSceneHandle == sceneHandle)
        {
            return;
        }

        OriginalShadowStrengths.Clear();
        _shadowStateSceneHandle = sceneHandle;
        _originalAmbientIntensity = RenderSettings.ambientIntensity;
        _ambientIntensityCaptured = true;
    }

}
