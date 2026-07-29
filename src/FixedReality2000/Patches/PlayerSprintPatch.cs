using com.DMT.BrokenReality2000;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FixedReality2000.Patches;

[HarmonyPatch(typeof(BrokenPlayer), "HandleMovement")]
internal static class PlayerSprintPatch
{
    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        MovementSpeed =
            AccessTools.FieldRefAccess<BrokenPlayer, float>("movementSpeed");

    private readonly struct MovementState
    {
        internal MovementState(Vector3 position, CharacterController? controller)
        {
            Position = position;
            Controller = controller;
        }

        internal Vector3 Position { get; }

        internal CharacterController? Controller { get; }
    }

    [HarmonyPrefix]
    private static void CaptureStart(BrokenPlayer __instance, out MovementState __state)
    {
        __state = new MovementState(
            __instance.transform.position,
            __instance.GetComponent<CharacterController>());
    }

    [HarmonyPostfix]
    private static void ApplySprint(BrokenPlayer __instance, MovementState __state)
    {
        Vector3 horizontalDelta = __instance.transform.position - __state.Position;
        horizontalDelta.y = 0f;
        CharacterController? controller = __state.Controller;

        Gamepad? gamepad = Gamepad.current;
        if (controller != null &&
            controller.enabled &&
            gamepad != null &&
            ControllerInputState.LastInputWasGamepad &&
            !__instance.isPausing &&
            !HyperlinkerChain.hyperTravel)
        {
            Vector2 stick =
                BrokenPlayerControllerSupportPatch.GetProcessedMoveStick(gamepad);
            Vector3 localDirection = new(stick.x, 0f, stick.y);
            Vector3 worldDirection =
                Quaternion.Euler(
                    0f,
                    __instance.transform.localEulerAngles.y,
                    0f) *
                localDirection;
            Vector3 desiredDelta =
                worldDirection.sqrMagnitude > 0f
                    ? worldDirection.normalized *
                      (MovementSpeed(__instance) *
                       Time.deltaTime *
                       Mathf.Clamp01(stick.magnitude))
                    : Vector3.zero;

            controller.Move(desiredDelta - horizontalDelta);
            horizontalDelta =
                __instance.transform.position - __state.Position;
            horizontalDelta.y = 0f;
        }

        if (ControllerSettings.SprintMode == ControllerSprintMode.Toggle &&
            (!ControllerInputState.LastInputWasGamepad ||
             gamepad == null ||
             BrokenPlayerControllerSupportPatch
                 .GetProcessedMoveStick(gamepad).sqrMagnitude <= 0.0001f))
        {
            ControllerSettings.CancelSprintToggle();
        }

        bool sprintInput =
            PlayerKeybindings.IsPressed(PlayerBinding.Sprint) ||
            (ControllerInputState.LastInputWasGamepad &&
             ControllerSettings.SprintActive());
        bool sprinting =
            Plugin.EnableSprint.Value &&
            sprintInput &&
            horizontalDelta.sqrMagnitude > 0.00000001f;

        if (sprinting && controller != null && controller.enabled)
        {
            float extraMultiplier = Mathf.Max(0f, Plugin.SprintMultiplier.Value - 1f);
            controller.Move(horizontalDelta * extraMultiplier);

            horizontalDelta = __instance.transform.position - __state.Position;
            horizontalDelta.y = 0f;
        }

        Plugin.RecordPlayerMovement(__instance, horizontalDelta.magnitude, sprinting);
    }
}
