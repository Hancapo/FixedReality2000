using System;
using System.Collections.Generic;
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

        if (!FocusModeActive &&
            ControllerNavigationUtility.HasNavigationInput(
                gamepad,
                includeRightStick: true))
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
        Selectable? initial =
            ControllerNavigationUtility.FindTopLeftSelectable(
                _navigationRoot ?? transform,
                selectable => !IsNavigationDockButton(selectable.transform));

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

}
