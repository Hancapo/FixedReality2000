using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

/// <summary>
/// Prepares the retail options UI for Unity's native controller navigation.
/// It deliberately does not synthesize movement or submit events: the active
/// UI input module already sends those and processing them twice makes
/// dropdowns close immediately or skip controls.
/// </summary>
[DefaultExecutionOrder(-10000)]
internal sealed class ControllerMenuNavigation : MonoBehaviour
{
    private readonly List<Selectable> _selectables = new();
    private LegacyGraphicsSettingsBridge? _bridge;
    private Transform? _navigationRoot;
    private bool _navigationPrepared;

    internal static bool FocusModeActive { get; private set; }

    private void Awake()
    {
        _bridge = GetComponent<LegacyGraphicsSettingsBridge>();
        _navigationRoot =
            GetComponentInParent<OptionsMenu>(true)?.transform ??
            transform.parent ??
            transform;

        // This component is hosted by OptionsPanel and is disabled as soon as
        // that screen closes. Keep a small focus guard on GameMenuNEW itself so
        // transitions to Home, Tasks, Items, and Data cannot strand the
        // EventSystem with a null or inactive selection.
        PauseMenu? pauseMenu = GetComponentInParent<PauseMenu>(true);
        if (pauseMenu != null &&
            pauseMenu.GetComponent<PauseMenuControllerFocusGuard>() == null)
        {
            pauseMenu.gameObject.AddComponent<PauseMenuControllerFocusGuard>();
        }
    }

    private void OnEnable()
    {
        _navigationPrepared = false;
        if (ControllerInputState.LastInputWasGamepad)
        {
            EnterFocusMode();
        }
    }

    private void OnDisable()
    {
        SliderControllerEditMode.Exit();
        LeaveFocusMode(clearSelection: true);
        _navigationPrepared = false;
    }

    private void Update()
    {
        Gamepad? gamepad = Gamepad.current;
        EventSystem? eventSystem = EventSystem.current;
        if (gamepad == null ||
            eventSystem == null ||
            _bridge?.IsCapturingBinding == true)
        {
            return;
        }

        if (!_navigationPrepared)
        {
            PrepareNavigation();
        }

        if (!FocusModeActive && HasNavigationInput(gamepad))
        {
            EnterFocusMode();
        }

        if (!FocusModeActive)
        {
            return;
        }

        GameObject? selected = eventSystem.currentSelectedGameObject;
        Selectable? selectable = selected?.GetComponent<Selectable>();
        if (selectable == null ||
            !selectable.IsActive() ||
            !selectable.IsInteractable() ||
            !selected!.activeInHierarchy)
        {
            SliderControllerEditMode.Exit();
            SelectInitial(eventSystem);
            return;
        }

        SliderControllerEditMode.Update(
            gamepad,
            selectable as Slider);
    }

    internal static void LeaveFocusMode(bool clearSelection)
    {
        SliderControllerEditMode.Exit();
        FocusModeActive = false;
        if (clearSelection)
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }
    }

    internal static void RestoreFocus(GameObject selection)
    {
        FocusModeActive = true;
        ControllerInputState.LastInputWasGamepad = true;
        Cursor.visible = false;
        EventSystem.current?.SetSelectedGameObject(selection);
    }

    private static bool HasNavigationInput(Gamepad gamepad)
    {
        return
            gamepad.dpad.ReadValue().sqrMagnitude > 0.01f ||
            gamepad.leftStick.ReadUnprocessedValue().sqrMagnitude > 0.16f ||
            gamepad.rightStick.ReadUnprocessedValue().sqrMagnitude > 0.16f ||
            gamepad.buttonSouth.wasPressedThisFrame;
    }

    private void EnterFocusMode()
    {
        FocusModeActive = true;
        ControllerInputState.LastInputWasGamepad = true;
        Cursor.visible = false;
        EventSystem? eventSystem = EventSystem.current;
        if (eventSystem != null &&
            eventSystem.currentSelectedGameObject == null)
        {
            SelectInitial(eventSystem);
        }
    }

    private void PrepareNavigation()
    {
        RefreshSelectables();
        foreach (Selectable selectable in _selectables)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            navigation.wrapAround = false;
            selectable.navigation = navigation;
        }

        _navigationPrepared = true;
    }

    private void RefreshSelectables()
    {
        _selectables.Clear();
        foreach (Selectable selectable in
                 (_navigationRoot ?? transform)
                 .GetComponentsInChildren<Selectable>(true))
        {
            if (selectable != null)
            {
                _selectables.Add(selectable);
            }
        }
    }

    private void SelectInitial(EventSystem eventSystem)
    {
        RefreshSelectables();
        Selectable? initial = null;
        Vector2 initialPosition = default;
        foreach (Selectable selectable in _selectables)
        {
            if (!selectable.IsActive() ||
                !selectable.IsInteractable() ||
                !selectable.gameObject.activeInHierarchy ||
                IsNavigationDockButton(selectable.transform))
            {
                continue;
            }

            Vector2 position = GetPosition(selectable);
            if (initial == null ||
                position.y > initialPosition.y + 0.5f ||
                (Mathf.Abs(position.y - initialPosition.y) <= 0.5f &&
                 position.x < initialPosition.x))
            {
                initial = selectable;
                initialPosition = position;
            }
        }

        if (initial != null)
        {
            eventSystem.SetSelectedGameObject(initial.gameObject);
        }
    }

    private static bool IsNavigationDockButton(Transform transform)
    {
        Transform? current = transform.parent;
        while (current != null)
        {
            if (current.name.Contains(
                    "Dock",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static Vector2 GetPosition(Selectable selectable)
    {
        if (selectable.transform is RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center);
        }

        return selectable.transform.position;
    }
}

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
        if (!HasNavigationInput(gamepad) &&
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

        Selectable? replacement = FindInitialSelectable(focusScope);
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

    private static Selectable? FindInitialSelectable(Transform screen)
    {
        Selectable? initial = null;
        Vector2 initialPosition = default;
        foreach (Selectable selectable in
                 screen.GetComponentsInChildren<Selectable>(true))
        {
            if (!selectable.IsActive() ||
                !selectable.IsInteractable() ||
                !selectable.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector2 position = GetCenter(selectable);
            if (initial == null ||
                position.y > initialPosition.y + 0.5f ||
                (Mathf.Abs(position.y - initialPosition.y) <= 0.5f &&
                 position.x < initialPosition.x))
            {
                initial = selectable;
                initialPosition = position;
            }
        }

        return initial;
    }

    private static bool HasNavigationInput(Gamepad gamepad)
    {
        return
            gamepad.dpad.ReadValue().sqrMagnitude > 0.01f ||
            gamepad.leftStick.ReadUnprocessedValue().sqrMagnitude > 0.16f ||
            gamepad.buttonSouth.wasPressedThisFrame;
    }

    private static Vector2 GetCenter(Selectable selectable)
    {
        if (selectable.transform is RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center);
        }

        return selectable.transform.position;
    }
}

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

        if (IsUsable(destination))
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
        Vector2 origin = GetCenter(source);
        Selectable? best = null;
        float bestScore = float.PositiveInfinity;

        foreach (Selectable candidate in
                 GetNavigationScope(source)
                 .GetComponentsInChildren<Selectable>(true))
        {
            if (candidate == source || !IsUsable(candidate))
            {
                continue;
            }

            Vector2 delta = GetCenter(candidate) - origin;
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

    private static bool IsUsable(Selectable? selectable)
    {
        return
            selectable != null &&
            selectable.IsActive() &&
            selectable.IsInteractable() &&
            selectable.gameObject.activeInHierarchy;
    }

    private static Vector2 GetCenter(Selectable selectable)
    {
        if (selectable.transform is RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center);
        }

        return selectable.transform.position;
    }
}

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
