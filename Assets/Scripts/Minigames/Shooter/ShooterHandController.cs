using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Hand;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Controls aiming and shooting via hand tracking.
    /// - Index fingertip screen position → aim ray (Camera.ScreenPointToRay)
    /// - Closed fist → fire (delegates to GunController which handles hitscan)
    /// - ThumbDown → safety toggle
    ///
    /// Hit detection is handled entirely by GunController (hitscan from muzzle).
    /// This controller only sets the aim direction and triggers shoot/reload.
    /// </summary>
    [RequireComponent(typeof(Hand3DProjector), typeof(GestureDetector))]
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

        private Hand3DProjector _projector;
        private GestureDetector _gestureDetector;
        private Camera _mainCamera;

        private bool _safetyOn;
        private float _lastFireTime;
        private bool _canFire = true;

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
            _projector = GetComponent<Hand3DProjector>();
            _gestureDetector = GetComponent<GestureDetector>();
            _mainCamera = Camera.main;
            _safetyOn = _startWithSafetyOn;
        }

        private void OnEnable()
        {
            _gestureDetector.OnClosedFist += HandleFist;
            _gestureDetector.OnThumbDown += HandleThumbDown;
        }

        private void OnDisable()
        {
            _gestureDetector.OnClosedFist -= HandleFist;
            _gestureDetector.OnThumbDown -= HandleThumbDown;
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
        /// Uses Camera.ScreenPointToRay for reliable depth projection — same approach
        /// as the debug mouse aiming in GunController.
        ///
        /// The normalized landmark coords from MediaPipe are mirrored on X (1f - x)
        /// to match the webcam mirror display.
        /// </summary>
        private void UpdateAimRay()
        {
            var norm = _projector.LastNormalizedLandmarks.landmarks;
            if (norm == null || norm.Count < 21)
            {
                IsAiming = false;
                return;
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            // Index fingertip (landmark 8) in normalized image coords
            var tip = norm[8];

            // Convert to screen pixel position, mirroring X for webcam parity
            // (matches Hand3DProjector's 1f - landmarks[i].x in HandleHandDetected)
            Vector3 screenPos = new Vector3(
                (1f - tip.x) * Screen.width,
                (1f - tip.y) * Screen.height,
                0f
            );

            // Aim origin: project fingertip to a point ~10 units in front of camera
            AimOrigin = _mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10f));

            // Cast ray from camera through the fingertip screen position
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

            // Debug visualization
            if (_showDebugRay)
            {
                Debug.DrawRay(AimOrigin, AimDirection * _maxRayDistance, _safetyOn ? Color.yellow : Color.red);
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
