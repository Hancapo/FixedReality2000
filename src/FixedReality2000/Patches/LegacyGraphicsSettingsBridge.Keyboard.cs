using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
    private void BuildKeyboardBindings(
        Transform keyboardPage,
        RectTransform controlsRect,
        TMP_Text labelTemplate,
        Button buttonTemplate)
    {
        float top = controlsRect.rect.yMax - 55f;
        const float RowSpacing = 76f;
        float leftLabelX = controlsRect.rect.xMin + 145f;
        float leftButtonX = controlsRect.rect.xMin + 445f;
        float rightLabelX = controlsRect.rect.center.x + 145f;
        float rightButtonX = controlsRect.rect.xMax - 145f;

        for (int index = 0; index < PlayerKeybindings.MenuOrder.Length; index++)
        {
            PlayerBinding binding = PlayerKeybindings.MenuOrder[index];
            bool rightColumn = index >= 5;
            int row = rightColumn ? index - 5 : index;
            float y = top - row * RowSpacing;
            float labelX = rightColumn ? rightLabelX : leftLabelX;
            float buttonX = rightColumn ? rightButtonX : leftButtonX;

            TMP_Text label = CreateMenuLabel(
                labelTemplate,
                keyboardPage,
                $"FixedReality2000_{binding}Label",
                PlayerKeybindings.GetActionLabel(binding),
                new Vector2(labelX, y));
            label.fontSize = Mathf.Min(label.fontSize, 19f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            Button button = Instantiate(
                buttonTemplate,
                keyboardPage,
                worldPositionStays: false);
            button.gameObject.name =
                $"FixedReality2000_{binding}Binding";
            RemoveInheritedGameScripts(button.gameObject);
            button.onClick = new Button.ButtonClickedEvent();
            RectTransform buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(buttonX, y);
            buttonRect.sizeDelta = new Vector2(230f, 54f);
            buttonRect.localScale = Vector3.one;
            buttonRect.localRotation = Quaternion.identity;

            foreach (Transform child in button.transform)
            {
                if (child.name.Contains("Arrow", StringComparison.OrdinalIgnoreCase))
                {
                    child.gameObject.SetActive(false);
                }
            }

            TMP_Text? value =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (value != null)
            {
                value.gameObject.SetActive(true);
                value.text = PlayerKeybindings.GetLabel(binding);
                value.fontSize = Mathf.Min(value.fontSize, 16f);
                value.textWrappingMode = TextWrappingModes.NoWrap;
                value.overflowMode = TextOverflowModes.Ellipsis;
            }

            button.onClick.AddListener(() => BeginBindingCapture(binding));
            _bindingButtons[binding] = button;
        }

        TMP_Text hint = CreateMenuLabel(
            labelTemplate,
            keyboardPage,
            "FixedReality2000_BindingHint",
            "SELECT A BINDING  ·  BACKSPACE CANCELS  ·  DELETE UNBINDS",
            new Vector2(-135f, controlsRect.rect.yMin + 34f));
        RectTransform hintRect = (RectTransform)hint.transform;
        hintRect.sizeDelta = new Vector2(650f, 30f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.fontSize = Mathf.Min(hint.fontSize, 12f);
        hint.color = new Color(1f, 1f, 1f, 0.72f);
        hint.textWrappingMode = TextWrappingModes.NoWrap;

        Button resetButton = Instantiate(
            buttonTemplate,
            keyboardPage,
            worldPositionStays: false);
        resetButton.gameObject.name =
            "FixedReality2000_ResetBindingsButton";
        RemoveInheritedGameScripts(resetButton.gameObject);
        resetButton.onClick = new Button.ButtonClickedEvent();
        RectTransform resetRect = (RectTransform)resetButton.transform;
        resetRect.anchorMin = new Vector2(0.5f, 0.5f);
        resetRect.anchorMax = new Vector2(0.5f, 0.5f);
        resetRect.pivot = new Vector2(0.5f, 0.5f);
        resetRect.anchoredPosition = new Vector2(
            rightLabelX + 30f,
            top - 4f * RowSpacing);
        resetRect.sizeDelta = new Vector2(300f, 64f);
        resetRect.localScale = Vector3.one;
        resetRect.localRotation = Quaternion.identity;
        foreach (Transform child in resetButton.transform)
        {
            if (child.name.Contains(
                    "Arrow",
                    StringComparison.OrdinalIgnoreCase))
            {
                child.gameObject.SetActive(false);
            }
        }

        TMP_Text? resetText =
            resetButton.GetComponentInChildren<TMP_Text>(
                includeInactive: true);
        if (resetText != null)
        {
            resetText.gameObject.SetActive(true);
            resetText.text = "RESET DEFAULTS";
            resetText.fontSize = Mathf.Min(resetText.fontSize, 16f);
            resetText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        resetButton.onClick.AddListener(
            () =>
            {
                CancelBindingCapture();
                PlayerKeybindings.ResetAll();
                RefreshBindingButtons();
            });
    }

    private void BuildControlsSubpageDock(Transform controlsPage)
    {
        if (_videoSubpageButtons == null ||
            _videoSubpageButtons.Length < 2 ||
            _videoSubpageButtons[0].transform.parent is not RectTransform sourceDock)
        {
            Plugin.Log.LogWarning(
                "Controls subpage navigation could not clone the Video dock.");
            return;
        }

        RectTransform controlsRect = (RectTransform)controlsPage;
        RectTransform dock = Instantiate(
            sourceDock,
            controlsPage,
            worldPositionStays: false);
        dock.gameObject.name = "FixedReality2000_ControlsSubpageDock";
        dock.anchoredPosition = new Vector2(
            0f,
            controlsRect.rect.yMin - dock.sizeDelta.y * 0.5f - 4f);
        dock.localScale = Vector3.one;
        dock.localRotation = Quaternion.identity;

        Button[] clonedButtons =
            dock.GetComponentsInChildren<Button>(includeInactive: true);
        List<Button> controlsButtons = clonedButtons.Take(2).ToList();
        controlsButtons.Add(
            Instantiate(controlsButtons[1], dock, worldPositionStays: false));
        _controlsSubpageButtons = controlsButtons.ToArray();
        for (int index = clonedButtons.Length - 1; index >= 2; index--)
        {
            Destroy(clonedButtons[index].gameObject);
        }
        HorizontalLayoutGroup? layout =
            dock.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            layout.enabled = false;
        }

        string[] labels = { "MOUSE", "KEYBOARD", "GAMEPAD" };
        const float spacing = 8f;
        float width =
            (dock.sizeDelta.x - spacing * 4f) / 3f;
        for (int index = 0; index < _controlsSubpageButtons.Length; index++)
        {
            int selected = index;
            Button button = _controlsSubpageButtons[index];
            button.gameObject.name =
                $"FixedReality2000_{labels[index]}Button";
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SetControlsSubpage(selected));
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot =
                new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, dock.sizeDelta.y - 8f);
            rect.anchoredPosition =
                new Vector2((index - 1f) * (width + spacing), 0f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            TMP_Text? text =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.text = labels[index];
            }
        }

        dock.SetAsLastSibling();
    }

    private void SetControlsSubpage(int selectedIndex)
    {
        if (_controlsSubpages == null)
        {
            return;
        }

        int selected = Mathf.Clamp(
            selectedIndex,
            0,
            _controlsSubpages.Length - 1);
        if (selected != 2)
        {
            CancelControllerBindingCapture();
        }
        for (int index = 0; index < _controlsSubpages.Length; index++)
        {
            bool active = index == selected;
            _controlsSubpages[index].SetActive(active);

            if (_controlsSubpageButtons == null ||
                index >= _controlsSubpageButtons.Length)
            {
                continue;
            }

            OptionsUiUtility.SetTabState(
                _controlsSubpageButtons[index],
                active);
        }
    }
}
