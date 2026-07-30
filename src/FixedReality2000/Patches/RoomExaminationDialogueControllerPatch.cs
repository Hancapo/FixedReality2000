using com.DMT.BrokenReality2000;
using com.DMT.BrokenReality2000.Dialogue;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

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
