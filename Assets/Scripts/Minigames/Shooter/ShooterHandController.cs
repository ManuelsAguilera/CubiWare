using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Hand;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Controls aiming and shooting via hand tracking.
    /// - Index finger tip position → aim ray direction
    /// - Closed fist → fire a bullet
    /// - ThumbDown → safety toggle (optional)
    /// </summary>
    [RequireComponent(typeof(Hand3DProjector), typeof(GestureDetector))]
    public class ShooterHandController : MonoBehaviour
    {
        [Header("Aiming")]
        [SerializeField] private float _maxRayDistance = 50f;
        [SerializeField] private LayerMask _targetLayer;
        [SerializeField] private bool _showDebugRay = true;

        [Header("Shooting")]
        [SerializeField] private GameObject _bulletPrefab;
        [SerializeField] private Transform _bulletSpawnPoint;
        [SerializeField] private float _bulletSpeed = 40f;
        [SerializeField] private float _fireCooldown = 0.3f;

        [Header("Safety")]
        [SerializeField] private bool _startWithSafetyOn = true;

        private Hand3DProjector _projector;
        private GestureDetector _gestureDetector;
        private Camera _mainCamera;

        private bool _safetyOn;
        private float _lastFireTime;
        private bool _canFire = true;

        /// <summary>Current aim ray direction in world space.</summary>
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        /// <summary>Current aim ray origin in world space.</summary>
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
        }

        /// <summary>
        /// Computes the aim ray from the index finger tip (landmark 8) forward direction.
        /// Uses the direction from index MCP (landmark 5) to index tip (landmark 8) as the aim vector.
        /// </summary>
        private void UpdateAimRay()
        {
            var positions = _projector.LandmarkWorldPositions;
            if (positions[0] == Vector3.back || positions[5] == Vector3.back || positions[8] == Vector3.back)
            {
                IsAiming = false;
                return;
            }

            Vector3 indexMcp = positions[5];   // Index MCP joint
            Vector3 indexTip = positions[8];    // Index fingertip

            AimOrigin = indexTip;
            AimDirection = (indexTip - indexMcp).normalized;

            if (AimDirection == Vector3.zero)
            {
                AimDirection = _mainCamera != null ? _mainCamera.transform.forward : Vector3.forward;
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
        /// If safety is off and cooldown has elapsed, fires a bullet.
        /// </summary>
        private void HandleFist()
        {
            if (_safetyOn)
            {
                Debug.Log("[ShooterHand] Safety is ON — fist ignored.");
                return;
            }

            if (!_canFire) return;
            if (Time.time - _lastFireTime < _fireCooldown) return;

            Fire();
        }

        /// <summary>Called when ThumbDown gesture is detected — toggles safety.</summary>
        private void HandleThumbDown()
        {
            _safetyOn = !_safetyOn;
            Debug.Log($"[ShooterHand] Safety {( _safetyOn ? "ON" : "OFF" )}");
        }

        /// <summary>Spawns a bullet and propels it along the aim direction.</summary>
        private void Fire()
        {
            _lastFireTime = Time.time;
            _canFire = false;

            Vector3 spawnPos = _bulletSpawnPoint != null
                ? _bulletSpawnPoint.position
                : AimOrigin;

            if (_bulletPrefab != null)
            {
                GameObject bullet = Instantiate(_bulletPrefab, spawnPos, Quaternion.LookRotation(AimDirection));
                Rigidbody rb = bullet.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = AimDirection * _bulletSpeed;
                }

                // Auto-destroy after max distance
                Destroy(bullet, _maxRayDistance / _bulletSpeed);
            }
            else
            {
                // No bullet prefab — use hitscan raycast
                PerformHitscan();
            }

            Debug.Log("[ShooterHand] FIRE!");

            // Reset fire cooldown
            Invoke(nameof(ResetFireCooldown), _fireCooldown);
        }

        private void ResetFireCooldown()
        {
            _canFire = true;
        }

        /// <summary>Hitscan fallback when no bullet prefab is assigned.</summary>
        private void PerformHitscan()
        {
            if (Physics.Raycast(AimOrigin, AimDirection, out RaycastHit hit, _maxRayDistance, _targetLayer))
            {
                Target target = hit.collider.GetComponent<Target>();
                if (target != null)
                {
                    target.OnHit();
                }
            }
        }
    }
}
