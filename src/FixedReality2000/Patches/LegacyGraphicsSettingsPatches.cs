using HarmonyLib;

namespace FixedReality2000.Patches;

[HarmonyPatch(typeof(NewOptionsScript), "Awake")]
internal static class LegacyGraphicsSettingsAwakePatch
{
    [HarmonyPostfix]
    private static void BuildActualGraphicsMenu(NewOptionsScript __instance)
    {
        LegacyGraphicsSettingsBridge.Attach(__instance);
    }
}

[HarmonyPatch(typeof(NewOptionsScript), "ChangeResolutionWidthHeight")]
internal static class LegacyResolutionSelectionPatch
{
    [HarmonyPrefix]
    private static bool UseDetectedResolution(NewOptionsScript __instance, int resIndex)
    {
        LegacyGraphicsSettingsBridge? bridge =
            __instance.GetComponent<LegacyGraphicsSettingsBridge>();
        return bridge == null || !bridge.ApplyResolution(resIndex);
    }
}

[HarmonyPatch(typeof(NewOptionsScript), "ChangeResolution")]
internal static class LegacyResolutionApplyPatch
{
    [HarmonyPrefix]
    private static bool ApplyDetectedResolution(NewOptionsScript __instance)
    {
        LegacyGraphicsSettingsBridge? bridge =
            __instance.GetComponent<LegacyGraphicsSettingsBridge>();
        return bridge == null || !bridge.ApplyCurrentResolution();
    }
}
