using System;
using com.DMT.BrokenReality2000;
using UnityEngine;

namespace FixedReality2000;

internal sealed class PlayerMotionEffects : IDisposable
{
    private BrokenPlayer? _player;
    private Transform? _cameraTransform;
    private Vector3 _appliedOffset;
    private float _phase;
    private float _horizontalDistance;
    private int _movementFrame = -10;
    private bool _sprinting;

    internal void RecordMovement(
        BrokenPlayer player,
        float horizontalDistance,
        bool sprinting)
    {
        _player = player;
        _horizontalDistance = horizontalDistance;
        _sprinting = sprinting;
        _movementFrame = Time.frameCount;
    }

    internal void LateTick()
    {
        if (!Plugin.EnableHeadBobbing.Value)
        {
            RestoreCameraOffset();
            return;
        }

        Transform? cameraTransform = ResolveCameraTransform();
        if (cameraTransform == null)
        {
            RestoreCameraOffset();
            return;
        }

        if (_cameraTransform != cameraTransform)
        {
            RestoreCameraOffset();
            _cameraTransform = cameraTransform;
            _appliedOffset = Vector3.zero;
        }

        Vector3 baseLocalPosition = cameraTransform.localPosition - _appliedOffset;
        bool movingThisFrame =
            Time.frameCount - _movementFrame <= 1 &&
            _horizontalDistance > 0.00001f &&
            Time.deltaTime > 0f;

        Vector3 targetOffset = Vector3.zero;
        if (movingThisFrame)
        {
            float frequency = Plugin.HeadBobFrequency.Value * (_sprinting ? 1.25f : 1f);
            float amplitude = Plugin.HeadBobAmplitude.Value * (_sprinting ? 1.15f : 1f);
            _phase += Time.deltaTime * frequency;

            targetOffset = new Vector3(
                Mathf.Cos(_phase * 0.5f) * amplitude * 0.35f,
                Mathf.Sin(_phase) * amplitude,
                0f);
        }

        float smoothing = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        _appliedOffset = Vector3.Lerp(_appliedOffset, targetOffset, smoothing);
        if (_appliedOffset.sqrMagnitude < 0.00000001f && targetOffset == Vector3.zero)
        {
            _appliedOffset = Vector3.zero;
        }

        cameraTransform.localPosition = baseLocalPosition + _appliedOffset;
    }

    internal void ApplyConfiguration()
    {
        if (!Plugin.EnableHeadBobbing.Value)
        {
            RestoreCameraOffset();
        }
    }

    internal void OnSceneLoaded()
    {
        RestoreCameraOffset();
        _player = null;
        _horizontalDistance = 0f;
        _movementFrame = -10;
        _sprinting = false;
        _phase = 0f;
    }

    public void Dispose()
    {
        OnSceneLoaded();
    }

    private Transform? ResolveCameraTransform()
    {
        BrokenPlayer? player = _player;
        if (player == null || !player.isActiveAndEnabled)
        {
            return null;
        }

        Camera playerCamera = player.cam;
        if (playerCamera != null && playerCamera.isActiveAndEnabled)
        {
            return playerCamera.transform;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null && mainCamera.isActiveAndEnabled
            ? mainCamera.transform
            : null;
    }

    private void RestoreCameraOffset()
    {
        Transform? cameraTransform = _cameraTransform;
        if (cameraTransform != null)
        {
            cameraTransform.localPosition -= _appliedOffset;
        }

        _cameraTransform = null;
        _appliedOffset = Vector3.zero;
    }
}
