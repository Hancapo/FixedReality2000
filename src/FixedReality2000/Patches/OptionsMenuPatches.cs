using com.DMT.BrokenReality2000;
using HarmonyLib;
using UnityEngine;

namespace FixedReality2000.Patches;

[HarmonyPatch(typeof(OptionsMenu), "Start")]
internal static class OptionsMenuStartPatch
{
    [HarmonyPostfix]
    private static void BuildCorrectVideoOptions(OptionsMenu __instance)
    {
        GraphicsSettingsMenuBridge.Attach(__instance);
    }
}

[HarmonyPatch(typeof(OptionsMenu), "OnEnable")]
internal static class OptionsMenuEnablePatch
{
    [HarmonyPostfix]
    private static void RefreshCorrectVideoOptions(OptionsMenu __instance)
    {
        __instance.GetComponent<GraphicsSettingsMenuBridge>()?.RefreshFromSavedValues();
    }
}

[HarmonyPatch(typeof(BrokenPlayer), "ZoomOut")]
internal static class BrokenPlayerBaseFovPatch
{
    [HarmonyPrefix]
    private static void UseConfiguredBaseFov(ref float targetFOV)
    {
        if (Mathf.Approximately(targetFOV, 60f))
        {
            targetFOV = GraphicsSettingsMenuBridge.SavedFov;
        }
    }
}
