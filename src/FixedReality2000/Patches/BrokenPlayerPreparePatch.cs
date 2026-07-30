using com.DMT.BrokenReality2000;
using HarmonyLib;

namespace FixedReality2000.Patches;

/// <summary>
/// Prevents BrokenPlayer.Prepare from restoring the game's hard-coded 60 FPS cap.
/// </summary>
[HarmonyPatch(typeof(BrokenPlayer), "Prepare")]
internal static class BrokenPlayerPreparePatch
{
    [HarmonyPostfix]
    private static void RemoveForcedFrameRateCap()
    {
        // The game reapplies its original quality preset while preparing the
        // player. Restore the modded lighting preset after that happens so
        // shadows do not depend on opening the options menu.
        GraphicsQualityLighting.ApplySaved();

        if (!Plugin.FixLowQualityFpsCap.Value)
        {
            return;
        }

        Plugin.ApplyConfiguredFramePacing();

        string description = Plugin.VSyncEnabled
            ? "V-Sync"
            : Plugin.TargetFrameRate == -1
                ? "uncapped"
                : $"{Plugin.TargetFrameRate} FPS";

        Plugin.Log.LogInfo(
            $"Forced 60 FPS cap override applied: {description}.");
    }
}
