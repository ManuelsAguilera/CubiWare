using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Hand;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Controls aiming and shooting via hand tracking.
    /// - Index fingertip screen position → aim ray (Camera.ScreenPointToRay)
    /// - Closed fist → fire (delegates to GunController which handles hitscan)
    /// - ThumbDown → safety toggle
    ///
    /// Hand data is consumed through the <see cref="IHandDetector"/> interface,
    /// decoupling this controller from MediaPipeController directly.
    ///
    /// Hit detection is handled entirely by GunController (hitscan from muzzle).
    /// This controller only sets the aim direction and triggers shoot/reload.
    /// </summary>
    public class ShooterHandController : MonoBehaviour
    {
        [Header("Gun Visual")]
        [SerializeField] private GunController _gunController;

        [Header("Aiming")]
        [SerializeField] private float _maxRayDistance = 50f;
        [SerializeField] private LayerMask _targetLayer;
        [SerializeField] private bool _showDebugRay = true;

        [Header("Shooting")]
        [SerializeField] private float _fireCooldown = 0.3f;

        [Header("Safety")]
        [SerializeField] private bool _startWithSafetyOn = true;

        private GestureDetector _gestureDetector;
        private Camera _mainCamera;

        private bool _safetyOn;
        private float _lastFireTime;
        private bool _canFire = true;

        // ── IHandDetector integration ───────────────────────────────────────
        private IHandDetector _handDetector;
        private HandLandmarkData _lastHandData;
        private bool _hasHandData;

        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        /// <summary>Target world point the gun should look at (computed from aim ray).</summary>
        private Vector3 _aimTargetPoint;

        /// <summary>Current aim ray direction in world space.</summary>
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        /// <summary>Current aim ray origin in world space (near-camera point).</summary>
        public Vector3 AimOrigin { get; private set; } = Vector3.zero;

        /// <summary>Whether the hand is currently detected and aiming.</summary>
        public bool IsAiming { get; private set; } = false;

        private void Awake()
        {
            _gestureDetector = GetComponent<GestureDetector>();
            _mainCamera = Camera.main;
            _safetyOn = _startWithSafetyOn;

            // Resolve IHandDetector
            _handDetector = FindFirstObjectByType<MonoBehaviour>() as IHandDetector;
            if (_handDetector == null && MediaPipeController.Instance != null)
            {
                _handDetector = MediaPipeController.Instance.HandDetector;
            }
        }

        private void OnEnable()
        {
            _gestureDetector.OnClosedFist += HandleFist;
            _gestureDetector.OnThumbDown += HandleThumbDown;

            if (_handDetector != null)
            {
                _handDetector.OnHandDetected += HandleHandDetected;
                _handDetector.OnHandLost += HandleHandLost;
            }
        }

        private void OnDisable()
        {
            _gestureDetector.OnClosedFist -= HandleFist;
            _gestureDetector.OnThumbDown -= HandleThumbDown;

            if (_handDetector != null)
            {
                _handDetector.OnHandDetected -= HandleHandDetected;
                _handDetector.OnHandLost -= HandleHandLost;
            }
        }

        /// <summary>
        /// Caches the latest hand landmark data for use in <see cref="UpdateAimRay"/>.
        /// </summary>
        private void HandleHandDetected(HandLandmarkData data)
        {
            _lastHandData = data;
            _hasHandData = data.Landmarks != null && data.Landmarks.Count >= 21;
        }

        /// <summary>
        /// Clears cached hand data when tracking is lost.
        /// </summary>
        private void HandleHandLost()
        {
            _hasHandData = false;
        }

        private void Update()
        {
            UpdateAimRay();

            // Rotate the gun to face the aim target
            if (_gunController != null && IsAiming)
            {
                _gunController.LookAt(_aimTargetPoint);
            }
        }

        /// <summary>
        /// Computes the aim ray from the index fingertip screen position.
        /// </summary>
        private void UpdateAimRay()
        {
            if (_hasHandData && _lastHandData.Landmarks != null && _lastHandData.Landmarks.Count >= 21)
            {
                if (_mainCamera == null) _mainCamera = Camera.main;
                if (_mainCamera == null) return;

                // Index fingertip (landmark 8)
                Vector2 tip = _lastHandData.Landmarks[8];

                // Convert to screen pixel position, mirroring X
                Vector3 screenPos = new Vector3(
                    (1f - tip.x) * Screen.width,
                    (1f - tip.y) * Screen.height,
                    0f
                );

                // Aim origin
                AimOrigin = _mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));

                // Cast ray
                Ray ray = _mainCamera.ScreenPointToRay(screenPos);

                if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _targetLayer))
                {
                    _aimTargetPoint = hit.point;
                    AimDirection = (_aimTargetPoint - AimOrigin).normalized;
                }
                else
                {
                    _aimTargetPoint = ray.origin + ray.direction * _maxRayDistance;
                    AimDirection = ray.direction;
                }

                IsAiming = true;

                if (_showDebugRay)
                {
                    Debug.DrawRay(AimOrigin, AimDirection * _maxRayDistance, _safetyOn ? Color.yellow : Color.red);
                }
            }
            else
            {
                IsAiming = false;
            }
        }

        /// <summary>
        /// Called when ClosedFist gesture is detected.
        /// If safety is off and cooldown has elapsed, fires the gun.
        /// </summary>
        private void HandleFist()
        {
            if (_safetyOn) return;

            if (!_canFire) return;
            if (Time.time - _lastFireTime < _fireCooldown) return;

            Fire();
        }

        /// <summary>Called when ThumbDown gesture is detected — toggles safety.</summary>
        private void HandleThumbDown()
        {
            _safetyOn = !_safetyOn;
        }

        /// <summary>
        /// Delegates firing to GunController.Shoot(), which handles:
        /// - Ammo decrement & auto-reload
        /// - Hitscan raycast from muzzle → Target hit detection
        /// - Bullet trail visual
        /// - Fire rate limiting
        /// </summary>
        private void Fire()
        {
            _lastFireTime = Time.time;
            _canFire = false;

            // GunController handles hitscan, aim preview, bullet trail, and events internally.
            _gunController?.Shoot();

            // Reset fire cooldown
            Invoke(nameof(ResetFireCooldown), _fireCooldown);
        }

        /// <summary>Trigger a reload animation on the gun (if assigned).</summary>
        public void Reload()
        {
            if (_gunController != null)
            {
                _gunController.Reload();
            }
        }

        private void ResetFireCooldown()
        {
            _canFire = true;
        }
    }
}
