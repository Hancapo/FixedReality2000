using com.DMT.BrokenReality2000;
using com.DMT.BrokenReality2000.Dialogue;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

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
