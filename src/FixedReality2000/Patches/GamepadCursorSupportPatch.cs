using com.DMT.BrokenReality2000.GameMenu;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace FixedReality2000.Patches;

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
