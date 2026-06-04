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

            float delta = IsCorrect ? _fillRate : -_drainRate;
            _fillAmount = Mathf.Clamp01(_fillAmount + delta * Time.deltaTime);

            RefreshUI();

            if (_fillAmount >= 1f)
            {
                _active = false;
                ServiceLogger.Instance.LogInfo(LogServiceName, "Bar filled — emotion confirmed.");
                OnBarFilled?.Invoke();
            }
            else if (_fillAmount <= 0f)
            {
                _active = false;
                ServiceLogger.Instance.LogInfo(LogServiceName, "Bar emptied — emotion failed.");
                OnBarEmptied?.Invoke();
            }
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts a fresh bar for the given required emotion.
        /// Call from ScriptController.OnElementStarted.
        /// </summary>
        public void Activate(EmotionLabel required)
        {
            _required   = required;
            _fillAmount = _initialFillAmount;
            _active     = true;
            RefreshUI();
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Bar activated — required: {required}");
        }

        /// <summary>
        /// Stops the bar without firing any event. Use when the countdown expires first
        /// so the bar doesn't also fire OnBarEmptied after CountdownController already called Fail.
        /// </summary>
        public void Deactivate()
        {
            _active = false;
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
