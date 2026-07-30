using com.DMT.BrokenReality2000;
using HarmonyLib;

namespace FixedReality2000.Patches;

[HarmonyPatch(typeof(BrokenPlayer), "PrimaryAction")]
internal static class ControllerPrimaryRumblePatch
{
    [HarmonyPostfix]
    private static void Pulse()
    {
        if (ControllerInputState.LastInputWasGamepad)
        {
            ControllerSettings.PulseRumble(0.18f, 0.42f, 0.09f);
        }
    }
}

[HarmonyPatch(typeof(BrokenPlayer), "SecondaryAction")]
internal static class ControllerSecondaryRumblePatch
{
    [HarmonyPostfix]
    private static void Pulse()
    {
        if (ControllerInputState.LastInputWasGamepad)
        {
            ControllerSettings.PulseRumble(0.28f, 0.16f, 0.08f);
        }
    }
}
