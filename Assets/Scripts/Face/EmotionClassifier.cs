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
        [SerializeField] private float _holdSeconds = 0.10f;
        [Tooltip("Minimum head-pose confidence required to update the classifier.")]
        [Range(0f, 1f)]
        [SerializeField] private float _minHeadConfidence = 0.50f;
 
        // ── Inspector — Confidence EMA ────────────────────────────────────────────
        [Header("Confidence Smoothing (EMA per emotion)")]
        [Tooltip("EMA factor for emotion confidence scores. Higher = slower but more stable.")]
        [Range(0f, 0.99f)]
        [SerializeField] private float _confidenceEma = 0.25f;
 
        // ── Inspector — Happy ─────────────────────────────────────────────────────
        [Header("Happy — smile ratio weights")]
        [Tooltip("Multiplier on (smileRatio - 1). At ratio=2 (twice neutral): score = 1 * weight.")]
        [SerializeField] private float _happySmileWeight  = 5.0f;
        [Tooltip("Furrow penalty — subtracts furrow * this value (can't smile while scowling).")]
        [SerializeField] private float _happyMouthPenalty = 2.0f;

        // ── Inspector — Surprised ─────────────────────────────────────────────────
        [Header("Surprised — weights")]
        [Tooltip("Geometric mean of (mouthOpen * browRaise) — forces both signals to co-activate.")]
        [SerializeField] private float _surpCombinedWeight  = 6.0f;
        [Tooltip("mouthFunnel (O-face shape) — replaces Pucker.")]
        [SerializeField] private float _surpFunnelWeight    = 2.0f;
        [Tooltip("Blink penalty — subtracts blinkAvg * this value; blinking suppresses Surprised.")]
        [SerializeField] private float _surpBlinkPenalty    = 1.5f;
        [Tooltip("Rate of brow raise (units/sec) — Surprised tends to be sudden. Clamped to [0,5].")]
        [SerializeField] private float _surpVelocityWeight  = 0.3f;

        // ── Inspector — Angry ─────────────────────────────────────────────────────
        [Header("Angry — weights")]
        [SerializeField] private float _angryFurrowWeight  = 2.0f;
        [SerializeField] private float _angryBrowLowWeight = 4.0f;
        [Tooltip("eyeSquint (eye narrowing) — angry signal, but also fires during Duchenne smiles.")]
        [SerializeField] private float _angrySquintWeight  = 3.0f;
        [SerializeField] private float _angryFrownWeight   = 2.0f;
        [SerializeField] private float _angryPressWeight   = 5.0f;
        [Tooltip("Smile penalty — subtracts smile * this value; can't be angry while genuinely smiling.")]
        [SerializeField] private float _angrySmilePenalty  = 4.0f;
 
        // ── Inspector — Neutral baseline offset ──────────────────────────────────
        [Header("Neutral — opponent process")]
        [SerializeField] private float _neutralBaseScore = 0.40f;
        [Tooltip("When any emotion raw score reaches this value, Neutral is fully suppressed. Lower = Neutral is suppressed earlier.")]
        [SerializeField] private float _neutralSuppressionRange = 0.5f;
        [Tooltip("Additional score added to the current emotion to prevent rapid flickering.")]
        [SerializeField] private float _hysteresis = 0.05f;

        // ── Inspector — Live debug (actualizado cada frame en Play Mode) ──────────
        [Header("Live — Happy")]
        [SerializeField] private float _dbgSmile;
        [SerializeField] private float _dbgMouthHappy;
        [SerializeField] private float _dbgFurrowPenH;
        [SerializeField] private float _dbgRawHappy;
        [SerializeField] private float _dbgConfHappy;
        [SerializeField] private bool  _dbgIsHappy;

        [Header("Live — Surprised")]
        [SerializeField] private float _dbgMouthSurp;
        [SerializeField] private float _dbgEyeAvg;
        [SerializeField] private float _dbgBrowRaise;
        [SerializeField] private float _dbgFunnel;
        [SerializeField] private float _dbgBlinkPen;
        [SerializeField] private float _dbgRawSurp;
        [SerializeField] private float _dbgConfSurp;
        [SerializeField] private bool  _dbgIsSurprised;

        [Header("Live — Angry")]
        [SerializeField] private float _dbgFurrow;
        [SerializeField] private float _dbgBrowLow;
        [SerializeField] private float _dbgSquint;
        [SerializeField] private float _dbgTrueFrown;
        [SerializeField] private float _dbgMouthPress;
        [SerializeField] private float _dbgSmilePenA;
        [SerializeField] private float _dbgRawAngry;
        [SerializeField] private float _dbgConfAngry;
        [SerializeField] private bool  _dbgIsAngry;

        [Header("Live — Neutral")]
        [SerializeField] private float _dbgRawNeutral;
        [SerializeField] private float _dbgConfNeutral;
        [SerializeField] private bool  _dbgIsNeutral;

        // ── Private state ─────────────────────────────────────────────────────────
        private FaceLandmarkReader _reader;

        private float[] _confidence = new float[4];

        private EmotionLabel _currentEmotion   = EmotionLabel.Neutral;
        private EmotionLabel _candidateEmotion = EmotionLabel.Neutral;
        private float        _candidateHoldTime = 0f;
        private float        _prevBrowRaise     = 0f;
 
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

            // Skip if no fresh data from camera this frame (face lost or camera paused).
            if (!_reader.HasFreshData) return;

            // ── Head-pose gate ─────────────────────────────────────────────────
            if (_reader.HeadConfidence < _minHeadConfidence) return;

            if (!_reader.IsCalibrated) return;

            // ── Read metrics ───────────────────────────────────────────────────
            // Positive-going use 0.5 baseline weight (partial subtraction).
            // Negative-going use full subtraction.
            float mouthOpen  = _reader.GetRelativeMetric(0, 0.5f);
            float browRaise  = _reader.GetRelativeMetric(3, 0.5f);
            float furrow     = _reader.GetRelativeMetric(5);
            float trueFrown  = _reader.GetRelativeMetric(6);
            float mouthPress = _reader.GetRelativeMetric(7);
            float funnel     = _reader.GetRelativeMetric(8, 0.5f);
            float squint     = _reader.GetRelativeMetric(9);

            // Happy uses smile RATIO (person-independent): 1.0 = neutral, 2.0 = twice neutral.
            // Subtracting 1 gives "how much above neutral" — always 0 at rest.
            float smileRatio = Mathf.Max(0f, _reader.GetRatioMetric(4) - 1f);

            // ── Brow raise velocity (Surprised is typically sudden) ────────────
            float browVelocity = Mathf.Clamp((browRaise - _prevBrowRaise) / Time.deltaTime, 0f, 5f);
            _prevBrowRaise = browRaise;

            // ── Raw confidence scores ──────────────────────────────────────────

            // Happy: smile ratio is the primary driver. Furrow penalises scowling.
            float rawHappy = Mathf.Max(0f,
                smileRatio   * _happySmileWeight
                - furrow     * _happyMouthPenalty);

            // Surprised: geometric mean of mouthOpen and browRaise — BOTH must be active.
            // Sqrt(A*B) = 0 if either A or B is 0. Prevents yawn-only or brow-only triggers.
            float mouthSignal = Mathf.Max(0f, mouthOpen - 0.03f);
            float browSignal  = Mathf.Max(0f, browRaise);
            float geoMean     = Mathf.Sqrt(mouthSignal * browSignal);
            float rawSurp = Mathf.Max(0f,
                geoMean     * _surpCombinedWeight
                + funnel    * _surpFunnelWeight
                + browVelocity * _surpVelocityWeight
                - _reader.BlinkAverage * _surpBlinkPenalty);

            // Angry: furrow + low brow + squint + frown + mouth press. Smile penalises.
            float rawAngry = Mathf.Max(0f,
                Mathf.Max(0f, furrow - 0.05f) * _angryFurrowWeight
                + Mathf.Max(0f, -browRaise)   * _angryBrowLowWeight
                + squint     * _angrySquintWeight
                + trueFrown  * _angryFrownWeight
                + mouthPress * _angryPressWeight
                - smileRatio * _angrySmilePenalty);

            // Neutral uses opponent process: when any emotion signal is strong,
            // Neutral is suppressed — so it recovers at full strength the moment
            // the expression relaxes, instead of waiting for EMA to decay.
            float emotionSignal = Mathf.Max(rawHappy, Mathf.Max(rawSurp, rawAngry));
            float rawNeutral = _neutralBaseScore
                * Mathf.Clamp01(1f - emotionSignal / Mathf.Max(0.001f, _neutralSuppressionRange));

            // ── EMA smoothing ──────────────────────────────────────────────────
            _confidence[(int)EmotionLabel.Neutral]  = Mathf.Lerp(rawNeutral, _confidence[(int)EmotionLabel.Neutral],  _confidenceEma);
            _confidence[(int)EmotionLabel.Happy]     = Mathf.Lerp(rawHappy,  _confidence[(int)EmotionLabel.Happy],     _confidenceEma);
            _confidence[(int)EmotionLabel.Surprised] = Mathf.Lerp(rawSurp,   _confidence[(int)EmotionLabel.Surprised], _confidenceEma);
            _confidence[(int)EmotionLabel.Angry]     = Mathf.Lerp(rawAngry,  _confidence[(int)EmotionLabel.Angry],     _confidenceEma);

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

            // ── Temporal hold ──────────────────────────────────────────────────
            if (detected == _candidateEmotion)
            {
                _candidateHoldTime += Time.deltaTime;
                if (_candidateHoldTime >= _holdSeconds && _currentEmotion != detected)
                {
                    _currentEmotion = detected;
                    OnEmotionChanged?.Invoke(_currentEmotion);
                }
            }
            else
            {
                _candidateEmotion  = detected;
                _candidateHoldTime = 0f;
            }

#if UNITY_EDITOR
            Debug.Log($"[Scores] N:{_confidence[0]:F3} H:{_confidence[1]:F3} S:{_confidence[2]:F3} A:{_confidence[3]:F3} | Head:{_reader.HeadConfidence:F2}");
#endif

            // ── Live debug fields ──────────────────────────────────────────────
            _dbgSmile       = smileRatio;    // ratio (0 = at neutral, 1 = twice neutral)
            _dbgMouthHappy  = mouthOpen;
            _dbgFurrowPenH  = furrow;
            _dbgRawHappy    = rawHappy;
            _dbgConfHappy   = _confidence[(int)EmotionLabel.Happy];
            _dbgIsHappy     = _currentEmotion == EmotionLabel.Happy;

            _dbgMouthSurp   = mouthSignal;   // mouth after dead zone
            _dbgEyeAvg      = geoMean;       // geometric mean (repurposed field)
            _dbgBrowRaise   = browRaise;
            _dbgFunnel      = funnel;
            _dbgBlinkPen    = browVelocity;  // brow velocity (repurposed field)
            _dbgRawSurp     = rawSurp;
            _dbgConfSurp    = _confidence[(int)EmotionLabel.Surprised];
            _dbgIsSurprised = _currentEmotion == EmotionLabel.Surprised;

            _dbgFurrow      = furrow;
            _dbgBrowLow     = Mathf.Max(0f, -browRaise);
            _dbgSquint      = squint;
            _dbgTrueFrown   = trueFrown;
            _dbgMouthPress  = mouthPress;
            _dbgSmilePenA   = smileRatio;
            _dbgRawAngry    = rawAngry;
            _dbgConfAngry   = _confidence[(int)EmotionLabel.Angry];
            _dbgIsAngry     = _currentEmotion == EmotionLabel.Angry;

            _dbgRawNeutral  = rawNeutral;       // shows suppressed value (0 when emoting)
            _dbgConfNeutral = _confidence[(int)EmotionLabel.Neutral];
            _dbgIsNeutral   = _currentEmotion == EmotionLabel.Neutral;
        }
 
        #endregion
 
        #region Public API
 
        /// <summary>The last confirmed emotion.</summary>
        public EmotionLabel CurrentEmotion => _currentEmotion;

        /// <summary>Live smoothed confidence scores — use the Console log or a debug UI to tune weights.</summary>
        public (float neutral, float happy, float surprised, float angry) DebugScores =>
            (_confidence[0], _confidence[1], _confidence[2], _confidence[3]);

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
            _confidence[(int)EmotionLabel.Neutral] = _neutralBaseScore;
            _prevBrowRaise = 0f;
            _currentEmotion   = EmotionLabel.Neutral;
            _candidateEmotion = EmotionLabel.Neutral;
            _candidateHoldTime = 0f;

            _dbgSmile = _dbgMouthHappy = _dbgFurrowPenH = _dbgRawHappy = _dbgConfHappy = 0f;
            _dbgIsHappy = false;
            _dbgMouthSurp = _dbgEyeAvg = _dbgBrowRaise = _dbgFunnel = _dbgBlinkPen = _dbgRawSurp = _dbgConfSurp = 0f;
            _dbgIsSurprised = false;
            _dbgFurrow = _dbgBrowLow = _dbgSquint = _dbgTrueFrown = _dbgMouthPress = _dbgSmilePenA = _dbgRawAngry = _dbgConfAngry = 0f;
            _dbgIsAngry = false;
            _dbgRawNeutral = _dbgConfNeutral = 0f;
            _dbgIsNeutral = true;
        }
 
        #endregion
    }
}
 