using System;
using UnityEngine;

namespace ARcadeRush.Face
{
    public enum EmotionLabel { Neutral, Happy, Surprised, Angry }

    /// <summary>Two detection modes for EmotionClassifier.</summary>
    public enum EmotionDetectionMode
    {
        /// <summary>Fires a snapshot automatically every _snapshotInterval seconds.</summary>
        AutoInterval,
        /// <summary>Reads nothing until OpenDetectionWindow() is called. During the window,
        /// accumulates face data and delivers the dominant emotion when it closes.</summary>
        Window
    }

    /// <summary>
    /// Classifies emotions from FaceLandmarkReader metrics.
    ///
    /// Two modes (switchable via SetMode or Inspector):
    ///   AutoInterval — snapshots every _snapshotInterval seconds. For debug scenes and
    ///                  continuous detection use cases.
    ///   Window       — reads nothing at rest. Call OpenDetectionWindow() to start a timed
    ///                  reading session; OnWindowClosed fires with the dominant emotion.
    ///                  Designed for the Director de Escena minigame.
    ///
    /// Scoring:
    ///   Happy    — smile ratio (person-independent) × weight, furrow penalty.
    ///   Surprised — geometric mean of mouthOpen and browRaise (both must co-activate).
    ///   Angry    — furrow + low-brow + squint + frown + mouthPress, smile penalty.
    ///   Neutral  — opponent process: suppressed when any emotion is strong,
    ///              recovers immediately when expression relaxes.
    /// </summary>
    [RequireComponent(typeof(FaceLandmarkReader))]
    public class EmotionClassifier : MonoBehaviour
    {
        // ── Events ────────────────────────────────────────────────────────────────
        /// <summary>Fires when the confirmed emotion changes (both modes).</summary>
        public event Action<EmotionLabel> OnEmotionChanged;

        /// <summary>Fires when a Window-mode reading session closes, with the dominant emotion.</summary>
        public event Action<EmotionLabel> OnWindowClosed;

        // ── Inspector — Activation ────────────────────────────────────────────────
        [Header("Activation")]
        [Tooltip("Start the classifier off. Enable at runtime via SetEnabled(true) or the Toggle button.")]
        [SerializeField] private bool _isEnabled = false;

        // ── Inspector — Timing ────────────────────────────────────────────────────
        [Header("Timing")]
        [Tooltip("AutoInterval: fires snapshots on a timer. Window: only reads during an OpenDetectionWindow() session.")]
        [SerializeField] private EmotionDetectionMode _detectionMode = EmotionDetectionMode.AutoInterval;
        [Tooltip("Seconds between automatic snapshots (AutoInterval mode only).")]
        [SerializeField] private float _snapshotInterval = 0.75f;
        [Tooltip("Duration of one reading window in seconds (Window mode only).")]
        [SerializeField] private float _windowDuration = 3f;
        [Tooltip("Minimum head-pose confidence required to classify.")]
        [Range(0f, 1f)]
        [SerializeField] private float _minHeadConfidence = 0.50f;

        // ── Inspector — Happy ─────────────────────────────────────────────────────
        [Header("Happy — smile ratio weights")]
        [Tooltip("Multiplier on (smileRatio - 1). At ratio=2 (twice neutral): score = 1 * weight.")]
        [SerializeField] private float _happySmileWeight  = 5.0f;
        [Tooltip("Furrow penalty — subtracts furrow * this value.")]
        [SerializeField] private float _happyMouthPenalty = 2.0f;

        // ── Inspector — Surprised ─────────────────────────────────────────────────
        [Header("Surprised — weights")]
        [Tooltip("Geometric mean of (mouthOpen * browRaise) — forces both signals to co-activate.")]
        [SerializeField] private float _surpCombinedWeight  = 6.0f;
        [Tooltip("mouthFunnel (O-face shape).")]
        [SerializeField] private float _surpFunnelWeight    = 2.0f;
        [Tooltip("Blink penalty — blinking suppresses Surprised.")]
        [SerializeField] private float _surpBlinkPenalty    = 1.5f;

        // ── Inspector — Angry ─────────────────────────────────────────────────────
        [Header("Angry — weights")]
        [SerializeField] private float _angryFurrowWeight  = 2.0f;
        [SerializeField] private float _angryBrowLowWeight = 4.0f;
        [Tooltip("eyeSquint — angry signal, also fires during Duchenne smiles.")]
        [SerializeField] private float _angrySquintWeight  = 3.0f;
        [SerializeField] private float _angryFrownWeight   = 2.0f;
        [SerializeField] private float _angryPressWeight   = 5.0f;
        [Tooltip("Smile penalty — can't be angry while genuinely smiling.")]
        [SerializeField] private float _angrySmilePenalty  = 4.0f;

        // ── Inspector — Neutral ───────────────────────────────────────────────────
        [Header("Neutral — opponent process")]
        [SerializeField] private float _neutralBaseScore = 0.40f;
        [Tooltip("Emotion raw score at which Neutral is fully suppressed. Lower = suppressed sooner.")]
        [SerializeField] private float _neutralSuppressionRange = 0.5f;
        [Tooltip("Bonus added to the currently active emotion to prevent rapid switching.")]
        [SerializeField] private float _hysteresis = 0.05f;

        // ── Inspector — Live debug ────────────────────────────────────────────────
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

        // Last snapshot's raw scores (used for debug display and hysteresis)
        private float[] _confidence = new float[4];

        private EmotionLabel _currentEmotion = EmotionLabel.Neutral;

        // AutoInterval state
        private float _snapshotTimer = 0f;

        // Window mode state
        private bool    _windowOpen  = false;
        private float   _windowTimer = 0f;
        private float[] _windowAccum = new float[4]; // seconds each emotion led during the window

        #region Unity lifecycle

        private void Awake()
        {
            _reader = GetComponent<FaceLandmarkReader>();
            _confidence[(int)EmotionLabel.Neutral] = _neutralBaseScore;
        }

        private void Update()
        {
            if (!_isEnabled) return;
            if (!_reader.HasFreshData) return;
            if (_reader.HeadConfidence < _minHeadConfidence) return;
            if (!_reader.IsCalibrated) return;

            if (_detectionMode == EmotionDetectionMode.AutoInterval)
            {
                _snapshotTimer += Time.deltaTime;
                if (_snapshotTimer >= _snapshotInterval)
                {
                    _snapshotTimer = 0f;
                    TakeSnapshot();
                }
            }
            else // Window
            {
                if (!_windowOpen) return;

                _windowTimer += Time.deltaTime;
                _windowAccum[(int)ComputeRawWinner()] += Time.deltaTime;

                if (_windowTimer >= _windowDuration)
                    CloseWindow();
            }
        }

        #endregion

        #region Public API

        /// <summary>The last confirmed emotion.</summary>
        public EmotionLabel CurrentEmotion => _currentEmotion;

        /// <summary>Active detection mode.</summary>
        public EmotionDetectionMode DetectionMode => _detectionMode;

        /// <summary>True while a Window-mode reading session is open.</summary>
        public bool IsWindowOpen => _windowOpen;

        /// <summary>Last snapshot scores per emotion (not EMA-smoothed — direct snapshot values).</summary>
        public float GetConfidence(EmotionLabel emotion) => _confidence[(int)emotion];

        /// <summary>Switches detection mode at runtime.</summary>
        public void SetMode(EmotionDetectionMode mode)
        {
            _detectionMode = mode;
            _snapshotTimer = 0f;
            if (_windowOpen) CloseWindow();
        }

        /// <summary>Enables or disables the classifier. Disabling resets state.</summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            if (!enabled) ResetState();
        }

        /// <summary>
        /// Takes an immediate snapshot of the current face expression, classifies it,
        /// updates CurrentEmotion, and fires OnEmotionChanged if the result changed.
        /// Works in both modes. Does NOT fire OnWindowClosed.
        /// Returns the detected emotion even if classifier is not enabled
        /// (useful for manual queries), but only classifies when IsCalibrated.
        /// </summary>
        public EmotionLabel TakeSnapshot()
        {
            if (!_reader.IsCalibrated) return _currentEmotion;

            ComputeScores(
                out float rawHappy, out float rawSurp, out float rawAngry, out float rawNeutral,
                out float smileRatio, out float mouthOpen, out float browRaise,
                out float furrow, out float mouthSignal, out float geoMean,
                out float funnel, out float squint, out float trueFrown, out float mouthPress);

            // Store scores for debug and hysteresis
            _confidence[(int)EmotionLabel.Neutral]   = rawNeutral;
            _confidence[(int)EmotionLabel.Happy]      = rawHappy;
            _confidence[(int)EmotionLabel.Surprised]  = rawSurp;
            _confidence[(int)EmotionLabel.Angry]      = rawAngry;

            EmotionLabel detected = PickWinner();

            if (detected != _currentEmotion)
            {
                _currentEmotion = detected;
                OnEmotionChanged?.Invoke(_currentEmotion);
            }

            UpdateDebugFields(smileRatio, mouthOpen, furrow, rawHappy, rawSurp, rawAngry, rawNeutral,
                mouthSignal, geoMean, browRaise, funnel, squint, trueFrown, mouthPress);

#if UNITY_EDITOR
            Debug.Log($"[Snapshot] N:{rawNeutral:F3} H:{rawHappy:F3} S:{rawSurp:F3} A:{rawAngry:F3} → {_currentEmotion}");
#endif

            return _currentEmotion;
        }

        /// <summary>
        /// Opens a detection window (Window mode only). The classifier samples the face
        /// for _windowDuration seconds and fires OnWindowClosed with the dominant emotion.
        /// Calling this while a window is already open restarts it.
        /// </summary>
        public void OpenDetectionWindow()
        {
            _windowTimer = 0f;
            _windowOpen  = true;
            System.Array.Clear(_windowAccum, 0, _windowAccum.Length);
        }

        /// <summary>Resets all state. Call alongside FaceLandmarkReader.ResetCalibration().</summary>
        public void ResetState()
        {
            for (int i = 0; i < _confidence.Length; i++) _confidence[i] = 0f;
            _confidence[(int)EmotionLabel.Neutral] = _neutralBaseScore;
            _currentEmotion = EmotionLabel.Neutral;
            _snapshotTimer  = 0f;
            _windowTimer    = 0f;
            _windowOpen     = false;
            System.Array.Clear(_windowAccum, 0, _windowAccum.Length);

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

        #region Internal scoring

        /// <summary>
        /// Reads current face metrics and computes raw emotion scores.
        /// Pure — does not modify any classifier state.
        /// </summary>
        private void ComputeScores(
            out float rawHappy, out float rawSurp, out float rawAngry, out float rawNeutral,
            out float smileRatio, out float mouthOpen, out float browRaise,
            out float furrow, out float mouthSignal, out float geoMean,
            out float funnel, out float squint, out float trueFrown, out float mouthPress)
        {
            mouthOpen  = _reader.GetRelativeMetric(0, 0.5f);
            browRaise  = _reader.GetRelativeMetric(3, 0.5f);
            furrow     = _reader.GetRelativeMetric(5);
            trueFrown  = _reader.GetRelativeMetric(6);
            mouthPress = _reader.GetRelativeMetric(7);
            funnel     = _reader.GetRelativeMetric(8, 0.5f);
            squint     = _reader.GetRelativeMetric(9);

            // Smile ratio: 0 at rest, >0 when smiling above neutral level
            smileRatio = Mathf.Max(0f, _reader.GetRatioMetric(4) - 1f);

            rawHappy = Mathf.Max(0f,
                smileRatio * _happySmileWeight
                - furrow   * _happyMouthPenalty);

            mouthSignal = Mathf.Max(0f, mouthOpen - 0.03f);
            float browSignal = Mathf.Max(0f, browRaise);
            geoMean = Mathf.Sqrt(mouthSignal * browSignal);

            rawSurp = Mathf.Max(0f,
                geoMean  * _surpCombinedWeight
                + funnel * _surpFunnelWeight
                - _reader.BlinkAverage * _surpBlinkPenalty);

            rawAngry = Mathf.Max(0f,
                Mathf.Max(0f, furrow - 0.05f) * _angryFurrowWeight
                + Mathf.Max(0f, -browRaise)   * _angryBrowLowWeight
                + squint     * _angrySquintWeight
                + trueFrown  * _angryFrownWeight
                + mouthPress * _angryPressWeight
                - smileRatio * _angrySmilePenalty);

            float emotionSignal = Mathf.Max(rawHappy, Mathf.Max(rawSurp, rawAngry));
            rawNeutral = _neutralBaseScore
                * Mathf.Clamp01(1f - emotionSignal / Mathf.Max(0.001f, _neutralSuppressionRange));
        }

        /// <summary>
        /// Picks the winning emotion from _confidence[] with hysteresis.
        /// Must be called after _confidence[] has been updated.
        /// </summary>
        private EmotionLabel PickWinner()
        {
            EmotionLabel detected = EmotionLabel.Neutral;
            float bestScore = _confidence[(int)EmotionLabel.Neutral];
            if (_currentEmotion == EmotionLabel.Neutral) bestScore += _hysteresis;

            for (int i = 1; i < _confidence.Length; i++)
            {
                float score = _confidence[i];
                if ((EmotionLabel)i == _currentEmotion) score += _hysteresis;
                if (score > bestScore) { bestScore = score; detected = (EmotionLabel)i; }
            }

            return detected;
        }

        /// <summary>
        /// Computes and returns the winning emotion for the current frame without
        /// updating any persistent state. Used by Window mode to accumulate votes.
        /// </summary>
        private EmotionLabel ComputeRawWinner()
        {
            ComputeScores(
                out float rawHappy, out float rawSurp, out float rawAngry, out float rawNeutral,
                out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);

            // Temporary override of _confidence for PickWinner, then restore
            float savedN = _confidence[0], savedH = _confidence[1],
                  savedS = _confidence[2], savedA = _confidence[3];

            _confidence[0] = rawNeutral;
            _confidence[1] = rawHappy;
            _confidence[2] = rawSurp;
            _confidence[3] = rawAngry;

            EmotionLabel winner = PickWinner();

            _confidence[0] = savedN; _confidence[1] = savedH;
            _confidence[2] = savedS; _confidence[3] = savedA;

            return winner;
        }

        private void CloseWindow()
        {
            _windowOpen  = false;
            _windowTimer = 0f;

            EmotionLabel dominant = EmotionLabel.Neutral;
            float maxTime = 0f;
            for (int i = 0; i < _windowAccum.Length; i++)
            {
                if (_windowAccum[i] > maxTime) { maxTime = _windowAccum[i]; dominant = (EmotionLabel)i; }
            }
            System.Array.Clear(_windowAccum, 0, _windowAccum.Length);

            if (dominant != _currentEmotion)
            {
                _currentEmotion = dominant;
                OnEmotionChanged?.Invoke(_currentEmotion);
            }
            OnWindowClosed?.Invoke(dominant);
        }

        private void UpdateDebugFields(
            float smileRatio, float mouthOpen, float furrow,
            float rawHappy, float rawSurp, float rawAngry, float rawNeutral,
            float mouthSignal, float geoMean, float browRaise,
            float funnel, float squint, float trueFrown, float mouthPress)
        {
            _dbgSmile       = smileRatio;
            _dbgMouthHappy  = mouthOpen;
            _dbgFurrowPenH  = furrow;
            _dbgRawHappy    = rawHappy;
            _dbgConfHappy   = _confidence[(int)EmotionLabel.Happy];
            _dbgIsHappy     = _currentEmotion == EmotionLabel.Happy;

            _dbgMouthSurp   = mouthSignal;
            _dbgEyeAvg      = geoMean;
            _dbgBrowRaise   = browRaise;
            _dbgFunnel      = funnel;
            _dbgBlinkPen    = _reader.BlinkAverage;
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

            _dbgRawNeutral  = rawNeutral;
            _dbgConfNeutral = _confidence[(int)EmotionLabel.Neutral];
            _dbgIsNeutral   = _currentEmotion == EmotionLabel.Neutral;
        }

        #endregion
    }
}
