using UnityEngine;
using ARcadeRush.Core;
using CubiWare.Core.Logging;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Components.Containers;
 
namespace ARcadeRush.Face
{
    /// <summary>
    /// Reads MediaPipe face landmarks AND blendshapes, computing 10 hybrid expression metrics.
    ///
    /// Metric layout:
    ///   [0] MouthOpen  — jawOpen
    ///   [1] EyeL       — eyeWideLeft  (pure wideness; blink tracked separately as BlinkAverage)
    ///   [2] EyeR       — eyeWideRight (pure wideness)
    ///   [3] BrowRaise  — browInnerUp
    ///   [4] Smile      — mouthSmileLeft/Right + cheekSquintLeft/Right
    ///   [5] Furrow     — browDownLeft/Right + noseSneerLeft/Right
    ///   [6] Frown      — mouthFrownLeft/Right
    ///   [7] MouthPress — mouthPressLeft/Right
    ///   [8] Funnel     — mouthFunnel (O-face; replaces Pucker for Surprised)
    ///   [9] Squint     — eyeSquintLeft/Right (eye narrowing; primary Angry signal)
    ///
    /// BlinkAverage — EMA-smoothed average of eyeBlinkLeft/Right, exposed as a property
    ///                for the EmotionClassifier to use as an explicit Surprised penalty.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class FaceLandmarkReader : MonoBehaviour
    {
        public float[] NormalizedMetrics { get; private set; } = new float[10];

        /// <summary>True only during the frame when fresh face data was processed. Use to reject stale metrics in loggers/tools.</summary>
        public bool HasFreshData { get; private set; } = false;

        /// <summary>Head-pose confidence [0..1]. Low = face turned sideways, metrics unreliable.</summary>
        public float HeadConfidence { get; private set; } = 1f;

        /// <summary>EMA-smoothed average of eyeBlinkLeft and eyeBlinkRight. Used by EmotionClassifier to penalise Surprised when the user blinks.</summary>
        public float BlinkAverage { get; private set; } = 0f;

        /// <summary>
        /// Nose-tip position in MediaPipe normalised image space [0..1].
        /// X increases left→right, Y increases top→bottom (flip Y for Unity viewport).
        /// Updated every frame a face is detected.
        /// </summary>
        public Vector2 FaceCenterNormalized { get; private set; }

        /// <summary>
        /// Face width as a fraction of image width (distance between cheekbone landmarks 234→454).
        /// Use as a scale reference for world-space AR overlays.
        /// </summary>
        public float FaceScale { get; private set; }
 
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("EMA Smoothing (0 = no smoothing, 1 = freeze)")]
        [Range(0f, 0.99f)]
        [Tooltip("Higher = smoother but slower to react. 0.5–0.7 is a good starting range.")]
        [SerializeField] private float _emaAlpha = 0.50f;
 
        [Header("Calibration")]
        [Tooltip("Seconds to auto-calibrate the neutral baseline after the component starts.")]
        [SerializeField] private float _calibrationDuration = 2f;
 
        [Header("Hybrid blend weights [0=pure landmark, 1=pure blendshape]")]
        [Range(0f, 1f)]
        [Tooltip("How much to trust blendshapes vs landmark geometry per metric.")]
        [SerializeField] private float _blendshapeWeight = 0.70f;
 
        // ── Private ───────────────────────────────────────────────────────────────
        private const string LogServiceName = "FaceLandmarkReader";
        private float[] _rawMetrics      = new float[10];
        private float[] _neutralBaseline = null;
        private float   _rawBlink        = 0f;
        private float   _calibrationTimer = 0f;
        private bool    _calibrated = false;
        public bool IsCalibrated => _calibrated;
        
        private bool    _hasNewData = false;
 
        // Cached blendshape values (updated each frame alongside landmarks)
        private float _bs_jawOpen        = 0f;
        private float _bs_eyeWideL       = 0f;
        private float _bs_eyeWideR       = 0f;
        private float _bs_eyeBlinkL      = 0f;
        private float _bs_eyeBlinkR      = 0f;
        private float _bs_browInnerUp    = 0f;
        private float _bs_browDownL      = 0f;
        private float _bs_browDownR      = 0f;
        private float _bs_smileL         = 0f;
        private float _bs_smileR         = 0f;
        private float _bs_frownL         = 0f;
        private float _bs_frownR         = 0f;
        private float _bs_pressL         = 0f;
        private float _bs_pressR         = 0f;
        private float _bs_mouthFunnel    = 0f;
        private float _bs_cheekSquintL   = 0f;
        private float _bs_cheekSquintR   = 0f;
        private float _bs_noseSneerL     = 0f;
        private float _bs_noseSneerR     = 0f;
        private float _bs_eyeSquintL     = 0f;
        private float _bs_eyeSquintR     = 0f;
        private bool  _hasBlendshapes    = false;
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Unity lifecycle
 
        private void OnEnable()  => TrySubscribeFaceEvents();
        private void Start()     => TrySubscribeFaceEvents();
 
        private void OnDisable()
        {
            if (MediaPipeController.Instance != null)
                MediaPipeController.Instance.OnFaceDetected -= HandleFaceDetected;
        }
 
        private void Update()
        {
            if (!_hasNewData) return;
            _hasNewData = false;
 
            // ── Calibration phase ─────────────────────────────────────────────
            if (!_calibrated)
            {
                _calibrationTimer += Time.deltaTime;
                if (_neutralBaseline == null)
                    _neutralBaseline = (float[])_rawMetrics.Clone();
                else
                    for (int i = 0; i < 10; i++)
                        _neutralBaseline[i] += (_rawMetrics[i] - _neutralBaseline[i]) * 0.05f; // More gradual averaging for stability
 
                if (_calibrationTimer >= _calibrationDuration)
                {
                    _calibrated = true;
                    ServiceLogger.Instance.LogInfo(LogServiceName, $"✓ Baseline calibrated " +
                              $"(blendshapes={_hasBlendshapes}): " +
                              $"mouth={_neutralBaseline[0]:F3} smile={_neutralBaseline[4]:F3} " +
                              $"furrow={_neutralBaseline[5]:F3}");
                }
            }
 
            // ── EMA smoothing ─────────────────────────────────────────────────
            HasFreshData = true;
            for (int i = 0; i < 10; i++)
                NormalizedMetrics[i] = Mathf.Lerp(_rawMetrics[i], NormalizedMetrics[i], _emaAlpha);
            BlinkAverage = Mathf.Lerp(_rawBlink, BlinkAverage, _emaAlpha);
        }

        private void LateUpdate()
        {
            HasFreshData = false;
        }
 
        #endregion
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Face result processing
 
        // ── Now receives the FULL FaceLandmarkerResult ────────────────────────
        private void HandleFaceDetected(FaceLandmarkerResult result)
        {
            if (result.faceLandmarks == null || result.faceLandmarks.Count == 0) return;
            var lm = result.faceLandmarks[0].landmarks;
            if (lm == null || lm.Count < 400) return;
 
            // ── 1. Extract blendshapes (if available) ─────────────────────────
            _hasBlendshapes = result.faceBlendshapes != null && result.faceBlendshapes.Count > 0;
            if (_hasBlendshapes)
                ExtractBlendshapes(result.faceBlendshapes[0].categories);
 
            // ── 2. Compute landmark-based geometry ────────────────────────────
            float faceHeight = Mathf.Max(0.001f, Dist2D(lm, 10, 152));
            float faceWidth  = Mathf.Max(0.001f, Dist2D(lm, 234, 454));
 
            // Landmark mouth openness: inner upper (13) vs inner lower (14)
            float lm_mouthOpen = Dist2D(lm, 13, 14) / faceHeight;
 
            // Landmark eye AR
            float eyeLW       = Mathf.Max(0.001f, Dist2D(lm, 33, 133));
            float lm_eyeL     = Dist2D(lm, 159, 145) / eyeLW;
            float eyeRW       = Mathf.Max(0.001f, Dist2D(lm, 362, 263));
            float lm_eyeR     = Dist2D(lm, 386, 374) / eyeRW;
 
            // Landmark brow raise
            float browLiftL   = (lm[159].y - lm[52].y)  / faceHeight;
            float browLiftR   = (lm[386].y - lm[282].y) / faceHeight;
            float lm_browRaise = (browLiftL + browLiftR) * 0.5f;
 
            // Landmark smile (lip corner above lip midpoint)
            float lipMidY     = (lm[13].y + lm[14].y) * 0.5f;
            float lm_smileL   = (lipMidY - lm[61].y)  / faceHeight;
            float lm_smileR   = (lipMidY - lm[291].y) / faceHeight;
            float lm_smile    = (lm_smileL + lm_smileR) * 0.5f;
 
            // Landmark furrow (inner brow proximity)
            float innerBrowDist = Dist2D(lm, 65, 295);
            float lm_furrow   = 1f - Mathf.Clamp01(innerBrowDist / (faceWidth * 0.35f));
 
            // ── 3. Blend: blendshape + landmark → final raw metric ────────────
            float w  = _hasBlendshapes ? _blendshapeWeight : 0f; // blendshape influence
            float w2 = 1f - w;                                    // landmark influence
 
            // [0] Mouth Openness
            // Blendshape jawOpen is very clean. Landmark normalizes well too.
            float bs_mouth = _hasBlendshapes ? _bs_jawOpen : 0f;
            _rawMetrics[0] = w * bs_mouth + w2 * lm_mouthOpen;
 
            // [1] Eye Openness Left — pure wideness signal; blink is tracked separately in BlinkAverage.
            float bs_eyeL = _hasBlendshapes ? _bs_eyeWideL : 0f;
            _rawMetrics[1] = w * bs_eyeL + w2 * lm_eyeL;

            // [2] Eye Openness Right — pure wideness signal.
            float bs_eyeR = _hasBlendshapes ? _bs_eyeWideR : 0f;
            _rawMetrics[2] = w * bs_eyeR + w2 * lm_eyeR;

            // BlinkAverage raw value — EMA-smoothed in Update(); used as explicit Surprised penalty.
            _rawBlink = _hasBlendshapes ? (_bs_eyeBlinkL + _bs_eyeBlinkR) * 0.5f : 0f;
 
            // [3] Brow Raise
            // browInnerUp is the clearest blendshape for surprise/sad brow lift.
            float bs_brow = _hasBlendshapes ? _bs_browInnerUp : 0f;
            _rawMetrics[3] = w * bs_brow + w2 * lm_browRaise;
 
            // [4] Smile Score — PRIMARY happy signal
            // mouthSmile + cheekSquint is the gold standard for genuine smiles.
            // The landmark smile gives geometric confirmation.
            float bs_smile = _hasBlendshapes
                ? (_bs_smileL + _bs_smileR) * 0.5f + (_bs_cheekSquintL + _bs_cheekSquintR) * 0.25f
                : 0f;
            _rawMetrics[4] = w * bs_smile + w2 * lm_smile;
 
            // [5] Brow Furrow — PRIMARY angry signal
            // browDown only — noseSneer removed because it also fires during genuine broad smiles
            // (nasolabial fold deepens), causing cross-activation with Happy.
            float bs_furrow = _hasBlendshapes
                ? (_bs_browDownL + _bs_browDownR) * 0.5f
                : 0f;
            _rawMetrics[5] = w * bs_furrow + w2 * lm_furrow;
 
            // [6] True Frown (n-shape)
            _rawMetrics[6] = _hasBlendshapes ? (_bs_frownL + _bs_frownR) * 0.5f : 0f;

            // [7] Mouth Press (tight lips)
            _rawMetrics[7] = _hasBlendshapes ? (_bs_pressL + _bs_pressR) * 0.5f : 0f;

            // [8] Funnel (O-face — surprised mouth shape; replaces Pucker)
            _rawMetrics[8] = _hasBlendshapes ? _bs_mouthFunnel : 0f;

            // [9] Squint (eye narrowing — primary Angry signal)
            _rawMetrics[9] = _hasBlendshapes ? (_bs_eyeSquintL + _bs_eyeSquintR) * 0.5f : 0f;

            // ── 4. Head-pose confidence (pure landmark — blendshapes don't encode yaw) ──
            float noseCentreX   = (lm[234].x + lm[454].x) * 0.5f;
            float noseTipOffset = Mathf.Abs(lm[1].x - noseCentreX) / (faceWidth * 0.5f);
            HeadConfidence = Mathf.Clamp01(1f - noseTipOffset * 2f);

            // ── 5. Face pose data for AR mask placement ───────────────────────────
            // Nose tip gives the best single-point face centre for overlay alignment.
            FaceCenterNormalized = new Vector2(lm[1].x, lm[1].y);
            FaceScale            = faceWidth; // normalised [0..1]; multiply by a world-space scalar in MaskController
 
            _hasNewData = true;
        }
 
        /// <summary>
        /// Extracts the 14 relevant blendshape scores into cached fields.
        /// Uses a linear scan — faster than Dictionary for 52 entries each frame.
        /// </summary>
        private void ExtractBlendshapes(System.Collections.Generic.List<Category> categories)
        {
            _bs_jawOpen = _bs_eyeWideL = _bs_eyeWideR = 0f;
            _bs_eyeBlinkL = _bs_eyeBlinkR = 0f;
            _bs_browInnerUp = _bs_browDownL = _bs_browDownR = 0f;
            _bs_smileL = _bs_smileR = 0f;
            _bs_frownL = _bs_frownR = 0f;
            _bs_pressL = _bs_pressR = 0f;
            _bs_mouthFunnel = 0f;
            _bs_cheekSquintL = _bs_cheekSquintR = 0f;
            _bs_noseSneerL = _bs_noseSneerR = 0f;
            _bs_eyeSquintL = _bs_eyeSquintR = 0f;
 
            foreach (var c in categories)
            {
                switch (c.categoryName)
                {
                    case "jawOpen":           _bs_jawOpen       = c.score; break;
                    case "eyeWideLeft":       _bs_eyeWideL      = c.score; break;
                    case "eyeWideRight":      _bs_eyeWideR      = c.score; break;
                    case "eyeBlinkLeft":      _bs_eyeBlinkL     = c.score; break;
                    case "eyeBlinkRight":     _bs_eyeBlinkR     = c.score; break;
                    case "browInnerUp":       _bs_browInnerUp   = c.score; break;
                    case "browDownLeft":      _bs_browDownL     = c.score; break;
                    case "browDownRight":     _bs_browDownR     = c.score; break;
                    case "mouthSmileLeft":    _bs_smileL        = c.score; break;
                    case "mouthSmileRight":   _bs_smileR        = c.score; break;
                    case "mouthFrownLeft":    _bs_frownL        = c.score; break;
                    case "mouthFrownRight":   _bs_frownR        = c.score; break;
                    case "mouthPressLeft":    _bs_pressL        = c.score; break;
                    case "mouthPressRight":   _bs_pressR        = c.score; break;
                    case "mouthFunnel":       _bs_mouthFunnel   = c.score; break;
                    case "cheekSquintLeft":   _bs_cheekSquintL  = c.score; break;
                    case "cheekSquintRight":  _bs_cheekSquintR  = c.score; break;
                    case "noseSneerLeft":     _bs_noseSneerL    = c.score; break;
                    case "noseSneerRight":    _bs_noseSneerR    = c.score; break;
                    case "eyeSquintLeft":     _bs_eyeSquintL    = c.score; break;
                    case "eyeSquintRight":    _bs_eyeSquintR    = c.score; break;
                }
            }
        }
 
        #endregion
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Neutral baseline helpers
 
        /// <summary>
        /// Metric relative to calibrated neutral baseline.
        /// Positive = above neutral, negative = below.
        /// Falls back to raw value when not yet calibrated.
        /// </summary>
        public float GetRelativeMetric(int index)
        {
            if (!_calibrated || _neutralBaseline == null) return NormalizedMetrics[index];
            return NormalizedMetrics[index] - _neutralBaseline[index];
        }
 
        /// <summary>Force recalibration (e.g. at the start of each game round).</summary>
        public void ResetCalibration()
        {
            _calibrated       = false;
            _calibrationTimer = 0f;
            _neutralBaseline  = null;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Calibration reset.");
        }
 
        #endregion
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Utility
 
        private void TrySubscribeFaceEvents()
        {
            if (MediaPipeController.Instance == null)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, "MediaPipeController instance is NULL");
                return;
            }
            MediaPipeController.Instance.OnFaceDetected -= HandleFaceDetected;
            MediaPipeController.Instance.OnFaceDetected += HandleFaceDetected;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Successfully subscribed to OnFaceDetected");
        }
 
        private static float Dist2D(System.Collections.Generic.IList<NormalizedLandmark> lm, int a, int b)
        {
            float dx = lm[a].x - lm[b].x;
            float dy = lm[a].y - lm[b].y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
 
        #endregion
    }
}