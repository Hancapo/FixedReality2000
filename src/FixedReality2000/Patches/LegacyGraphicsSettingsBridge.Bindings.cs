using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using InputKey = UnityEngine.InputSystem.Key;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
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
}
