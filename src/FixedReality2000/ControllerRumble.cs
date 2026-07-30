using UnityEngine;
using UnityEngine.InputSystem;

namespace FixedReality2000;

internal static partial class ControllerSettings
{
    internal static void PulseRumble(
        float lowFrequency,
        float highFrequency,
        float duration)
    {
        if (!VibrationEnabled || VibrationIntensity <= 0f)
        {
            return;
        }

        _rumbleLow = Mathf.Clamp01(lowFrequency) * VibrationIntensity;
        _rumbleHigh = Mathf.Clamp01(highFrequency) * VibrationIntensity;
        _rumbleEndsAt = Time.unscaledTime + Mathf.Max(0.01f, duration);
    }

    internal static void TickRumble()
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return;
        }

        if (!VibrationEnabled || Time.unscaledTime >= _rumbleEndsAt)
        {
            gamepad.SetMotorSpeeds(0f, 0f);
            return;
        }

        gamepad.SetMotorSpeeds(_rumbleLow, _rumbleHigh);
    }

    internal static void StopRumble()
    {
        _rumbleEndsAt = 0f;
        Gamepad.current?.SetMotorSpeeds(0f, 0f);
    }

}
