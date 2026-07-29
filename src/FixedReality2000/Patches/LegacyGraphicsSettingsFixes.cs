using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using InputKey = UnityEngine.InputSystem.Key;

namespace FixedReality2000.Patches;

[HarmonyPatch(typeof(NewOptionsScript), "Awake")]
internal static class LegacyGraphicsSettingsAwakePatch
{
    [HarmonyPostfix]
    private static void BuildActualGraphicsMenu(NewOptionsScript __instance)
    {
        LegacyGraphicsSettingsBridge.Attach(__instance);
    }
}

[HarmonyPatch(typeof(NewOptionsScript), "ChangeResolutionWidthHeight")]
internal static class LegacyResolutionSelectionPatch
{
    [HarmonyPrefix]
    private static bool UseDetectedResolution(NewOptionsScript __instance, int resIndex)
    {
        LegacyGraphicsSettingsBridge? bridge =
            __instance.GetComponent<LegacyGraphicsSettingsBridge>();
        return bridge == null || !bridge.ApplyResolution(resIndex);
    }
}

[HarmonyPatch(typeof(NewOptionsScript), "ChangeResolution")]
internal static class LegacyResolutionApplyPatch
{
    [HarmonyPrefix]
    private static bool ApplyDetectedResolution(NewOptionsScript __instance)
    {
        LegacyGraphicsSettingsBridge? bridge =
            __instance.GetComponent<LegacyGraphicsSettingsBridge>();
        return bridge == null || !bridge.ApplyCurrentResolution();
    }
}

internal sealed class LegacyGraphicsSettingsBridge : MonoBehaviour
{
    private const string InjectedFovRowName = "FixedReality2000_FovRow";
    private const string AspectRatioPreference =
        "FixedReality2000.AspectRatioMode";
    private const string ObsoleteAspectRatioPreference =
        "FixedReality2000.AspectRatio";
    private const string MonitorPreference =
        "FixedReality2000.MonitorIndex";

    private static LegacyGraphicsSettingsBridge? _bindingCaptureOwner;
    private static readonly AccessTools.FieldRef<NewOptionsScript, int> ResXField =
        AccessTools.FieldRefAccess<NewOptionsScript, int>("resx");

    private static readonly AccessTools.FieldRef<NewOptionsScript, int> ResYField =
        AccessTools.FieldRefAccess<NewOptionsScript, int>("resy");

    private static readonly AccessTools.FieldRef<NewOptionsScript, int> ResModeField =
        AccessTools.FieldRefAccess<NewOptionsScript, int>("resmode");

    private readonly List<ResolutionChoice> _allChoices = new();
    private readonly List<ResolutionChoice> _choices = new();
    private readonly List<DisplayInfo> _displayLayout = new();

    private NewOptionsScript? _menu;
    private TMP_Dropdown? _resolutionDropdown;
    private Slider? _gameFovSlider;
    private TMP_Dropdown? _videoFpsDropdown;
    private TMP_Dropdown? _videoVsyncDropdown;
    private TMP_Dropdown? _videoTextureDropdown;
    private TMP_Dropdown? _videoAaDropdown;
    private TMP_Dropdown? _videoPostAaDropdown;
    private TMP_Dropdown? _videoAspectDropdown;
    private TMP_Dropdown? _monitorDropdown;
    private TMP_Dropdown? _displayModeDropdown;
    private GameObject[]? _videoSubpages;
    private Button[]? _videoSubpageButtons;
    private GameObject[]? _controlsSubpages;
    private Button[]? _controlsSubpageButtons;
    private readonly Dictionary<PlayerBinding, Button> _bindingButtons = new();
    private readonly Dictionary<ControllerAction, Button>
        _controllerBindingButtons = new();
    private GameObject[]? _gamepadSubpages;
    private Button[]? _gamepadSubpageButtons;
    private Slider? _controllerLookSensitivitySlider;
    private Slider? _controllerMoveDeadzoneSlider;
    private Slider? _controllerLookDeadzoneSlider;
    private Slider? _controllerCursorSpeedSlider;
    private Slider? _controllerTriggerThresholdSlider;
    private Slider? _controllerVibrationIntensitySlider;
    private TMP_Dropdown? _controllerResponseCurveDropdown;
    private TMP_Dropdown? _controllerSprintModeDropdown;
    private TMP_Dropdown? _controllerStickLayoutDropdown;
    private TMP_Dropdown? _controllerInvertXDropdown;
    private TMP_Dropdown? _controllerInvertYDropdown;
    private TMP_Dropdown? _controllerVibrationDropdown;
    private PlayerBinding? _bindingBeingCaptured;
    private ControllerAction? _controllerBindingBeingCaptured;
    private int _bindingCaptureStartFrame;
    private int _selectedAspectIndex;
    private int _selectedMonitorIndex;
    private int _resolutionChoiceOffset;
    private bool _awaitingResolutionSelection;

    internal bool IsCapturingBinding =>
        _bindingBeingCaptured.HasValue ||
        _controllerBindingBeingCaptured.HasValue;

    internal static void Attach(NewOptionsScript menu)
    {
        LegacyGraphicsSettingsBridge bridge =
            menu.GetComponent<LegacyGraphicsSettingsBridge>() ??
            menu.gameObject.AddComponent<LegacyGraphicsSettingsBridge>();
        bridge.Initialize(menu);
        if (menu.GetComponent<ControllerMenuNavigation>() == null)
        {
            menu.gameObject.AddComponent<ControllerMenuNavigation>();
        }
    }

    private void OnEnable()
    {
        RefreshDisplayModeControl();
        RefreshFovControl();
        RefreshControllerUi();
        Plugin.ApplyConfiguredFramePacing();
        RefreshFramePacingControls();
    }

    private void OnDisable()
    {
        CancelBindingCapture();
        CancelControllerBindingCapture();
    }

    private void OnDestroy()
    {
        CancelBindingCapture();
        _displayModeDropdown?.onValueChanged.RemoveListener(
            OnDisplayModeChanged);
        _monitorDropdown?.onValueChanged.RemoveListener(
            OnMonitorChanged);
        _videoVsyncDropdown?.onValueChanged.RemoveListener(
            OnVSyncChanged);
    }

    internal bool ApplyResolution(int index)
    {
        int choiceIndex = index - _resolutionChoiceOffset;
        if (_menu == null ||
            choiceIndex < 0 ||
            choiceIndex >= _choices.Count)
        {
            return _resolutionChoiceOffset > 0;
        }

        ResolutionChoice choice = _choices[choiceIndex];
        ResXField(_menu) = choice.Width;
        ResYField(_menu) = choice.Height;
        PlayerPrefs.SetInt("resx", choice.Width);
        PlayerPrefs.SetInt("resy", choice.Height);
        ApplyChoice(choice);
        return true;
    }

    internal bool ApplyCurrentResolution()
    {
        if (_resolutionDropdown == null)
        {
            return false;
        }

        int choiceIndex =
            _resolutionDropdown.value - _resolutionChoiceOffset;
        return choiceIndex >= 0 &&
               choiceIndex < _choices.Count &&
               ApplyResolution(_resolutionDropdown.value);
    }

    private void Initialize(NewOptionsScript menu)
    {
        if (_menu != null)
        {
            return;
        }

        _menu = menu;
        Transform root = menu.transform.root;
        _resolutionDropdown = FindDropdown(
            root,
            menu,
            "ChangeResolutionWidthHeight");
        if (_resolutionDropdown == null)
        {
            Plugin.Log.LogWarning(
                "The active NewOptionsScript was found, but its resolution dropdown was not.");
            return;
        }

        RefreshDisplayLayout();
        BuildResolutionChoices();
        _selectedAspectIndex = PlayerPrefs.HasKey(AspectRatioPreference)
            ? Mathf.Clamp(
                PlayerPrefs.GetInt(AspectRatioPreference),
                0,
                UltrawideResolutionTests.AspectRatioLabels.Length - 1)
            : 0;
        if (PlayerPrefs.HasKey(ObsoleteAspectRatioPreference))
        {
            PlayerPrefs.DeleteKey(ObsoleteAspectRatioPreference);
            PlayerPrefs.Save();
        }
        ApplyAspectRatioFilter();
        PopulateResolutionDropdown();
        BuildInjectedControls(root);

        Plugin.Log.LogInfo(
            $"Active graphics menu fixed: {_allChoices.Count} detected resolutions; " +
            $"resolution control '{BuildPath(_resolutionDropdown.transform)}'; " +
            $"FOV slider {(_gameFovSlider != null ? "added to Game" : "not found")}.");
    }

    private void BuildResolutionChoices()
    {
        var modes = new Dictionary<(int Width, int Height), HashSet<int>>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            AddMode(
                modes,
                resolution.width,
                resolution.height,
                GetRefreshRate(resolution));
        }

        Resolution current = Screen.currentResolution;
        AddMode(modes, current.width, current.height, GetRefreshRate(current));
        AddMode(modes, Screen.width, Screen.height, GetRefreshRate(current));

        if (TryGetSelectedMonitor(out DisplayInfo selectedMonitor))
        {
            AddMode(
                modes,
                selectedMonitor.width,
                selectedMonitor.height,
                GetRefreshRate(current));
        }

        _allChoices.Clear();
        foreach (var pair in modes)
        {
            _allChoices.Add(
                new ResolutionChoice(
                    pair.Key.Width,
                    pair.Key.Height,
                    pair.Value.OrderByDescending(rate => rate).ToArray()));
        }

        _allChoices.Sort((left, right) =>
        {
            long leftPixels = (long)left.Width * left.Height;
            long rightPixels = (long)right.Width * right.Height;
            int pixels = rightPixels.CompareTo(leftPixels);
            return pixels != 0
                ? pixels
                : right.Width.CompareTo(left.Width);
        });
    }

    private void ApplyAspectRatioFilter()
    {
        if (_selectedAspectIndex == 0)
        {
            _choices.Clear();
            _choices.AddRange(_allChoices);
            return;
        }

        var modes = new Dictionary<(int Width, int Height), HashSet<int>>();
        foreach (ResolutionChoice choice in _allChoices)
        {
            if (UltrawideResolutionTests.MatchesAspectRatio(
                    choice.Width,
                    choice.Height,
                    _selectedAspectIndex))
            {
                AddChoice(modes, choice.Width, choice.Height, choice.RefreshRates);
            }
        }

        if (GetSelectedDisplayMode() == 2)
        {
            AddCalculatedWindowSizes(modes);
        }

        _choices.Clear();
        foreach (var pair in modes)
        {
            _choices.Add(
                new ResolutionChoice(
                    pair.Key.Width,
                    pair.Key.Height,
                    pair.Value.OrderByDescending(rate => rate).ToArray()));
        }

        _choices.Sort((left, right) =>
        {
            long leftPixels = (long)left.Width * left.Height;
            long rightPixels = (long)right.Width * right.Height;
            int pixels = rightPixels.CompareTo(leftPixels);
            return pixels != 0
                ? pixels
                : right.Width.CompareTo(left.Width);
        });
    }

    private void AddCalculatedWindowSizes(
        IDictionary<(int Width, int Height), HashSet<int>> modes)
    {
        bool hasSelectedMonitor =
            TryGetSelectedMonitor(out DisplayInfo selectedMonitor);
        int maximumWidth = hasSelectedMonitor
            ? selectedMonitor.width
            : Screen.currentResolution.width;
        int maximumHeight = hasSelectedMonitor
            ? selectedMonitor.height
            : Screen.currentResolution.height;
        float aspect = UltrawideResolutionTests.GetAspectRatio(
            _selectedAspectIndex);
        int fallbackRate = GetRefreshRate(Screen.currentResolution);

        AddCalculatedFromWidth(
            modes,
            maximumWidth,
            maximumWidth,
            maximumHeight,
            aspect,
            fallbackRate);
        AddCalculatedFromHeight(
            modes,
            maximumHeight,
            maximumWidth,
            maximumHeight,
            aspect,
            fallbackRate);

        foreach (ResolutionChoice source in _allChoices)
        {
            int refreshRate = source.RefreshRates.FirstOrDefault();
            if (refreshRate <= 0)
            {
                refreshRate = fallbackRate;
            }

            AddCalculatedFromWidth(
                modes,
                source.Width,
                maximumWidth,
                maximumHeight,
                aspect,
                refreshRate);
            AddCalculatedFromHeight(
                modes,
                source.Height,
                maximumWidth,
                maximumHeight,
                aspect,
                refreshRate);
        }
    }

    private static void AddCalculatedFromWidth(
        IDictionary<(int Width, int Height), HashSet<int>> modes,
        int width,
        int maximumWidth,
        int maximumHeight,
        float aspect,
        int refreshRate)
    {
        int height = RoundToEven(width / aspect);
        AddCalculatedMode(
            modes,
            width,
            height,
            maximumWidth,
            maximumHeight,
            refreshRate);
    }

    private static void AddCalculatedFromHeight(
        IDictionary<(int Width, int Height), HashSet<int>> modes,
        int height,
        int maximumWidth,
        int maximumHeight,
        float aspect,
        int refreshRate)
    {
        int width = RoundToEven(height * aspect);
        AddCalculatedMode(
            modes,
            width,
            height,
            maximumWidth,
            maximumHeight,
            refreshRate);
    }

    private static void AddCalculatedMode(
        IDictionary<(int Width, int Height), HashSet<int>> modes,
        int width,
        int height,
        int maximumWidth,
        int maximumHeight,
        int refreshRate)
    {
        if (width < 640 ||
            height < 360 ||
            width > maximumWidth ||
            height > maximumHeight)
        {
            return;
        }

        AddChoice(modes, width, height, new[] { refreshRate });
    }

    private static void AddChoice(
        IDictionary<(int Width, int Height), HashSet<int>> modes,
        int width,
        int height,
        IEnumerable<int> refreshRates)
    {
        var key = (width, height);
        if (!modes.TryGetValue(key, out HashSet<int>? rates))
        {
            rates = new HashSet<int>();
            modes.Add(key, rates);
        }

        foreach (int refreshRate in refreshRates)
        {
            if (refreshRate > 0)
            {
                rates.Add(refreshRate);
            }
        }
    }

    private static int RoundToEven(float value)
    {
        int rounded = Mathf.RoundToInt(value);
        return (rounded & 1) == 0 ? rounded : rounded + 1;
    }

    private void PopulateResolutionDropdown()
    {
        if (_resolutionDropdown == null)
        {
            return;
        }

        _resolutionDropdown.ClearOptions();
        if (_choices.Count == 0)
        {
            _resolutionChoiceOffset = 0;
            _resolutionDropdown.AddOptions(
                new List<string> { "NO AVAILABLE RESOLUTIONS" });
            _resolutionDropdown.SetValueWithoutNotify(0);
            _resolutionDropdown.RefreshShownValue();
            _resolutionDropdown.interactable = false;
            return;
        }

        _resolutionDropdown.interactable = true;
        var labels = _choices
            .Select(
                choice =>
                    UltrawideResolutionTests.Format(
                        choice.Width,
                        choice.Height))
            .ToList();
        _resolutionChoiceOffset = _awaitingResolutionSelection ? 1 : 0;
        if (_resolutionChoiceOffset > 0)
        {
            labels.Insert(0, "SELECT RESOLUTION");
        }

        _resolutionDropdown.AddOptions(labels);

        if (_resolutionChoiceOffset > 0)
        {
            _resolutionDropdown.SetValueWithoutNotify(0);
            _resolutionDropdown.RefreshShownValue();
            return;
        }

        int savedWidth = PlayerPrefs.GetInt("resx", Screen.width);
        int savedHeight = PlayerPrefs.GetInt("resy", Screen.height);
        int selected = _choices.FindIndex(
            choice => choice.Width == savedWidth && choice.Height == savedHeight);
        if (selected < 0)
        {
            selected = _choices.FindIndex(
                choice => choice.Width == Screen.width && choice.Height == Screen.height);
        }

        selected = Mathf.Max(0, selected);
        _resolutionDropdown.SetValueWithoutNotify(selected);
        _resolutionDropdown.RefreshShownValue();
        if (_menu != null)
        {
            ResXField(_menu) = _choices[selected].Width;
            ResYField(_menu) = _choices[selected].Height;
        }
    }

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
            $"'{BuildPath(videoContainer)}'; label references " +
            $"'{BuildPath(resolutionLabel)}' and '{BuildPath(displayLabel)}'.");
    }

    private void BuildVideoSubpages(
        Transform videoContainer,
        Transform resolutionLabel,
        Transform resolutionControl,
        Transform displayLabel,
        Transform displayControl,
        Transform qualityLabel,
        Transform qualityControl,
        Transform? aspectLabel,
        Transform aspectControl,
        Transform? monitorLabel,
        Transform monitorControl,
        Transform? fpsLabel,
        Transform fpsControl,
        Transform? vsyncLabel,
        Transform vsyncControl,
        Transform? textureLabel,
        Transform textureControl,
        Transform? aaLabel,
        Transform aaControl,
        Transform? postAaLabel,
        Transform postAaControl,
        Vector2 rowStep)
    {
        Transform? dockTemplate =
            videoContainer.parent.Find("ButtonDock/BG PC");
        if (dockTemplate == null ||
            aspectLabel == null ||
            monitorLabel == null ||
            fpsLabel == null ||
            vsyncLabel == null ||
            textureLabel == null ||
            aaLabel == null ||
            postAaLabel == null)
        {
            Plugin.Log.LogWarning(
                "Video subpages could not be created because their navigation " +
                "template or one of the injected controls was not found.");
            return;
        }

        RectTransform videoRect = (RectTransform)videoContainer;
        RectTransform dock = Instantiate(
            (RectTransform)dockTemplate,
            videoContainer,
            worldPositionStays: false);
        dock.gameObject.name = "FixedReality2000_SubpageDock";
        dock.anchorMin = new Vector2(0.5f, 0.5f);
        dock.anchorMax = new Vector2(0.5f, 0.5f);
        dock.pivot = new Vector2(0.5f, 0.5f);
        dock.sizeDelta = new Vector2(
            Mathf.Min(900f, videoRect.rect.width - 80f),
            dock.sizeDelta.y);
        dock.anchoredPosition = new Vector2(
            0f,
            videoRect.rect.yMin - dock.sizeDelta.y * 0.5f - 4f);
        dock.localScale = Vector3.one;
        dock.localRotation = Quaternion.identity;

        HorizontalLayoutGroup? inheritedLayout =
            dock.GetComponent<HorizontalLayoutGroup>();
        if (inheritedLayout != null)
        {
            inheritedLayout.enabled = false;
        }

        Button[] clonedButtons =
            dock.GetComponentsInChildren<Button>(includeInactive: true);
        if (clonedButtons.Length < 2)
        {
            Destroy(dock.gameObject);
            Plugin.Log.LogWarning(
                "Video subpages could not be created because the cloned dock " +
                "does not contain two buttons.");
            return;
        }

        for (int index = clonedButtons.Length - 1; index >= 2; index--)
        {
            Destroy(clonedButtons[index].gameObject);
        }

        string[] pageNames = { "DISPLAY", "GRAPHICS" };
        _videoSubpageButtons = clonedButtons.Take(2).ToArray();
        float buttonSpacing = 8f;
        float buttonVerticalPadding = 4f;
        float buttonWidth =
            (dock.sizeDelta.x -
             buttonSpacing * (_videoSubpageButtons.Length + 1)) /
            _videoSubpageButtons.Length;
        for (int index = 0; index < _videoSubpageButtons.Length; index++)
        {
            int pageIndex = index;
            Button button = _videoSubpageButtons[index];
            button.gameObject.name =
                $"FixedReality2000_{pageNames[index]}Button";
            button.gameObject.SetActive(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SetVideoSubpage(pageIndex));

            RectTransform buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(
                buttonWidth,
                dock.sizeDelta.y - buttonVerticalPadding * 2f);
            buttonRect.anchoredPosition = new Vector2(
                (index - (_videoSubpageButtons.Length - 1) * 0.5f) *
                (buttonWidth + buttonSpacing),
                0f);
            buttonRect.localScale = Vector3.one;
            buttonRect.localRotation = Quaternion.identity;

            TMP_Text? label =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                label.text = pageNames[index];
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.fontSize = Mathf.Min(label.fontSize, 19f);
                label.raycastTarget = false;
            }
        }

        _videoSubpages = new[]
        {
            CreateVideoSubpage(videoContainer, "FixedReality2000_DisplayPage"),
            CreateVideoSubpage(videoContainer, "FixedReality2000_GraphicsPage")
        };

        float topRowY = videoRect.rect.yMax - 65f;
        float rowSpacing = Mathf.Max(90f, Mathf.Abs(rowStep.y));
        Vector2 leftLabelPosition =
            new(GetLocalCenter((RectTransform)resolutionLabel, videoRect).x, topRowY);
        Vector2 leftControlPosition =
            new(GetLocalCenter((RectTransform)resolutionControl, videoRect).x, topRowY);
        Vector2 rightLabelPosition =
            new(GetLocalCenter((RectTransform)displayLabel, videoRect).x, topRowY);
        Vector2 rightControlPosition =
            new(GetLocalCenter((RectTransform)displayControl, videoRect).x, topRowY);
        Vector2 nextLeftLabelPosition =
            leftLabelPosition + Vector2.down * rowSpacing;
        Vector2 nextLeftControlPosition =
            leftControlPosition + Vector2.down * rowSpacing;
        Vector2 nextRightLabelPosition =
            rightLabelPosition + Vector2.down * rowSpacing;
        Vector2 nextRightControlPosition =
            rightControlPosition + Vector2.down * rowSpacing;
        Vector2 thirdLeftLabelPosition =
            nextLeftLabelPosition + Vector2.down * rowSpacing;
        Vector2 thirdLeftControlPosition =
            nextLeftControlPosition + Vector2.down * rowSpacing;
        Vector2 thirdRightLabelPosition =
            nextRightLabelPosition + Vector2.down * rowSpacing;
        Vector2 thirdRightControlPosition =
            nextRightControlPosition + Vector2.down * rowSpacing;

        MoveVideoControl(
            resolutionLabel,
            _videoSubpages[0].transform,
            leftLabelPosition);
        MoveVideoControl(
            resolutionControl,
            _videoSubpages[0].transform,
            leftControlPosition);
        MoveVideoControl(
            displayLabel,
            _videoSubpages[0].transform,
            rightLabelPosition);
        MoveVideoControl(
            displayControl,
            _videoSubpages[0].transform,
            rightControlPosition);
        MoveVideoControl(
            aspectLabel,
            _videoSubpages[0].transform,
            nextLeftLabelPosition);
        MoveVideoControl(
            aspectControl,
            _videoSubpages[0].transform,
            nextLeftControlPosition);
        MoveVideoControl(
            fpsLabel,
            _videoSubpages[0].transform,
            nextRightLabelPosition);
        MoveVideoControl(
            fpsControl,
            _videoSubpages[0].transform,
            nextRightControlPosition);
        MoveVideoControl(
            monitorLabel,
            _videoSubpages[0].transform,
            thirdLeftLabelPosition);
        MoveVideoControl(
            monitorControl,
            _videoSubpages[0].transform,
            thirdLeftControlPosition);
        MoveVideoControl(
            vsyncLabel,
            _videoSubpages[0].transform,
            thirdRightLabelPosition);
        MoveVideoControl(
            vsyncControl,
            _videoSubpages[0].transform,
            thirdRightControlPosition);

        MoveVideoControl(
            qualityLabel,
            _videoSubpages[1].transform,
            leftLabelPosition);
        MoveVideoControl(
            qualityControl,
            _videoSubpages[1].transform,
            leftControlPosition);
        MoveVideoControl(
            textureLabel,
            _videoSubpages[1].transform,
            rightLabelPosition);
        MoveVideoControl(
            textureControl,
            _videoSubpages[1].transform,
            rightControlPosition);
        MoveVideoControl(
            aaLabel,
            _videoSubpages[1].transform,
            nextLeftLabelPosition);
        MoveVideoControl(
            aaControl,
            _videoSubpages[1].transform,
            nextLeftControlPosition);
        MoveVideoControl(
            postAaLabel,
            _videoSubpages[1].transform,
            nextRightLabelPosition);
        MoveVideoControl(
            postAaControl,
            _videoSubpages[1].transform,
            nextRightControlPosition);

        dock.SetAsLastSibling();
        SetVideoSubpage(0);
        Plugin.Log.LogInfo(
            "Video subpages created: Display and Graphics.");
    }

    private void BuildControlsTab(Transform optionsPanel)
    {
        Transform? dock = optionsPanel.Find("ButtonDock/BG PC");
        Transform? videoPage = optionsPanel.Find("VideoSettings");
        Transform? audioPage = optionsPanel.Find("AudioSettings");
        Transform? gamePage = optionsPanel.Find("GameSettings");
        Transform? controlsPage = optionsPanel.Find("ControlsSettings");
        Button? videoButton =
            dock?.Find("VideoSettingsButton")?.GetComponent<Button>();
        Button? audioButton =
            dock?.Find("AudioSettingsButton")?.GetComponent<Button>();
        Button? gameButton =
            dock?.Find("GameSettingsButton")?.GetComponent<Button>();
        Button? controlsButton =
            dock?.Find("ControlsSettingsButton")?.GetComponent<Button>();
        CarrouselUIHandler? carousel =
            optionsPanel.GetComponent<CarrouselUIHandler>();
        if (dock == null ||
            videoPage == null ||
            audioPage == null ||
            gamePage == null ||
            controlsPage == null ||
            videoButton == null ||
            audioButton == null ||
            gameButton == null ||
            controlsButton == null ||
            carousel == null)
        {
            Plugin.Log.LogWarning(
                "The retail Controls tab could not be enabled because one of " +
                "its hidden objects was not found.");
            return;
        }

        if (controlsPage.Find("FixedReality2000_MousePage") != null)
        {
            return;
        }

        controlsButton.gameObject.SetActive(true);
        controlsButton.interactable = true;
        // This button ships hidden and its serialized event/state is not part
        // of the retail three-page carousel. Give it a clean runtime event so
        // stale persistent wiring cannot swallow the first click.
        controlsButton.onClick = new Button.ButtonClickedEvent();
        TMP_Text? controlsTabText =
            controlsButton.GetComponentInChildren<TMP_Text>(includeInactive: true);
        if (controlsTabText != null)
        {
            controlsTabText.gameObject.SetActive(true);
            controlsTabText.text = "CONTROLS";
            controlsTabText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Button[] mainButtons =
        {
            videoButton,
            audioButton,
            gameButton,
            controlsButton
        };
        GameObject[] mainPages =
        {
            videoPage.gameObject,
            audioPage.gameObject,
            gamePage.gameObject,
            controlsPage.gameObject
        };
        TextMeshProUGUI[] mainTexts = mainButtons
            .Select(button =>
                button.GetComponentInChildren<TextMeshProUGUI>(true))
            .Where(text => text != null)
            .ToArray()!;
        if (mainTexts.Length != mainButtons.Length)
        {
            Plugin.Log.LogWarning(
                "The Controls tab was found, but one of the main tab labels is missing.");
            return;
        }

        Transform? sensitivityLabel = gamePage.Find("SensitivityTitle");
        Transform? sensitivityControl = gamePage.Find("SensitivitySlider");
        Transform? invertLabel = gamePage.Find("InvertMouseTitle");
        Transform? invertControl = gamePage.Find("InvertMouseButton");
        Transform? fovLabel = gamePage.Find("FixedReality2000_FovLabel");
        Transform? fovControl = gamePage.Find(InjectedFovRowName);
        Transform? controlsSensitivityLabel =
            controlsPage.Find("SensitivityTitle");
        Transform? controlsSensitivityControl =
            controlsPage.Find("SensitivitySlider");
        Transform? controlsInvertLabel =
            controlsPage.Find("InvertMouseTitle");
        Transform? controlsInvertControl =
            controlsPage.Find("InvertMouseButton");
        if (sensitivityLabel == null ||
            sensitivityControl == null ||
            invertLabel == null ||
            invertControl == null ||
            fovLabel == null ||
            fovControl == null ||
            controlsSensitivityLabel == null ||
            controlsSensitivityControl == null ||
            controlsInvertLabel == null ||
            controlsInvertControl == null)
        {
            Plugin.Log.LogWarning(
                "The Controls tab could not be populated because its mouse rows " +
                "or the active Game controls were not found.");
            return;
        }

        RectTransform gameRect = (RectTransform)gamePage;
        RectTransform controlsRect = (RectTransform)controlsPage;
        Vector2 formerSensitivityLabelPosition =
            GetLocalCenter((RectTransform)sensitivityLabel, gameRect);
        Vector2 formerSensitivityControlPosition =
            GetLocalCenter((RectTransform)sensitivityControl, gameRect);
        Vector2 mouseSensitivityLabelPosition =
            GetLocalCenter((RectTransform)controlsSensitivityLabel, controlsRect);
        Vector2 mouseSensitivityControlPosition =
            GetLocalCenter((RectTransform)controlsSensitivityControl, controlsRect);
        Vector2 mouseInvertLabelPosition =
            GetLocalCenter((RectTransform)controlsInvertLabel, controlsRect);
        Vector2 mouseInvertControlPosition =
            GetLocalCenter((RectTransform)controlsInvertControl, controlsRect);

        _controlsSubpages = new[]
        {
            CreateVideoSubpage(controlsPage, "FixedReality2000_MousePage"),
            CreateVideoSubpage(controlsPage, "FixedReality2000_KeyboardPage"),
            CreateVideoSubpage(controlsPage, "FixedReality2000_GamepadPage")
        };

        MoveVideoControl(
            sensitivityLabel,
            _controlsSubpages[0].transform,
            mouseSensitivityLabelPosition);
        MoveVideoControl(
            sensitivityControl,
            _controlsSubpages[0].transform,
            mouseSensitivityControlPosition);
        MoveVideoControl(
            invertLabel,
            _controlsSubpages[0].transform,
            mouseInvertLabelPosition);
        MoveVideoControl(
            invertControl,
            _controlsSubpages[0].transform,
            mouseInvertControlPosition);
        MoveVideoControl(fovLabel, gamePage, formerSensitivityLabelPosition);
        MoveVideoControl(fovControl, gamePage, formerSensitivityControlPosition);

        Button bindingButtonTemplate = invertControl.GetComponent<Button>();
        TMP_Text bindingLabelTemplate =
            sensitivityLabel.GetComponent<TMP_Text>();
        Destroy(controlsSensitivityLabel.gameObject);
        Destroy(controlsSensitivityControl.gameObject);
        Destroy(controlsInvertLabel.gameObject);
        Destroy(controlsInvertControl.gameObject);

        BuildKeyboardBindings(
            _controlsSubpages[1].transform,
            controlsRect,
            bindingLabelTemplate,
            bindingButtonTemplate);
        BuildGamepadPages(
            _controlsSubpages[2].transform,
            controlsRect,
            bindingLabelTemplate,
            bindingButtonTemplate,
            sensitivityControl.GetComponent<Slider>(),
            _videoAaDropdown);
        BuildControlsSubpageDock(controlsPage);

        carousel.buttonsInCarrousel = mainButtons;
        carousel.panelsInCarroussel = mainPages;
        carousel.texts = mainTexts;
        carousel.objectToEnable = new[]
        {
            _resolutionDropdown?.gameObject ?? videoPage.gameObject,
            audioPage.GetComponentInChildren<Slider>(true)?.gameObject ??
                audioPage.gameObject,
            gamePage.Find("ToolbarButton")?.gameObject ?? gamePage.gameObject,
            sensitivityControl.gameObject
        };
        int activeMainPage = Array.FindIndex(
            mainPages,
            page => page.activeSelf);
        for (int index = 0; index < mainButtons.Length; index++)
        {
            int selectedIndex = index;
            mainButtons[index].onClick.AddListener(
                () =>
                {
                    carousel.CarrousselIndex = selectedIndex;
                    carousel.UpdateCarroussel();
                });
        }

        SetControlsSubpage(0);
        carousel.CarrousselIndex =
            activeMainPage >= 0
                ? activeMainPage
                : Mathf.Clamp(carousel.CarrousselIndex, 0, mainPages.Length - 1);
        carousel.UpdateCarroussel();
        Plugin.Log.LogInfo(
            "Retail Controls tab enabled with Mouse, Keyboard, and Gamepad subpages.");
    }

    private void BuildGamepadPages(
        Transform gamepadPage,
        RectTransform controlsRect,
        TMP_Text labelTemplate,
        Button buttonTemplate,
        Slider sliderTemplate,
        TMP_Dropdown? dropdownTemplate)
    {
        if (dropdownTemplate == null)
        {
            Plugin.Log.LogWarning(
                "Gamepad settings could not be created because the dropdown template is missing.");
            return;
        }

        _gamepadSubpages = new[]
        {
            CreateVideoSubpage(gamepadPage, "FixedReality2000_GamepadTuningPage"),
            CreateVideoSubpage(gamepadPage, "FixedReality2000_GamepadPreferencesPage")
        };
        BuildGamepadInnerDock(gamepadPage, controlsRect);

        float top = controlsRect.rect.yMax - 120f;
        const float rowSpacing = 82f;
        float leftLabelX = controlsRect.rect.xMin + 145f;
        float leftControlX = controlsRect.rect.xMin + 445f;
        float rightLabelX = controlsRect.rect.center.x + 145f;
        float rightControlX = controlsRect.rect.xMax - 145f;

        Transform tuning = _gamepadSubpages[0].transform;
        _controllerLookSensitivitySlider = CreateControllerSliderRow(
            tuning, controlsRect, labelTemplate, sliderTemplate,
            "LOOK SENS.", new Vector2(leftLabelX, top),
            new Vector2(leftControlX, top), 0.25f, 3f,
            ControllerSettings.LookSensitivity, false,
            value => ControllerSettings.LookSensitivity = value);
        _controllerMoveDeadzoneSlider = CreateControllerSliderRow(
            tuning, controlsRect, labelTemplate, sliderTemplate,
            "MOVE DEADZONE", new Vector2(leftLabelX, top - rowSpacing),
            new Vector2(leftControlX, top - rowSpacing), 0f, 0.4f,
            ControllerSettings.MoveDeadzone, false,
            value => ControllerSettings.MoveDeadzone = value);
        _controllerLookDeadzoneSlider = CreateControllerSliderRow(
            tuning, controlsRect, labelTemplate, sliderTemplate,
            "LOOK DEADZONE", new Vector2(leftLabelX, top - 2f * rowSpacing),
            new Vector2(leftControlX, top - 2f * rowSpacing), 0f, 0.4f,
            ControllerSettings.LookDeadzone, false,
            value => ControllerSettings.LookDeadzone = value);
        _controllerCursorSpeedSlider = CreateControllerSliderRow(
            tuning, controlsRect, labelTemplate, sliderTemplate,
            "CURSOR SPEED", new Vector2(leftLabelX, top - 3f * rowSpacing),
            new Vector2(leftControlX, top - 3f * rowSpacing), 300f, 1800f,
            ControllerSettings.CursorSpeed, true,
            value => ControllerSettings.CursorSpeed = value);

        _controllerResponseCurveDropdown = CreateControllerDropdownRow(
            tuning, labelTemplate, dropdownTemplate, "RESPONSE CURVE",
            new Vector2(rightLabelX, top), new Vector2(rightControlX, top),
            new[] { "LINEAR", "STANDARD", "DYNAMIC" },
            (int)ControllerSettings.ResponseCurve,
            value => ControllerSettings.ResponseCurve =
                (ControllerResponseCurve)value);
        _controllerSprintModeDropdown = CreateControllerDropdownRow(
            tuning, labelTemplate, dropdownTemplate, "SPRINT",
            new Vector2(rightLabelX, top - rowSpacing),
            new Vector2(rightControlX, top - rowSpacing),
            new[] { "HOLD", "TOGGLE" },
            (int)ControllerSettings.SprintMode,
            value => ControllerSettings.SprintMode =
                (ControllerSprintMode)value);
        _controllerTriggerThresholdSlider = CreateControllerSliderRow(
            tuning, controlsRect, labelTemplate, sliderTemplate,
            "TRIGGER THRESHOLD",
            new Vector2(rightLabelX, top - 2f * rowSpacing),
            new Vector2(rightControlX, top - 2f * rowSpacing),
            0.05f, 0.9f, ControllerSettings.TriggerThreshold, false,
            value => ControllerSettings.TriggerThreshold = value);

        Transform preferences = _gamepadSubpages[1].transform;
        _controllerInvertXDropdown = CreateControllerDropdownRow(
            preferences, labelTemplate, dropdownTemplate, "INVERT X",
            new Vector2(leftLabelX, top), new Vector2(leftControlX, top),
            new[] { "OFF", "ON" }, ControllerSettings.InvertX ? 1 : 0,
            value => ControllerSettings.InvertX = value == 1);
        _controllerInvertYDropdown = CreateControllerDropdownRow(
            preferences, labelTemplate, dropdownTemplate, "INVERT Y",
            new Vector2(leftLabelX, top - rowSpacing),
            new Vector2(leftControlX, top - rowSpacing),
            new[] { "OFF", "ON" }, ControllerSettings.InvertY ? 1 : 0,
            value => ControllerSettings.InvertY = value == 1);
        _controllerStickLayoutDropdown = CreateControllerDropdownRow(
            preferences, labelTemplate, dropdownTemplate, "STICK LAYOUT",
            new Vector2(leftLabelX, top - 2f * rowSpacing),
            new Vector2(leftControlX, top - 2f * rowSpacing),
            new[] { "STANDARD", "SOUTHPAW" },
            (int)ControllerSettings.StickLayout,
            value => ControllerSettings.StickLayout =
                (ControllerStickLayout)value);
        _controllerVibrationDropdown = CreateControllerDropdownRow(
            preferences, labelTemplate, dropdownTemplate, "VIBRATION",
            new Vector2(rightLabelX, top),
            new Vector2(rightControlX, top),
            new[] { "OFF", "ON" },
            ControllerSettings.VibrationEnabled ? 1 : 0,
            value => ControllerSettings.VibrationEnabled = value == 1);
        _controllerVibrationIntensitySlider = CreateControllerSliderRow(
            preferences, controlsRect, labelTemplate, sliderTemplate,
            "VIBRATION POWER",
            new Vector2(rightLabelX, top - rowSpacing),
            new Vector2(rightControlX, top - rowSpacing),
            0f, 1f, ControllerSettings.VibrationIntensity, false,
            value => ControllerSettings.VibrationIntensity = value);

        SetGamepadSubpage(0);
    }

    private Slider CreateControllerSliderRow(
        Transform page,
        RectTransform controlsRect,
        TMP_Text labelTemplate,
        Slider sliderTemplate,
        string labelText,
        Vector2 labelPosition,
        Vector2 controlPosition,
        float minimum,
        float maximum,
        float value,
        bool wholeNumbers,
        UnityEngine.Events.UnityAction<float> changed)
    {
        TMP_Text label = CreateMenuLabel(
            labelTemplate,
            page,
            $"FixedReality2000_Gamepad{labelText.Replace(" ", string.Empty)}Label",
            labelText,
            labelPosition);
        label.fontSize = Mathf.Min(label.fontSize, 18f);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        Slider slider = Instantiate(sliderTemplate, page, false);
        slider.gameObject.name =
            $"FixedReality2000_Gamepad{labelText.Replace(" ", string.Empty)}Slider";
        RemoveInheritedGameScripts(slider.gameObject);
        RemoveInheritedSliderText(slider.gameObject);
        slider.onValueChanged = new Slider.SliderEvent();
        RectTransform rect = (RectTransform)slider.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = controlPosition;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = new Vector2(
            Mathf.Min(300f, controlsRect.rect.width * 0.24f),
            Mathf.Max(24f, rect.sizeDelta.y));
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = wholeNumbers;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(changed);
        if (slider.handleRect != null)
        {
            slider.handleRect.localScale = Vector3.one;
            Image? image = slider.handleRect.GetComponent<Image>();
            if (image != null)
            {
                image.preserveAspect = true;
            }
        }

        return slider;
    }

    private TMP_Dropdown CreateControllerDropdownRow(
        Transform page,
        TMP_Text labelTemplate,
        TMP_Dropdown dropdownTemplate,
        string labelText,
        Vector2 labelPosition,
        Vector2 controlPosition,
        IEnumerable<string> options,
        int selected,
        UnityEngine.Events.UnityAction<int> changed)
    {
        TMP_Text label = CreateMenuLabel(
            labelTemplate,
            page,
            $"FixedReality2000_Gamepad{labelText.Replace(" ", string.Empty)}Label",
            labelText,
            labelPosition);
        label.fontSize = Mathf.Min(label.fontSize, 18f);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_Dropdown dropdown = CreateDropdown(
            dropdownTemplate,
            page,
            $"FixedReality2000_Gamepad{labelText.Replace(" ", string.Empty)}Dropdown",
            controlPosition,
            options);
        RemoveInheritedGameScripts(dropdown.gameObject);
        dropdown.SetValueWithoutNotify(
            Mathf.Clamp(selected, 0, dropdown.options.Count - 1));
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(changed);
        return dropdown;
    }

    private void BuildControllerBindings(
        Transform page,
        RectTransform controlsRect,
        TMP_Text labelTemplate,
        Button buttonTemplate,
        float top,
        float rowSpacing,
        float leftLabelX,
        float leftButtonX,
        float rightLabelX,
        float rightButtonX)
    {
        for (int index = 0; index < ControllerSettings.MenuOrder.Length; index++)
        {
            ControllerAction action = ControllerSettings.MenuOrder[index];
            bool right = index >= 4;
            int row = right ? index - 4 : index;
            float y = top - row * rowSpacing;
            TMP_Text label = CreateMenuLabel(
                labelTemplate,
                page,
                $"FixedReality2000_Controller{action}Label",
                ControllerSettings.GetActionLabel(action),
                new Vector2(right ? rightLabelX : leftLabelX, y));
            label.fontSize = Mathf.Min(label.fontSize, 18f);
            label.textWrappingMode = TextWrappingModes.NoWrap;

            Button button = Instantiate(buttonTemplate, page, false);
            button.gameObject.name =
                $"FixedReality2000_Controller{action}Binding";
            RemoveInheritedGameScripts(button.gameObject);
            button.onClick = new Button.ButtonClickedEvent();
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition =
                new Vector2(right ? rightButtonX : leftButtonX, y);
            rect.sizeDelta = new Vector2(230f, 54f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            foreach (Transform child in button.transform)
            {
                if (child.name.Contains("Arrow", StringComparison.OrdinalIgnoreCase))
                {
                    child.gameObject.SetActive(false);
                }
            }

            button.onClick.AddListener(
                () => BeginControllerBindingCapture(action));
            _controllerBindingButtons[action] = button;
        }

        TMP_Text hint = CreateMenuLabel(
            labelTemplate,
            page,
            "FixedReality2000_ControllerBindingHint",
            "SELECT A BINDING  ·  BACKSPACE CANCELS  ·  DELETE RESETS",
            new Vector2(-135f, controlsRect.rect.yMin + 34f));
        RectTransform hintRect = (RectTransform)hint.transform;
        hintRect.sizeDelta = new Vector2(650f, 30f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.fontSize = Mathf.Min(hint.fontSize, 12f);
        hint.color = new Color(1f, 1f, 1f, 0.72f);
        hint.textWrappingMode = TextWrappingModes.NoWrap;

        Button reset = Instantiate(buttonTemplate, page, false);
        reset.gameObject.name = "FixedReality2000_ResetControllerButton";
        RemoveInheritedGameScripts(reset.gameObject);
        reset.onClick = new Button.ButtonClickedEvent();
        RectTransform resetRect = (RectTransform)reset.transform;
        resetRect.anchorMin = resetRect.anchorMax = resetRect.pivot =
            new Vector2(0.5f, 0.5f);
        resetRect.anchoredPosition =
            new Vector2(rightLabelX + 30f, top - 3f * rowSpacing);
        resetRect.sizeDelta = new Vector2(300f, 64f);
        resetRect.localScale = Vector3.one;
        TMP_Text? resetText = reset.GetComponentInChildren<TMP_Text>(true);
        if (resetText != null)
        {
            resetText.gameObject.SetActive(true);
            resetText.text = "RESET DEFAULTS";
            resetText.fontSize = Mathf.Min(resetText.fontSize, 16f);
        }
        reset.onClick.AddListener(
            () =>
            {
                CancelControllerBindingCapture();
                ControllerSettings.ResetAll();
                RefreshControllerUi();
            });
        RefreshControllerBindingButtons();
    }

    private void BuildGamepadInnerDock(
        Transform gamepadPage,
        RectTransform controlsRect)
    {
        if (_videoSubpageButtons == null ||
            _videoSubpageButtons[0].transform.parent is not RectTransform source)
        {
            return;
        }

        RectTransform dock = Instantiate(source, gamepadPage, false);
        dock.gameObject.name = "FixedReality2000_GamepadInnerDock";
        dock.anchoredPosition =
            new Vector2(0f, controlsRect.rect.yMax - 32f);
        dock.localScale = Vector3.one;
        HorizontalLayoutGroup? layout = dock.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.enabled = false;
        }

        List<Button> buttons =
            dock.GetComponentsInChildren<Button>(true).Take(2).ToList();
        _gamepadSubpageButtons = buttons.ToArray();
        string[] labels = { "TUNING", "PREFERENCES" };
        float spacing = 8f;
        float width =
            (dock.sizeDelta.x - spacing * 3f) / 2f;
        for (int index = 0; index < buttons.Count; index++)
        {
            int selected = index;
            Button button = buttons[index];
            button.gameObject.SetActive(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SetGamepadSubpage(selected));
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, dock.sizeDelta.y - 8f);
            rect.anchoredPosition = new Vector2(
                (index - 0.5f) * (width + spacing), 0f);
            rect.localScale = Vector3.one;
            TMP_Text? text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.gameObject.SetActive(true);
                text.text = labels[index];
                text.fontSize = Mathf.Min(text.fontSize, 17f);
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
        dock.SetAsLastSibling();
    }

    private void SetGamepadSubpage(int selectedIndex)
    {
        if (_gamepadSubpages == null || _gamepadSubpageButtons == null)
        {
            return;
        }

        int selected =
            Mathf.Clamp(selectedIndex, 0, _gamepadSubpages.Length - 1);
        for (int index = 0; index < _gamepadSubpages.Length; index++)
        {
            bool active = index == selected;
            _gamepadSubpages[index].SetActive(active);
            _gamepadSubpageButtons[index].interactable = !active;
            TMP_Text? text = _gamepadSubpageButtons[index]
                .GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.color = active ? Color.black : Color.white;
            }
        }
    }

    private void BuildKeyboardBindings(
        Transform keyboardPage,
        RectTransform controlsRect,
        TMP_Text labelTemplate,
        Button buttonTemplate)
    {
        float top = controlsRect.rect.yMax - 55f;
        const float RowSpacing = 76f;
        float leftLabelX = controlsRect.rect.xMin + 145f;
        float leftButtonX = controlsRect.rect.xMin + 445f;
        float rightLabelX = controlsRect.rect.center.x + 145f;
        float rightButtonX = controlsRect.rect.xMax - 145f;

        for (int index = 0; index < PlayerKeybindings.MenuOrder.Length; index++)
        {
            PlayerBinding binding = PlayerKeybindings.MenuOrder[index];
            bool rightColumn = index >= 5;
            int row = rightColumn ? index - 5 : index;
            float y = top - row * RowSpacing;
            float labelX = rightColumn ? rightLabelX : leftLabelX;
            float buttonX = rightColumn ? rightButtonX : leftButtonX;

            TMP_Text label = CreateMenuLabel(
                labelTemplate,
                keyboardPage,
                $"FixedReality2000_{binding}Label",
                PlayerKeybindings.GetActionLabel(binding),
                new Vector2(labelX, y));
            label.fontSize = Mathf.Min(label.fontSize, 19f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Button button = Instantiate(
                buttonTemplate,
                keyboardPage,
                worldPositionStays: false);
            button.gameObject.name =
                $"FixedReality2000_{binding}Binding";
            RemoveInheritedGameScripts(button.gameObject);
            button.onClick = new Button.ButtonClickedEvent();
            RectTransform buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(buttonX, y);
            buttonRect.sizeDelta = new Vector2(230f, 54f);
            buttonRect.localScale = Vector3.one;
            buttonRect.localRotation = Quaternion.identity;

            foreach (Transform child in button.transform)
            {
                if (child.name.Contains("Arrow", StringComparison.OrdinalIgnoreCase))
                {
                    child.gameObject.SetActive(false);
                }
            }

            TMP_Text? value =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (value != null)
            {
                value.gameObject.SetActive(true);
                value.text = PlayerKeybindings.GetLabel(binding);
                value.fontSize = Mathf.Min(value.fontSize, 16f);
                value.textWrappingMode = TextWrappingModes.NoWrap;
                value.overflowMode = TextOverflowModes.Ellipsis;
            }

            button.onClick.AddListener(() => BeginBindingCapture(binding));
            _bindingButtons[binding] = button;
        }

        TMP_Text hint = CreateMenuLabel(
            labelTemplate,
            keyboardPage,
            "FixedReality2000_BindingHint",
            "SELECT A BINDING  ·  BACKSPACE CANCELS  ·  DELETE UNBINDS",
            new Vector2(-135f, controlsRect.rect.yMin + 34f));
        RectTransform hintRect = (RectTransform)hint.transform;
        hintRect.sizeDelta = new Vector2(650f, 30f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.fontSize = Mathf.Min(hint.fontSize, 12f);
        hint.color = new Color(1f, 1f, 1f, 0.72f);
        hint.textWrappingMode = TextWrappingModes.NoWrap;

        Button resetButton = Instantiate(
            buttonTemplate,
            keyboardPage,
            worldPositionStays: false);
        resetButton.gameObject.name =
            "FixedReality2000_ResetBindingsButton";
        RemoveInheritedGameScripts(resetButton.gameObject);
        resetButton.onClick = new Button.ButtonClickedEvent();
        RectTransform resetRect = (RectTransform)resetButton.transform;
        resetRect.anchorMin = new Vector2(0.5f, 0.5f);
        resetRect.anchorMax = new Vector2(0.5f, 0.5f);
        resetRect.pivot = new Vector2(0.5f, 0.5f);
        resetRect.anchoredPosition = new Vector2(
            rightLabelX + 30f,
            top - 4f * RowSpacing);
        resetRect.sizeDelta = new Vector2(300f, 64f);
        resetRect.localScale = Vector3.one;
        resetRect.localRotation = Quaternion.identity;
        foreach (Transform child in resetButton.transform)
        {
            if (child.name.Contains(
                    "Arrow",
                    StringComparison.OrdinalIgnoreCase))
            {
                child.gameObject.SetActive(false);
            }
        }

        TMP_Text? resetText =
            resetButton.GetComponentInChildren<TMP_Text>(
                includeInactive: true);
        if (resetText != null)
        {
            resetText.gameObject.SetActive(true);
            resetText.text = "RESET DEFAULTS";
            resetText.fontSize = Mathf.Min(resetText.fontSize, 16f);
            resetText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        resetButton.onClick.AddListener(
            () =>
            {
                CancelBindingCapture();
                PlayerKeybindings.ResetAll();
                RefreshBindingButtons();
            });
    }

    private void BuildControlsSubpageDock(Transform controlsPage)
    {
        if (_videoSubpageButtons == null ||
            _videoSubpageButtons.Length < 2 ||
            _videoSubpageButtons[0].transform.parent is not RectTransform sourceDock)
        {
            Plugin.Log.LogWarning(
                "Controls subpage navigation could not clone the Video dock.");
            return;
        }

        RectTransform controlsRect = (RectTransform)controlsPage;
        RectTransform dock = Instantiate(
            sourceDock,
            controlsPage,
            worldPositionStays: false);
        dock.gameObject.name = "FixedReality2000_ControlsSubpageDock";
        dock.anchoredPosition = new Vector2(
            0f,
            controlsRect.rect.yMin - dock.sizeDelta.y * 0.5f - 4f);
        dock.localScale = Vector3.one;
        dock.localRotation = Quaternion.identity;

        Button[] clonedButtons =
            dock.GetComponentsInChildren<Button>(includeInactive: true);
        List<Button> controlsButtons = clonedButtons.Take(2).ToList();
        controlsButtons.Add(
            Instantiate(controlsButtons[1], dock, worldPositionStays: false));
        _controlsSubpageButtons = controlsButtons.ToArray();
        for (int index = clonedButtons.Length - 1; index >= 2; index--)
        {
            Destroy(clonedButtons[index].gameObject);
        }
        HorizontalLayoutGroup? layout =
            dock.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.enabled = false;
        }

        string[] labels = { "MOUSE", "KEYBOARD", "GAMEPAD" };
        const float spacing = 8f;
        float width =
            (dock.sizeDelta.x - spacing * 4f) / 3f;
        for (int index = 0; index < _controlsSubpageButtons.Length; index++)
        {
            int selected = index;
            Button button = _controlsSubpageButtons[index];
            button.gameObject.name =
                $"FixedReality2000_{labels[index]}Button";
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SetControlsSubpage(selected));
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, dock.sizeDelta.y - 8f);
            rect.anchoredPosition =
                new Vector2((index - 1f) * (width + spacing), 0f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            TMP_Text? text =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.text = labels[index];
            }
        }

        dock.SetAsLastSibling();
    }

    private void SetControlsSubpage(int selectedIndex)
    {
        if (_controlsSubpages == null)
        {
            return;
        }

        int selected = Mathf.Clamp(
            selectedIndex,
            0,
            _controlsSubpages.Length - 1);
        if (selected != 2)
        {
            CancelControllerBindingCapture();
        }
        for (int index = 0; index < _controlsSubpages.Length; index++)
        {
            bool active = index == selected;
            _controlsSubpages[index].SetActive(active);

            if (_controlsSubpageButtons == null ||
                index >= _controlsSubpageButtons.Length)
            {
                continue;
            }

            Button button = _controlsSubpageButtons[index];
            button.interactable = !active;
            TMP_Text? text =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.color = active ? Color.black : Color.white;
            }
        }
    }

    private void BeginBindingCapture(PlayerBinding binding)
    {
        if (_bindingCaptureOwner != null &&
            _bindingCaptureOwner != this)
        {
            _bindingCaptureOwner.CancelBindingCapture();
        }

        _bindingBeingCaptured = binding;
        _bindingCaptureOwner = this;
        _bindingCaptureStartFrame = Time.frameCount + 1;
        RefreshBindingButtons();
    }

    private void CancelBindingCapture()
    {
        _bindingBeingCaptured = null;
        if (_bindingCaptureOwner == this)
        {
            _bindingCaptureOwner = null;
        }

        RefreshBindingButtons();
    }

    private void Update()
    {
        if (Time.frameCount < _bindingCaptureStartFrame)
        {
            return;
        }

        Keyboard? keyboard = Keyboard.current;
        if (_controllerBindingBeingCaptured is ControllerAction controllerAction)
        {
            if (keyboard?.backspaceKey.wasPressedThisFrame == true)
            {
                CancelControllerBindingCapture();
                return;
            }

            if (keyboard?.deleteKey.wasPressedThisFrame == true)
            {
                ControllerSettings.Unbind(controllerAction);
                CancelControllerBindingCapture();
                return;
            }

            Gamepad? gamepad = Gamepad.current;
            if (gamepad != null)
            {
                ControllerBinding detected =
                    ControllerSettings.DetectPressedBinding(gamepad);
                if (detected != ControllerBinding.None)
                {
                    ControllerSettings.SetBinding(controllerAction, detected);
                    CancelControllerBindingCapture();
                }
            }
            return;
        }

        if (_bindingBeingCaptured is not PlayerBinding binding ||
            keyboard == null)
        {
            return;
        }

        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            CancelBindingCapture();
            return;
        }

        if (keyboard.deleteKey.wasPressedThisFrame)
        {
            PlayerKeybindings.Unbind(binding);
            CancelBindingCapture();
            return;
        }

        foreach (KeyControl key in keyboard.allKeys)
        {
            if (!key.wasPressedThisFrame ||
                key.keyCode is InputKey.None or InputKey.Escape or
                    InputKey.Backspace or InputKey.Delete)
            {
                continue;
            }

            PlayerKeybindings.Set(binding, key.keyCode);
            CancelBindingCapture();
            return;
        }
    }

    private void RefreshBindingButtons()
    {
        foreach (var pair in _bindingButtons)
        {
            TMP_Text? text =
                pair.Value.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.text =
                    _bindingBeingCaptured == pair.Key
                        ? "PRESS A KEY"
                        : PlayerKeybindings.GetLabel(pair.Key);
            }
        }
    }

    private void BeginControllerBindingCapture(ControllerAction action)
    {
        CancelBindingCapture();
        _controllerBindingBeingCaptured = action;
        _bindingCaptureStartFrame = Time.frameCount + 1;
        RefreshControllerBindingButtons();
    }

    private void CancelControllerBindingCapture()
    {
        _controllerBindingBeingCaptured = null;
        RefreshControllerBindingButtons();
    }

    private void RefreshControllerBindingButtons()
    {
        foreach (var pair in _controllerBindingButtons)
        {
            TMP_Text? text =
                pair.Value.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
            {
                continue;
            }

            text.gameObject.SetActive(true);
            text.text =
                _controllerBindingBeingCaptured == pair.Key
                    ? "PRESS A BUTTON"
                    : ControllerSettings.GetBindingLabel(pair.Key);
            text.fontSize = Mathf.Min(text.fontSize, 15f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private void RefreshControllerUi()
    {
        _controllerLookSensitivitySlider?.SetValueWithoutNotify(
            ControllerSettings.LookSensitivity);
        _controllerMoveDeadzoneSlider?.SetValueWithoutNotify(
            ControllerSettings.MoveDeadzone);
        _controllerLookDeadzoneSlider?.SetValueWithoutNotify(
            ControllerSettings.LookDeadzone);
        _controllerCursorSpeedSlider?.SetValueWithoutNotify(
            ControllerSettings.CursorSpeed);
        _controllerTriggerThresholdSlider?.SetValueWithoutNotify(
            ControllerSettings.TriggerThreshold);
        _controllerVibrationIntensitySlider?.SetValueWithoutNotify(
            ControllerSettings.VibrationIntensity);
        SetDropdownWithoutNotify(
            _controllerResponseCurveDropdown,
            (int)ControllerSettings.ResponseCurve);
        SetDropdownWithoutNotify(
            _controllerSprintModeDropdown,
            (int)ControllerSettings.SprintMode);
        SetDropdownWithoutNotify(
            _controllerStickLayoutDropdown,
            (int)ControllerSettings.StickLayout);
        SetDropdownWithoutNotify(
            _controllerInvertXDropdown,
            ControllerSettings.InvertX ? 1 : 0);
        SetDropdownWithoutNotify(
            _controllerInvertYDropdown,
            ControllerSettings.InvertY ? 1 : 0);
        SetDropdownWithoutNotify(
            _controllerVibrationDropdown,
            ControllerSettings.VibrationEnabled ? 1 : 0);
        RefreshControllerBindingButtons();
    }

    private static void SetDropdownWithoutNotify(
        TMP_Dropdown? dropdown,
        int value)
    {
        if (dropdown == null || dropdown.options.Count == 0)
        {
            return;
        }

        dropdown.SetValueWithoutNotify(
            Mathf.Clamp(value, 0, dropdown.options.Count - 1));
        dropdown.RefreshShownValue();
    }

    private static GameObject CreateVideoSubpage(Transform parent, string name)
    {
        var page = new GameObject(name, typeof(RectTransform));
        page.layer = parent.gameObject.layer;
        RectTransform rect = page.GetComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return page;
    }

    private static void MoveVideoControl(
        Transform control,
        Transform page,
        Vector2 anchoredPosition)
    {
        RectTransform rect = (RectTransform)control;
        rect.SetParent(page, worldPositionStays: false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.localRotation = Quaternion.identity;
    }

    private void SetVideoSubpage(int selectedIndex)
    {
        if (_videoSubpages == null || _videoSubpageButtons == null)
        {
            return;
        }

        int selected = Mathf.Clamp(
            selectedIndex,
            0,
            _videoSubpages.Length - 1);
        for (int index = 0; index < _videoSubpages.Length; index++)
        {
            bool active = index == selected;
            _videoSubpages[index].SetActive(active);

            Button button = _videoSubpageButtons[index];
            // The retail tabs mark the active button as non-interactable.
            // This drives their native white selected state. Every cloned tab
            // must be made interactable again when another page is selected.
            button.interactable = !active;

            TMP_Text? text =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.color = active ? Color.black : Color.white;
            }
        }
    }

    private void OnAspectRatioChanged(int index)
    {
        _selectedAspectIndex = Mathf.Clamp(
            index,
            0,
            UltrawideResolutionTests.AspectRatioLabels.Length - 1);
        PlayerPrefs.SetInt(AspectRatioPreference, _selectedAspectIndex);
        PlayerPrefs.Save();
        ApplyAspectRatioFilter();
        _awaitingResolutionSelection = true;
        PopulateResolutionDropdown();
    }

    private void OnVSyncChanged(int index)
    {
        Plugin.SetVSyncFromUi(index != 0);
        RefreshFramePacingControls();
    }

    private void RefreshFramePacingControls()
    {
        if (_videoFpsDropdown != null)
        {
            // Unity ignores Application.targetFrameRate while V-Sync is
            // active. Preserve the chosen limiter for later, but disable its
            // control so the menu reflects which setting currently wins.
            _videoFpsDropdown.interactable = !Plugin.VSyncEnabled;
        }
    }

    private void OnDisplayModeChanged(int index)
    {
        if (_menu == null)
        {
            return;
        }

        int modeIndex = Mathf.Clamp(index, 0, 2);
        ResModeField(_menu) = modeIndex;
        PlayerPrefs.SetInt("resmode", modeIndex);
        _awaitingResolutionSelection = false;
        ApplyAspectRatioFilter();
        if (_choices.Count == 0 && _selectedAspectIndex != 0)
        {
            _selectedAspectIndex = 0;
            PlayerPrefs.SetInt(AspectRatioPreference, 0);
            _videoAspectDropdown?.SetValueWithoutNotify(0);
            _videoAspectDropdown?.RefreshShownValue();
            ApplyAspectRatioFilter();
        }

        PopulateResolutionDropdown();
        ApplyCurrentResolution();
    }

    private void RefreshDisplayModeControl()
    {
        if (_menu == null || _displayModeDropdown == null)
        {
            return;
        }

        int savedMode = Mathf.Clamp(
            PlayerPrefs.GetInt(
                "resmode",
                Mathf.Clamp(ResModeField(_menu), 0, 2)),
            0,
            2);
        ResModeField(_menu) = savedMode;
        _displayModeDropdown.SetValueWithoutNotify(savedMode);
        _displayModeDropdown.RefreshShownValue();
    }

    private void OnMonitorChanged(int index)
    {
        if (_displayLayout.Count == 0)
        {
            return;
        }

        _selectedMonitorIndex = Mathf.Clamp(
            index,
            0,
            _displayLayout.Count - 1);
        PlayerPrefs.SetInt(MonitorPreference, _selectedMonitorIndex);
        PlayerPrefs.Save();

        BuildResolutionChoices();
        ApplyAspectRatioFilter();
        PopulateResolutionDropdown();
        ApplyCurrentResolution();

        StopCoroutine(nameof(RefreshResolutionsAfterMonitorMove));
        StartCoroutine(nameof(RefreshResolutionsAfterMonitorMove));
    }

    private System.Collections.IEnumerator RefreshResolutionsAfterMonitorMove()
    {
        int requestedMonitor = _selectedMonitorIndex;
        yield return new WaitForSecondsRealtime(0.75f);
        if (requestedMonitor != _selectedMonitorIndex)
        {
            yield break;
        }

        RefreshDisplayLayout(preserveSelection: true);
        BuildResolutionChoices();
        ApplyAspectRatioFilter();
        PopulateResolutionDropdown();
    }

    private void RefreshDisplayLayout(bool preserveSelection = false)
    {
        _displayLayout.Clear();
        Screen.GetDisplayLayout(_displayLayout);
        if (_displayLayout.Count == 0)
        {
            _selectedMonitorIndex = 0;
            return;
        }

        int currentMonitor = _displayLayout.FindIndex(
            display => display.Equals(Screen.mainWindowDisplayInfo));
        int preferredMonitor = preserveSelection
            ? _selectedMonitorIndex
            : PlayerPrefs.GetInt(
                MonitorPreference,
                currentMonitor >= 0 ? currentMonitor : 0);
        _selectedMonitorIndex = Mathf.Clamp(
            preferredMonitor,
            0,
            _displayLayout.Count - 1);
    }

    private IEnumerable<string> BuildMonitorLabels()
    {
        if (_displayLayout.Count == 0)
        {
            return new[] { "1: UNKNOWN" };
        }

        return _displayLayout.Select(
            (display, index) =>
                string.IsNullOrWhiteSpace(display.name)
                    ? $"MONITOR {index + 1}"
                    : ShortenMonitorName(display.name));
    }

    private static string ShortenMonitorName(string name)
    {
        const int MaximumVisibleCharacters = 11;
        string trimmed = name.Trim();
        return trimmed.Length <= MaximumVisibleCharacters
            ? trimmed
            : trimmed[..MaximumVisibleCharacters].TrimEnd() + "…";
    }

    private static void ConfigureMonitorDropdownText(TMP_Dropdown dropdown)
    {
        TMP_Text? caption = dropdown.captionText;
        if (caption == null)
        {
            return;
        }

        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform captionRect = caption.rectTransform;
        captionRect.offsetMax = new Vector2(
            Mathf.Min(captionRect.offsetMax.x, -42f),
            captionRect.offsetMax.y);
    }

    private bool TryGetSelectedMonitor(out DisplayInfo monitor)
    {
        if (_selectedMonitorIndex >= 0 &&
            _selectedMonitorIndex < _displayLayout.Count)
        {
            monitor = _displayLayout[_selectedMonitorIndex];
            return true;
        }

        monitor = default;
        return false;
    }

    private int GetSelectedDisplayMode()
    {
        if (_menu != null)
        {
            return Mathf.Clamp(ResModeField(_menu), 0, 2);
        }

        return _displayModeDropdown != null
            ? Mathf.Clamp(_displayModeDropdown.value, 0, 2)
            : Mathf.Clamp(PlayerPrefs.GetInt("resmode", 0), 0, 2);
    }

    private void BuildGameFovControl(Transform optionsPanel)
    {
        Transform? gameContainer = optionsPanel.Find("GameSettings");
        Transform? sensitivityLabel = gameContainer?.Find("SensitivityTitle");
        Transform? invertLabel = gameContainer?.Find("InvertMouseTitle");
        Slider? sensitivitySlider =
            gameContainer?.Find("SensitivitySlider")?.GetComponent<Slider>();
        if (gameContainer == null ||
            sensitivityLabel == null ||
            invertLabel == null ||
            sensitivitySlider == null)
        {
            Plugin.Log.LogWarning(
                "The FOV control could not be added to Game Settings because " +
                "its reference rows were not found.");
            return;
        }

        RectTransform gameRect = (RectTransform)gameContainer;
        RectTransform sensitivityLabelRect = (RectTransform)sensitivityLabel;
        RectTransform invertLabelRect = (RectTransform)invertLabel;
        RectTransform sensitivitySliderRect =
            (RectTransform)sensitivitySlider.transform;

        Vector2 sensitivityLabelPosition =
            GetLocalCenter(sensitivityLabelRect, gameRect);
        Vector2 invertLabelPosition =
            GetLocalCenter(invertLabelRect, gameRect);
        Vector2 sensitivityControlPosition =
            GetLocalCenter(sensitivitySliderRect, gameRect);
        Vector2 gameRowStep = new(
            0f,
            invertLabelPosition.y - sensitivityLabelPosition.y);
        if (Mathf.Abs(gameRowStep.y) < 20f)
        {
            gameRowStep = new Vector2(0f, -110f);
        }

        Vector2 fovPosition = sensitivityLabelPosition + gameRowStep * 2f;
        CreateMenuLabel(
            sensitivityLabel.GetComponent<TMP_Text>(),
            gameContainer,
            "FixedReality2000_FovLabel",
            "FOV",
            fovPosition);

        _gameFovSlider = CreateFovSlider(
            sensitivitySlider,
            gameContainer,
            sensitivitySliderRect,
            sensitivityControlPosition + gameRowStep * 2f);
        if (_gameFovSlider != null)
        {
            _gameFovSlider.gameObject.name = InjectedFovRowName;
            Plugin.Log.LogInfo(
                $"FOV control added to Game Settings at {fovPosition}.");
        }
        else
        {
            Plugin.Log.LogWarning(
                "The reusable sensitivity slider could not be cloned for FOV.");
        }
    }

    private static int ConfigureQualityDropdown(TMP_Dropdown dropdown)
    {
        int selected = PlayerPrefs.HasKey("FixedReality2000.Quality")
            ? Mathf.Clamp(PlayerPrefs.GetInt("FixedReality2000.Quality"), 0, 3)
            : Mathf.Clamp(dropdown.value + 1, 1, 3);

        dropdown.ClearOptions();
        dropdown.AddOptions(
            new List<string> { "Very High", "High", "Medium", "Low" });
        dropdown.SetValueWithoutNotify(selected);
        dropdown.RefreshShownValue();
        return selected;
    }

    private Slider? CreateFovSlider(
        Slider? source,
        Transform parent,
        RectTransform referenceRect,
        Vector2 anchoredPosition)
    {
        if (source == null)
        {
            return null;
        }

        Slider slider = Instantiate(source, parent, worldPositionStays: false);
        RemoveInheritedGameScripts(slider.gameObject);
        RemoveInheritedSliderText(slider.gameObject);
        slider.onValueChanged = new Slider.SliderEvent();
        RectTransform rect = (RectTransform)slider.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(
            referenceRect.sizeDelta.x,
            Mathf.Max(24f, rect.sizeDelta.y));
        rect.localScale = referenceRect.localScale;
        if (slider.handleRect != null)
        {
            slider.handleRect.localScale = Vector3.one;
            Image? handleImage = slider.handleRect.GetComponent<Image>();
            if (handleImage != null)
            {
                handleImage.preserveAspect = true;
            }
        }

        slider.minValue = 50f;
        slider.maxValue = 120f;
        slider.wholeNumbers = true;
        slider.SetValueWithoutNotify(GraphicsSettingsMenuBridge.SavedFov);
        slider.onValueChanged.AddListener(ApplyFov);
        return slider;
    }

    private static TMP_Text CreateMenuLabel(
        TMP_Text template,
        Transform parent,
        string name,
        string text,
        Vector2 anchoredPosition)
    {
        TMP_Text result = Instantiate(template, parent, worldPositionStays: false);
        result.gameObject.name = name;
        result.gameObject.SetActive(true);
        result.text = text;
        result.raycastTarget = false;
        result.enableAutoSizing = false;
        result.fontSharedMaterial = template.fontSharedMaterial;

        RectTransform resultRect = (RectTransform)result.transform;
        resultRect.anchoredPosition = anchoredPosition;
        resultRect.localScale = template.rectTransform.localScale;
        resultRect.localRotation = Quaternion.identity;
        return result;
    }

    private static TMP_Dropdown CreateDropdown(
        TMP_Dropdown template,
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        IEnumerable<string> options)
    {
        TMP_Dropdown dropdown = Instantiate(template, parent, worldPositionStays: false);
        dropdown.gameObject.name = name;
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        RectTransform rect = (RectTransform)dropdown.transform;
        RectTransform templateRect = (RectTransform)template.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = templateRect.sizeDelta;
        rect.localScale = templateRect.localScale;
        dropdown.ClearOptions();
        dropdown.AddOptions(options.ToList());
        return dropdown;
    }

    private static void RemoveInheritedGameScripts(GameObject sliderObject)
    {
        MonoBehaviour[] behaviours =
            sliderObject.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null ||
                behaviour is Slider ||
                behaviour.GetType().Assembly != typeof(NewOptionsScript).Assembly)
            {
                continue;
            }

            Destroy(behaviour);
        }
    }

    private static void RemoveInheritedSliderText(GameObject sliderObject)
    {
        TMP_Text[] inheritedText =
            sliderObject.GetComponentsInChildren<TMP_Text>(includeInactive: true);
        foreach (TMP_Text text in inheritedText)
        {
            Destroy(text.gameObject);
        }
    }

    private void RefreshFovControl()
    {
        float fov = GraphicsSettingsMenuBridge.SavedFov;
        _gameFovSlider?.SetValueWithoutNotify(fov);
    }

    private static Vector2 GetLocalCenter(
        RectTransform source,
        RectTransform targetParent)
    {
        Vector3 worldCenter = source.TransformPoint(source.rect.center);
        Vector3 localCenter = targetParent.InverseTransformPoint(worldCenter);
        return new Vector2(localCenter.x, localCenter.y);
    }

    private void ApplyFov(float value)
    {
        float fov = Mathf.Clamp(Mathf.Round(value), 50f, 120f);
        SaveManager.SetFloat("FOV", fov);
        PlayerPrefs.SetFloat("camFOV", fov);
        PlayerPrefs.SetFloat("FixedReality2000.FOV", fov);
        PlayerPrefs.Save();

        if (_gameFovSlider != null &&
            !Mathf.Approximately(_gameFovSlider.value, fov))
        {
            _gameFovSlider.SetValueWithoutNotify(fov);
        }
        com.DMT.BrokenReality2000.BrokenPlayer player =
            UnityEngine.Object.FindFirstObjectByType<
                com.DMT.BrokenReality2000.BrokenPlayer>(
                FindObjectsInactive.Exclude);
        Camera camera = player != null ? player.cam : Camera.main;
        if (camera != null)
        {
            camera.fieldOfView = fov;
        }
    }

    private void ApplyChoice(ResolutionChoice choice)
    {
        if (_menu == null)
        {
            return;
        }

        int refreshRate = choice.RefreshRates.FirstOrDefault();
        if (refreshRate <= 0)
        {
            refreshRate = GetRefreshRate(Screen.currentResolution);
        }

        int modeIndex = Mathf.Clamp(ResModeField(_menu), 0, 2);
        FullScreenMode mode = modeIndex switch
        {
            0 => FullScreenMode.ExclusiveFullScreen,
            1 => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.Windowed
        };

        PlayerPrefs.SetInt("resmode", modeIndex);
        PlayerPrefs.SetInt("refreshRate", refreshRate);
        Plugin.ApplyScreenMode(
            choice.Width,
            choice.Height,
            mode,
            refreshRate,
            _selectedMonitorIndex);
        PlayerPrefs.Save();
    }

    private static TMP_Dropdown? FindDropdown(
        Transform root,
        NewOptionsScript target,
        string methodName)
    {
        TMP_Dropdown[] dropdowns =
            root.GetComponentsInChildren<TMP_Dropdown>(includeInactive: true);
        foreach (TMP_Dropdown dropdown in dropdowns)
        {
            int count = dropdown.onValueChanged.GetPersistentEventCount();
            for (int index = 0; index < count; index++)
            {
                if (dropdown.onValueChanged.GetPersistentTarget(index) == target &&
                    string.Equals(
                        dropdown.onValueChanged.GetPersistentMethodName(index),
                        methodName,
                        StringComparison.Ordinal))
                {
                    return dropdown;
                }
            }
        }

        return null;
    }

    private static Transform? FindLabel(Transform root, params string[] expected)
    {
        TMP_Text[] labels =
            root.GetComponentsInChildren<TMP_Text>(includeInactive: true);
        foreach (TMP_Text label in labels)
        {
            string normalized = NormalizeLabel(label.text);
            if (expected.Any(
                    value => string.Equals(
                        normalized,
                        NormalizeLabel(value),
                        StringComparison.Ordinal)))
            {
                return label.transform;
            }
        }

        return null;
    }

    private static string NormalizeLabel(string value)
    {
        return new string(
            value
                .Where(character => !char.IsWhiteSpace(character))
                .Select(char.ToUpperInvariant)
                .ToArray());
    }

    private static void AddMode(
        IDictionary<(int Width, int Height), HashSet<int>> modes,
        int width,
        int height,
        int refreshRate)
    {
        if (width < 640 || height < 480)
        {
            return;
        }

        var key = (width, height);
        if (!modes.TryGetValue(key, out HashSet<int>? rates))
        {
            rates = new HashSet<int>();
            modes.Add(key, rates);
        }

        if (refreshRate > 0)
        {
            rates.Add(refreshRate);
        }
    }

    private static int GetRefreshRate(Resolution resolution)
    {
        return Mathf.Max(1, Mathf.RoundToInt((float)resolution.refreshRateRatio.value));
    }

    private static string BuildPath(Transform transform)
    {
        var parts = new Stack<string>();
        Transform? current = transform;
        while (current != null)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", parts);
    }

    private sealed class ResolutionChoice
    {
        internal ResolutionChoice(
            int width,
            int height,
            int[] refreshRates)
        {
            Width = width;
            Height = height;
            RefreshRates = refreshRates;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal int[] RefreshRates { get; }

    }
}
