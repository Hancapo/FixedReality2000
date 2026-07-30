using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

/// <summary>
/// Gives controller sliders an explicit edit state. Outside that state,
/// directional input navigates to another Selectable instead of silently
/// changing the slider value.
/// </summary>
internal static class SliderControllerEditMode
{
    private static Slider? _editingSlider;
    private static int _cancelConsumedFrame = -1;

    internal static bool WasCancelConsumedThisFrame =>
        _cancelConsumedFrame == Time.frameCount;

    internal static bool IsEditing(Slider slider)
    {
        return
            _editingSlider == slider &&
            slider != null &&
            slider.IsActive() &&
            slider.IsInteractable() &&
            slider.gameObject.activeInHierarchy;
    }

    internal static void Update(
        Gamepad gamepad,
        Slider? selectedSlider)
    {
        if (_editingSlider != null &&
            _editingSlider != selectedSlider)
        {
            Exit();
        }

        if (selectedSlider == null)
        {
            return;
        }

        if (gamepad.buttonSouth.wasPressedThisFrame)
        {
            if (IsEditing(selectedSlider))
            {
                Exit();
            }
            else
            {
                _editingSlider = selectedSlider;
                ControllerSettings.PulseRumble(
                    0.06f,
                    0.12f,
                    0.04f);
            }

            return;
        }

        if (IsEditing(selectedSlider) &&
            gamepad.buttonEast.wasPressedThisFrame)
        {
            _cancelConsumedFrame = Time.frameCount;
            Exit();
            ControllerSettings.PulseRumble(
                0.04f,
                0.08f,
                0.03f);
        }
    }

    internal static void Exit()
    {
        _editingSlider = null;
    }

    internal static bool IsControllerDirectionActive()
    {
        Gamepad? gamepad = Gamepad.current;
        return
            gamepad != null &&
            (gamepad.dpad.ReadValue().sqrMagnitude > 0.01f ||
             gamepad.leftStick.ReadUnprocessedValue().sqrMagnitude > 0.16f);
    }
}

[HarmonyPatch(typeof(Slider), nameof(Slider.OnMove))]
internal static class SliderControllerNavigationPatch
{
    [HarmonyPrefix]
    private static bool RequireActivationBeforeEditing(
        Slider __instance,
        AxisEventData eventData)
    {
        if (!ControllerMenuNavigation.FocusModeActive ||
            !ControllerInputState.LastInputWasGamepad ||
            !SliderControllerEditMode.IsControllerDirectionActive())
        {
            return true;
        }

        if (SliderControllerEditMode.IsEditing(__instance))
        {
            if (eventData.moveDir is MoveDirection.Left or MoveDirection.Right)
            {
                ApplyCoherentStep(
                    __instance,
                    eventData.moveDir == MoveDirection.Right ? 1 : -1);
                eventData.Use();
                return false;
            }

            // Vertical movement leaves edit mode and continues through the
            // menu. This avoids trapping the user in a horizontal slider.
            SliderControllerEditMode.Exit();
        }

        Selectable? destination =
            FindBestDirectionalSelectable(__instance, eventData.moveDir) ??
            FindUnityFallback(__instance, eventData.moveDir);

        if (ControllerNavigationUtility.IsUsable(destination))
        {
            EventSystem.current?.SetSelectedGameObject(
                destination!.gameObject);
        }

        eventData.Use();
        return false;
    }

    private static Selectable? FindBestDirectionalSelectable(
        Slider source,
        MoveDirection direction)
    {
        Vector2 origin = ControllerNavigationUtility.GetCenter(source);
        Selectable? best = null;
        float bestScore = float.PositiveInfinity;

        foreach (Selectable candidate in
                 GetNavigationScope(source)
                 .GetComponentsInChildren<Selectable>(true))
        {
            if (candidate == source ||
                !ControllerNavigationUtility.IsUsable(candidate))
            {
                continue;
            }

            Vector2 delta =
                ControllerNavigationUtility.GetCenter(candidate) - origin;
            float primary;
            float perpendicular;
            switch (direction)
            {
                case MoveDirection.Left when delta.x < -1f:
                    primary = -delta.x;
                    perpendicular = Mathf.Abs(delta.y);
                    break;
                case MoveDirection.Right when delta.x > 1f:
                    primary = delta.x;
                    perpendicular = Mathf.Abs(delta.y);
                    break;
                case MoveDirection.Up when delta.y > 1f:
                    primary = delta.y;
                    perpendicular = Mathf.Abs(delta.x);
                    break;
                case MoveDirection.Down when delta.y < -1f:
                    primary = -delta.y;
                    perpendicular = Mathf.Abs(delta.x);
                    break;
                default:
                    continue;
            }

            // Strongly prefer controls on the same row or column. Unity's
            // automatic navigation instead favours edge geometry, which fails
            // on the deliberately asymmetric two-column options layout.
            float score = primary + perpendicular * 4f;
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static Transform GetNavigationScope(Slider source)
    {
        return
            source.GetComponentInParent<OptionsMenu>(true)?.transform ??
            source.transform.root;
    }

    private static Selectable? FindUnityFallback(
        Slider source,
        MoveDirection direction)
    {
        return direction switch
        {
            MoveDirection.Left => source.FindSelectableOnLeft(),
            MoveDirection.Right => source.FindSelectableOnRight(),
            MoveDirection.Up => source.FindSelectableOnUp(),
            MoveDirection.Down => source.FindSelectableOnDown(),
            _ => null
        };
    }

    private static void ApplyCoherentStep(Slider slider, int direction)
    {
        float step = GetSemanticStep(slider);
        float currentStep =
            Mathf.Round((slider.value - slider.minValue) / step);
        float next =
            slider.minValue + (currentStep + direction) * step;

        // Quantize once more to keep values such as 0.15 from accumulating
        // floating-point noise in the config and in the value label.
        next = Mathf.Round(next * 10000f) / 10000f;
        slider.value = Mathf.Clamp(next, slider.minValue, slider.maxValue);
        ControllerSettings.PulseRumble(0.025f, 0.055f, 0.018f);
    }

    private static float GetSemanticStep(Slider slider)
    {
        string name = slider.gameObject.name;
        if (name.Contains("Deadzone", StringComparison.OrdinalIgnoreCase))
        {
            return 0.01f;
        }

        if (name.Contains("CursorSpeed", StringComparison.OrdinalIgnoreCase))
        {
            return 25f;
        }

        if (name.Contains("TriggerThreshold", StringComparison.OrdinalIgnoreCase))
        {
            return 0.05f;
        }

        if (name.Contains("LookSens", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Sensitivity", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Vibration", StringComparison.OrdinalIgnoreCase))
        {
            return 0.05f;
        }

        if (name.Contains("FOV", StringComparison.OrdinalIgnoreCase))
        {
            return 1f;
        }

        float range = slider.maxValue - slider.minValue;
        if (slider.wholeNumbers)
        {
            return range > 500f ? 25f : 1f;
        }

        if (range <= 1f)
        {
            return 0.05f;
        }

        if (range <= 5f)
        {
            return 0.05f;
        }

        return range <= 20f ? 0.5f : 1f;
    }

}
