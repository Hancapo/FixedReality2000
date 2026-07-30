using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using com.DMT.BrokenReality2000;
using HarmonyLib;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FixedReality2000;

[HarmonyPatch(typeof(BrokenPlayer), "HandleMovement")]
internal static class BrokenPlayerKeybindingsPatch
{
    private static readonly IReadOnlyDictionary<MethodInfo, MethodInfo>
        GetterReplacements = BuildReplacements();

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReplaceHardcodedKeys(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> patched = instructions.ToList();
        for (int index = 0; index < patched.Count; index++)
        {
            CodeInstruction instruction = patched[index];
            if (instruction.opcode == OpCodes.Ldstr &&
                instruction.operand is string prompt &&
                TryGetPromptReplacement(prompt, out MethodInfo? promptGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = promptGetter;
                continue;
            }

            if (index < patched.Count - 1 &&
                instruction.operand is MethodInfo called &&
                GetterReplacements.TryGetValue(
                    called,
                    out MethodInfo? replacement) &&
                patched[index + 1].operand is MethodInfo stateGetter &&
                IsButtonStateGetter(stateGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                patched[index + 1].opcode = OpCodes.Nop;
                patched[index + 1].operand = null;
            }
        }

        return patched;
    }

    private static bool TryGetPromptReplacement(
        string prompt,
        out MethodInfo? replacement)
    {
        string? methodName = prompt switch
        {
            "Q" => nameof(PlayerKeybindings.GetPreviousToolPrompt),
            "E" => nameof(PlayerKeybindings.GetNextToolPrompt),
            "F: CHANGE LENS" => nameof(PlayerKeybindings.GetLensPrompt),
            "E: CHANGE FILTER" => nameof(PlayerKeybindings.GetFilterPrompt),
            _ => null
        };
        replacement = methodName == null
            ? null
            : AccessTools.Method(typeof(PlayerKeybindings), methodName);
        return replacement != null;
    }

    private static bool IsButtonStateGetter(MethodInfo method)
    {
        return method.DeclaringType == typeof(ButtonControl) &&
               method.Name is
                   "get_isPressed" or
                   "get_wasPressedThisFrame";
    }

    private static IReadOnlyDictionary<MethodInfo, MethodInfo>
        BuildReplacements()
    {
        return new Dictionary<MethodInfo, MethodInfo>
        {
            [AccessTools.PropertyGetter(typeof(Keyboard), "wKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Forward)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "sKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Backward)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "aKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Left)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "dKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Right)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "qKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.PreviousTool)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "eKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.NextTool)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "fKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.Utility)),
            [AccessTools.PropertyGetter(typeof(Keyboard), "downArrowKey")] =
                AccessTools.Method(typeof(PlayerKeybindings), nameof(PlayerKeybindings.ToggleToolbar))
        };
    }
}

/// <summary>
/// ToolPromptChanger refreshes its helper labels every half second using
/// private retail defaults. Keep that refresh path synchronized with the
/// remappable bindings as well.
/// </summary>
[HarmonyPatch(typeof(ToolPromptChanger), "ChangeUI")]
internal static class ToolPromptChangerKeybindingsPatch
{
    [HarmonyPrefix]
    private static void ApplyCurrentBindings(
        ref string ___leftKBBH,
        ref string ___rightKBBH)
    {
        ___leftKBBH =
            PlayerKeybindings.GetLabel(PlayerBinding.PreviousTool);
        ___rightKBBH =
            PlayerKeybindings.GetLabel(PlayerBinding.NextTool);
    }
}
