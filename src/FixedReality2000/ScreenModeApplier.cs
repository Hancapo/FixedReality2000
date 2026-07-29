using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FixedReality2000;

internal sealed class ScreenModeApplier : IDisposable
{
    private static readonly float[] RetryDelays = { 0f, 0.10f, 0.25f };

    private readonly MonoBehaviour _host;
    private Coroutine? _applyRoutine;
    private int _requestVersion;

    internal ScreenModeApplier(MonoBehaviour host)
    {
        _host = host;
    }

    internal void Request(
        int width,
        int height,
        FullScreenMode mode,
        int refreshRate,
        int monitorIndex = -1)
    {
        width = Mathf.Max(640, width);
        height = Mathf.Max(360, height);
        refreshRate = Mathf.Max(1, refreshRate);

        _requestVersion++;
        if (_applyRoutine != null)
        {
            _host.StopCoroutine(_applyRoutine);
        }

        _applyRoutine = _host.StartCoroutine(
            ApplyRoutine(
                _requestVersion,
                width,
                height,
                mode,
                refreshRate,
                monitorIndex));
    }

    public void Dispose()
    {
        _requestVersion++;
        if (_applyRoutine != null)
        {
            _host.StopCoroutine(_applyRoutine);
            _applyRoutine = null;
        }
    }

    private IEnumerator ApplyRoutine(
        int version,
        int width,
        int height,
        FullScreenMode mode,
        int refreshRate,
        int monitorIndex)
    {
        yield return MoveToMonitor(monitorIndex);
        Apply(width, height, mode, refreshRate);
        yield return null;

        foreach (float delay in RetryDelays)
        {
            if (version != _requestVersion)
            {
                yield break;
            }

            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            if (Matches(width, height, mode, monitorIndex))
            {
                _applyRoutine = null;
                yield break;
            }

            yield return MoveToMonitor(monitorIndex);
            Apply(width, height, mode, refreshRate);
            yield return null;
        }

        bool applied = Matches(width, height, mode, monitorIndex);
        Plugin.Log.LogInfo(
            $"Display request {width}x{height} {mode} @ {refreshRate} Hz" +
            (monitorIndex >= 0 ? $" on monitor {monitorIndex + 1}" : string.Empty) +
            ": " +
            (applied
                ? "applied."
                : $"Unity reports {Screen.width}x{Screen.height} " +
                  $"{Screen.fullScreenMode}."));
        _applyRoutine = null;
    }

    private static IEnumerator MoveToMonitor(int monitorIndex)
    {
        if (!TryGetMonitor(monitorIndex, out DisplayInfo monitor) ||
            Screen.mainWindowDisplayInfo.Equals(monitor))
        {
            yield break;
        }

        AsyncOperation move =
            Screen.MoveMainWindowTo(in monitor, Vector2Int.zero);
        if (move != null)
        {
            yield return move;
        }
    }

    private static bool TryGetMonitor(
        int monitorIndex,
        out DisplayInfo monitor)
    {
        var layout = new List<DisplayInfo>();
        Screen.GetDisplayLayout(layout);
        if (monitorIndex >= 0 && monitorIndex < layout.Count)
        {
            monitor = layout[monitorIndex];
            return true;
        }

        monitor = default;
        return false;
    }

    private static void Apply(
        int width,
        int height,
        FullScreenMode mode,
        int refreshRate)
    {
        Screen.SetResolution(
            width,
            height,
            mode,
            new RefreshRate
            {
                numerator = (uint)refreshRate,
                denominator = 1
            });
    }

    private static bool Matches(
        int width,
        int height,
        FullScreenMode mode,
        int monitorIndex)
    {
        if (Screen.fullScreenMode != mode)
        {
            return false;
        }

        if (TryGetMonitor(monitorIndex, out DisplayInfo monitor) &&
            !Screen.mainWindowDisplayInfo.Equals(monitor))
        {
            return false;
        }

        // Borderless uses the desktop-sized host window on Windows. Unity may
        // therefore report the desktop dimensions even when it renders a
        // lower selected resolution.
        return mode == FullScreenMode.FullScreenWindow ||
               (Screen.width == width && Screen.height == height);
    }
}
