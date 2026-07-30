using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
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
            OptionsUiUtility.SetTabState(
                _gamepadSubpageButtons[index],
                active);
        }
    }
}
