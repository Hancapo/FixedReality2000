using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FixedReality2000.Patches;

internal static class SceneObjectCache
{
    private static readonly Dictionary<string, GameObject> GameObjects = new();
    private static Camera? _mainCamera;

    internal static Camera? MainCamera
    {
        get
        {
            if (!Plugin.OptimizePerFrameLookups.Value)
            {
                return Camera.main;
            }

            if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
            {
                _mainCamera = Camera.main;
            }

            return _mainCamera;
        }
    }

    internal static GameObject? FindActiveGameObject(string name)
    {
        if (!Plugin.OptimizePerFrameLookups.Value)
        {
            return GameObject.Find(name);
        }

        if (GameObjects.TryGetValue(name, out GameObject cached) &&
            cached != null &&
            cached.activeInHierarchy)
        {
            return cached;
        }

        GameObject found = GameObject.Find(name);
        if (found != null)
        {
            GameObjects[name] = found;
        }
        else
        {
            GameObjects.Remove(name);
        }

        return found;
    }

    internal static void Clear()
    {
        GameObjects.Clear();
        _mainCamera = null;
    }
}

[HarmonyPatch(typeof(AdLookAtPlayer), "Update")]
internal static class AdLookAtPlayerUpdatePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertyGetter(typeof(Camera), nameof(Camera.main));
        var replacement = AccessTools.PropertyGetter(typeof(SceneObjectCache), nameof(SceneObjectCache.MainCamera));

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                yield return new CodeInstruction(OpCodes.Call, replacement)
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}

[HarmonyPatch(typeof(BrokenBookmarker), "Update")]
internal static class BrokenBookmarkerUpdatePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(
            typeof(GameObject),
            nameof(GameObject.Find),
            new[] { typeof(string) });
        var replacement = AccessTools.Method(
            typeof(SceneObjectCache),
            nameof(SceneObjectCache.FindActiveGameObject));

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                yield return new CodeInstruction(OpCodes.Call, replacement)
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}
