using System;
using UnityEngine;
 
namespace ARcadeRush.Face
{
    public enum EmotionLabel { Neutral, Happy, Surprised, Angry }
 
    /// <summary>
    /// Classifies emotions from FaceLandmarkReader metrics using a confidence-scoring approach
    /// instead of brittle hard thresholds.
    ///
    /// Key improvements over the original version:
    ///  • Smile score (metric[4]) drives Happy — not raw mouth openness.
    ///  • Each emotion gets a continuous confidence value, smoothed via EMA.
    ///  • Time-based hold (seconds) rather than frame-count, immune to FPS variation.
    ///  • Head-pose gating: skips updates when the face is turned too far sideways.
    ///  • Relative metrics from the calibrated neutral baseline reduce person-to-person variance.
    ///  • All weights/thresholds exposed in the Inspector for tuning without recompile.
    /// </summary>
    [RequireComponent(typeof(FaceLandmarkReader))]
    public class EmotionClassifier : MonoBehaviour
    {
        // ── Events ────────────────────────────────────────────────────────────────
        public event Action<EmotionLabel> OnEmotionChanged;
 
        // ── Inspector — Activation ────────────────────────────────────────────────
        [Header("Activation")]
        [Tooltip("Start the classifier off. Enable at runtime via SetEnabled(true) or the Toggle button.")]
        [SerializeField] private bool _isEnabled = false;

        // ── Inspector — Timing ────────────────────────────────────────────────────
        [Header("Timing")]
        [Tooltip("Seconds the winning emotion must remain dominant before it is confirmed.")]
        [SerializeField] private float _holdSeconds = 0.15f;
        [Tooltip("Minimum head-pose confidence required to update the classifier.")]
        [Range(0f, 1f)]
        [SerializeField] private float _minHeadConfidence = 0.50f;
 
        // ── Inspector — Confidence EMA ────────────────────────────────────────────
        [Header("Confidence Smoothing (EMA per emotion)")]
        [Tooltip("EMA factor for emotion confidence scores. Higher = slower but more stable.")]
        [Range(0f, 0.99f)]
        [SerializeField] private float _confidenceEma = 0.55f;
 
        // ── Inspector — Happy ─────────────────────────────────────────────────────
        [Header("Happy — weights relative to neutral baseline")]
        [Tooltip("Smile score (mouthSmile + cheekSquint) weight — primary Happy driver.")]
        [SerializeField] private float _happySmileWeight  = 3.0f;
        [Tooltip("Mouth opening contribution.")]
        [SerializeField] private float _happyMouthWeight  = 1.5f;
        [Tooltip("Furrow penalty — subtracts furrow * this value (can't smile while scowling).")]
        [SerializeField] private float _happyMouthPenalty = 2.0f;

        // ── Inspector — Surprised ─────────────────────────────────────────────────
        [Header("Surprised — weights")]
        [SerializeField] private float _surpMouthWeight     = 3.0f;
        [SerializeField] private float _surpEyeWeight       = 0.5f;
        [SerializeField] private float _surpBrowRaiseWeight = 2.0f;
        [Tooltip("mouthFunnel (O-face shape) — replaces Pucker.")]
        [SerializeField] private float _surpFunnelWeight    = 2.0f;
        [Tooltip("Blink penalty — subtracts blinkAvg * this value; blinking suppresses Surprised.")]
        [SerializeField] private float _surpBlinkPenalty    = 3.0f;

        // ── Inspector — Angry ─────────────────────────────────────────────────────
        [Header("Angry — weights")]
        [SerializeField] private float _angryFurrowWeight  = 2.0f;
        [SerializeField] private float _angryBrowLowWeight = 4.0f;
        [Tooltip("eyeSquint (eye narrowing) — primary anatomical Angry signal.")]
        [SerializeField] private float _angrySquintWeight  = 3.0f;
        [SerializeField] private float _angryFrownWeight   = 2.0f;
        [SerializeField] private float _angryPressWeight   = 5.0f;
 
        // ── Inspector — Neutral baseline offset ──────────────────────────────────
        [Header("Neutral — base confidence (opponent to all emotions)")]
        [SerializeField] private float _neutralBaseScore = 0.75f;
        [Tooltip("Additional score added to the current emotion to prevent rapid flickering.")]
        [SerializeField] private float _hysteresis = 0.05f;
 
        // ── Private state ─────────────────────────────────────────────────────────
        private FaceLandmarkReader _reader;
 
        // Smoothed confidence per emotion, indexed by (int)EmotionLabel
        private float[] _confidence = new float[4];
 
        private EmotionLabel _currentEmotion   = EmotionLabel.Neutral;
        private EmotionLabel _candidateEmotion = EmotionLabel.Neutral;
        private float        _candidateHoldTime = 0f;
 
        #region Unity lifecycle
 
        private void Awake()
        {
            _reader = GetComponent<FaceLandmarkReader>();
            for (int i = 0; i < _confidence.Length; i++) _confidence[i] = 0f;
            _confidence[(int)EmotionLabel.Neutral] = 1f;
        }
 
        private void Update()
        {
            if (!_isEnabled) return;

            // ── Head-pose gate ─────────────────────────────────────────────────
            if (_reader.HeadConfidence < _minHeadConfidence) return;

            // During calibration phase, metrics are collected but emotions not confirmed
            if (!_reader.IsCalibrated) 
            {
                //Debug.Log("[EmotionClassifier] ⏳ Calibrating... (metrics only, no emotion confirmation)");
                return;
            }
 
            // ── Read metrics relative to calibrated neutral ────────────────────
            // Using relative values removes a lot of person-to-person variance.
            float mouthOpen  = _reader.GetRelativeMetric(0);
            float eyeL       = _reader.GetRelativeMetric(1);
            float eyeR       = _reader.GetRelativeMetric(2);
            float browRaise  = _reader.GetRelativeMetric(3);
            float smile      = _reader.GetRelativeMetric(4);
            float furrow     = _reader.GetRelativeMetric(5);
            float trueFrown  = _reader.GetRelativeMetric(6);
            float mouthPress = _reader.GetRelativeMetric(7);
            float funnel     = _reader.GetRelativeMetric(8);
            float squint     = _reader.GetRelativeMetric(9);

            float eyeAvg     = (eyeL + eyeR) * 0.5f;

            // ── Raw confidence scores (un-smoothed) ────────────────────────────
            float rawNeutral = _neutralBaseScore;

            // Happy: smile (mouthSmile + cheekSquint) is the primary driver.
            // Mouth opening is a secondary confirmation. Furrow penalises (can't smile while scowling).
            // Pucker removed — anatomically incompatible with a wide smile.
            float rawHappy = Mathf.Max(0f,
                smile      * _happySmileWeight
                + mouthOpen  * _happyMouthWeight
                - furrow     * _happyMouthPenalty);

            // Surprised: jaw drop + wide eyes + brow raise + mouthFunnel (O-face).
            // Blinking explicitly penalises the score — you can't be wide-eyed while blinking.
            float rawSurp = Mathf.Max(0f,
                Mathf.Max(0f, mouthOpen - 0.03f) * _surpMouthWeight
                + Mathf.Max(0f, eyeAvg  - 0.01f) * _surpEyeWeight
                + browRaise * _surpBrowRaiseWeight
                + funnel    * _surpFunnelWeight
                - _reader.BlinkAverage * _surpBlinkPenalty);

            // Angry: brow furrow + low brow + eye squint + true frown + mouth press.
            // EyeR (eyeWide) removed — widening eyes is not anatomically linked to anger.
            // Squint (eyeSquintLeft/Right) is the correct anatomical anger signal.
            float rawAngry = Mathf.Max(0f,
                Mathf.Max(0f, furrow - 0.05f) * _angryFurrowWeight
                + Mathf.Max(0f, -browRaise)    * _angryBrowLowWeight
                + squint     * _angrySquintWeight
                + trueFrown  * _angryFrownWeight
                + mouthPress * _angryPressWeight);
 
            // ── EMA smoothing of confidence scores ─────────────────────────────
            _confidence[(int)EmotionLabel.Neutral]   = Mathf.Lerp(rawNeutral, _confidence[(int)EmotionLabel.Neutral],   _confidenceEma);
            _confidence[(int)EmotionLabel.Happy]      = Mathf.Lerp(rawHappy,   _confidence[(int)EmotionLabel.Happy],      _confidenceEma);
            _confidence[(int)EmotionLabel.Surprised]  = Mathf.Lerp(rawSurp,    _confidence[(int)EmotionLabel.Surprised],  _confidenceEma);
            _confidence[(int)EmotionLabel.Angry]      = Mathf.Lerp(rawAngry,   _confidence[(int)EmotionLabel.Angry],      _confidenceEma);
 
            // ── Pick winning emotion ───────────────────────────────────────────
            EmotionLabel detected = EmotionLabel.Neutral;
            float bestScore = _confidence[(int)EmotionLabel.Neutral];
            if (_currentEmotion == EmotionLabel.Neutral) bestScore += _hysteresis;

            for (int i = 1; i < _confidence.Length; i++)
            {
                float score = _confidence[i];
                if ((EmotionLabel)i == _currentEmotion) score += _hysteresis;

                if (score > bestScore)
                {
                    bestScore = score;
                    detected  = (EmotionLabel)i;
                }
            }
 
            // ── Temporal hold: candidate must stay dominant for _holdSeconds ───
            if (detected == _candidateEmotion)
            {
                _candidateHoldTime += Time.deltaTime;
                if (_candidateHoldTime >= _holdSeconds && _currentEmotion != detected && _reader.IsCalibrated)
                {
                    _currentEmotion = detected;
                    //Debug.Log($"[EmotionClassifier] ✓ CONFIRMED: {_currentEmotion} " +
                    //          $"| scores N:{_confidence[0]:F2} H:{_confidence[1]:F2} " +
                    //          $"S:{_confidence[2]:F2} A:{_confidence[3]:F2}");
                    OnEmotionChanged?.Invoke(_currentEmotion);
                }
            }
            else
            {
                _candidateEmotion  = detected;
                _candidateHoldTime = 0f;
                //Debug.Log($"[EmotionClassifier] → Candidate: {_candidateEmotion} " +
                //          $"(smile={smile:F3} mouth={mouthOpen:F3} brow={browRaise:F3} furrow={furrow:F3})");
            }

            //Debug.Log($"[Scores] N:{_confidence[0]:F3} H:{_confidence[1]:F3} S:{_confidence[2]:F3} A:{_confidence[3]:F3} | Head:{_reader.HeadConfidence:F2}");           

        }
 
        #endregion
 
        #region Public API
 
        /// <summary>The last confirmed emotion.</summary>
        public EmotionLabel CurrentEmotion => _currentEmotion;

        /// <summary>Confidence for a specific emotion in [0..∞] (relative scores, not normalised).</summary>
        public float GetConfidence(EmotionLabel emotion) => _confidence[(int)emotion];

        /// <summary>Enables or disables the classifier. Disabling immediately resets all scores to initial state.</summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled) ResetState();
        }

        /// <summary>Resets confidence scores and candidate state to their initial values. Call alongside FaceLandmarkReader.ResetCalibration().</summary>
        public void ResetState()
        {
            for (int i = 0; i < _confidence.Length; i++) _confidence[i] = 0f;
            _confidence[(int)EmotionLabel.Neutral] = 1f;
            _currentEmotion   = EmotionLabel.Neutral;
            _candidateEmotion = EmotionLabel.Neutral;
            _candidateHoldTime = 0f;
        }
 
        #endregion
    }
}
 