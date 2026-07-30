using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

/// <summary>
/// Recovers controller focus after retail pause-menu transitions. Several
/// screens disable their previous panel before assigning a selection in the
/// next panel, and Options additionally disables its navigation component.
/// Mouse input remains untouched; recovery only happens after fresh controller
/// navigation input.
/// </summary>
[DefaultExecutionOrder(-9000)]
internal sealed class PauseMenuControllerFocusGuard : MonoBehaviour
{
    private static readonly string[] MainScreenNames =
    {
        "HomePanel",
        "TasksPanel",
        "ItemsPanel",
        "DataPanel",
        "OptionsPanel",
        "TutorialPanel"
    };

    private EventSystem? _eventSystem;
    private Transform? _lastFocusScope;

    private void Update()
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return;
        }

        Transform? activeScreen = FindActiveMainScreen();
        if (activeScreen == null)
        {
            _lastFocusScope = null;
            return;
        }

        if (activeScreen.name == "HomePanel")
        {
            PrepareHomeNavigation(activeScreen);
        }

        Transform focusScope = FindActiveModal(activeScreen) ?? activeScreen;
        if (focusScope != activeScreen)
        {
            PrepareConfirmationNavigation(focusScope);
        }

        bool focusScopeChanged = focusScope != _lastFocusScope;
        _lastFocusScope = focusScope;
        if (!ControllerNavigationUtility.HasNavigationInput(gamepad) &&
            !(focusScopeChanged && ControllerInputState.LastInputWasGamepad))
        {
            return;
        }

        _eventSystem ??= FindPauseEventSystem();
        if (_eventSystem == null)
        {
            return;
        }

        GameObject? selected = _eventSystem.currentSelectedGameObject;
        Selectable? selectable = selected?.GetComponent<Selectable>();
        if (selected != null &&
            selected.activeInHierarchy &&
            selected.transform.IsChildOf(focusScope) &&
            selectable != null &&
            selectable.IsActive() &&
            selectable.IsInteractable())
        {
            return;
        }

        Selectable? replacement =
            ControllerNavigationUtility.FindTopLeftSelectable(focusScope);
        if (replacement == null)
        {
            return;
        }

        ControllerMenuNavigation.RestoreFocus(replacement.gameObject);
        Plugin.Log.LogDebug(
            $"Recovered controller focus on pause screen " +
            $"'{focusScope.name}' with '{replacement.name}'.");
    }

    private static Transform? FindActiveModal(Transform activeScreen)
    {
        foreach (Transform candidate in
                 activeScreen.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == "Confirmation" &&
                candidate.gameObject.activeInHierarchy)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void PrepareConfirmationNavigation(Transform confirmation)
    {
        Button? logOut = FindButton(confirmation, "Exit_1");
        Button? quitGame = FindButton(confirmation, "Exit_2");
        if (logOut == null || quitGame == null)
        {
            return;
        }

        Navigation logOutNavigation = logOut.navigation;
        Navigation quitGameNavigation = quitGame.navigation;
        if (logOutNavigation.mode == Navigation.Mode.Explicit &&
            logOutNavigation.selectOnDown == quitGame &&
            quitGameNavigation.mode == Navigation.Mode.Explicit &&
            quitGameNavigation.selectOnUp == logOut)
        {
            return;
        }

        SetExplicitNavigation(
            logOut,
            up: null,
            down: quitGame,
            left: null,
            right: null);
        SetExplicitNavigation(
            quitGame,
            up: logOut,
            down: null,
            left: null,
            right: null);
        Plugin.Log.LogDebug(
            "Prepared controller navigation for the exit confirmation.");
    }

    private void PrepareHomeNavigation(Transform homeScreen)
    {
        Button? tasks = FindButton(homeScreen, "TasksButton");
        Button? items = FindButton(homeScreen, "ItemsButton");
        Button? data = FindButton(homeScreen, "DataButton");
        Button? options = FindButton(homeScreen, "OptionsButton");
        Button? exit = FindButton(homeScreen, "Exit");
        if (tasks == null || items == null || data == null)
        {
            return;
        }

        if (HasExpectedHomeNavigation(
                tasks,
                items,
                data,
                options,
                exit))
        {
            return;
        }

        // The retail buttons are staggered and use different widths. Their
        // animated rectangles make Unity's Automatic mode intermittently
        // decide that Data/Skins is a better vertical neighbour than Items.
        // An explicit graph preserves the visual order throughout animation.
        SetExplicitNavigation(
            tasks,
            up: null,
            down: items,
            left: null,
            right: options);
        SetExplicitNavigation(
            items,
            up: tasks,
            down: data,
            left: null,
            right: options);
        SetExplicitNavigation(
            data,
            up: items,
            down: null,
            left: null,
            right: options ?? exit);

        if (options != null)
        {
            SetExplicitNavigation(
                options,
                up: tasks,
                down: exit,
                left: data,
                right: null);
        }

        if (exit != null)
        {
            SetExplicitNavigation(
                exit,
                up: options ?? data,
                down: null,
                left: data,
                right: null);
        }

        Plugin.Log.LogDebug(
            "Prepared an explicit controller navigation graph for pause Home.");
    }

    private static bool HasExpectedHomeNavigation(
        Button tasks,
        Button items,
        Button data,
        Button? options,
        Button? exit)
    {
        Navigation tasksNavigation = tasks.navigation;
        Navigation itemsNavigation = items.navigation;
        Navigation dataNavigation = data.navigation;
        if (tasksNavigation.mode != Navigation.Mode.Explicit ||
            tasksNavigation.selectOnDown != items ||
            itemsNavigation.mode != Navigation.Mode.Explicit ||
            itemsNavigation.selectOnUp != tasks ||
            itemsNavigation.selectOnDown != data ||
            dataNavigation.mode != Navigation.Mode.Explicit ||
            dataNavigation.selectOnUp != items)
        {
            return false;
        }

        if (options != null)
        {
            Navigation optionsNavigation = options.navigation;
            if (optionsNavigation.mode != Navigation.Mode.Explicit ||
                optionsNavigation.selectOnLeft != data ||
                optionsNavigation.selectOnDown != exit)
            {
                return false;
            }
        }

        return
            exit == null ||
            (exit.navigation.mode == Navigation.Mode.Explicit &&
             exit.navigation.selectOnUp == (options ?? data));
    }

    private static Button? FindButton(Transform root, string name)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
            {
                return button;
            }
        }

        return null;
    }

    private static void SetExplicitNavigation(
        Selectable selectable,
        Selectable? up,
        Selectable? down,
        Selectable? left,
        Selectable? right)
    {
        Navigation navigation = selectable.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.wrapAround = false;
        navigation.selectOnUp = up;
        navigation.selectOnDown = down;
        navigation.selectOnLeft = left;
        navigation.selectOnRight = right;
        selectable.navigation = navigation;
    }

    private Transform? FindActiveMainScreen()
    {
        foreach (string screenName in MainScreenNames)
        {
            Transform? screen = transform.Find(screenName);
            if (screen != null && screen.gameObject.activeInHierarchy)
            {
                return screen;
            }
        }

        return null;
    }

    private EventSystem? FindPauseEventSystem()
    {
        Transform? pauseRoot = transform.parent?.parent;
        return pauseRoot?.GetComponentInChildren<EventSystem>(true) ??
               EventSystem.current;
    }

}
