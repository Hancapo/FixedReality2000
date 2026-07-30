using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

/// <summary>
/// The retail pause-menu stack returns to the GameMenuNEW root when leaving
/// Options, but that root is only a container: it does not reactivate the Home
/// panel. The result is an active pause menu with every screen disabled and no
/// selectable object, which looks exactly like a disconnected controller.
/// </summary>
[HarmonyPatch(
    typeof(PauseMenuStackHandler),
    nameof(PauseMenuStackHandler.GoBackOneScreen))]
internal static class PauseMenuBackNavigationPatch
{
    [HarmonyPrefix]
    private static bool KeepOpenDropdownCancelInsideCurrentPage(
        PauseMenuStackHandler __instance,
        out bool __state)
    {
        if (__instance != null &&
            Gamepad.current?.buttonEast.wasPressedThisFrame == true &&
            CloseActiveExitConfirmation(__instance.transform))
        {
            __state = false;
            return false;
        }

        __state =
            __instance != null &&
            __instance.MenuStack.Count == 0 &&
            IsHomeScreenActive(__instance.transform) &&
            Gamepad.current?.buttonEast.wasPressedThisFrame == true;
        return
            !DropdownControllerCancelGuard.WasConsumedThisFrame &&
            !SliderControllerEditMode.WasCancelConsumedThisFrame;
    }

    private static bool CloseActiveExitConfirmation(Transform menuRoot)
    {
        Transform? home = menuRoot.Find("HomePanel");
        if (home == null || !home.gameObject.activeInHierarchy)
        {
            return false;
        }

        Transform? confirmation = null;
        foreach (Transform candidate in
                 home.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Confirmation" &&
                candidate.gameObject.activeInHierarchy)
            {
                confirmation = candidate;
                break;
            }
        }

        if (confirmation == null)
        {
            return false;
        }

        confirmation.gameObject.SetActive(false);
        foreach (Button button in home.GetComponentsInChildren<Button>(true))
        {
            if (button.name != "Exit" ||
                !button.IsActive() ||
                !button.IsInteractable() ||
                !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            ControllerMenuNavigation.RestoreFocus(button.gameObject);
            break;
        }

        Plugin.Log.LogDebug(
            "Controller Back closed the exit confirmation.");
        return true;
    }

    [HarmonyPostfix]
    private static void RestoreHomeScreenWhenStackReachesRoot(
        PauseMenuStackHandler __instance,
        bool __runOriginal,
        bool __state)
    {
        if (!__runOriginal || __instance == null)
        {
            return;
        }

        if (__state)
        {
            UnpauseFromHome(__instance);
            return;
        }

        if (
            __instance == null ||
            !__instance.gameObject.activeInHierarchy ||
            HasActiveMainScreen(__instance.transform))
        {
            return;
        }

        PauseMenu? pauseMenu = __instance.GetComponent<PauseMenu>();
        if (pauseMenu == null)
        {
            return;
        }

        pauseMenu.ReturnToHomeScreen();
        SelectFirstHomeControl(__instance.transform);
        Plugin.Log.LogDebug(
            "Recovered the retail pause-menu stack after controller Back.");
    }

    private static bool IsHomeScreenActive(Transform menuRoot)
    {
        Transform? home = menuRoot.Find("HomePanel");
        return home != null && home.gameObject.activeSelf;
    }

    private static void UnpauseFromHome(
        PauseMenuStackHandler stackHandler)
    {
        EventSystemHelper? helper = stackHandler.eSysHelper;
        if (helper == null ||
            helper.player == null ||
            !helper.player.isPausing)
        {
            return;
        }

        AccessTools.Method(
                typeof(com.DMT.BrokenReality2000.BrokenPlayer),
                "NATEMOS")
            ?.Invoke(helper.player, null);
        ControllerMenuNavigation.LeaveFocusMode(clearSelection: true);
        Plugin.Log.LogDebug(
            "Controller Back closed the pause menu from Home.");
    }

    private static bool HasActiveMainScreen(Transform menuRoot)
    {
        ReadOnlySpan<string> screenNames =
        [
            "HomePanel",
            "TasksPanel",
            "ItemsPanel",
            "DataPanel",
            "OptionsPanel",
            "TutorialPanel"
        ];

        foreach (string screenName in screenNames)
        {
            Transform? screen = menuRoot.Find(screenName);
            if (screen != null && screen.gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private static void SelectFirstHomeControl(Transform menuRoot)
    {
        Transform? home = menuRoot.Find("HomePanel");
        if (home == null)
        {
            return;
        }

        Button? firstButton = null;
        Vector2 firstPosition = default;
        foreach (Button button in home.GetComponentsInChildren<Button>(true))
        {
            if (!button.IsActive() ||
                !button.IsInteractable() ||
                !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector2 position =
                button.transform is RectTransform rect
                    ? rect.TransformPoint(rect.rect.center)
                    : button.transform.position;
            if (firstButton == null ||
                position.y > firstPosition.y + 0.5f ||
                (Mathf.Abs(position.y - firstPosition.y) <= 0.5f &&
                 position.x < firstPosition.x))
            {
                firstButton = button;
                firstPosition = position;
            }
        }

        if (firstButton != null)
        {
            ControllerMenuNavigation.RestoreFocus(firstButton.gameObject);
        }
    }
}
