using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Manages tomato-splat visual effects when the audience reacts negatively.
    /// Uses a pool of Image components on a full-screen overlay canvas.
    ///
    /// Setup in Unity Editor:
    ///   1. Create a Canvas GameObject (Screen Space - Overlay, high sort order).
    ///   2. Add this component and assign _splatPrefab (an Image prefab with a tomato splat sprite).
    ///   3. Set _poolSize to 8-12 for smooth reuse.
    ///   4. Wire to SceneDirectorGame events or call SplatAtRandomPosition() directly.
    ///
    /// Splats fade in, hold briefly, then fade out and return to pool.
    /// Multiple splats can be active simultaneously for dramatic effect.
    /// </summary>
    public class TomatoSplatController : MonoBehaviour
    {
        public static TomatoSplatController Instance { get; private set; }

        [Header("References")]
        [Tooltip("Parent transform under which splat images are instantiated.")]
        [SerializeField] private Transform _poolParent;
        [Tooltip("Image prefab with a tomato-splat sprite. Must have Image component.")]
        [SerializeField] private Image _splatPrefab;

        [Header("Splat Settings")]
        [SerializeField] private int _poolSize = 10;
        [SerializeField] private float _fadeInDuration = 0.15f;
        [SerializeField] private float _holdDuration = 1.5f;
        [SerializeField] private float _fadeOutDuration = 0.8f;
        [SerializeField] private float _minScale = 0.6f;
        [SerializeField] private float _maxScale = 1.2f;
        [Tooltip("Minimum radius (normalized 0-1) from screen center for splat placement.")]
        [SerializeField] [Range(0f, 0.5f)] private float _edgeMargin = 0.1f;

        private Queue<Image> _pool;
        private HashSet<Image> _activeSplats = new HashSet<Image>();

        private const string LogServiceName = "TomatoSplatController";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializePool();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopAllCoroutines();
        }

        // ── Pool Initialization ─────────────────────────────────────────────────

        private void InitializePool()
        {
            _pool = new Queue<Image>(_poolSize);

            Transform parent = _poolParent != null ? _poolParent : transform;

            for (int i = 0; i < _poolSize; i++)
            {
                Image splat = Instantiate(_splatPrefab, parent);
                splat.gameObject.name = $"TomatoSplat_{i}";
                splat.gameObject.SetActive(false);
                splat.raycastTarget = false; // don't block UI clicks
                _pool.Enqueue(splat);
            }

            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"TomatoSplat pool initialized with {_poolSize} instances.");
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns a single tomato splat at a random screen position.
        /// </summary>
        public void SplatAtRandomPosition()
        {
            Vector2 randomPos = GetRandomScreenPosition();
            SpawnSplat(randomPos, Random.Range(_minScale, _maxScale));
        }

        /// <summary>
        /// Spawns multiple tomato splats simultaneously (for emphatic negative reactions).
        /// </summary>
        /// <param name="count">Number of splats to spawn (1-5).</param>
        public void SplatBurst(int count = 3)
        {
            count = Mathf.Clamp(count, 1, 5);
            for (int i = 0; i < count; i++)
            {
                SplatAtRandomPosition();
            }
        }

        /// <summary>
        /// Spawns a splat at the specified screen position (0-1 range, 0,0=bottom-left).
        /// </summary>
        public void SplatAtPosition(float normalizedX, float normalizedY, float scale = 1f)
        {
            SpawnSplat(new Vector2(normalizedX, normalizedY), scale);
        }

        /// <summary>
        /// Clears all active splats immediately (e.g., on round end or scene unload).
        /// </summary>
        public void ClearAllSplats()
        {
            foreach (var splat in _activeSplats)
            {
                if (splat != null)
                {
                    splat.gameObject.SetActive(false);
                    _pool.Enqueue(splat);
                }
            }
            _activeSplats.Clear();
        }

        // ── Internal ────────────────────────────────────────────────────────────

        private void SpawnSplat(Vector2 normalizedPosition, float scale)
        {
            if (_pool == null || _pool.Count == 0)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName,
                    "No available splats in pool — consider increasing _poolSize.");
                return;
            }

            Image splat = _pool.Dequeue();
            _activeSplats.Add(splat);

            // Position the splat (using anchoredPosition on the RectTransform)
            RectTransform rt = splat.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.anchoredPosition = new Vector2(
                normalizedPosition.x * Screen.width,
                normalizedPosition.y * Screen.height);
            rt.localScale = Vector3.one * scale;

            // Random rotation for variety
            rt.localRotation = Quaternion.Euler(0, 0, Random.Range(-30f, 30f));

            splat.gameObject.SetActive(true);
            StartCoroutine(SplatLifecycle(splat));
        }

        private IEnumerator SplatLifecycle(Image splat)
        {
            // Fade in
            float elapsed = 0f;
            Color c = splat.color;
            c.a = 0f;
            splat.color = c;
            while (elapsed < _fadeInDuration)
            {
                elapsed += Time.deltaTime;
                c.a = Mathf.Lerp(0f, 1f, elapsed / _fadeInDuration);
                splat.color = c;
                yield return null;
            }
            c.a = 1f;
            splat.color = c;

            // Hold
            yield return new WaitForSeconds(_holdDuration);

            // Fade out
            elapsed = 0f;
            while (elapsed < _fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                c = splat.color;
                c.a = Mathf.Lerp(1f, 0f, elapsed / _fadeOutDuration);
                splat.color = c;
                yield return null;
            }

            // Return to pool
            if (splat != null)
            {
                splat.gameObject.SetActive(false);
                _activeSplats.Remove(splat);
                _pool.Enqueue(splat);
            }
        }

        private Vector2 GetRandomScreenPosition()
        {
            float margin = _edgeMargin;
            return new Vector2(
                Random.Range(margin, 1f - margin),
                Random.Range(margin, 1f - margin));
        }
    }
}