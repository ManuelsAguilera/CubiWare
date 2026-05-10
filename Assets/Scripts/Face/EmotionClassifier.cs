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
 
        // ── Inspector — Timing ────────────────────────────────────────────────────
        [Header("Timing")]
        [Tooltip("Seconds the winning emotion must remain dominant before it is confirmed.")]
        [SerializeField] private float _holdSeconds = 0.30f;
        [Tooltip("Minimum head-pose confidence required to update the classifier.")]
        [Range(0f, 1f)]
        [SerializeField] private float _minHeadConfidence = 0.50f;
 
        // ── Inspector — Confidence EMA ────────────────────────────────────────────
        [Header("Confidence Smoothing (EMA per emotion)")]
        [Tooltip("EMA factor for emotion confidence scores. Higher = slower but more stable.")]
        [Range(0f, 0.99f)]
        [SerializeField] private float _confidenceEma = 0.75f;
 
        // ── Inspector — Happy ─────────────────────────────────────────────────────
        [Header("Happy — weight / threshold relative to neutral baseline")]
        [Tooltip("Smile score (lip corner raise) weight.")]
        [SerializeField] private float _happySmileWeight    = 2.5f;
        [Tooltip("Penalise excessive mouth openness (open mouth alone is not a smile).")]
        [SerializeField] private float _happyMouthPenalty   = 1.0f;
 
        // ── Inspector — Surprised ─────────────────────────────────────────────────
        [Header("Surprised — weights")]
        [SerializeField] private float _surpMouthWeight     = 1.5f;
        [SerializeField] private float _surpEyeWeight       = 1.0f;
        [SerializeField] private float _surpBrowRaiseWeight = 0.8f;
 
        // ── Inspector — Angry ─────────────────────────────────────────────────────
        [Header("Angry — weights")]
        [SerializeField] private float _angryFurrowWeight   = 2.0f;
        [SerializeField] private float _angryEyeSquintWeight = 0.8f; // low eye openness = squint
        [SerializeField] private float _angryBrowLowWeight  = 0.5f;  // brow below neutral
 
        // ── Inspector — Neutral baseline offset ──────────────────────────────────
        [Header("Neutral — base confidence (opponent to all emotions)")]
        [SerializeField] private float _neutralBaseScore = 0.35f;
 
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
            // ── Head-pose gate ─────────────────────────────────────────────────
            if (_reader.HeadConfidence < _minHeadConfidence) return;
 
            // ── Read metrics relative to calibrated neutral ────────────────────
            // Using relative values removes a lot of person-to-person variance.
            float mouthOpen = _reader.GetRelativeMetric(0);
            float eyeL      = _reader.GetRelativeMetric(1);
            float eyeR      = _reader.GetRelativeMetric(2);
            float browRaise = _reader.GetRelativeMetric(3);
            float smile     = _reader.GetRelativeMetric(4);
            float furrow    = _reader.GetRelativeMetric(5);
 
            float eyeAvg    = (eyeL + eyeR) * 0.5f;
 
            // ── Raw confidence scores (un-smoothed) ────────────────────────────
            float rawNeutral    = _neutralBaseScore;
 
            // Happy: smile is the primary driver; penalise if mouth is gaping wide
            // (pure surprise also opens mouth but does NOT raise lip corners)
            float rawHappy  = Mathf.Max(0f,
                smile     * _happySmileWeight
                - Mathf.Max(0f, mouthOpen - 0.05f) * _happyMouthPenalty);
 
            // Surprised: jaw drop + wide eyes + brow raise
            float rawSurp   = Mathf.Max(0f,
                mouthOpen * _surpMouthWeight
                + eyeAvg  * _surpEyeWeight
                + Mathf.Max(0f, browRaise) * _surpBrowRaiseWeight);
 
            // Angry: inner brow furrow + low brow + squinted eyes
            // Eye squint = eye openness BELOW the neutral baseline
            float eyeSquint = Mathf.Max(0f, -eyeAvg); // negative eyeAvg = closed
            float browLow   = Mathf.Max(0f, -browRaise);
            float rawAngry  = Mathf.Max(0f,
                furrow    * _angryFurrowWeight
                + eyeSquint * _angryEyeSquintWeight
                + browLow   * _angryBrowLowWeight);
 
            // ── EMA smoothing of confidence scores ─────────────────────────────
            _confidence[(int)EmotionLabel.Neutral]   = Mathf.Lerp(rawNeutral, _confidence[(int)EmotionLabel.Neutral],   _confidenceEma);
            _confidence[(int)EmotionLabel.Happy]      = Mathf.Lerp(rawHappy,   _confidence[(int)EmotionLabel.Happy],      _confidenceEma);
            _confidence[(int)EmotionLabel.Surprised]  = Mathf.Lerp(rawSurp,    _confidence[(int)EmotionLabel.Surprised],  _confidenceEma);
            _confidence[(int)EmotionLabel.Angry]      = Mathf.Lerp(rawAngry,   _confidence[(int)EmotionLabel.Angry],      _confidenceEma);
 
            // ── Pick winning emotion ───────────────────────────────────────────
            EmotionLabel detected = EmotionLabel.Neutral;
            float bestScore = _confidence[(int)EmotionLabel.Neutral];
 
            for (int i = 1; i < _confidence.Length; i++)
            {
                if (_confidence[i] > bestScore)
                {
                    bestScore = _confidence[i];
                    detected  = (EmotionLabel)i;
                }
            }
 
            // ── Temporal hold: candidate must stay dominant for _holdSeconds ───
            if (detected == _candidateEmotion)
            {
                _candidateHoldTime += Time.deltaTime;
                if (_candidateHoldTime >= _holdSeconds && _currentEmotion != detected)
                {
                    _currentEmotion = detected;
                    Debug.Log($"[EmotionClassifier] ✓ CONFIRMED: {_currentEmotion} " +
                              $"| scores N:{_confidence[0]:F2} H:{_confidence[1]:F2} " +
                              $"S:{_confidence[2]:F2} A:{_confidence[3]:F2}");
                    OnEmotionChanged?.Invoke(_currentEmotion);
                }
            }
            else
            {
                _candidateEmotion  = detected;
                _candidateHoldTime = 0f;
                Debug.Log($"[EmotionClassifier] → Candidate: {_candidateEmotion} " +
                          $"(smile={smile:F3} mouth={mouthOpen:F3} brow={browRaise:F3} furrow={furrow:F3})");
            }
        }
 
        #endregion
 
        #region Public API
 
        /// <summary>The last confirmed emotion.</summary>
        public EmotionLabel CurrentEmotion => _currentEmotion;
 
        /// <summary>Confidence for a specific emotion in [0..∞] (relative scores, not normalised).</summary>
        public float GetConfidence(EmotionLabel emotion) => _confidence[(int)emotion];
 
        #endregion
    }
}
 