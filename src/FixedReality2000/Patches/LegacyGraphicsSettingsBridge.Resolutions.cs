using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
    private void BuildResolutionChoices()
    {
        var modes = new Dictionary<(int Width, int Height), HashSet<int>>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            DisplayResolutionUtility.AddMode(
                modes,
                resolution.width,
                resolution.height,
                DisplayResolutionUtility.GetRefreshRate(resolution));
        }

        Resolution current = Screen.currentResolution;
        DisplayResolutionUtility.AddMode(
            modes,
            current.width,
            current.height,
            DisplayResolutionUtility.GetRefreshRate(current));
        DisplayResolutionUtility.AddMode(
            modes,
            Screen.width,
            Screen.height,
            DisplayResolutionUtility.GetRefreshRate(current));

        if (TryGetSelectedMonitor(out DisplayInfo selectedMonitor))
        {
            DisplayResolutionUtility.AddMode(
                modes,
                selectedMonitor.width,
                selectedMonitor.height,
                DisplayResolutionUtility.GetRefreshRate(current));
        }

        _allChoices.Clear();
        foreach (var pair in modes)
        {
            _allChoices.Add(
                new DisplayResolutionChoice(
                    pair.Key.Width,
                    pair.Key.Height,
                    pair.Value.OrderByDescending(rate => rate).ToArray()));
        }

        _allChoices.Sort(DisplayResolutionUtility.CompareBySizeDescending);
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
        foreach (DisplayResolutionChoice choice in _allChoices)
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
                new DisplayResolutionChoice(
                    pair.Key.Width,
                    pair.Key.Height,
                    pair.Value.OrderByDescending(rate => rate).ToArray()));
        }

        _choices.Sort(DisplayResolutionUtility.CompareBySizeDescending);
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
        int fallbackRate =
            DisplayResolutionUtility.GetRefreshRate(Screen.currentResolution);

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

        foreach (DisplayResolutionChoice source in _allChoices)
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

}
