using System.Collections.Generic;
using System.Linq;
using com.DMT.BrokenReality2000;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed class GraphicsSettingsMenuBridge : MonoBehaviour
{
    private static readonly AccessTools.FieldRef<OptionsMenu, TMP_Dropdown>
        ResolutionDropdownField =
            AccessTools.FieldRefAccess<OptionsMenu, TMP_Dropdown>("resolutionDropdown");

    private static readonly AccessTools.FieldRef<OptionsMenu, TMP_Dropdown>
        RefreshRateDropdownField =
            AccessTools.FieldRefAccess<OptionsMenu, TMP_Dropdown>("refreshRateDropdown");

    private static readonly AccessTools.FieldRef<OptionsMenu, TMP_Dropdown>
        DisplayModeDropdownField =
            AccessTools.FieldRefAccess<OptionsMenu, TMP_Dropdown>("displayModeDropdown");

    private static readonly AccessTools.FieldRef<OptionsMenu, Slider>
        FovSliderField =
            AccessTools.FieldRefAccess<OptionsMenu, Slider>("fovSlider");

    private static readonly AccessTools.FieldRef<OptionsMenu, GameConfig>
        GameConfigField =
            AccessTools.FieldRefAccess<OptionsMenu, GameConfig>("gameConfig");

    private readonly List<DisplayResolutionChoice> _resolutionChoices = new();
    private readonly List<int> _refreshRates = new();

    private OptionsMenu? _menu;
    private TMP_Dropdown? _resolutionDropdown;
    private TMP_Dropdown? _refreshRateDropdown;
    private TMP_Dropdown? _displayModeDropdown;
    private Slider? _fovSlider;
    private GameConfig? _gameConfig;
    private bool _updatingControls;

    internal static float SavedFov =>
        Mathf.Clamp(
            PlayerPrefs.HasKey("FixedReality2000.FOV")
                ? PlayerPrefs.GetFloat("FixedReality2000.FOV")
                : SaveManager.GetFloat(
                    "FOV",
                    PlayerPrefs.GetFloat("camFOV", 60f)),
            50f,
            120f);

    internal static void Attach(OptionsMenu menu)
    {
        GraphicsSettingsMenuBridge bridge =
            menu.GetComponent<GraphicsSettingsMenuBridge>() ??
            menu.gameObject.AddComponent<GraphicsSettingsMenuBridge>();
        bridge.Initialize(menu);
    }

    internal void RefreshFromSavedValues()
    {
        if (_menu == null)
        {
            return;
        }

        SelectSavedResolution();
        SelectSavedDisplayMode();
        ConfigureFovSlider();
    }

    private void Initialize(OptionsMenu menu)
    {
        if (_menu != null)
        {
            RefreshFromSavedValues();
            return;
        }

        _menu = menu;
        _resolutionDropdown = ResolutionDropdownField(menu);
        _refreshRateDropdown = RefreshRateDropdownField(menu);
        _displayModeDropdown = DisplayModeDropdownField(menu);
        _fovSlider = FovSliderField(menu);
        _gameConfig = GameConfigField(menu);

        BuildResolutionChoices();
        PopulateResolutionDropdown();
        SelectSavedDisplayMode();
        ConfigureFovSlider();

        _resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        _refreshRateDropdown.onValueChanged.AddListener(OnRefreshRateChanged);
        _displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);
        _fovSlider.onValueChanged.AddListener(OnFovChanged);

        Plugin.Log.LogInfo(
            $"Graphics settings UI fixed: {_resolutionChoices.Count} unique resolutions, " +
            $"{_refreshRates.Count} refresh rates, FOV {SavedFov:0}.");
    }

    private void OnDestroy()
    {
        if (_resolutionDropdown != null)
        {
            _resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
        }

        if (_refreshRateDropdown != null)
        {
            _refreshRateDropdown.onValueChanged.RemoveListener(OnRefreshRateChanged);
        }

        if (_fovSlider != null)
        {
            _fovSlider.onValueChanged.RemoveListener(OnFovChanged);
        }

        if (_displayModeDropdown != null)
        {
            _displayModeDropdown.onValueChanged.RemoveListener(
                OnDisplayModeChanged);
        }
    }

    private void BuildResolutionChoices()
    {
        var modesBySize = new Dictionary<(int Width, int Height), HashSet<int>>();
        foreach (Resolution mode in Screen.resolutions)
        {
            DisplayResolutionUtility.AddMode(
                modesBySize,
                mode.width,
                mode.height,
                DisplayResolutionUtility.GetRefreshRate(mode));
        }

        Resolution current = Screen.currentResolution;
        DisplayResolutionUtility.AddMode(
            modesBySize,
            current.width,
            current.height,
            DisplayResolutionUtility.GetRefreshRate(current));
        DisplayResolutionUtility.AddMode(
            modesBySize,
            Screen.width,
            Screen.height,
            DisplayResolutionUtility.GetRefreshRate(current));

        Display display = Display.main;
        if (display != null)
        {
            DisplayResolutionUtility.AddMode(
                modesBySize,
                display.systemWidth,
                display.systemHeight,
                DisplayResolutionUtility.GetRefreshRate(current));

        }

        _resolutionChoices.Clear();
        foreach (var pair in modesBySize)
        {
            _resolutionChoices.Add(
                new DisplayResolutionChoice(
                    pair.Key.Width,
                    pair.Key.Height,
                    pair.Value.OrderByDescending(rate => rate).ToArray()));
        }

        _resolutionChoices.Sort(
            DisplayResolutionUtility.CompareBySizeDescending);
    }

    private void PopulateResolutionDropdown()
    {
        if (_resolutionDropdown == null)
        {
            return;
        }

        _updatingControls = true;
        _resolutionDropdown.ClearOptions();
        _resolutionDropdown.AddOptions(
            _resolutionChoices
                .Select(
                    choice =>
                        UltrawideResolutionTests.Format(
                            choice.Width,
                            choice.Height))
                .ToList());
        SelectSavedResolution();
        _updatingControls = false;
    }

    private void SelectSavedResolution()
    {
        if (_resolutionDropdown == null || _resolutionChoices.Count == 0)
        {
            return;
        }

        int width = PlayerPrefs.HasKey("resx")
            ? PlayerPrefs.GetInt("resx")
            : _gameConfig?.ResolutionX ?? Screen.width;
        int height = PlayerPrefs.HasKey("resy")
            ? PlayerPrefs.GetInt("resy")
            : _gameConfig?.ResolutionY ?? Screen.height;

        int index = _resolutionChoices.FindIndex(
            choice => choice.Width == width && choice.Height == height);
        if (index < 0)
        {
            index = _resolutionChoices.FindIndex(
                choice => choice.Width == Screen.width && choice.Height == Screen.height);
        }

        index = Mathf.Max(0, index);
        _updatingControls = true;
        _resolutionDropdown.SetValueWithoutNotify(index);
        _resolutionDropdown.RefreshShownValue();
        PopulateRefreshRates(_resolutionChoices[index]);
        _updatingControls = false;
    }

    private void PopulateRefreshRates(DisplayResolutionChoice choice)
    {
        if (_refreshRateDropdown == null)
        {
            return;
        }

        _refreshRates.Clear();
        _refreshRates.AddRange(choice.RefreshRates.Where(rate => rate > 0));
        if (_refreshRates.Count == 0)
        {
            _refreshRates.Add(
                DisplayResolutionUtility.GetRefreshRate(
                    Screen.currentResolution));
        }

        _refreshRateDropdown.ClearOptions();
        _refreshRateDropdown.AddOptions(
            _refreshRates.Select(rate => $"{rate} hz").ToList());

        int savedRate = PlayerPrefs.GetInt(
            "refreshRate",
            _gameConfig?.RefreshRate ??
            DisplayResolutionUtility.GetRefreshRate(Screen.currentResolution));
        int rateIndex = _refreshRates.IndexOf(savedRate);
        if (rateIndex < 0)
        {
            rateIndex = 0;
        }

        _refreshRateDropdown.SetValueWithoutNotify(rateIndex);
        _refreshRateDropdown.RefreshShownValue();
    }

    private void SelectSavedDisplayMode()
    {
        if (_displayModeDropdown == null)
        {
            return;
        }

        int savedMode = Mathf.Clamp(
            PlayerPrefs.GetInt(
                "resmode",
                _gameConfig != null
                    ? (int)_gameConfig.DisplayMode
                    : 0),
            0,
            2);
        _displayModeDropdown.SetValueWithoutNotify(savedMode);
        _displayModeDropdown.RefreshShownValue();
        if (_gameConfig != null)
        {
            _gameConfig.DisplayMode = (DisplayMode)savedMode;
        }
    }

    private void ConfigureFovSlider()
    {
        if (_fovSlider == null)
        {
            return;
        }

        _fovSlider.minValue = 50f;
        _fovSlider.maxValue = 120f;
        _fovSlider.wholeNumbers = true;
        _fovSlider.SetValueWithoutNotify(SavedFov);
        if (_gameConfig != null)
        {
            _gameConfig.FOV = SavedFov;
        }
    }

    private void OnResolutionChanged(int index)
    {
        if (_updatingControls ||
            index < 0 ||
            index >= _resolutionChoices.Count)
        {
            return;
        }

        DisplayResolutionChoice choice = _resolutionChoices[index];
        _updatingControls = true;
        PopulateRefreshRates(choice);
        _updatingControls = false;

        if (_gameConfig != null)
        {
            _gameConfig.ResolutionX = choice.Width;
            _gameConfig.ResolutionY = choice.Height;
        }

        PlayerPrefs.SetInt("resx", choice.Width);
        PlayerPrefs.SetInt("resy", choice.Height);
        ApplySelectedResolution();
    }

    private void OnRefreshRateChanged(int index)
    {
        if (_updatingControls || index < 0 || index >= _refreshRates.Count)
        {
            return;
        }

        int refreshRate = _refreshRates[index];
        PlayerPrefs.SetInt("refreshRate", refreshRate);
        if (_gameConfig != null)
        {
            _gameConfig.RefreshRate = refreshRate;
        }

        ApplySelectedResolution();
    }

    private void OnDisplayModeChanged(int index)
    {
        if (_updatingControls)
        {
            return;
        }

        int modeIndex = Mathf.Clamp(index, 0, 2);
        PlayerPrefs.SetInt("resmode", modeIndex);
        if (_gameConfig != null)
        {
            _gameConfig.DisplayMode = (DisplayMode)modeIndex;
        }

        ApplySelectedResolution();
    }

    private void ApplySelectedResolution()
    {
        if (_resolutionDropdown == null ||
            _resolutionDropdown.value < 0 ||
            _resolutionDropdown.value >= _resolutionChoices.Count)
        {
            return;
        }

        DisplayResolutionChoice choice =
            _resolutionChoices[_resolutionDropdown.value];
        int modeIndex = _displayModeDropdown != null
            ? Mathf.Clamp(_displayModeDropdown.value, 0, 2)
            : Mathf.Clamp(PlayerPrefs.GetInt("resmode", 0), 0, 2);
        FullScreenMode mode = modeIndex switch
        {
            0 => FullScreenMode.ExclusiveFullScreen,
            1 => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.Windowed
        };

        int refreshRate =
            _refreshRates.Count > 0 &&
            _refreshRateDropdown != null &&
            _refreshRateDropdown.value >= 0 &&
            _refreshRateDropdown.value < _refreshRates.Count
                ? _refreshRates[_refreshRateDropdown.value]
                : 0;
        if (refreshRate <= 0)
        {
            refreshRate =
                DisplayResolutionUtility.GetRefreshRate(Screen.currentResolution);
        }

        PlayerPrefs.SetInt("resmode", modeIndex);
        Plugin.ApplyScreenMode(
            choice.Width,
            choice.Height,
            mode,
            refreshRate);
        PlayerPrefs.Save();
    }

    private void OnFovChanged(float value)
    {
        float fov = Mathf.Clamp(Mathf.Round(value), 50f, 120f);
        SaveManager.SetFloat("FOV", fov);
        PlayerPrefs.SetFloat("camFOV", fov);
        if (_gameConfig != null)
        {
            _gameConfig.FOV = fov;
        }

        BrokenPlayer player =
            UnityEngine.Object.FindFirstObjectByType<BrokenPlayer>(
                FindObjectsInactive.Exclude);
        if (player != null && player.cam != null)
        {
            player.cam.fieldOfView = fov;
        }
        else if (Camera.main != null)
        {
            Camera.main.fieldOfView = fov;
        }
    }

}
