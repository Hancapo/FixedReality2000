using com.DMT.BrokenReality2000;
using HarmonyLib;
using UnityEngine;

namespace FixedReality2000.Patches;

/// <summary>
/// Keeps the first-person camera from rotating far enough to intersect the
/// player's own body when looking straight up or down.
/// </summary>
[HarmonyPatch(typeof(BrokenPlayer), "HandleMovement")]
internal static class PlayerLookPitchPatch
{
    private const float MaximumPitch = 80f;

    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        MinimumPitch =
            AccessTools.FieldRefAccess<BrokenPlayer, float>("minimumY");

    private static readonly AccessTools.FieldRef<BrokenPlayer, float>
        MaximumPitchField =
            AccessTools.FieldRefAccess<BrokenPlayer, float>("maximumY");

    [HarmonyPrefix]
    private static void ApplySafePitchLimits(BrokenPlayer __instance)
    {
        MinimumPitch(__instance) = Mathf.Max(
            MinimumPitch(__instance),
            -MaximumPitch);
        MaximumPitchField(__instance) = Mathf.Min(
            MaximumPitchField(__instance),
            MaximumPitch);
    }
}
