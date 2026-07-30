using UnityEngine;
using UnityEngine.InputSystem;

namespace FixedReality2000;

internal static partial class ControllerSettings
{
    internal static void UpdateInputState(Gamepad? gamepad)
    {
        if (_inputFrame == Time.frameCount)
        {
            return;
        }

        _inputFrame = Time.frameCount;
        foreach (ControllerAction action in MenuOrder)
        {
            bool previous = CurrentState.TryGetValue(action, out bool old) && old;
            bool current =
                gamepad != null &&
                IsBindingHeld(gamepad, GetBinding(action));
            PreviousState[action] = previous;
            CurrentState[action] = current;
            PressedState[action] = current && !previous;
            ReleasedState[action] = !current && previous;
        }

        if (SprintMode == ControllerSprintMode.Toggle &&
            Pressed(ControllerAction.Sprint))
        {
            _sprintToggle = !_sprintToggle;
        }
    }

    internal static bool Held(ControllerAction action)
    {
        UpdateInputState(Gamepad.current);
        return CurrentState.TryGetValue(action, out bool value) && value;
    }

    internal static bool Pressed(ControllerAction action)
    {
        UpdateInputState(Gamepad.current);
        return PressedState.TryGetValue(action, out bool value) && value;
    }

    internal static bool Released(ControllerAction action)
    {
        UpdateInputState(Gamepad.current);
        return ReleasedState.TryGetValue(action, out bool value) && value;
    }

    internal static bool SprintActive()
    {
        UpdateInputState(Gamepad.current);
        return SprintMode == ControllerSprintMode.Toggle
            ? _sprintToggle
            : Held(ControllerAction.Sprint);
    }

    internal static void CancelSprintToggle()
    {
        _sprintToggle = false;
    }

    internal static Vector2 ApplyCurve(Vector2 value)
    {
        float magnitude = Mathf.Clamp01(value.magnitude);
        if (magnitude <= 0f)
        {
            return Vector2.zero;
        }

        float curved = ResponseCurve switch
        {
            ControllerResponseCurve.Linear => magnitude,
            ControllerResponseCurve.Dynamic =>
                Mathf.SmoothStep(0f, 1f, magnitude),
            _ => magnitude * magnitude
        };
        return value / magnitude * curved;
    }


    internal static bool HasActivity(Gamepad gamepad)
    {
        return
            gamepad.leftStick.ReadUnprocessedValue().magnitude > MoveDeadzone ||
            gamepad.rightStick.ReadUnprocessedValue().magnitude > LookDeadzone ||
            gamepad.leftTrigger.ReadValue() > TriggerThreshold ||
            gamepad.rightTrigger.ReadValue() > TriggerThreshold ||
            gamepad.buttonSouth.isPressed ||
            gamepad.buttonNorth.isPressed ||
            gamepad.buttonEast.isPressed ||
            gamepad.buttonWest.isPressed ||
            gamepad.leftShoulder.isPressed ||
            gamepad.rightShoulder.isPressed ||
            gamepad.leftStickButton.isPressed ||
            gamepad.rightStickButton.isPressed ||
            gamepad.startButton.isPressed ||
            gamepad.selectButton.isPressed ||
            gamepad.dpad.IsPressed();
    }


}
