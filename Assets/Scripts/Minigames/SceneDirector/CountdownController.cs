using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Per-emotion countdown timer. Fires OnCountdownExpired when time runs out.
    /// Call StartCountdown(duration) at the start of each script element.
    /// Call Stop() when the player succeeds (approval bar fills) to cancel without firing.
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach to a UI GameObject in the Director canvas.
    ///   2. Assign _timeText (TextMeshProUGUI) for the seconds display.
    ///   3. Assign _fillBar (Image with Type = Filled, Fill Method = Horizontal or Radial)
    ///      for the visual drain bar.
    ///   4. Optionally tune _warningThreshold and colors in the Inspector.
    /// </summary>
    public class CountdownController : MonoBehaviour
    {
        public static CountdownController Instance { get; private set; }

        /// <summary>Fires when the countdown reaches zero. Trigger the fail state here.</summary>
        public event Action OnCountdownExpired;

        /// <summary>Fires every frame while running. Carries normalized time [1=full, 0=expired].</summary>
        public event Action<float> OnTick;

        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _timeText;
        [SerializeField] private Image           _fillBar;

        [Header("Colors")]
        [SerializeField] private Color _colorNormal  = Color.green;
        [SerializeField] private Color _colorWarning = Color.red;
        [Tooltip("Normalized time below which the bar turns to the warning color.")]
        [Range(0f, 1f)]
        [SerializeField] private float _warningThreshold = 0.35f;

        // ── State ─────────────────────────────────────────────────────────────────
        private float _duration;
        private float _remaining;
        private bool  _running;
        private bool  _paused;

        public float NormalizedTime    => _duration > 0f ? _remaining / _duration : 0f;
        public float RemainingSeconds  => _remaining;
        public bool  IsRunning         => _running && !_paused;

        private const string LogServiceName = "CountdownController";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Code-based discovery if Inspector wiring is missing
            if (_timeText == null) _timeText = GameObject.Find("TimerText")?.GetComponent<TextMeshProUGUI>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_running || _paused) return;

            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _running   = false;
                RefreshUI();
                ServiceLogger.Instance.LogInfo(LogServiceName, "Countdown expired.");
                OnCountdownExpired?.Invoke();
                return;
            }

            RefreshUI();
            OnTick?.Invoke(NormalizedTime);
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Starts a fresh countdown of the given duration in seconds.</summary>
        public void StartCountdown(float duration)
        {
            _duration  = duration;
            _remaining = duration;
            _running   = true;
            _paused    = false;
            RefreshUI();
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Countdown started: {duration:F1}s");
        }

        /// <summary>Stops the countdown without firing OnCountdownExpired. Call on player success.</summary>
        public void Stop()
        {
            _running = false;
            _paused  = false;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Countdown stopped.");
        }

        /// <summary>Pauses the countdown without resetting it.</summary>
        public void Pause()
        {
            _paused = true;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Countdown paused.");
        }

        /// <summary>Resumes a paused countdown.</summary>
        public void Resume()
        {
            _paused = false;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Countdown resumed.");
        }

        // ── UI ────────────────────────────────────────────────────────────────────

        private void RefreshUI()
        {
            float normalized = NormalizedTime;
            Color current    = Color.Lerp(_colorWarning, _colorNormal, normalized);

            if (_timeText != null)
            {
                _timeText.text  = Mathf.CeilToInt(_remaining).ToString();
                _timeText.color = normalized <= _warningThreshold ? _colorWarning : _colorNormal;
            }

            if (_fillBar != null)
            {
                _fillBar.fillAmount = normalized;
                _fillBar.color      = current;
            }
        }
    }
}
