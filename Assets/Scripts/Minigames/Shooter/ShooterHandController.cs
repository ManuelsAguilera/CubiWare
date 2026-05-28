using System.Collections.Generic;
using UnityEngine;
using ARcadeRush.Hand;
using ARcadeRush.Core;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Moves the gun to follow the palm center (cursor approach).
    /// The gun translates in XY screen space at a fixed Z depth — no rotation.
    /// ClosedFist gesture fires.
    /// </summary>
    public class ShooterHandController : MonoBehaviour
    {
        [Header("Gun Visual")]
        [SerializeField] private GunController _gunController;

        [Header("Aiming")]
        [SerializeField] private float _maxRayDistance = 50f;
        [SerializeField] private bool _showDebugRay = true;

        [Header("Shooting")]
        [SerializeField] private float _fireCooldown = 0.3f;

        [Header("Cursor Settings")]
        [SerializeField] private Hand3DProjector _projector;
        [SerializeField] private Vector3 _gunLocalOffset = new Vector3(0f, -0.3f, 0.6f);
        [SerializeField] private int _smoothingFrames = 5;

        [Header("Camera Follow")]
        [SerializeField] private float _minCameraY = 0.55f;
        [SerializeField] private float _maxCameraY = 10f;
        [SerializeField] private float _maxExtendX = 8f;
        [SerializeField] private float _maxExtendY = 5f;

        private static readonly int[] PalmIndices = { 0, 5, 9, 13, 17 };

        private GestureDetector _gestureDetector;
        private Camera _mainCamera;

        private float _lastFireTime;
        private bool _canFire = true;

        private readonly Queue<float> _palmXBuffer = new Queue<float>();
        private readonly Queue<float> _palmYBuffer = new Queue<float>();
        private Vector3 _initialCameraPos;

        /// <summary>Whether the hand is currently tracked and the gun is following it.</summary>
        public bool IsAiming { get; private set; } = false;

        private void Awake()
        {
            _gestureDetector = GetComponent<GestureDetector>();
            _mainCamera = Camera.main;
            if (_mainCamera != null)
                _initialCameraPos = _mainCamera.transform.position;
        }

        private void Start()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || _gunController == null) return;

            _initialCameraPos = _mainCamera.transform.position;
            _gunController.transform.position =
                _mainCamera.transform.position
                + _mainCamera.transform.right   * _gunLocalOffset.x
                + _mainCamera.transform.up      * _gunLocalOffset.y
                + _mainCamera.transform.forward * _gunLocalOffset.z;
            _gunController.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        }

        private void OnEnable()
        {
            _gestureDetector.OnClosedFist += HandleClosedFist;
        }

        private void OnDisable()
        {
            _gestureDetector.OnClosedFist -= HandleClosedFist;
        }

        private void Update()
        {
            UpdateGunPosition();
        }

        private void UpdateGunPosition()
        {
            if (_projector == null)
            {
                IsAiming = false;
                return;
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            var normalized = _projector.LastNormalizedLandmarks;
            if (normalized.landmarks == null || normalized.landmarks.Count < 21)
            {
                IsAiming = false;
                return;
            }

            // Palm center: average of landmarks 0, 5, 9, 13, 17 in screen space.
            // X is mirrored (1-x) to compensate for webcam horizontal flip.
            // Y is NOT flipped — MediaPipe Y=0 is at bottom, matching Unity screen space.
            float sumX = 0f, sumY = 0f;
            foreach (int i in PalmIndices)
            {
                sumX += (1f - normalized.landmarks[i].x) * Screen.width;
                sumY += normalized.landmarks[i].y * Screen.height;
            }
            float rawX = sumX / PalmIndices.Length;
            float rawY = sumY / PalmIndices.Length;

            float smoothX = Smooth(_palmXBuffer, rawX);
            float smoothY = Smooth(_palmYBuffer, rawY);

            // Direct mapping: hand position → camera position, no lag or threshold zones.
            // Full screen width maps to [-_maxExtendX, +_maxExtendX] around initial position.
            float palmNormX = smoothX / Screen.width;
            float palmNormY = smoothY / Screen.height;

            float targetX = _initialCameraPos.x + (palmNormX - 0.5f) * 2f * _maxExtendX;
            float targetY = _initialCameraPos.y + (palmNormY - 0.5f) * 2f * _maxExtendY;
            targetY = Mathf.Clamp(targetY, _minCameraY, _maxCameraY);

            _mainCamera.transform.position = new Vector3(targetX, targetY, _initialCameraPos.z);

            // Gun stays fixed relative to camera using local offset.
            // X = right/left, Y = up/down, Z = forward depth (all in camera-local space).
            Vector3 gunWorldPos = _mainCamera.transform.position
                + _mainCamera.transform.right   * _gunLocalOffset.x
                + _mainCamera.transform.up      * _gunLocalOffset.y
                + _mainCamera.transform.forward * _gunLocalOffset.z;

            _gunController.transform.position = gunWorldPos;
            _gunController.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            IsAiming = true;

            if (_showDebugRay)
                Debug.DrawRay(gunWorldPos, _gunController.transform.forward * _maxRayDistance, Color.red);
        }

        private void HandleClosedFist()
        {
            if (!_canFire) return;
            if (Time.time - _lastFireTime < _fireCooldown) return;
            Fire();
        }

        private void Fire()
        {
            _lastFireTime = Time.time;
            _canFire = false;
            _gunController?.Shoot();
            Invoke(nameof(ResetFireCooldown), _fireCooldown);
        }

        public void Reload()
        {
            _gunController?.Reload();
        }

        private void ResetFireCooldown()
        {
            _canFire = true;
        }

        private float Smooth(Queue<float> buffer, float value)
        {
            buffer.Enqueue(value);
            if (buffer.Count > _smoothingFrames) buffer.Dequeue();
            float sum = 0f;
            foreach (var v in buffer) sum += v;
            return sum / buffer.Count;
        }
    }
}
