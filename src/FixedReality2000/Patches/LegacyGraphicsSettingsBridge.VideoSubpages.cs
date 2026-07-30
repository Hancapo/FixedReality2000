using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
    private void BuildVideoSubpages(
        Transform videoContainer,
        Transform resolutionLabel,
        Transform resolutionControl,
        Transform displayLabel,
        Transform displayControl,
        Transform qualityLabel,
        Transform qualityControl,
        Transform? aspectLabel,
        Transform aspectControl,
        Transform? monitorLabel,
        Transform monitorControl,
        Transform? fpsLabel,
        Transform fpsControl,
        Transform? vsyncLabel,
        Transform vsyncControl,
        Transform? textureLabel,
        Transform textureControl,
        Transform? aaLabel,
        Transform aaControl,
        Transform? postAaLabel,
        Transform postAaControl,
        Vector2 rowStep)
    {
        Transform? dockTemplate =
            videoContainer.parent.Find("ButtonDock/BG PC");
        if (dockTemplate == null ||
            aspectLabel == null ||
            monitorLabel == null ||
            fpsLabel == null ||
            vsyncLabel == null ||
            textureLabel == null ||
            aaLabel == null ||
            postAaLabel == null)
        {
            Plugin.Log.LogWarning(
                "Video subpages could not be created because their navigation " +
                "template or one of the injected controls was not found.");
            return;
        }

        RectTransform videoRect = (RectTransform)videoContainer;
        RectTransform dock = Instantiate(
            (RectTransform)dockTemplate,
            videoContainer,
            worldPositionStays: false);
        dock.gameObject.name = "FixedReality2000_SubpageDock";
        dock.anchorMin = new Vector2(0.5f, 0.5f);
        dock.anchorMax = new Vector2(0.5f, 0.5f);
        dock.pivot = new Vector2(0.5f, 0.5f);
        dock.sizeDelta = new Vector2(
            Mathf.Min(900f, videoRect.rect.width - 80f),
            dock.sizeDelta.y);
        dock.anchoredPosition = new Vector2(
            0f,
            videoRect.rect.yMin - dock.sizeDelta.y * 0.5f - 4f);
        dock.localScale = Vector3.one;
        dock.localRotation = Quaternion.identity;

        HorizontalLayoutGroup? inheritedLayout =
            dock.GetComponent<HorizontalLayoutGroup>();
        if (inheritedLayout != null)
        {
            inheritedLayout.enabled = false;
        }

        Button[] clonedButtons =
            dock.GetComponentsInChildren<Button>(includeInactive: true);
        if (clonedButtons.Length < 2)
        {
            Destroy(dock.gameObject);
            Plugin.Log.LogWarning(
                "Video subpages could not be created because the cloned dock " +
                "does not contain two buttons.");
            return;
        }

        for (int index = clonedButtons.Length - 1; index >= 2; index--)
        {
            Destroy(clonedButtons[index].gameObject);
        }

        string[] pageNames = { "DISPLAY", "GRAPHICS" };
        _videoSubpageButtons = clonedButtons.Take(2).ToArray();
        float buttonSpacing = 8f;
        float buttonVerticalPadding = 4f;
        float buttonWidth =
            (dock.sizeDelta.x -
             buttonSpacing * (_videoSubpageButtons.Length + 1)) /
            _videoSubpageButtons.Length;
        for (int index = 0; index < _videoSubpageButtons.Length; index++)
        {
            int pageIndex = index;
            Button button = _videoSubpageButtons[index];
            button.gameObject.name =
                $"FixedReality2000_{pageNames[index]}Button";
            button.gameObject.SetActive(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => SetVideoSubpage(pageIndex));

            RectTransform buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(
                buttonWidth,
                dock.sizeDelta.y - buttonVerticalPadding * 2f);
            buttonRect.anchoredPosition = new Vector2(
                (index - (_videoSubpageButtons.Length - 1) * 0.5f) *
                (buttonWidth + buttonSpacing),
                0f);
            buttonRect.localScale = Vector3.one;
            buttonRect.localRotation = Quaternion.identity;

            TMP_Text? label =
                button.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                label.text = pageNames[index];
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.fontSize = Mathf.Min(label.fontSize, 19f);
                label.raycastTarget = false;
            }
        }

        _videoSubpages = new[]
        {
            CreateVideoSubpage(videoContainer, "FixedReality2000_DisplayPage"),
            CreateVideoSubpage(videoContainer, "FixedReality2000_GraphicsPage")
        };

        float topRowY = videoRect.rect.yMax - 65f;
        float rowSpacing = Mathf.Max(90f, Mathf.Abs(rowStep.y));
        Vector2 leftLabelPosition =
            new(GetLocalCenter((RectTransform)resolutionLabel, videoRect).x, topRowY);
        Vector2 leftControlPosition =
            new(GetLocalCenter((RectTransform)resolutionControl, videoRect).x, topRowY);
        Vector2 rightLabelPosition =
            new(GetLocalCenter((RectTransform)displayLabel, videoRect).x, topRowY);
        Vector2 rightControlPosition =
            new(GetLocalCenter((RectTransform)displayControl, videoRect).x, topRowY);
        Vector2 nextLeftLabelPosition =
            leftLabelPosition + Vector2.down * rowSpacing;
        Vector2 nextLeftControlPosition =
            leftControlPosition + Vector2.down * rowSpacing;
        Vector2 nextRightLabelPosition =
            rightLabelPosition + Vector2.down * rowSpacing;
        Vector2 nextRightControlPosition =
            rightControlPosition + Vector2.down * rowSpacing;
        Vector2 thirdLeftLabelPosition =
            nextLeftLabelPosition + Vector2.down * rowSpacing;
        Vector2 thirdLeftControlPosition =
            nextLeftControlPosition + Vector2.down * rowSpacing;
        Vector2 thirdRightLabelPosition =
            nextRightLabelPosition + Vector2.down * rowSpacing;
        Vector2 thirdRightControlPosition =
            nextRightControlPosition + Vector2.down * rowSpacing;

        MoveVideoControl(
            resolutionLabel,
            _videoSubpages[0].transform,
            leftLabelPosition);
        MoveVideoControl(
            resolutionControl,
            _videoSubpages[0].transform,
            leftControlPosition);
        MoveVideoControl(
            displayLabel,
            _videoSubpages[0].transform,
            rightLabelPosition);
        MoveVideoControl(
            displayControl,
            _videoSubpages[0].transform,
            rightControlPosition);
        MoveVideoControl(
            aspectLabel,
            _videoSubpages[0].transform,
            nextLeftLabelPosition);
        MoveVideoControl(
            aspectControl,
            _videoSubpages[0].transform,
            nextLeftControlPosition);
        MoveVideoControl(
            fpsLabel,
            _videoSubpages[0].transform,
            nextRightLabelPosition);
        MoveVideoControl(
            fpsControl,
            _videoSubpages[0].transform,
            nextRightControlPosition);
        MoveVideoControl(
            monitorLabel,
            _videoSubpages[0].transform,
            thirdLeftLabelPosition);
        MoveVideoControl(
            monitorControl,
            _videoSubpages[0].transform,
            thirdLeftControlPosition);
        MoveVideoControl(
            vsyncLabel,
            _videoSubpages[0].transform,
            thirdRightLabelPosition);
        MoveVideoControl(
            vsyncControl,
            _videoSubpages[0].transform,
            thirdRightControlPosition);

        MoveVideoControl(
            qualityLabel,
            _videoSubpages[1].transform,
            leftLabelPosition);
        MoveVideoControl(
            qualityControl,
            _videoSubpages[1].transform,
            leftControlPosition);
        MoveVideoControl(
            textureLabel,
            _videoSubpages[1].transform,
            rightLabelPosition);
        MoveVideoControl(
            textureControl,
            _videoSubpages[1].transform,
            rightControlPosition);
        MoveVideoControl(
            aaLabel,
            _videoSubpages[1].transform,
            nextLeftLabelPosition);
        MoveVideoControl(
            aaControl,
            _videoSubpages[1].transform,
            nextLeftControlPosition);
        MoveVideoControl(
            postAaLabel,
            _videoSubpages[1].transform,
            nextRightLabelPosition);
        MoveVideoControl(
            postAaControl,
            _videoSubpages[1].transform,
            nextRightControlPosition);

        dock.SetAsLastSibling();
        SetVideoSubpage(0);
        Plugin.Log.LogInfo(
            "Video subpages created: Display and Graphics.");
    }
}
