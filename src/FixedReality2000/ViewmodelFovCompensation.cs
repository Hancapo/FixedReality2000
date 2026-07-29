using System;
using com.DMT.BrokenReality2000;
using UnityEngine;

namespace FixedReality2000;

/// <summary>
/// Keeps the first-person hand and held tools framed as they were at the
/// game's original 60-degree field of view.
/// </summary>
internal sealed class ViewmodelFovCompensation : IDisposable
{
    private const float ReferenceFov = 60f;
    private const float SearchInterval = 0.5f;

    private Camera? _worldCamera;
    private Transform? _handOffset;
    private Vector3 _baseLocalPosition;
    private Vector3 _baseScale;
    private float _nextSearchTime;
    private bool _scaleApplied;

    internal void LateTick()
    {
        if (_worldCamera == null ||
            _handOffset == null ||
            !_worldCamera.isActiveAndEnabled ||
            !_handOffset.gameObject.activeInHierarchy)
        {
            ResolveViewmodel();
        }

        Camera? camera = _worldCamera;
        Transform? handOffset = _handOffset;
        if (camera == null || handOffset == null)
        {
            return;
        }

        float currentFov = Mathf.Clamp(camera.fieldOfView, 1f, 179f);
        float projectionScale =
            Mathf.Tan(currentFov * 0.5f * Mathf.Deg2Rad) /
            Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);

        Vector3 compensatedScale = new(
            _baseScale.x * projectionScale,
            _baseScale.y * projectionScale,
            _baseScale.z);
        Vector3 compensatedPosition = new(
            _baseLocalPosition.x * projectionScale,
            _baseLocalPosition.y * projectionScale,
            _baseLocalPosition.z);

        if ((handOffset.localScale - compensatedScale).sqrMagnitude > 0.00000001f)
        {
            handOffset.localScale = compensatedScale;
        }

        if ((handOffset.localPosition - compensatedPosition).sqrMagnitude >
            0.00000001f)
        {
            handOffset.localPosition = compensatedPosition;
        }

        _scaleApplied = true;
    }

    internal void OnSceneLoaded()
    {
        RestoreScale();
        _worldCamera = null;
        _handOffset = null;
        _nextSearchTime = 0f;
    }

    public void Dispose()
    {
        RestoreScale();
        _worldCamera = null;
        _handOffset = null;
    }

    private void ResolveViewmodel()
    {
        if (Time.unscaledTime < _nextSearchTime)
        {
            return;
        }

        _nextSearchTime = Time.unscaledTime + SearchInterval;

        BrokenPlayer? player =
            UnityEngine.Object.FindFirstObjectByType<BrokenPlayer>(
                FindObjectsInactive.Exclude);
        Camera? camera = player != null && player.cam != null
            ? player.cam
            : Camera.main;
        if (camera == null || !camera.isActiveAndEnabled)
        {
            return;
        }

        Transform? handOffset = camera.transform.Find("hand_offset");
        if (handOffset == null)
        {
            return;
        }

        if (_worldCamera == camera && _handOffset == handOffset)
        {
            return;
        }

        RestoreScale();
        _worldCamera = camera;
        _handOffset = handOffset;
        _baseLocalPosition = handOffset.localPosition;
        _baseScale = handOffset.localScale;
        _scaleApplied = false;

        Plugin.Log.LogInfo(
            $"Viewmodel FOV compensation attached to '{handOffset.name}' " +
            $"using {ReferenceFov:0} degrees as the reference.");
    }

    private void RestoreScale()
    {
        if (_scaleApplied && _handOffset != null)
        {
            _handOffset.localPosition = _baseLocalPosition;
            _handOffset.localScale = _baseScale;
        }

        _scaleApplied = false;
    }
}
