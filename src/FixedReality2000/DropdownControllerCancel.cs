using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FixedReality2000.Patches;

/// <summary>
/// B is both TMP_Dropdown's native Cancel input and the retail pause menu's
/// global Back input. Without arbitration, one press closes the dropdown and
/// immediately leaves the current menu page. Whichever system runs first
/// marks the frame so the global Back handler ignores that same press.
/// </summary>
internal static class DropdownControllerCancelGuard
{
    private static int _consumedFrame = -1;

    internal static bool WasConsumedThisFrame =>
        _consumedFrame == Time.frameCount;

    internal static bool ConsumeExpandedDropdown()
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null ||
            !gamepad.buttonEast.wasPressedThisFrame)
        {
            return false;
        }

        foreach (TMP_Dropdown dropdown in
                 UnityEngine.Object.FindObjectsByType<TMP_Dropdown>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None))
        {
            if (dropdown == null ||
                !dropdown.IsExpanded ||
                !dropdown.gameObject.activeInHierarchy)
            {
                continue;
            }

            _consumedFrame = Time.frameCount;
            dropdown.Hide();
            ControllerMenuNavigation.RestoreFocus(dropdown.gameObject);
            return true;
        }

        return WasConsumedThisFrame;
    }

    internal static void MarkNativeCancel(TMP_Dropdown dropdown)
    {
        Gamepad? gamepad = Gamepad.current;
        if (dropdown != null &&
            dropdown.IsExpanded &&
            gamepad != null &&
            gamepad.buttonEast.wasPressedThisFrame)
        {
            _consumedFrame = Time.frameCount;
        }
    }
}

[HarmonyPatch(typeof(TMP_Dropdown), nameof(TMP_Dropdown.OnCancel))]
internal static class DropdownNativeCancelPatch
{
    [HarmonyPrefix]
    private static void MarkControllerCancel(TMP_Dropdown __instance)
    {
        DropdownControllerCancelGuard.MarkNativeCancel(__instance);
    }
}

[HarmonyPatch(
    typeof(EventSystemHelper),
    nameof(EventSystemHelper.HandlePauseMovement))]
internal static class PauseMenuDropdownCancelPatch
{
    [HarmonyPrefix]
    private static bool CloseDropdownBeforeGlobalBack()
    {
        return
            !SliderControllerEditMode.WasCancelConsumedThisFrame &&
            !DropdownControllerCancelGuard.ConsumeExpandedDropdown();
    }
}
