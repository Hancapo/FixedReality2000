using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FixedReality2000;

internal sealed class StoreCameraOptimization : IDisposable
{
    private const string StoreCameraName = "player_storecamera";

    private Camera? _disabledCamera;
    private bool _originalEnabled;
    private UniversalAdditionalCameraData? _stackOwner;
    private int _stackIndex = -1;
    private bool _allowedForCurrentScene;

    internal void OnSceneLoaded()
    {
        Restore();
        _allowedForCurrentScene = false;
    }

    internal void ApplyConfiguration()
    {
        if (!Plugin.DisableUnusedStoreCamera.Value)
        {
            _allowedForCurrentScene = true;
            Restore();
            return;
        }

        if (_allowedForCurrentScene || _disabledCamera != null)
        {
            return;
        }

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        Camera? storeCamera = Array.Find(
            cameras,
            camera =>
                camera != null &&
                camera.isActiveAndEnabled &&
                string.Equals(
                    camera.name,
                    StoreCameraName,
                    StringComparison.OrdinalIgnoreCase));
        if (storeCamera == null)
        {
            return;
        }

        RememberStackPosition(cameras, storeCamera);
        _disabledCamera = storeCamera;
        _originalEnabled = storeCamera.enabled;

        if (_stackOwner?.cameraStack is { } stack)
        {
            stack.Remove(storeCamera);
        }

        storeCamera.enabled = false;
        Plugin.Log.LogInfo(
            $"Disabled unused store camera '{storeCamera.name}'.");
    }

    internal void ReloadConfiguration()
    {
        _allowedForCurrentScene = false;
        ApplyConfiguration();
    }

    internal string Toggle()
    {
        if (_disabledCamera != null)
        {
            string cameraName = _disabledCamera.name;
            _allowedForCurrentScene = true;
            Restore();
            return $"Store camera restored: {cameraName}";
        }

        _allowedForCurrentScene = false;
        ApplyConfiguration();
        return _disabledCamera != null
            ? $"Store camera disabled: {_disabledCamera.name}"
            : "No active store camera found";
    }

    public void Dispose()
    {
        Restore();
    }

    private void Restore()
    {
        Camera? camera = _disabledCamera;
        UniversalAdditionalCameraData? stackOwner = _stackOwner;
        int stackIndex = _stackIndex;
        bool originalEnabled = _originalEnabled;

        _disabledCamera = null;
        _stackOwner = null;
        _stackIndex = -1;

        if (camera == null)
        {
            return;
        }

        if (stackOwner?.cameraStack is { } stack && !stack.Contains(camera))
        {
            stack.Insert(Mathf.Clamp(stackIndex, 0, stack.Count), camera);
        }

        camera.enabled = originalEnabled;
    }

    private void RememberStackPosition(Camera[] cameras, Camera target)
    {
        _stackOwner = null;
        _stackIndex = -1;

        foreach (Camera camera in cameras)
        {
            if (camera == null ||
                !camera.TryGetComponent(
                    out UniversalAdditionalCameraData additionalData) ||
                additionalData.renderType != CameraRenderType.Base)
            {
                continue;
            }

            List<Camera>? stack = additionalData.cameraStack;
            int index = stack?.IndexOf(target) ?? -1;
            if (index < 0)
            {
                continue;
            }

            _stackOwner = additionalData;
            _stackIndex = index;
            return;
        }
    }
}
