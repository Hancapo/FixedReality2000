using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
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

    private readonly List<DisplayResolutionChoice> _allChoices = new();
    private readonly List<DisplayResolutionChoice> _choices = new();
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

        DisplayResolutionChoice choice = _choices[choiceIndex];
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
            $"resolution control '{TransformPathUtility.GetPath(_resolutionDropdown.transform)}'; " +
            $"FOV slider {(_gameFovSlider != null ? "added to Game" : "not found")}.");
    }

}
