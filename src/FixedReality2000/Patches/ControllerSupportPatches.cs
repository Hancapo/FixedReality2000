using com.DMT.BrokenReality2000;
using com.DMT.BrokenReality2000.Dialogue;
using com.DMT.BrokenReality2000.GameMenu;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

/// <summary>
/// Repairs the retail controller implementation while preserving its
/// original gameplay button layout.
/// </summary>
[HarmonyPatch(typeof(BrokenPlayer), "HandleMovement")]
internal static class BrokenPlayerControllerSupportPatch
{
    private readonly struct LookState
    {
        internal LookState(
            float rotationX,
            float rotationY,
            bool invertY)
        {
            RotationX = rotationX;
            RotationY = rotationY;
            InvertY = invertY;
        }

        internal float RotationX { get; }
        internal float RotationY { get; }
        internal bool InvertY { get; }
    }

    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        RotationX = AccessTools.FieldRefAccess<BrokenPlayer, float>("rotationX");
    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        RotationY = AccessTools.FieldRefAccess<BrokenPlayer, float>("rotationY");
    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        MinimumY = AccessTools.FieldRefAccess<BrokenPlayer, float>("minimumY");
    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        MaximumY = AccessTools.FieldRefAccess<BrokenPlayer, float>("maximumY");
    private static readonly AccessTools.FieldRef<BrokenPlayer, bool>
        PlayerLocked =
            AccessTools.FieldRefAccess<BrokenPlayer, bool>("playerLocked");

    [HarmonyPrefix]
    private static void DetectControllerActivity(
        BrokenPlayer __instance,
        out LookState __state)
    {
        __state = new LookState(
            RotationX(__instance),
            RotationY(__instance),
            __instance.InvertYAxis);
        Gamepad? gamepad = Gamepad.current;
        ControllerSettings.UpdateInputState(gamepad);

        Keyboard? keyboard = Keyboard.current;
        Mouse? mouse = GamepadCursorSupportPatch.FindPhysicalMouse();
        if ((keyboard != null && keyboard.anyKey.isPressed) ||
            (mouse != null &&
             (mouse.delta.ReadValue().sqrMagnitude > 0.01f ||
              mouse.scroll.ReadValue().sqrMagnitude > 0.01f ||
              mouse.leftButton.isPressed ||
              mouse.rightButton.isPressed ||
              mouse.middleButton.isPressed)))
        {
            __instance.currentControl =
                BrokenPlayer.ControlMethod.MouseKeyboard;
            ControllerInputState.LastInputWasGamepad = false;
            return;
        }

        if (gamepad != null && ControllerSettings.HasActivity(gamepad))
        {
            gamepad.leftTrigger.pressPoint = ControllerSettings.TriggerThreshold;
            gamepad.rightTrigger.pressPoint = ControllerSettings.TriggerThreshold;
            __instance.currentControl = BrokenPlayer.ControlMethod.Controller;
            ControllerInputState.LastInputWasGamepad = true;
        }
    }

    [HarmonyPostfix]
    private static void ApplyConfiguredLook(
        BrokenPlayer __instance,
        LookState __state)
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null ||
            !ControllerInputState.LastInputWasGamepad ||
            __instance.isPausing ||
            PlayerLocked(__instance) ||
            HyperlinkerChain.hyperTravel)
        {
            return;
        }

        Vector2 configuredInput =
            GetProcessedLookStick(gamepad, __state.InvertY);
        float frameScale = Time.unscaledDeltaTime * 60f;
        float targetRotationX =
            __state.RotationX +
            configuredInput.x *
            (4f * ControllerSettings.LookSensitivity * frameScale);
        float targetRotationY = Mathf.Clamp(
            __state.RotationY +
            configuredInput.y *
            (4f * ControllerSettings.LookSensitivity * frameScale),
            MinimumY(__instance),
            MaximumY(__instance));
        Vector2 correction = new(
            targetRotationX - RotationX(__instance),
            targetRotationY - RotationY(__instance));
        if (correction.sqrMagnitude <= 0.00000001f)
        {
            return;
        }

        RotationX(__instance) = targetRotationX;
        RotationY(__instance) = targetRotationY;

        Transform body = __instance.transform;
        Vector3 bodyAngles = body.localEulerAngles;
        bodyAngles.y += correction.x;
        body.localEulerAngles = bodyAngles;

        Camera? pitchCamera =
            __instance.InvertYAxis ? Camera.main : __instance.cam;
        if (pitchCamera != null)
        {
            Transform pitch = pitchCamera.transform;
            Vector3 pitchAngles = pitch.localEulerAngles;
            pitchAngles.x +=
                __state.InvertY ? correction.y : -correction.y;
            pitch.localEulerAngles = pitchAngles;
        }
    }

    internal static Vector2 GetProcessedMoveStick(Gamepad gamepad)
    {
        StickControl stick =
            ControllerSettings.StickLayout == ControllerStickLayout.Southpaw
                ? gamepad.rightStick
                : gamepad.leftStick;
        return ControllerSettings.ApplyCurve(
            ApplyRadialDeadzone(
                stick.ReadUnprocessedValue(),
                ControllerSettings.MoveDeadzone));
    }

    internal static Vector2 GetProcessedLookStick(
        Gamepad gamepad,
        bool retailInvertY)
    {
        StickControl stick =
            ControllerSettings.StickLayout == ControllerStickLayout.Southpaw
                ? gamepad.leftStick
                : gamepad.rightStick;
        Vector2 value = ControllerSettings.ApplyCurve(
            ApplyRadialDeadzone(
                stick.ReadUnprocessedValue(),
                ControllerSettings.LookDeadzone));
        if (ControllerSettings.InvertX)
        {
            value.x = -value.x;
        }

        // The retail mouse setting changes the final camera sign. Cancel that
        // sign for controller input, then apply the controller-only inversion.
        if (retailInvertY ^ ControllerSettings.InvertY)
        {
            value.y = -value.y;
        }

        return value;
    }

    internal static Vector2 ApplyRadialDeadzone(
        Vector2 value,
        float deadzone)
    {
        float magnitude = value.magnitude;
        if (magnitude <= deadzone)
        {
            return Vector2.zero;
        }

        float scaledMagnitude = Mathf.Clamp01(
            (magnitude - deadzone) / (1f - deadzone));
        return value / magnitude * scaledMagnitude;
    }
}

internal static class ControllerInputState
{
    internal static bool LastInputWasGamepad { get; set; }
}

/// <summary>
/// Keeps every retail control prompt synchronized with the active input
/// method and the keyboard bindings selected in the mod.
/// </summary>
[HarmonyPatch(typeof(BrokenPlayer), "HandleMovement")]
internal static class PlayerControlPromptPatch
{
    [HarmonyPostfix]
    private static void RefreshPrompts(BrokenPlayer __instance)
    {
        if (__instance.currentControl == BrokenPlayer.ControlMethod.Controller)
        {
            SetText(__instance.cameraClickTM, "A: PHOTO");
            SetText(__instance.cameraClickScanTM, "HOLD A: SCAN");
            SetText(__instance.cameraClickGlitchTM, "A: HACK");
            SetText(__instance.cameraLensTM, "Y: CHANGE LENS");
            SetText(__instance.cameraFilterTM, "X: CHANGE FILTER");
            SetText(__instance.BrowseRightText, "RB");
            SetText(__instance.BrowseLeftText, "LB");
            return;
        }

        string previousTool =
            PlayerKeybindings.GetLabel(PlayerBinding.PreviousTool);
        string nextTool =
            PlayerKeybindings.GetLabel(PlayerBinding.NextTool);
        string utility =
            PlayerKeybindings.GetLabel(PlayerBinding.Utility);

        SetText(__instance.cameraClickTM, "LEFT CLICK: PHOTO");
        SetText(__instance.cameraClickScanTM, "LEFT CLICK: SCAN");
        SetText(__instance.cameraClickGlitchTM, "LEFT CLICK: HACK");
        SetText(__instance.cameraLensTM, $"{utility}: CHANGE LENS");
        SetText(__instance.cameraFilterTM, $"{nextTool}: CHANGE FILTER");
        SetText(__instance.BrowseRightText, nextTool);
        SetText(__instance.BrowseLeftText, previousTool);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null && target.text != value)
        {
            target.text = value;
        }
    }
}

/// <summary>
/// Replaces the retail virtual-cursor update, which had no deadzone and used
/// scaled time even while the pause menu was active.
/// </summary>
[HarmonyPatch(typeof(GamepadCursor), "UpdateMotion")]
internal static class GamepadCursorSupportPatch
{
    private static Mouse? _trackedPhysicalMouse;
    private static Vector2 _previousPhysicalPosition;
    private static bool _physicalPositionInitialized;

    [HarmonyPrefix]
    private static bool UpdateCursor(
        Mouse ___virtualMouse,
        Mouse ___currentMouse,
        RectTransform ___cursorTransform,
        Canvas ___canvas,
        RectTransform ___canvasRectTransform,
        Camera ___mainCamera)
    {
        if (___cursorTransform == null)
        {
            return false;
        }

        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null || ___virtualMouse == null)
        {
            ControllerInputState.LastInputWasGamepad = false;
            ___cursorTransform.gameObject.SetActive(false);
            Cursor.visible = true;
            return false;
        }

        Mouse? physicalMouse = FindPhysicalMouse();
        bool physicalMouseActive = HasPhysicalMouseActivity(physicalMouse);
        StickControl cursorStick =
            ControllerSettings.StickLayout == ControllerStickLayout.Southpaw
                ? gamepad.rightStick
                : gamepad.leftStick;
        Vector2 rawStick = cursorStick.ReadUnprocessedValue();
        Vector2 stick =
            BrokenPlayerControllerSupportPatch.ApplyRadialDeadzone(
                rawStick,
                ControllerSettings.MoveDeadzone);
        bool gamepadActive =
            rawStick.magnitude > ControllerSettings.MoveDeadzone ||
            gamepad.buttonSouth.isPressed;

        if (physicalMouseActive)
        {
            // Dialogue locks BrokenPlayer.HandleMovement, which is where the
            // retail game normally switches back to controller input. Do not
            // erase the active dialogue selection while changing to a
            // physical mouse: without it, returning to the gamepad leaves the
            // answer list with no Submit target and soft-locks the
            // conversation.
            ControllerMenuNavigation.LeaveFocusMode(
                clearSelection: !NATEM_SOURCEDATA.isDialogue);
            ControllerInputState.LastInputWasGamepad = false;
            ___cursorTransform.gameObject.SetActive(false);
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
            physicalMouse?.MakeCurrent();

            if (physicalMouse != null)
            {
                Vector2 physicalPosition = physicalMouse.position.ReadValue();
                InputState.Change(___virtualMouse.position, physicalPosition);
                InputState.Change(___virtualMouse.delta, Vector2.zero);
            }

            ___virtualMouse.CopyState<MouseState>(
                out MouseState physicalMouseState);
            physicalMouseState.WithButton(MouseButton.Left, false);
            InputState.Change(___virtualMouse, physicalMouseState);
            return false;
        }

        if (ControllerMenuNavigation.FocusModeActive)
        {
            ___cursorTransform.gameObject.SetActive(false);
            Cursor.visible = false;
            ___virtualMouse.CopyState<MouseState>(
                out MouseState navigationMouseState);
            navigationMouseState.WithButton(MouseButton.Left, false);
            InputState.Change(___virtualMouse, navigationMouseState);
            return false;
        }

        if (gamepadActive)
        {
            if (!ControllerInputState.LastInputWasGamepad &&
                ___currentMouse != null)
            {
                Vector2 physicalPosition =
                    ___currentMouse.position.ReadValue();
                InputState.Change(___virtualMouse.position, physicalPosition);
                InputState.Change(___virtualMouse.delta, Vector2.zero);
            }

            ControllerInputState.LastInputWasGamepad = true;
        }

        if (!ControllerInputState.LastInputWasGamepad)
        {
            ___cursorTransform.gameObject.SetActive(false);
            Cursor.visible = true;
            return false;
        }

        ___cursorTransform.gameObject.SetActive(true);
        Cursor.visible = false;
        Vector2 delta =
            stick * ControllerSettings.CursorSpeed * Time.unscaledDeltaTime;
        Vector2 position = ___virtualMouse.position.ReadValue() + delta;
        position.x = Mathf.Clamp(
            position.x,
            Screen.width * 0.1f,
            Screen.width * 0.9f);
        position.y = Mathf.Clamp(
            position.y,
            Screen.height * 0.1f,
            Screen.height * 0.9f);

        InputState.Change(___virtualMouse.position, position);
        InputState.Change(___virtualMouse.delta, delta);
        ___virtualMouse.CopyState<MouseState>(out MouseState mouseState);
        mouseState.WithButton(
            MouseButton.Left,
            gamepad.buttonSouth.isPressed);
        InputState.Change(___virtualMouse, mouseState);

        Camera? eventCamera =
            ___canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : ___mainCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            ___canvasRectTransform,
            position,
            eventCamera,
            out Vector2 localPoint);
        ___cursorTransform.anchoredPosition = localPoint;
        return false;
    }

    internal static bool HasPhysicalMouseActivity(Mouse? physicalMouse)
    {
        if (physicalMouse == null)
        {
            _trackedPhysicalMouse = null;
            _physicalPositionInitialized = false;
            return false;
        }

        if (!ReferenceEquals(_trackedPhysicalMouse, physicalMouse))
        {
            _trackedPhysicalMouse = physicalMouse;
            _previousPhysicalPosition =
                physicalMouse.position.ReadValue();
            _physicalPositionInitialized = true;
        }

        Vector2 position = physicalMouse.position.ReadValue();
        bool moved =
            _physicalPositionInitialized &&
            (position - _previousPhysicalPosition).sqrMagnitude > 0.25f;
        _previousPhysicalPosition = position;
        _physicalPositionInitialized = true;

        return
            moved ||
            physicalMouse.delta.ReadValue().sqrMagnitude > 0.01f ||
            physicalMouse.leftButton.isPressed ||
            physicalMouse.rightButton.isPressed ||
            physicalMouse.middleButton.isPressed ||
            physicalMouse.scroll.ReadValue().sqrMagnitude > 0.01f;
    }

    internal static Mouse? FindPhysicalMouse()
    {
        foreach (InputDevice device in InputSystem.devices)
        {
            if (!(device is Mouse mouse))
            {
                continue;
            }

            bool isVirtual =
                string.Equals(
                    mouse.name,
                    "VirtualMouse",
                    System.StringComparison.OrdinalIgnoreCase) ||
                mouse.layout.IndexOf(
                    "Virtual",
                    System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isVirtual)
            {
                return mouse;
            }
        }

        return null;
    }
}

[HarmonyPatch(typeof(BrokenPlayer), "Update")]
internal static class PauseMenuHybridCursorPatch
{
    [HarmonyPostfix]
    private static void RestorePhysicalCursor(BrokenPlayer __instance)
    {
        if (!__instance.isPausing)
        {
            return;
        }

        Mouse? physicalMouse = GamepadCursorSupportPatch.FindPhysicalMouse();
        if (!GamepadCursorSupportPatch.HasPhysicalMouseActivity(physicalMouse))
        {
            return;
        }

        ControllerInputState.LastInputWasGamepad = false;
        __instance.currentControl = BrokenPlayer.ControlMethod.MouseKeyboard;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
        physicalMouse?.MakeCurrent();
    }
}

/// <summary>
/// Keeps dialogue input usable when the player alternates between a gamepad
/// and keyboard/mouse. BrokenPlayer deliberately skips HandleMovement during
/// conversations, so retail never updates its control method in that state.
/// </summary>
[HarmonyPatch(typeof(BrokenPlayer), "Update")]
internal static class DialogueHybridInputRecoveryPatch
{
    [HarmonyPrefix]
    private static void RestoreDialogueInput(BrokenPlayer __instance)
    {
        DialogueManager? manager = DialogueManager.instance;
        if (manager == null ||
            manager.CharacterDialogue == null ||
            !manager.CharacterDialogue.activeInHierarchy)
        {
            return;
        }

        Keyboard? keyboard = Keyboard.current;
        Mouse? physicalMouse =
            GamepadCursorSupportPatch.FindPhysicalMouse();
        bool keyboardActive =
            keyboard != null && keyboard.anyKey.isPressed;
        bool mouseActive =
            GamepadCursorSupportPatch.HasPhysicalMouseActivity(
                physicalMouse);

        if (keyboardActive || mouseActive)
        {
            __instance.currentControl =
                BrokenPlayer.ControlMethod.MouseKeyboard;
            ControllerInputState.LastInputWasGamepad = false;

            if (mouseActive)
            {
                Cursor.lockState = CursorLockMode.Confined;
                Cursor.visible = true;
                physicalMouse?.MakeCurrent();
            }

            return;
        }

        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null || !ControllerSettings.HasActivity(gamepad))
        {
            return;
        }

        __instance.currentControl =
            BrokenPlayer.ControlMethod.Controller;
        ControllerInputState.LastInputWasGamepad = true;
        Cursor.visible = false;

        EventSystem? eventSystem = EventSystem.current;
        if (eventSystem == null ||
            IsValidDialogueSelection(
                eventSystem.currentSelectedGameObject,
                manager))
        {
            return;
        }

        GameObject? replacement = FindDialogueSelection(manager);
        if (replacement != null)
        {
            eventSystem.SetSelectedGameObject(replacement);
            Plugin.Log.LogDebug(
                $"Recovered dialogue controller focus with " +
                $"'{replacement.name}'.");
        }
    }

    private static bool IsValidDialogueSelection(
        GameObject? selected,
        DialogueManager manager)
    {
        if (selected == null ||
            !selected.activeInHierarchy ||
            !selected.transform.IsChildOf(
                manager.CharacterDialogue.transform))
        {
            return false;
        }

        Selectable? selectable = selected.GetComponent<Selectable>();
        return
            selectable != null &&
            selectable.IsActive() &&
            selectable.IsInteractable();
    }

    private static GameObject? FindDialogueSelection(
        DialogueManager manager)
    {
        AnswerMenu? answerMenu = manager.AnswerMenu;
        if (answerMenu != null &&
            answerMenu.gameObject.activeInHierarchy)
        {
            foreach (GameObject answer in answerMenu.answers)
            {
                if (answer == null || !answer.activeInHierarchy)
                {
                    continue;
                }

                Button? button = answer.GetComponent<Button>();
                if (button != null &&
                    button.IsActive() &&
                    button.IsInteractable())
                {
                    return answer;
                }
            }
        }

        Button? conversationButton = manager.ConversationButton;
        if (conversationButton != null &&
            conversationButton.enabled &&
            conversationButton.IsActive() &&
            conversationButton.IsInteractable())
        {
            return conversationButton.gameObject;
        }

        return null;
    }
}

[HarmonyPatch(typeof(BrokenPlayer), "PrimaryAction")]
internal static class ControllerPrimaryRumblePatch
{
    [HarmonyPostfix]
    private static void Pulse()
    {
        if (ControllerInputState.LastInputWasGamepad)
        {
            ControllerSettings.PulseRumble(0.18f, 0.42f, 0.09f);
        }
    }
}

/// <summary>
/// Restores controller progression for the examination dialogues in 00_room.
/// Retail mouse input can click their full-screen conversation button while
/// the player is locked, but the equivalent gamepad action is rejected by
/// BrokenPlayer.HandleMovement.
/// </summary>
[HarmonyPatch(typeof(BrokenPlayer), "Update")]
internal static class RoomExaminationDialogueControllerPatch
{
    private static int _handledFrame = -1;
    private static int _selectedAnswer;
    private static bool _answerMenuWasActive;
    private static bool _verticalAxisLatched;

    [HarmonyPostfix]
    private static void ContinueWithController()
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null ||
            !string.Equals(
                SceneManager.GetActiveScene().name,
                "00_room",
                System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DialogueManager? manager = DialogueManager.instance;
        if (manager == null ||
            manager.CharacterDialogue == null ||
            !manager.CharacterDialogue.activeInHierarchy)
        {
            ResetAnswerState();
            return;
        }

        if (GamepadCursorSupportPatch.HasPhysicalMouseActivity(
                GamepadCursorSupportPatch.FindPhysicalMouse()))
        {
            ControllerInputState.LastInputWasGamepad = false;
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
            return;
        }

        if (manager.AnswerMenu != null &&
            manager.AnswerMenu.gameObject.activeInHierarchy)
        {
            HandleAnswers(gamepad, manager.AnswerMenu);
            return;
        }

        ResetAnswerState();
        if (!gamepad.buttonSouth.wasPressedThisFrame ||
            _handledFrame == Time.frameCount ||
            manager.ConversationButton == null ||
            !manager.ConversationButton.enabled ||
            !manager.ConversationButton.interactable)
        {
            return;
        }

        _handledFrame = Time.frameCount;
        ActivateGamepadMode();
        manager.ConversationButton.onClick.Invoke();
        ControllerSettings.PulseRumble(0.12f, 0.22f, 0.06f);
    }

    private static void HandleAnswers(
        Gamepad gamepad,
        AnswerMenu answerMenu)
    {
        int activeCount = CountActiveAnswers(answerMenu);
        if (activeCount == 0)
        {
            ResetAnswerState();
            return;
        }

        if (!_answerMenuWasActive)
        {
            _selectedAnswer = 0;
            _answerMenuWasActive = true;
            _verticalAxisLatched = false;
            SelectAnswer(answerMenu, _selectedAnswer);
        }

        int direction = 0;
        if (gamepad.dpad.up.wasPressedThisFrame)
        {
            direction = -1;
        }
        else if (gamepad.dpad.down.wasPressedThisFrame)
        {
            direction = 1;
        }
        else
        {
            Vector2 movement =
                BrokenPlayerControllerSupportPatch.GetProcessedMoveStick(
                    gamepad);
            if (Mathf.Abs(movement.y) < 0.35f)
            {
                _verticalAxisLatched = false;
            }
            else if (!_verticalAxisLatched)
            {
                direction = movement.y > 0f ? -1 : 1;
                _verticalAxisLatched = true;
            }
        }

        if (direction != 0)
        {
            ActivateGamepadMode();
            _selectedAnswer =
                (_selectedAnswer + direction + activeCount) % activeCount;
            SelectAnswer(answerMenu, _selectedAnswer);
            ControllerSettings.PulseRumble(0.06f, 0.1f, 0.035f);
        }

        if (!gamepad.buttonSouth.wasPressedThisFrame ||
            _handledFrame == Time.frameCount)
        {
            return;
        }

        GameObject? answer =
            GetActiveAnswer(answerMenu, _selectedAnswer);
        Button? button = answer?.GetComponent<Button>();
        if (button == null || !button.enabled || !button.interactable)
        {
            return;
        }

        _handledFrame = Time.frameCount;
        ActivateGamepadMode();
        button.onClick.Invoke();
        EventSystem.current?.SetSelectedGameObject(null);
        ControllerSettings.PulseRumble(0.12f, 0.22f, 0.06f);
        ResetAnswerState();
    }

    private static int CountActiveAnswers(AnswerMenu answerMenu)
    {
        int count = 0;
        foreach (GameObject answer in answerMenu.answers)
        {
            if (answer != null && answer.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    private static GameObject? GetActiveAnswer(
        AnswerMenu answerMenu,
        int activeIndex)
    {
        int index = 0;
        foreach (GameObject answer in answerMenu.answers)
        {
            if (answer == null || !answer.activeInHierarchy)
            {
                continue;
            }

            if (index == activeIndex)
            {
                return answer;
            }

            index++;
        }

        return null;
    }

    private static void SelectAnswer(
        AnswerMenu answerMenu,
        int activeIndex)
    {
        GameObject? answer = GetActiveAnswer(answerMenu, activeIndex);
        if (answer == null)
        {
            return;
        }

        EventSystem.current?.SetSelectedGameObject(answer);
    }

    private static void ResetAnswerState()
    {
        _answerMenuWasActive = false;
        _selectedAnswer = 0;
        _verticalAxisLatched = false;
    }

    private static void ActivateGamepadMode()
    {
        ControllerInputState.LastInputWasGamepad = true;
        Cursor.visible = false;
    }
}

[HarmonyPatch(typeof(BrokenPlayer), "SecondaryAction")]
internal static class ControllerSecondaryRumblePatch
{
    [HarmonyPostfix]
    private static void Pulse()
    {
        if (ControllerInputState.LastInputWasGamepad)
        {
            ControllerSettings.PulseRumble(0.28f, 0.16f, 0.08f);
        }
    }
}
