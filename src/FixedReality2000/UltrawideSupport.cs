using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FixedReality2000;

internal sealed class UltrawideSupport : IDisposable
{
    private const float ReferenceAspect = 16f / 9f;
    private const float AspectTolerance = 0.05f;
    private const float ScanInterval = 1f;

    private readonly Dictionary<int, CanvasScalerState> _canvasScalers = new();
    private readonly Dictionary<int, WeakReference<Camera>> _adjustedCameras = new();

    private int _lastWidth;
    private int _lastHeight;
    private float _nextScanTime;
    private bool _active;

    internal void OnSceneLoaded()
    {
        Restore();
        _nextScanTime = 0f;
    }

    internal void Tick()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        bool resolutionChanged =
            Screen.width != _lastWidth ||
            Screen.height != _lastHeight;
        if (!resolutionChanged && Time.unscaledTime < _nextScanTime)
        {
            return;
        }

        _lastWidth = Screen.width;
        _lastHeight = Screen.height;
        _nextScanTime = Time.unscaledTime + ScanInterval;

        float aspect = Screen.width / (float)Screen.height;
        bool shouldBeActive = aspect > ReferenceAspect + AspectTolerance;
        if (!shouldBeActive)
        {
            if (_active)
            {
                Restore();
            }

            return;
        }

        _active = true;
        ApplyToCanvasScalers();
        ApplyToCameras(aspect);
    }

    public void Dispose()
    {
        Restore();
    }

    private void ApplyToCanvasScalers()
    {
        CanvasScaler[] scalers =
            UnityEngine.Object.FindObjectsByType<CanvasScaler>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (CanvasScaler scaler in scalers)
        {
            if (scaler == null ||
                scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                continue;
            }

            Canvas canvas = scaler.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
            {
                continue;
            }

            int instanceId = scaler.GetInstanceID();
            if (!_canvasScalers.ContainsKey(instanceId))
            {
                _canvasScalers.Add(
                    instanceId,
                    new CanvasScalerState(
                        new WeakReference<CanvasScaler>(scaler),
                        scaler.screenMatchMode,
                        scaler.matchWidthOrHeight));
            }

            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }
    }

    private void ApplyToCameras(float aspect)
    {
        Camera[] cameras =
            UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera == null ||
                camera.targetTexture != null ||
                !IsFullScreenViewport(camera.rect) ||
                Mathf.Abs(camera.aspect - aspect) <= AspectTolerance)
            {
                continue;
            }

            int instanceId = camera.GetInstanceID();
            if (!_adjustedCameras.ContainsKey(instanceId))
            {
                _adjustedCameras.Add(
                    instanceId,
                    new WeakReference<Camera>(camera));
            }

            camera.aspect = aspect;
        }
    }

    private void Restore()
    {
        foreach (CanvasScalerState state in _canvasScalers.Values)
        {
            if (!state.Scaler.TryGetTarget(out CanvasScaler? scaler) ||
                scaler == null)
            {
                continue;
            }

            scaler.screenMatchMode = state.ScreenMatchMode;
            scaler.matchWidthOrHeight = state.MatchWidthOrHeight;
        }

        foreach (WeakReference<Camera> reference in _adjustedCameras.Values)
        {
            if (reference.TryGetTarget(out Camera? camera) && camera != null)
            {
                camera.ResetAspect();
            }
        }

        _canvasScalers.Clear();
        _adjustedCameras.Clear();
        _active = false;
    }

    private static bool IsFullScreenViewport(Rect rect)
    {
        return Mathf.Abs(rect.x) < 0.001f &&
               Mathf.Abs(rect.y) < 0.001f &&
               Mathf.Abs(rect.width - 1f) < 0.001f &&
               Mathf.Abs(rect.height - 1f) < 0.001f;
    }

    private sealed class CanvasScalerState
    {
        internal CanvasScalerState(
            WeakReference<CanvasScaler> scaler,
            CanvasScaler.ScreenMatchMode screenMatchMode,
            float matchWidthOrHeight)
        {
            Scaler = scaler;
            ScreenMatchMode = screenMatchMode;
            MatchWidthOrHeight = matchWidthOrHeight;
        }

        internal WeakReference<CanvasScaler> Scaler { get; }
        internal CanvasScaler.ScreenMatchMode ScreenMatchMode { get; }
        internal float MatchWidthOrHeight { get; }
    }
}

internal static class UltrawideResolutionTests
{
    internal static readonly string[] AspectRatioLabels =
    {
        "AUTO",
        "4:3",
        "16:9",
        "16:10",
        "21:9",
        "32:9"
    };

    private static readonly float[] AspectRatios =
    {
        4f / 3f,
        16f / 9f,
        16f / 10f,
        64f / 27f,
        32f / 9f
    };

    internal static string Format(int width, int height)
    {
        return $"{width} x {height}";
    }

    internal static int GetAspectRatioIndex(int width, int height)
    {
        float aspect = width / (float)Mathf.Max(1, height);
        int closestIndex = 0;
        float closestDifference = float.MaxValue;
        for (int index = 0; index < AspectRatios.Length; index++)
        {
            float difference = Mathf.Abs(aspect - AspectRatios[index]);
            if (difference >= closestDifference)
            {
                continue;
            }

            closestDifference = difference;
            closestIndex = index;
        }

        return closestIndex + 1;
    }

    internal static float GetAspectRatio(int index)
    {
        return AspectRatios[
            Mathf.Clamp(index - 1, 0, AspectRatios.Length - 1)];
    }

    internal static bool MatchesAspectRatio(
        int width,
        int height,
        int aspectRatioIndex)
    {
        if (aspectRatioIndex == 0)
        {
            return true;
        }

        float target = GetAspectRatio(aspectRatioIndex);
        float actual = width / (float)Mathf.Max(1, height);
        return Mathf.Abs(actual - target) / target <= 0.0125f;
    }
}
