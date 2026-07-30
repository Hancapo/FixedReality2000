using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
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
            OptionsUiUtility.SetTabState(button, active);
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
}
