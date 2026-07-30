using com.DMT.BrokenReality2000;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
