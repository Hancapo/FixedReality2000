using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000.Patches;

internal static class OptionsUiUtility
{
    internal static void SetTabState(Button button, bool active)
    {
        button.interactable = !active;
        TMP_Text? text =
            button.GetComponentInChildren<TMP_Text>(includeInactive: true);
        if (text != null)
        {
            text.color = active ? Color.black : Color.white;
        }
    }
}
