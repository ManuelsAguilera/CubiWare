using UnityEngine;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Self-contained visual controller for the revolver gun prefab.
    /// Handles shooting animations (cylinder rotation, cockpit kick, recoil),
    /// reload animation (barrel hinge open/close), and smooth LookAt aiming.
    /// 
    /// MediaPipe-independent — driven entirely by its public API:
    ///   Shoot(), Reload(), LookAt(Vector3)
    /// 
    /// Cylinder bore axis is fixed to local Z (Vector3.forward) — confirmed correct for this FBX.
    /// 
    /// Includes an optional debug input mode (mouse click to shoot, P to toggle mouse aim)
    /// for standalone testing outside the MediaPipe pipeline.
    /// </summary>
    public class GunController : MonoBehaviour
    {
        [Header("Animated Parts")]
        [SerializeField] private Transform _cylinder;
        [SerializeField] private Transform _barrelHinge;
        [SerializeField] private Transform _cockpit;
        [SerializeField] private Transform _recoilRoot;

        [Header("Shoot Animation — Cylinder")]
        [SerializeField] private float _cylinderRotationAngle = 60f;
        // Bore axis is local Z (Vector3.forward) — fixed, not configurable.

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

        [Header("Debug Input (standalone testing)")]
        [SerializeField] private bool _useDebugInput = true;

        // Cached rest poses (local values only)
        private Quaternion _cylinderRestRotation;
        private Vector3 _cylinderRestLocalPosition; // needed because RotateAround shifts localPosition
        private Vector3 _cylinderPivotOffset;       // mesh center in _cylinder local space
        private Vector3 _cockpitRestPosition;
        private Vector3 _recoilRootRestPosition;
        private Quaternion _barrelHingeRestRotation;

        // State
        private bool _isShooting;
        private bool _isReloading;
        private bool _debugInputEnabled = true;

        // ------ Public API ------

        /// <summary>True while a shoot animation is playing.</summary>
        public bool IsShooting => _isShooting;

        /// <summary>True while a reload animation is playing.</summary>
        public bool IsReloading => _isReloading;

        /// <summary>
        /// Fire one shot — muzzle flash, cylinder snap-rotates, then recoil + cockpit kick.
        /// Ignored if already shooting or reloading.
        /// </summary>
        public void Shoot()
        {
            if (_isShooting || _isReloading) return;
            StartCoroutine(ShootSequence());
        }

        /// <summary>
        /// Open and close the barrel hinge for reloading.
        /// Ignored if already shooting or reloading.
        /// </summary>
        public void Reload()
        {
            if (_isShooting || _isReloading) return;
            StartCoroutine(ReloadSequence());
        }

        /// <summary>
        /// Smoothly rotate the gun toward a world-space target position.
        /// </summary>
        public void LookAt(Vector3 target)
        {
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
        }

        private void Update()
        {
            if (!_useDebugInput) return;

            HandleDebugInput();
        }

        // ------ Coroutines ------

        private System.Collections.IEnumerator ShootSequence()
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
                // RotateAround the mesh's visual center so the pipe spins
                // on its own bore axis (local Z) even when the pivot is offset.
                Vector3 spinCenter = _cylinder.TransformPoint(_cylinderPivotOffset);
                Vector3 worldAxis  = _cylinder.TransformDirection(Vector3.forward);

                // RotateAround is additive on the transform — no need to track accumulated
                // rotation here. _cylinderRestRotation stays as the Awake-cached rest pose
                // so Reload can correctly restore it.
                _cylinder.RotateAround(spinCenter, worldAxis, _cylinderRotationAngle);
            }

            // Phase 2: Brief gap — muzzle visible, cylinder in next chamber
            yield return new WaitForSeconds(0.02f);

            // Phase 3: Recoil + cockpit kick back (concurrent, then return)
            float elapsed = 0f;
            float totalDuration = Mathf.Max(_cockpitDuration, _recoilDuration);

            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;

                // Cockpit — kick out then return
                if (_cockpit != null)
                {
                    float t = Mathf.Clamp01(elapsed / _cockpitDuration);
                    float phase = t < 0.5f
                        ? EaseOutCubic(t * 2f)              // 0→1 (kicking back)
                        : 1f - EaseInCubic((t - 0.5f) * 2f); // 1→0 (returning)
                    _cockpit.localPosition = _cockpitRestPosition
                        + _cockpitKickAxis.normalized * (_cockpitTravel * phase);
                }

                // Recoil — kick out then return
                if (_recoilRoot != null)
                {
                    float t = Mathf.Clamp01(elapsed / _recoilDuration);
                    float phase = t < 0.4f
                        ? EaseOutCubic(t / 0.4f)             // 0→1 (kicking back)
                        : 1f - EaseInCubic((t - 0.4f) / 0.6f); // 1→0 (returning)
                    _recoilRoot.localPosition = _recoilRootRestPosition
                        + _recoilKickAxis.normalized * (_recoilDistance * phase);
                }

                yield return null;
            }

            // Restore
            if (_cockpit != null)
                _cockpit.localPosition = _cockpitRestPosition;

            if (_recoilRoot != null)
                _recoilRoot.localPosition = _recoilRootRestPosition;

            // Muzzle flash cleanup
            if (flashInstance != null)
                Destroy(flashInstance, _muzzleFlashDuration);

            _isShooting = false;
        }

        private System.Collections.IEnumerator ReloadSequence()
        {
            _isReloading = true;

            if (_barrelHinge != null)
            {
                // Snap to rest pose first so the open animation always starts from default.
                _barrelHinge.localRotation = _barrelHingeRestRotation;

                Vector3 axis = _barrelOpenAxis.normalized;

                // Phase 1: Open
                float elapsed = 0f;
                while (elapsed < _barrelOpenDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = EaseInOutCubic(Mathf.Clamp01(elapsed / _barrelOpenDuration));
                    _barrelHinge.localRotation = _barrelHingeRestRotation
                        * Quaternion.AngleAxis(_barrelOpenAngle * t, axis);
                    yield return null;
                }

                // Phase 2: Hold open
                yield return new WaitForSeconds(_barrelHoldDuration);

                // Phase 3: Close
                elapsed = 0f;
                while (elapsed < _barrelCloseDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = EaseInOutCubic(Mathf.Clamp01(elapsed / _barrelCloseDuration));
                    _barrelHinge.localRotation = _barrelHingeRestRotation
                        * Quaternion.AngleAxis(_barrelOpenAngle * (1f - t), axis);
                    yield return null;
                }

                // Restore barrel
                _barrelHinge.localRotation = _barrelHingeRestRotation;
            }

            // Restore cylinder to its original spawn pose so repeated
            // reload cycles don't accumulate position drift from RotateAround.
            if (_cylinder != null)
            {
                _cylinder.localPosition = _cylinderRestLocalPosition;
                _cylinder.localRotation = _cylinderRestRotation;
            }

            _isReloading = false;
        }

        // ------ Helpers ------

        private void CacheRestPoses()
        {
            if (_cylinder != null)
            {
                _cylinderRestRotation      = _cylinder.localRotation;
                _cylinderRestLocalPosition = _cylinder.localPosition;

                // Cache the mesh center in _cylinder's local space so
                // RotateAround spins the pipe in-place (not around the pivot).
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
            // P key toggles mouse aiming on/off
            if (Input.GetKeyDown(KeyCode.P))
            {
                _debugInputEnabled = !_debugInputEnabled;
                Debug.Log($"[GunController] Debug input mode: {(_debugInputEnabled ? "ON (mouse aim)" : "OFF (programmatic only)")}");
            }

            // Mouse aiming — always use Camera.main so the ray is independent of the gun's transform
            if (_debugInputEnabled)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                    {
                        LookAt(hit.point);
                    }
                    else
                    {
                        LookAt(ray.origin + ray.direction * 50f);
                    }
                }
            }

            // Left click to shoot
            if (Input.GetMouseButtonDown(0))
            {
                Shoot();
            }

            // R key to reload
            if (Input.GetKeyDown(KeyCode.R))
            {
                Reload();
            }
        }

        // ------ Easing Functions ------

        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }
    }
}
