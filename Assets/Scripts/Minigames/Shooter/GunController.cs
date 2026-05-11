using System;
using System.Collections;
using UnityEngine;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Self-contained visual controller for the revolver gun prefab.
    /// Handles shooting animations (cylinder rotation, cockpit kick, recoil),
    /// reload animation (barrel hinge open/close), smooth LookAt aiming,
    /// ammo tracking (6-chamber revolver), hitscan detection, aim preview,
    /// bullet trail visual, and fire rate limiting.
    ///
    /// MediaPipe-independent — driven entirely by its public API:
    ///   Shoot(), Reload(), LookAt(Vector3)
    ///
    /// The hitscan ray originates from _muzzleTransform (barrel tip) along its forward
    /// direction. An aim preview sphere shows the current hit point continuously.
    /// On shoot, a bullet trail (LineRenderer) is drawn from muzzle to hit point.
    ///
    /// Cylinder bore axis is fixed to local Z (Vector3.forward) — confirmed correct for this FBX.
    ///
    /// Includes an optional debug input mode (mouse click to shoot, P to toggle mouse aim)
    /// for standalone testing outside the MediaPipe pipeline.
    /// </summary>
    public class GunController : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("3 gunshot clips that cycle/alternate on each shot.")]
        [SerializeField] private AudioClip[] _shootSFX;
        [SerializeField] private AudioClip _reloadSFX;

        [Header("Animated Parts")]
        [SerializeField] private Transform _cylinder;
        [SerializeField] private Transform _barrelHinge;
        [SerializeField] private Transform _cockpit;
        [SerializeField] private Transform _recoilRoot;

        [Header("Shoot Animation — Cylinder")]
        [SerializeField] private float _cylinderRotationAngle = 60f;

        [Header("Shoot Animation — Cockpit Kick")]
        [SerializeField] private float _cockpitTravel = 0.05f;
        [SerializeField] private Vector3 _cockpitKickAxis = Vector3.back;
        [SerializeField] private float _cockpitDuration = 0.12f;

        [Header("Shoot Animation — Recoil")]
        [SerializeField] private float _recoilDistance = 0.03f;
        [SerializeField] private Vector3 _recoilKickAxis = Vector3.back;
        [SerializeField] private float _recoilDuration = 0.10f;

        [Header("Reload Animation — Barrel Hinge")]
        [SerializeField] private float _barrelOpenAngle = 45f;
        [SerializeField] private Vector3 _barrelOpenAxis = Vector3.up;
        [SerializeField] private float _barrelOpenDuration = 0.30f;
        [SerializeField] private float _barrelHoldDuration = 0.20f;
        [SerializeField] private float _barrelCloseDuration = 0.30f;

        [Header("LookAt")]
        [SerializeField] private float _rotationSpeed = 360f;

        [Header("Muzzle Flash")]
        [SerializeField] private GameObject _muzzleFlash;
        [SerializeField] private float _muzzleFlashDuration = 0.05f;

        [Header("Ammo")]
        [SerializeField] private int _maxAmmo = 6;
        [SerializeField] private bool _autoReloadOnEmpty = true;

        [Header("Hitscan / Shooting")]
        [Tooltip("Barrel tip transform — ray origin for hitscan. If null, falls back to this transform.")]
        [SerializeField] private Transform _muzzleTransform;
        [SerializeField] private float _maxShootDistance = 50f;
        public LayerMask _hitLayerMask = 0;

        [Header("Fire Rate")]
        [SerializeField] private float _fireDelay = 0.3f;

        [Header("Aim Preview")]
        [Tooltip("Optional prefab (e.g. small sphere) placed at the current aim hit point.")]
        [SerializeField] private GameObject _aimPreviewPrefab;
        [SerializeField] private Color _aimPreviewValidColor = Color.green;
        [SerializeField] private Color _aimPreviewInvalidColor = Color.red;

        [Header("Bullet Trail")]
        [Tooltip("Optional material for the procedural bullet trail LineRenderer.")]
        [SerializeField] private Material _bulletTrailMaterial;
        [SerializeField] private float _bulletTrailDuration = 0.1f;
        [SerializeField] private float _bulletTrailWidth = 0.02f;
        [SerializeField] private Color _bulletTrailColor = Color.white;

        [Header("Debug Input (standalone testing)")]
        [SerializeField] private bool _useDebugInput = false;
        [SerializeField] private float _debugMouseSensitivity = 2f;

        // Cached rest poses (local values only)
        private Quaternion _cylinderRestRotation;
        private Vector3 _cylinderRestLocalPosition;
        private Vector3 _cylinderPivotOffset;
        private Vector3 _cockpitRestPosition;
        private Vector3 _recoilRootRestPosition;
        private Quaternion _barrelHingeRestRotation;

        // State
        private bool _isShooting;
        private bool _isReloading;
        private bool _debugInputEnabled = true;
        private bool _debugInputAllowed;
        private int _currentAmmo;
        private bool _debugOverridingRotation;
        private float _lastFireTime;
        private int _lastShootIndex;

        // Aim preview instance
        private GameObject _aimPreviewInstance;
        private AimPreview _aimPreviewComponent;

        // Debug yaw/pitch
        private float _debugYaw;
        private float _debugPitch;

        // ------ Events ------

        public event Action<int, int> OnAmmoChanged;
        public event Action OnOutOfAmmo;
        public event Action OnReloadStarted;
        public event Action OnReloadCompleted;
        public event Action<Target> OnTargetHit;
        public event Action OnShotMissed;

        // ------ Public API ------

        public void SetDebugInputAllowed(bool allowed) => _debugInputAllowed = allowed;

        public bool IsShooting => _isShooting;
        public bool IsReloading => _isReloading;
        public int CurrentAmmo => _currentAmmo;
        public int MaxAmmo => _maxAmmo;
        public bool IsEmpty => _currentAmmo <= 0;

        public Vector3 MuzzlePosition
        {
            get
            {
                if (_muzzleTransform != null) return _muzzleTransform.position;
                if (_barrelHinge != null) return _barrelHinge.position;
                return transform.position;
            }
        }

        public Vector3 MuzzleForward
        {
            get
            {
                if (_muzzleTransform != null) return _muzzleTransform.forward;
                if (_barrelHinge != null) return _barrelHinge.forward;
                return transform.forward;
            }
        }

        /// <summary>
        /// Fire one shot — muzzle flash, cylinder snap-rotates, then recoil + cockpit kick.
        /// Returns true if the shot was fired, false if empty, on cooldown, or already animating.
        /// Performs hitscan from the muzzle, shows bullet trail, and fires events.
        /// </summary>
        public bool Shoot()
        {
            if (_isShooting || _isReloading) return false;

            if (Time.time - _lastFireTime < _fireDelay) return false;

            if (_currentAmmo <= 0)
            {
                if (_autoReloadOnEmpty && !_isReloading)
                    Reload();
                return false;
            }

            _currentAmmo--;
            _lastFireTime = Time.time;
            OnAmmoChanged?.Invoke(_currentAmmo, _maxAmmo);

            if (_currentAmmo <= 0)
                OnOutOfAmmo?.Invoke();

            PerformHitscan();

            // Play alternating shoot sound
            if (_audioSource != null && _shootSFX != null && _shootSFX.Length > 0)
            {
                AudioClip clip = _shootSFX[_lastShootIndex % _shootSFX.Length];
                _audioSource.PlayOneShot(clip);
                _lastShootIndex++;
            }

            StartCoroutine(ShootSequence());
            return true;
        }

        public bool Reload()
        {
            if (_isShooting || _isReloading) return false;
            if (_currentAmmo >= _maxAmmo) return false;

            OnReloadStarted?.Invoke();

            if (_audioSource != null && _reloadSFX != null)
                _audioSource.PlayOneShot(_reloadSFX);

            StartCoroutine(ReloadSequence());
            return true;
        }

        /// <summary>
        /// Smoothly rotate the gun toward a world-space target position.
        /// </summary>
        public void LookAt(Vector3 target)
        {
            if (_debugOverridingRotation) return;

            Transform root = _recoilRoot != null ? _recoilRoot : transform;
            Vector3 direction = (target - root.position).normalized;

            if (direction.sqrMagnitude < 0.001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(direction);
            root.rotation = Quaternion.RotateTowards(root.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
        }

        // ------ Unity Lifecycle ------

        private void Awake()
        {
            CacheRestPoses();
            _currentAmmo = _maxAmmo;
            OnAmmoChanged?.Invoke(_currentAmmo, _maxAmmo);
            AutoCreateMuzzlePoint();
            CreateAimPreview();
        }

        private void Update()
        {
            UpdateAimPreview();

            if (!_useDebugInput) return;
            HandleDebugInput();
        }

        public void SetAimPreviewActive(bool active)
        {
            if (_aimPreviewInstance != null)
                _aimPreviewInstance.SetActive(active);
        }

        // ------ Coroutines ------

        private IEnumerator ShootSequence()
        {
            _isShooting = true;

            // Phase 1: Muzzle flash + cylinder snap rotation
            GameObject flashInstance = null;
            if (_muzzleFlash != null)
            {
                flashInstance = Instantiate(_muzzleFlash, _muzzleFlash.transform.position, _muzzleFlash.transform.rotation, transform);
                flashInstance.SetActive(true);
            }

            if (_cylinder != null)
            {
                Vector3 spinCenter = _cylinder.TransformPoint(_cylinderPivotOffset);
                Vector3 worldAxis  = _cylinder.TransformDirection(Vector3.forward);
                _cylinder.RotateAround(spinCenter, worldAxis, _cylinderRotationAngle);
            }

            yield return new WaitForSeconds(0.02f);

            // Phase 2: Recoil + cockpit kick back (concurrent, then return)
            float elapsed = 0f;
            float totalDuration = Mathf.Max(_cockpitDuration, _recoilDuration);

            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;

                if (_cockpit != null)
                {
                    float t = Mathf.Clamp01(elapsed / _cockpitDuration);
                    float phase = t < 0.5f
                        ? EaseOutCubic(t * 2f)
                        : 1f - EaseInCubic((t - 0.5f) * 2f);
                    _cockpit.localPosition = _cockpitRestPosition
                        + _cockpitKickAxis.normalized * (_cockpitTravel * phase);
                }

                if (_recoilRoot != null)
                {
                    float t = Mathf.Clamp01(elapsed / _recoilDuration);
                    float phase = t < 0.4f
                        ? EaseOutCubic(t / 0.4f)
                        : 1f - EaseInCubic((t - 0.4f) / 0.6f);
                    _recoilRoot.localPosition = _recoilRootRestPosition
                        + _recoilKickAxis.normalized * (_recoilDistance * phase);
                }

                yield return null;
            }

            if (_cockpit != null)
                _cockpit.localPosition = _cockpitRestPosition;

            if (_recoilRoot != null)
                _recoilRoot.localPosition = _recoilRootRestPosition;

            if (flashInstance != null)
                Destroy(flashInstance, _muzzleFlashDuration);

            _isShooting = false;
        }

        private IEnumerator ReloadSequence()
        {
            _isReloading = true;

            if (_barrelHinge != null)
            {
                _barrelHinge.localRotation = _barrelHingeRestRotation;

                Vector3 axis = _barrelOpenAxis.normalized;

                // Open
                float elapsed = 0f;
                while (elapsed < _barrelOpenDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = EaseInOutCubic(Mathf.Clamp01(elapsed / _barrelOpenDuration));
                    _barrelHinge.localRotation = _barrelHingeRestRotation
                        * Quaternion.AngleAxis(_barrelOpenAngle * t, axis);
                    yield return null;
                }

                yield return new WaitForSeconds(_barrelHoldDuration);

                // Close
                elapsed = 0f;
                while (elapsed < _barrelCloseDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = EaseInOutCubic(Mathf.Clamp01(elapsed / _barrelCloseDuration));
                    _barrelHinge.localRotation = _barrelHingeRestRotation
                        * Quaternion.AngleAxis(_barrelOpenAngle * (1f - t), axis);
                    yield return null;
                }

                _barrelHinge.localRotation = _barrelHingeRestRotation;
            }

            if (_cylinder != null)
            {
                _cylinder.localPosition = _cylinderRestLocalPosition;
                _cylinder.localRotation = _cylinderRestRotation;
            }

            _currentAmmo = _maxAmmo;
            OnAmmoChanged?.Invoke(_currentAmmo, _maxAmmo);
            OnReloadCompleted?.Invoke();
            _isReloading = false;
        }

        // ------ Helpers ------

        private void CacheRestPoses()
        {
            if (_cylinder != null)
            {
                _cylinderRestRotation = _cylinder.localRotation;
                _cylinderRestLocalPosition = _cylinder.localPosition;

                MeshFilter mf = _cylinder.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Vector3 worldCenter = mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
                    _cylinderPivotOffset = _cylinder.InverseTransformPoint(worldCenter);
                }
            }

            if (_cockpit != null)
                _cockpitRestPosition = _cockpit.localPosition;

            if (_recoilRoot != null)
                _recoilRootRestPosition = _recoilRoot.localPosition;

            if (_barrelHinge != null)
                _barrelHingeRestRotation = _barrelHinge.localRotation;
        }

        private void HandleDebugInput()
        {
            if (!_debugInputAllowed) return;

            if (Input.GetKeyDown(KeyCode.P))
            {
                _debugInputEnabled = !_debugInputEnabled;
                Cursor.lockState = _debugInputEnabled ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !_debugInputEnabled;
                _debugOverridingRotation = _debugInputEnabled;
            }

            if (!_debugInputEnabled) return;

            float mouseX = Input.GetAxis("Mouse X") * _debugMouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * _debugMouseSensitivity;

            _debugYaw += mouseX;
            _debugPitch += mouseY;
            _debugPitch = Mathf.Clamp(_debugPitch, -80f, 80f);

            // Apply rotation to the gun root
            Transform root = _recoilRoot != null ? _recoilRoot : transform;
            root.localRotation = Quaternion.Euler(0f, _debugYaw, _debugPitch);

            if (Input.GetMouseButtonDown(0)) { Shoot(); }
            if (Input.GetKeyDown(KeyCode.R)) { Reload(); }
        }

        // ------ Auto Muzzle Creation ------

        private void AutoCreateMuzzlePoint()
        {
            if (_muzzleTransform != null) return;

            Transform parent = _barrelHinge != null ? _barrelHinge : transform;
            MeshFilter mf = _barrelHinge != null ? _barrelHinge.GetComponentInChildren<MeshFilter>() : null;

            GameObject muzzleObj = new GameObject("Muzzle_Auto");
            muzzleObj.transform.SetParent(parent, false);

            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 meshCenter = mf.sharedMesh.bounds.center;
                Vector3 meshExtents = mf.sharedMesh.bounds.extents;
                muzzleObj.transform.localPosition = new Vector3(0f, 0f, meshCenter.z + meshExtents.z);
            }
            else
            {
                muzzleObj.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            }

            muzzleObj.transform.localRotation = Quaternion.identity;
            _muzzleTransform = muzzleObj.transform;
        }

        // ------ Hitscan & Bullet Trail ------

        private void PerformHitscan()
        {
            Vector3 origin = MuzzlePosition;
            Vector3 direction = MuzzleForward;
            bool hitTarget = false;
            Vector3 hitPoint;

            Debug.DrawRay(origin, direction * _maxShootDistance, Color.red, 2f);

            Ray ray = new Ray(origin, direction);
            if (Physics.Raycast(ray, out RaycastHit hit3D, _maxShootDistance, _hitLayerMask, QueryTriggerInteraction.Collide))
            {
                hitPoint = hit3D.point;
                Target target = hit3D.collider.GetComponent<Target>();
                if (target != null)
                {
                    target.OnHit();
                    OnTargetHit?.Invoke(target);
                    hitTarget = true;
                }
                Debug.Log($"[GunController] Hitscan: origin={origin} dir={direction} hit={hit3D.collider.name} point={hitPoint} target={hitTarget}");
            }
            else
            {
                hitPoint = origin + direction * _maxShootDistance;
                Debug.Log($"[GunController] Hitscan: origin={origin} dir={direction} miss (max dist) point={hitPoint}");
            }

            if (!hitTarget)
                OnShotMissed?.Invoke();

            ShowBulletTrail(origin, hitPoint);
        }

        private void ShowBulletTrail(Vector3 from, Vector3 to)
        {
            GameObject trailObj = new GameObject("BulletTrail");
            trailObj.transform.position = from;

            LineRenderer lr = trailObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = _bulletTrailWidth;
            lr.endWidth = _bulletTrailWidth * 0.5f;
            lr.material = _bulletTrailMaterial != null
                ? _bulletTrailMaterial
                : new Material(Shader.Find("Sprites/Default"));
            lr.startColor = _bulletTrailColor;
            lr.endColor = new Color(_bulletTrailColor.r, _bulletTrailColor.g, _bulletTrailColor.b, 0f);

            StartCoroutine(CoFadeTrail(lr, trailObj));
        }

        private IEnumerator CoFadeTrail(LineRenderer lr, GameObject trailObj)
        {
            float elapsed = 0f;
            Color startColor = _bulletTrailColor;
            Color endColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

            while (elapsed < _bulletTrailDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / _bulletTrailDuration;
                lr.startColor = Color.Lerp(startColor, endColor, t);
                lr.endColor = new Color(lr.startColor.r, lr.startColor.g, lr.startColor.b, 0f);
                yield return null;
            }

            Destroy(trailObj);
        }

        // ------ Aim Preview ------

        private void CreateAimPreview()
        {
            if (_aimPreviewPrefab != null)
            {
                _aimPreviewInstance = Instantiate(_aimPreviewPrefab, transform);
                _aimPreviewComponent = _aimPreviewInstance.GetComponent<AimPreview>();
            }
            else
            {
                _aimPreviewInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _aimPreviewInstance.name = "AimPreview";
                _aimPreviewInstance.transform.SetParent(transform, false);
                _aimPreviewInstance.transform.localScale = Vector3.one * 0.5f;
                Destroy(_aimPreviewInstance.GetComponent<Collider>());
                _aimPreviewComponent = _aimPreviewInstance.AddComponent<AimPreview>();
            }
        }

        private void UpdateAimPreview()
        {
            if (_aimPreviewInstance == null || !_aimPreviewInstance.activeInHierarchy)
                return;

            if (_isShooting || _isReloading)
                return;

            Vector3 origin = MuzzlePosition;
            Vector3 direction = MuzzleForward;
            Vector3 previewPoint;
            bool isTarget = false;

            Ray ray = new Ray(origin, direction);
            if (Physics.Raycast(ray, out RaycastHit hit3D, _maxShootDistance, _hitLayerMask, QueryTriggerInteraction.Collide))
            {
                previewPoint = hit3D.point;
                isTarget = hit3D.collider.GetComponent<Target>() != null;
            }
            else
            {
                previewPoint = origin + direction * _maxShootDistance;
            }

            _aimPreviewInstance.transform.position = previewPoint;

            if (_aimPreviewComponent != null)
            {
                _aimPreviewComponent.SetPreviewColor(isTarget
                    ? _aimPreviewValidColor
                    : _aimPreviewInvalidColor);
            }
        }

        // ------ Easing Functions ------

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseInCubic(float t) => t * t * t;

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }
    }
}
