using System.Collections;
using UnityEngine;
using ARcadeRush.Core;

namespace ARcadeRush.Minigames.Shooter
{
    public enum TargetType { Bandit, Innocent }

    /// <summary>
    /// Pre-placed target for the shooter minigame.
    /// Starts disabled in the scene; activated by TargetManager.
    /// - Type is randomized 50/50 on each activation (not pre-assigned).
    /// - Plays a raise animation (reverse of fall) when activated.
    /// - Plays a fall animation when hit or timed out.
    /// - Fires OnTargetDeactivated when done (so TargetManager can recycle it).
    /// </summary>
    public class Target : MonoBehaviour
    {
        [Header("Row Assignment")]
        [Tooltip("Must match a TargetManager RowConfig label (e.g. Easy, Medium, Hard).")]
        [SerializeField] private string _rowLabel = "Easy";

        /// <summary>Auto-assigned by TargetManager on discovery. Do not set manually.</summary>
        private int _targetId;

        [Header("Score Defaults (overridden by TargetManager on activate)")]
        [SerializeField] private int _banditScore = 10;
        [SerializeField] private int _innocentScore = -20;

        [Header("Sprites")]
        [SerializeField] private Sprite _banditSprite;
        [SerializeField] private Sprite _innocentSprite;

        [Header("Hit Effect")]
        [SerializeField] private GameObject _hitEffectPrefab;
        [SerializeField] private float _hitEffectDuration = 1f;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip[] _hitSFX;
        [SerializeField][Range(0f, 1f)] private float _soundChance = 0.25f;

        [Header("Raise / Fall Animation")]
        [SerializeField] private float _raiseDuration = 0.5f;
        [SerializeField] private float _fallDuration = 0.5f;
        [SerializeField] private float _poolReturnDelay = 0.8f;
        [Tooltip("Half the sprite height — pivot sits this far below the center.")]
        [SerializeField] private float _spriteHalfHeight = 1f;

        // --- Public read-only ---

        public string RowLabel => _rowLabel;
        public int TargetId => _targetId;

        /// <summary>Internal setter for TargetManager to auto-assign IDs on discovery.</summary>
        internal void AssignId(int id) => _targetId = id;
        public TargetType Type => _type;
        public bool IsActive { get; private set; } = false;

        /// <summary>Points awarded (or deducted) when this target is hit.</summary>
        public int HitScore => _type == TargetType.Bandit ? _banditScore : _innocentScore;

        /// <summary>Fired when the target finishes deactivation (hit or timeout).</summary>
        public System.Action<Target> OnTargetDeactivated;

        // --- Private state ---

        private TargetType _type;
        private bool _isAnimating;
        private Collider _collider;
        [SerializeField] private SpriteRenderer _spriteRenderer;
        private Coroutine _raiseCo;
        private Coroutine _fallCo;
        private Coroutine _timeoutCo;
        private Vector3 _startPosition;
        private float _activeDuration;

        // --- Unity Lifecycle ---

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _startPosition = transform.position;
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (!IsActive) return; // Only process if Activate() was called

            _isAnimating = true;

            // Randomise type 50/50 each activation
            _type = Random.Range(0, 2) == 0 ? TargetType.Bandit : TargetType.Innocent;
            ApplySprite();

            // Disable collider during raise animation
            if (_collider != null) _collider.enabled = false;

            // Position at fall-end state so raise animation reverses it
            Vector3 pivot = _startPosition - transform.up * _spriteHalfHeight;
            transform.position = pivot;
            transform.rotation = Quaternion.identity;

            _raiseCo = StartCoroutine(CoRaiseAndActivate());
        }

        private void OnDisable()
        {
            // Safety net: if the target was active and is being disabled
            // (by any code path — normal deactivation, game end, scene unload, etc.),
            // notify the TargetManager so it can recycle this target.
            // Without this, any interruption to CoFallAndDeactivate() would
            // leave the TargetManager believing this target is still active.
            if (IsActive)
            {
                IsActive = false;
                OnTargetDeactivated?.Invoke(this);
            }

            StopAllCoroutines();
            _raiseCo = null;
            _fallCo = null;
            _timeoutCo = null;
            _isAnimating = false;
        }

        // --- Public API ---

        /// <summary>
        /// Called by TargetManager to activate this target.
        /// Sets dynamic score values and duration, then enables the GameObject.
        /// </summary>
        public void Activate(int banditScore, int innocentScore, float activeDuration)
        {
            _banditScore = banditScore;
            _innocentScore = innocentScore;
            _activeDuration = activeDuration;
            IsActive = true;

            gameObject.SetActive(true);
        }

        /// <summary>
        /// Called when a bullet/projectile hits this target.
        /// </summary>
        public void OnHit()
        {
            if (_isAnimating || !IsActive) return;
            _isAnimating = true;

            if (_collider != null) _collider.enabled = false;

            int points = _type == TargetType.Bandit ? _banditScore : _innocentScore;
            string label = _type == TargetType.Bandit ? "Bandit" : "Innocent";

            Debug.Log($"[Target] {label} ({_rowLabel}) hit! Score: {points}");

            if (_hitEffectPrefab != null)
            {
                GameObject effect = Instantiate(_hitEffectPrefab, transform.position, transform.rotation);
                ParticleSystem ps = effect.GetComponent<ParticleSystem>();
                if (ps != null) ps.Play();
                Destroy(effect, _hitEffectDuration);
            }

            // Play hit sound — 25% chance, randomly picks one of the clips
            if (_audioSource != null && _hitSFX != null && _hitSFX.Length > 0 && Random.value <= _soundChance)
            {
                AudioClip clip = _hitSFX[Random.Range(0, _hitSFX.Length)];
                _audioSource.PlayOneShot(clip);
            }

            // Stop timeout if active
            if (_timeoutCo != null)
            {
                StopCoroutine(_timeoutCo);
                _timeoutCo = null;
            }

            _fallCo = StartCoroutine(CoFallAndDeactivate());
        }

        /// <summary>
        /// Force-deactivate (used by TargetManager for cleanup).
        /// </summary>
        public void Deactivate()
        {
            if (!IsActive) return;
            IsActive = false;

            StopAllCoroutines();
            _raiseCo = null;
            _fallCo = null;
            _timeoutCo = null;
            _isAnimating = false;

            OnTargetDeactivated?.Invoke(this);
            gameObject.SetActive(false);
        }

        // --- Sprite ---

        private void ApplySprite()
        {
            if (_spriteRenderer == null) return;
            _spriteRenderer.sprite = _type == TargetType.Bandit ? _banditSprite : _innocentSprite;
        }

        // --- Raise Animation (reverse of fall) ---

        private IEnumerator CoRaiseAndActivate()
        {
            Vector3 pivot = _startPosition - transform.up * _spriteHalfHeight;
            Vector3 pivotToCenter = _startPosition - pivot;
            Vector3 fallAxis = transform.right;

            float elapsed = 0f;
            while (elapsed < _raiseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _raiseDuration);
                float angle = 90f * (1f - t); // 90° → 0° (reverse of fall)

                Quaternion rot = Quaternion.AngleAxis(angle, fallAxis);
                transform.rotation = rot;
                transform.position = pivot + rot * pivotToCenter;
                yield return null;
            }

            // Fully upright
            transform.rotation = Quaternion.identity;
            transform.position = _startPosition;
            _isAnimating = false;

            // Enable interaction
            if (_collider != null) _collider.enabled = true;

            // Start timeout countdown
            _timeoutCo = StartCoroutine(CoActiveCountdown());
        }

        // --- Fall Animation ---

        private IEnumerator CoFallAndDeactivate()
        {
            Vector3 pivot = _startPosition - transform.up * _spriteHalfHeight;
            Vector3 pivotToCenter = _startPosition - pivot;
            Vector3 fallAxis = transform.right;

            float elapsed = 0f;
            while (elapsed < _fallDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _fallDuration);
                float angle = 90f * t; // 0° → 90°

                Quaternion rot = Quaternion.AngleAxis(angle, fallAxis);
                transform.rotation = rot;
                transform.position = pivot + rot * pivotToCenter;
                yield return null;
            }

            yield return new WaitForSeconds(_poolReturnDelay - _fallDuration);

            IsActive = false;
            _isAnimating = false;

            OnTargetDeactivated?.Invoke(this);
            gameObject.SetActive(false);
        }

        // --- Timeout ---

        private IEnumerator CoActiveCountdown()
        {
            yield return new WaitForSeconds(_activeDuration);

            // Timeout — play fall animation then deactivate (no score awarded)
            if (_collider != null) _collider.enabled = false;
            _isAnimating = true;

            Debug.Log($"[Target] {_rowLabel}#{_targetId} timed out — deactivating.");

            _fallCo = StartCoroutine(CoFallAndDeactivate());
        }
    }
}
