using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal sealed partial class LegacyGraphicsSettingsBridge : MonoBehaviour
{
    private void BuildControlsTab(Transform optionsPanel)
    {
        Transform? dock = optionsPanel.Find("ButtonDock/BG PC");
        Transform? videoPage = optionsPanel.Find("VideoSettings");
        Transform? audioPage = optionsPanel.Find("AudioSettings");
        Transform? gamePage = optionsPanel.Find("GameSettings");
        Transform? controlsPage = optionsPanel.Find("ControlsSettings");
        Button? videoButton =
            dock?.Find("VideoSettingsButton")?.GetComponent<Button>();
        Button? audioButton =
            dock?.Find("AudioSettingsButton")?.GetComponent<Button>();
        Button? gameButton =
            dock?.Find("GameSettingsButton")?.GetComponent<Button>();
        Button? controlsButton =
            dock?.Find("ControlsSettingsButton")?.GetComponent<Button>();
        CarrouselUIHandler? carousel =
            optionsPanel.GetComponent<CarrouselUIHandler>();
        if (dock == null ||
            videoPage == null ||
            audioPage == null ||
            gamePage == null ||
            controlsPage == null ||
            videoButton == null ||
            audioButton == null ||
            gameButton == null ||
            controlsButton == null ||
            carousel == null)
        {
            Plugin.Log.LogWarning(
                "The retail Controls tab could not be enabled because one of " +
                "its hidden objects was not found.");
            return;
        }

        if (controlsPage.Find("FixedReality2000_MousePage") != null)
        {
            return;
        }

        controlsButton.gameObject.SetActive(true);
        controlsButton.interactable = true;
        // This button ships hidden and its serialized event/state is not part
        // of the retail three-page carousel. Give it a clean runtime event so
        // stale persistent wiring cannot swallow the first click.
        controlsButton.onClick = new Button.ButtonClickedEvent();
        TMP_Text? controlsTabText =
            controlsButton.GetComponentInChildren<TMP_Text>(includeInactive: true);
        if (controlsTabText != null)
        {
            controlsTabText.gameObject.SetActive(true);
            controlsTabText.text = "CONTROLS";
            controlsTabText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Button[] mainButtons =
        {
            videoButton,
            audioButton,
            gameButton,
            controlsButton
        };
        GameObject[] mainPages =
        {
            videoPage.gameObject,
            audioPage.gameObject,
            gamePage.gameObject,
            controlsPage.gameObject
        };
        TextMeshProUGUI[] mainTexts = mainButtons
            .Select(button =>
                button.GetComponentInChildren<TextMeshProUGUI>(true))
            .Where(text => text != null)
            .ToArray()!;
        if (mainTexts.Length != mainButtons.Length)
        {
            Plugin.Log.LogWarning(
                "The Controls tab was found, but one of the main tab labels is missing.");
            return;
        }

        Transform? sensitivityLabel = gamePage.Find("SensitivityTitle");
        Transform? sensitivityControl = gamePage.Find("SensitivitySlider");
        Transform? invertLabel = gamePage.Find("InvertMouseTitle");
        Transform? invertControl = gamePage.Find("InvertMouseButton");
        Transform? fovLabel = gamePage.Find("FixedReality2000_FovLabel");
        Transform? fovControl = gamePage.Find(InjectedFovRowName);
        Transform? controlsSensitivityLabel =
            controlsPage.Find("SensitivityTitle");
        Transform? controlsSensitivityControl =
            controlsPage.Find("SensitivitySlider");
        Transform? controlsInvertLabel =
            controlsPage.Find("InvertMouseTitle");
        Transform? controlsInvertControl =
            controlsPage.Find("InvertMouseButton");
        if (sensitivityLabel == null ||
            sensitivityControl == null ||
            invertLabel == null ||
            invertControl == null ||
            fovLabel == null ||
            fovControl == null ||
            controlsSensitivityLabel == null ||
            controlsSensitivityControl == null ||
            controlsInvertLabel == null ||
            controlsInvertControl == null)
        {
            Plugin.Log.LogWarning(
                "The Controls tab could not be populated because its mouse rows " +
                "or the active Game controls were not found.");
            return;
        }

        RectTransform gameRect = (RectTransform)gamePage;
        RectTransform controlsRect = (RectTransform)controlsPage;
        Vector2 formerSensitivityLabelPosition =
            GetLocalCenter((RectTransform)sensitivityLabel, gameRect);
        Vector2 formerSensitivityControlPosition =
            GetLocalCenter((RectTransform)sensitivityControl, gameRect);
        Vector2 mouseSensitivityLabelPosition =
            GetLocalCenter((RectTransform)controlsSensitivityLabel, controlsRect);
        Vector2 mouseSensitivityControlPosition =
            GetLocalCenter((RectTransform)controlsSensitivityControl, controlsRect);
        Vector2 mouseInvertLabelPosition =
            GetLocalCenter((RectTransform)controlsInvertLabel, controlsRect);
        Vector2 mouseInvertControlPosition =
            GetLocalCenter((RectTransform)controlsInvertControl, controlsRect);

        _controlsSubpages = new[]
        {
            CreateVideoSubpage(controlsPage, "FixedReality2000_MousePage"),
            CreateVideoSubpage(controlsPage, "FixedReality2000_KeyboardPage"),
            CreateVideoSubpage(controlsPage, "FixedReality2000_GamepadPage")
        };

        MoveVideoControl(
            sensitivityLabel,
            _controlsSubpages[0].transform,
            mouseSensitivityLabelPosition);
        MoveVideoControl(
            sensitivityControl,
            _controlsSubpages[0].transform,
            mouseSensitivityControlPosition);
        MoveVideoControl(
            invertLabel,
            _controlsSubpages[0].transform,
            mouseInvertLabelPosition);
        MoveVideoControl(
            invertControl,
            _controlsSubpages[0].transform,
            mouseInvertControlPosition);
        MoveVideoControl(fovLabel, gamePage, formerSensitivityLabelPosition);
        MoveVideoControl(fovControl, gamePage, formerSensitivityControlPosition);

        Button bindingButtonTemplate = invertControl.GetComponent<Button>();
        TMP_Text bindingLabelTemplate =
            sensitivityLabel.GetComponent<TMP_Text>();
        Destroy(controlsSensitivityLabel.gameObject);
        Destroy(controlsSensitivityControl.gameObject);
        Destroy(controlsInvertLabel.gameObject);
        Destroy(controlsInvertControl.gameObject);

        BuildKeyboardBindings(
            _controlsSubpages[1].transform,
            controlsRect,
            bindingLabelTemplate,
            bindingButtonTemplate);
        BuildGamepadPages(
            _controlsSubpages[2].transform,
            controlsRect,
            bindingLabelTemplate,
            bindingButtonTemplate,
            sensitivityControl.GetComponent<Slider>(),
            _videoAaDropdown);
        BuildControlsSubpageDock(controlsPage);

        carousel.buttonsInCarrousel = mainButtons;
        carousel.panelsInCarroussel = mainPages;
        carousel.texts = mainTexts;
        carousel.objectToEnable = new[]
        {
            _resolutionDropdown?.gameObject ?? videoPage.gameObject,
            audioPage.GetComponentInChildren<Slider>(true)?.gameObject ??
                audioPage.gameObject,
            gamePage.Find("ToolbarButton")?.gameObject ?? gamePage.gameObject,
            sensitivityControl.gameObject
        };
        int activeMainPage = Array.FindIndex(
            mainPages,
            page => page.activeSelf);
        for (int index = 0; index < mainButtons.Length; index++)
        {
            int selectedIndex = index;
            mainButtons[index].onClick.AddListener(
                () =>
                {
                    carousel.CarrousselIndex = selectedIndex;
                    carousel.UpdateCarroussel();
                });
        }

        SetControlsSubpage(0);
        carousel.CarrousselIndex =
            activeMainPage >= 0
                ? activeMainPage
                : Mathf.Clamp(carousel.CarrousselIndex, 0, mainPages.Length - 1);
        carousel.UpdateCarroussel();
        Plugin.Log.LogInfo(
            "Retail Controls tab enabled with Mouse, Keyboard, and Gamepad subpages.");
    }
}
