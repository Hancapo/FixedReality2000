using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
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

    private void ApplyChoice(DisplayResolutionChoice choice)
    {
        if (_menu == null)
        {
            return;
        }

        int refreshRate = choice.RefreshRates.FirstOrDefault();
        if (refreshRate <= 0)
        {
            refreshRate =
                DisplayResolutionUtility.GetRefreshRate(Screen.currentResolution);
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

}
