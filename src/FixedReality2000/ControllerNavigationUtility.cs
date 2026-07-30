using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal static class ControllerNavigationUtility
{
    internal static bool HasNavigationInput(
        Gamepad gamepad,
        bool includeRightStick = false)
    {
        return
            gamepad.dpad.ReadValue().sqrMagnitude > 0.01f ||
            gamepad.leftStick.ReadUnprocessedValue().sqrMagnitude > 0.16f ||
            (includeRightStick &&
             gamepad.rightStick.ReadUnprocessedValue().sqrMagnitude > 0.16f) ||
            gamepad.buttonSouth.wasPressedThisFrame;
    }

    internal static Selectable? FindTopLeftSelectable(
        Transform root,
        Func<Selectable, bool>? extraFilter = null)
    {
        Selectable? best = null;
        Vector2 bestPosition = default;
        foreach (Selectable selectable in
                 root.GetComponentsInChildren<Selectable>(true))
        {
            if (!IsUsable(selectable) ||
                (extraFilter != null && !extraFilter(selectable)))
            {
                continue;
            }

            Vector2 position = GetCenter(selectable);
            if (best == null ||
                position.y > bestPosition.y + 0.5f ||
                (Mathf.Abs(position.y - bestPosition.y) <= 0.5f &&
                 position.x < bestPosition.x))
            {
                best = selectable;
                bestPosition = position;
            }
        }

        return best;
    }

    internal static bool IsUsable(Selectable? selectable)
    {
        return
            selectable != null &&
            selectable.IsActive() &&
            selectable.IsInteractable() &&
            selectable.gameObject.activeInHierarchy;
    }

    internal static Vector2 GetCenter(Selectable selectable)
    {
        if (selectable.transform is RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center);
        }

        return selectable.transform.position;
    }
}
