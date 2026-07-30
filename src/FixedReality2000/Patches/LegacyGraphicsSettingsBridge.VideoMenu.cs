using System;
using TMPro;
using UnityEngine;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
    private void BuildInjectedControls(Transform root)
    {
        if (_resolutionDropdown == null)
        {
            return;
        }

        Transform videoContainer = _resolutionDropdown.transform.parent;
        Transform? resolutionLabel = FindLabel(videoContainer, "RESOLUTION");
        Transform? qualityLabel =
            FindLabel(videoContainer, "GRAPHICS QUALITY", "GRAPHICSQUALITY");
        Transform? displayLabel =
            FindLabel(videoContainer, "DISPLAY MODE", "DISPLAYMODE");
        TMP_Dropdown? qualityDropdown = _menu == null
            ? null
            : FindDropdown(videoContainer, _menu, "ChangeQuality");
        TMP_Dropdown? displayDropdown = _menu == null
            ? null
            : FindDropdown(videoContainer, _menu, "ChangeResolutionMode");
        if (resolutionLabel == null ||
            qualityLabel == null ||
            displayLabel == null ||
            qualityDropdown == null ||
            displayDropdown == null ||
            _resolutionDropdown == null)
        {
            Plugin.Log.LogWarning(
                "The additional Video options could not be built because one of the " +
                "three original menu rows was not found.");
            return;
        }

        _displayModeDropdown = displayDropdown;
        RefreshDisplayModeControl();
        _displayModeDropdown.onValueChanged.AddListener(
            OnDisplayModeChanged);

        if (videoContainer.Find("FixedReality2000_SubpageDock") != null ||
            videoContainer.Find("FixedReality2000_FpsDropdown") != null)
        {
            return;
        }

        RectTransform resolutionLabelRect = (RectTransform)resolutionLabel;
        RectTransform qualityLabelRect = (RectTransform)qualityLabel;
        RectTransform displayLabelRect = (RectTransform)displayLabel;
        RectTransform resolutionControlRect =
            (RectTransform)_resolutionDropdown.transform;
        RectTransform displayControlRect = (RectTransform)displayDropdown.transform;
        RectTransform qualityControlRect = (RectTransform)qualityDropdown.transform;
        RectTransform videoRect = (RectTransform)videoContainer;

        Vector2 resolutionLabelPosition =
            GetLocalCenter(resolutionLabelRect, videoRect);
        Vector2 qualityLabelPosition =
            GetLocalCenter(qualityLabelRect, videoRect);
        Vector2 displayLabelPosition =
            GetLocalCenter(displayLabelRect, videoRect);
        Vector2 resolutionControlPosition =
            GetLocalCenter(resolutionControlRect, videoRect);
        Vector2 displayControlPosition =
            GetLocalCenter(displayControlRect, videoRect);

        Vector2 rowStep = qualityLabelPosition - resolutionLabelPosition;
        if (Mathf.Abs(rowStep.y) < 20f)
        {
            rowStep = new Vector2(0f, -100f);
        }

        TMP_Text leftLabelReference = resolutionLabel.GetComponent<TMP_Text>();
        TMP_Text rightLabelReference = displayLabel.GetComponent<TMP_Text>();
        int qualityIndex = ConfigureQualityDropdown(qualityDropdown);

        qualityLabelRect.anchoredPosition += rowStep;
        qualityControlRect.anchoredPosition += rowStep;

        CreateMenuLabel(
            leftLabelReference,
            videoContainer,
            "FixedReality2000_AspectLabel",
            "ASPECT\nRATIO",
            resolutionLabelPosition + rowStep);
        CreateMenuLabel(
            rightLabelReference,
            videoContainer,
            "FixedReality2000_FpsLabel",
            "FPS LIMIT",
            displayLabelPosition + rowStep * 3f);
        CreateMenuLabel(
            rightLabelReference,
            videoContainer,
            "FixedReality2000_VsyncLabel",
            "V-SYNC",
            displayLabelPosition + rowStep * 2f);
        CreateMenuLabel(
            rightLabelReference,
            videoContainer,
            "FixedReality2000_TextureLabel",
            "TEXTURE\nFILTER",
            displayLabelPosition + rowStep);
        CreateMenuLabel(
            leftLabelReference,
            videoContainer,
            "FixedReality2000_AaLabel",
            "MSAA",
            resolutionLabelPosition + rowStep * 2f);
        CreateMenuLabel(
            rightLabelReference,
            videoContainer,
            "FixedReality2000_PostAaLabel",
            "POST-PROCESS\nAA",
            displayLabelPosition + rowStep * 2f);
        CreateMenuLabel(
            leftLabelReference,
            videoContainer,
            "FixedReality2000_MonitorLabel",
            "MONITOR",
            resolutionLabelPosition + rowStep * 2f);
        BuildGameFovControl(videoContainer.parent);

        _videoAspectDropdown = CreateDropdown(
            _resolutionDropdown,
            videoContainer,
            "FixedReality2000_AspectDropdown",
            resolutionControlPosition + rowStep,
            UltrawideResolutionTests.AspectRatioLabels);
        _videoAspectDropdown.SetValueWithoutNotify(_selectedAspectIndex);
        _videoAspectDropdown.RefreshShownValue();
        _videoAspectDropdown.onValueChanged.AddListener(OnAspectRatioChanged);

        _monitorDropdown = CreateDropdown(
            _resolutionDropdown,
            videoContainer,
            "FixedReality2000_MonitorDropdown",
            resolutionControlPosition + rowStep * 2f,
            BuildMonitorLabels());
        _monitorDropdown.SetValueWithoutNotify(_selectedMonitorIndex);
        _monitorDropdown.RefreshShownValue();
        ConfigureMonitorDropdownText(_monitorDropdown);
        _monitorDropdown.onValueChanged.AddListener(OnMonitorChanged);

        _videoFpsDropdown = CreateDropdown(
            _resolutionDropdown,
            videoContainer,
            "FixedReality2000_FpsDropdown",
            resolutionControlPosition + rowStep * 3f,
            new[] { "UNLIMITED", "60", "120", "144", "165", "240", "360" });
        int[] frameRates = { -1, 60, 120, 144, 165, 240, 360 };
        int fpsIndex = Array.IndexOf(frameRates, Plugin.TargetFrameRate);
        _videoFpsDropdown.SetValueWithoutNotify(Mathf.Max(0, fpsIndex));
        _videoFpsDropdown.RefreshShownValue();
        _videoFpsDropdown.onValueChanged.AddListener(
            index => Plugin.SetFrameRateFromUi(
                frameRates[Mathf.Clamp(index, 0, frameRates.Length - 1)]));

        _videoVsyncDropdown = CreateDropdown(
            displayDropdown,
            videoContainer,
            "FixedReality2000_VsyncDropdown",
            displayControlPosition + rowStep * 2f,
            new[] { "OFF", "ON" });
        _videoVsyncDropdown.SetValueWithoutNotify(
            Plugin.VSyncEnabled ? 1 : 0);
        _videoVsyncDropdown.RefreshShownValue();
        _videoVsyncDropdown.onValueChanged.AddListener(OnVSyncChanged);
        RefreshFramePacingControls();

        _videoTextureDropdown = CreateDropdown(
            displayDropdown,
            videoContainer,
            "FixedReality2000_TextureDropdown",
            displayControlPosition + rowStep,
            new[] { "NEAREST", "ORIGINAL" });
        _videoTextureDropdown.SetValueWithoutNotify(
            Plugin.ForceNearestTextureFiltering ? 0 : 1);
        _videoTextureDropdown.RefreshShownValue();
        _videoTextureDropdown.onValueChanged.AddListener(
            index => Plugin.SetTextureFilteringFromUi(index == 0));

        _videoAaDropdown = CreateDropdown(
            displayDropdown,
            videoContainer,
            "FixedReality2000_AaDropdown",
            displayControlPosition + rowStep * 2f,
            new[] { "OFF", "2X", "4X", "8X" });
        int[] msaaSamples = { 0, 2, 4, 8 };
        int aaIndex = Array.IndexOf(msaaSamples, Plugin.MsaaSampleCount);
        _videoAaDropdown.SetValueWithoutNotify(Mathf.Max(0, aaIndex));
        _videoAaDropdown.RefreshShownValue();
        _videoAaDropdown.onValueChanged.AddListener(
            index => Plugin.SetMsaaFromUi(
                msaaSamples[Mathf.Clamp(index, 0, msaaSamples.Length - 1)]));

        _videoPostAaDropdown = CreateDropdown(
            displayDropdown,
            videoContainer,
            "FixedReality2000_PostAaDropdown",
            displayControlPosition + rowStep * 2f,
            new[] { "OFF", "FXAA", "SMAA" });
        _videoPostAaDropdown.SetValueWithoutNotify(
            Mathf.Clamp(Plugin.PostProcessAaMode, 0, 2));
        _videoPostAaDropdown.RefreshShownValue();
        _videoPostAaDropdown.onValueChanged.AddListener(
            Plugin.SetPostProcessAaFromUi);

        BuildVideoSubpages(
            videoContainer,
            resolutionLabel,
            _resolutionDropdown.transform,
            displayLabel,
            displayDropdown.transform,
            qualityLabel,
            qualityDropdown.transform,
            videoContainer.Find("FixedReality2000_AspectLabel"),
            _videoAspectDropdown.transform,
            videoContainer.Find("FixedReality2000_MonitorLabel"),
            _monitorDropdown.transform,
            videoContainer.Find("FixedReality2000_FpsLabel"),
            _videoFpsDropdown.transform,
            videoContainer.Find("FixedReality2000_VsyncLabel"),
            _videoVsyncDropdown.transform,
            videoContainer.Find("FixedReality2000_TextureLabel"),
            _videoTextureDropdown.transform,
            videoContainer.Find("FixedReality2000_AaLabel"),
            _videoAaDropdown.transform,
            videoContainer.Find("FixedReality2000_PostAaLabel"),
            _videoPostAaDropdown.transform,
            rowStep);
        BuildControlsTab(videoContainer.parent);

        SliderValueDisplay.AttachToOptionsPanel(
            videoContainer.parent,
            leftLabelReference);
        GraphicsQualityLighting.Apply(qualityIndex);
        RefreshFovControl();
        Plugin.Log.LogInfo(
            $"Injected Video controls using row step {rowStep} and panel " +
            $"'{TransformPathUtility.GetPath(videoContainer)}'; label references " +
            $"'{TransformPathUtility.GetPath(resolutionLabel)}' and " +
            $"'{TransformPathUtility.GetPath(displayLabel)}'.");
    }

}
