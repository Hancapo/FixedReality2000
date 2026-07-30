using com.DMT.BrokenReality2000;
using HarmonyLib;
using TMPro;

namespace FixedReality2000.Patches;

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
