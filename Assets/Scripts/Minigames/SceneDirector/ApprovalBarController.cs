using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Per-emotion approval bar — the core scoring mechanic.
    ///
    /// FILL LOGIC
    /// ──────────
    ///   • Correct emotion detected  → bar fills at _fillRate / second
    ///   • Wrong or neutral detected → bar drains at _drainRate / second
    ///
    /// WIN/FAIL TRIGGERS
    /// ─────────────────
    ///   • Bar reaches 1.0            → OnBarFilled  → ScriptController.PassCurrentElement()
    ///   • Bar reaches 0.0            → OnBarEmptied → ScriptController.FailCurrentElement()
    ///   • CountdownController fires  → also triggers FailCurrentElement() (whichever comes first)
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach to the approval bar UI panel.
    ///   2. Assign _fillBar (Image, Type = Filled, Fill Method = Horizontal).
    ///   3. Optionally assign _feedbackText for CORRECT / WRONG / HOLD overlay text.
    ///   4. Call Activate(requiredEmotion) when a new script element begins.
    ///   5. Call SetDetectedEmotion() every frame from the game controller.
    ///      In testing mode CameraController does this via H/S/A/N keys.
    /// </summary>
    public class ApprovalBarController : MonoBehaviour
    {
        public static ApprovalBarController Instance { get; private set; }

        // ── Events ────────────────────────────────────────────────────────────────
        /// <summary>Bar reached 1.0 — player held the correct emotion long enough. Wire to ScriptController.PassCurrentElement().</summary>
        public event Action OnBarFilled;
        /// <summary>Bar drained to 0.0 — player showed the wrong emotion too long. Wire to ScriptController.FailCurrentElement().</summary>
        public event Action OnBarEmptied;

        // ── Inspector — UI ────────────────────────────────────────────────────────
        [Header("UI References")]
        [Tooltip("Image with Type = Filled. fillAmount is driven by this script.")]
        [SerializeField] private Image           _fillBar;
        [Tooltip("Optional overlay text showing CORRECT / WRONG / HOLD.")]
        [SerializeField] private TextMeshProUGUI _feedbackText;

        // ── Inspector — Rates ─────────────────────────────────────────────────────
        [Header("Fill / Drain Rates (units per second)")]
        [Tooltip("How fast the bar fills when the player shows the correct emotion.")]
        [SerializeField] private float _fillRate  = 0.35f;
        [Tooltip("How fast the bar drains when the player shows the wrong or neutral emotion.")]
        [SerializeField] private float _drainRate = 0.20f;
        [Tooltip("Initial fill amount when bar activates (head start so minor drift doesn't immediately drain).")]
        [Range(0f, 1f)]
        [SerializeField] private float _initialFillAmount = 0.15f;
        [Tooltip("Bar value at which the color begins shifting from normal to warning.")]
        [Range(0f, 1f)]
        [SerializeField] private float _warningThreshold = 0.30f;

        // ── Inspector — Colors ────────────────────────────────────────────────────
        [Header("Colors")]
        [SerializeField] private Color _colorFilling  = Color.green;
        [SerializeField] private Color _colorDraining = Color.red;

        // ── State ─────────────────────────────────────────────────────────────────
        private EmotionLabel _required = EmotionLabel.Neutral;
        private EmotionLabel _detected = EmotionLabel.Neutral;
        private float        _fillAmount = 0f;
        private bool         _active     = false;
        private float        _graceTimeRemaining = 0f;
        private bool         _graceJustEnded     = false;
        private int          _lastLoggedGraceSec = -1;
        private int          _lastLoggedActiveSec = -1;

        public bool InGracePeriod => _graceTimeRemaining > 0f;

        public float        FillAmount       => _fillAmount;
        public bool         IsActive         => _active;
        public bool         IsCorrect        => _active && _detected == _required && _required != EmotionLabel.Neutral;
        public EmotionLabel RequiredEmotion  => _required;

        private const string LogServiceName = "ApprovalBarController";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Code-based discovery if Inspector wiring is missing
            if (_fillBar == null)      _fillBar      = GameObject.Find("ApprovalBarFill")?.GetComponent<Image>();
            if (_feedbackText == null) _feedbackText = GameObject.Find("FeedbackText")?.GetComponent<TextMeshProUGUI>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!_active) return;

            // ── GRACE PHASE — bar frozen, player reads the emotion ─────────────
            if (_graceTimeRemaining > 0f)
            {
                _graceTimeRemaining -= Time.deltaTime;

                int sec = Mathf.CeilToInt(_graceTimeRemaining);
                if (sec != _lastLoggedGraceSec && sec >= 0)
                {
                    _lastLoggedGraceSec = sec;
                    Debug.Log($"[Bar] 📖 leyendo {_required}... {sec}s restantes de gracia");
                }

                if (_graceTimeRemaining <= 0f)
                    _graceJustEnded = true;

                RefreshUI();
                return;
            }

            // ── ONE-TIME LOG when grace ends ───────────────────────────────────
            if (_graceJustEnded)
            {
                _graceJustEnded = false;
                string arrow = IsCorrect ? "✓ CORRECTO" : (_detected == EmotionLabel.Neutral ? "~ neutral" : "✗ incorrecto");
                Debug.Log($"[Bar] 🎬 ¡ACTÚA AHORA! | required={_required} | detected={_detected} ({arrow}) | fill={_fillAmount:F2}");
            }

            // ── ACTIVE PHASE — fill/drain based on emotion ─────────────────────
            bool isActivelyWrong = _detected != EmotionLabel.Neutral && _detected != _required;
            float delta = IsCorrect ? _fillRate : (isActivelyWrong ? -_drainRate : 0f);
            _fillAmount = Mathf.Clamp01(_fillAmount + delta * Time.deltaTime);

            // Per-second log during active phase
            int activeSec = Mathf.CeilToInt(_fillAmount * 10); // proxy — log on fill change ~10%
            float fillRounded = Mathf.Round(_fillAmount * 10f) / 10f;
            int fillBucket = Mathf.RoundToInt(fillRounded * 10);
            if (fillBucket != _lastLoggedActiveSec)
            {
                _lastLoggedActiveSec = fillBucket;
                string dir = delta > 0 ? "↑ subiendo" : (delta < 0 ? "↓ bajando" : "— estable");
                Debug.Log($"[Bar] 🎭 actuando | detected={_detected} | fill={_fillAmount:F2} {dir}");
            }

            RefreshUI();

            if (_fillAmount >= 1f)
            {
                _active = false;
                Debug.Log($"[Bar] ✅ CORRECTO — barra llena");
                ServiceLogger.Instance.LogInfo(LogServiceName, "Bar filled — emotion confirmed.");
                OnBarFilled?.Invoke();
            }
            else if (_fillAmount <= 0f)
            {
                _active = false;
                Debug.Log($"[Bar] ❌ FALLADO — barra vacía | detected={_detected}");
                ServiceLogger.Instance.LogInfo(LogServiceName, "Bar emptied — emotion failed.");
                OnBarEmptied?.Invoke();
            }
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts a fresh bar for the given required emotion.
        /// Call from ScriptController.OnElementStarted.
        /// </summary>
        /// <param name="gracePeriod">Seconds the bar stays frozen before fill/drain starts.</param>
        public void Activate(EmotionLabel required, float gracePeriod = 8f)
        {
            _required            = required;
            _fillAmount          = 0.5f;
            _active              = true;
            _graceTimeRemaining  = gracePeriod;
            _graceJustEnded      = false;
            _lastLoggedGraceSec  = -1;
            _lastLoggedActiveSec = -1;
            RefreshUI();
            Debug.Log($"[Bar] ► INICIO: {required} | gracia={gracePeriod:F0}s | fill=0.50 | drain={_drainRate:F2}/s");
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Bar activated — required: {required}  grace: {gracePeriod:F0}s");
        }

        /// <summary>
        /// Stops the bar without firing any event. Use when the countdown expires first
        /// so the bar doesn't also fire OnBarEmptied after CountdownController already called Fail.
        /// </summary>
        public void Deactivate()
        {
            _active              = false;
            _graceTimeRemaining  = 0f;
            _graceJustEnded      = false;
            _lastLoggedGraceSec  = -1;
            _lastLoggedActiveSec = -1;
            RefreshUI();
        }

        /// <summary>
        /// Updates the emotion the game considers currently detected.
        /// Call every frame from the game controller.
        /// In testing mode CameraController calls this when H/S/A/N keys are pressed.
        /// TODO: Replace with live EmotionClassifier.OnEmotionChanged subscription.
        /// </summary>
        public void SetDetectedEmotion(EmotionLabel emotion)
        {
            _detected = emotion;
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private void RefreshUI()
        {
            if (_fillBar != null)
            {
                _fillBar.fillAmount = _fillAmount;
                _fillBar.color      = Color.Lerp(_colorDraining, _colorFilling, _fillAmount);
            }

            if (_feedbackText != null)
            {
                if (!_active)
                {
                    _feedbackText.text = string.Empty;
                }
                else if (IsCorrect)
                {
                    _feedbackText.text  = "CORRECT";
                    _feedbackText.color = _colorFilling;
                }
                else
                {
                    _feedbackText.text  = $"SHOW: {_required}";
                    _feedbackText.color = _fillAmount <= _warningThreshold ? _colorDraining : Color.white;
                }
            }
        }
    }
}
