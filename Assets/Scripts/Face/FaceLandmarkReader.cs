
using UnityEngine;
using ARcadeRush.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Components.Containers;
 
namespace ARcadeRush.Face
{
    /// <summary>
    /// Reads MediaPipe face landmarks and computes 6 normalized, EMA-smoothed expression metrics.
    ///
    /// Metric layout:
    ///   [0] Mouth Openness  — vertical gap between inner lips, normalized by face height
    ///   [1] Eye Openness L  — left eye  height/width aspect ratio
    ///   [2] Eye Openness R  — right eye height/width aspect ratio
    ///   [3] Brow Raise      — how far the brows sit above the eye tops, normalized by face height
    ///   [4] Smile Score     — how much lip corners are raised above lip midpoint (key for Happy)
    ///   [5] Brow Furrow     — how close inner brows are together (key for Angry)
    ///
    /// All metrics are filtered with an exponential moving average (EMA) to reduce
    /// frame-to-frame jitter before the classifier sees them.
    /// </summary>
    [DefaultExecutionOrder(-10)] // run before EmotionClassifier
    public class FaceLandmarkReader : MonoBehaviour
    {
        // ── Public API ────────────────────────────────────────────────────────────
        public float[] NormalizedMetrics { get; private set; } = new float[6];
 
        /// <summary>Head-pose confidence [0..1]. Values below HeadConfidenceThreshold
        /// mean the face is tilted/turned enough to distort metrics — classifier
        /// can use this to stay on the current emotion rather than misclassify.</summary>
        public float HeadConfidence { get; private set; } = 1f;
 
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("EMA Smoothing (0 = no smoothing, 1 = freeze)")]
        [Range(0f, 0.99f)]
        [Tooltip("Higher = smoother but slower to react. 0.5–0.7 is a good starting range.")]
        [SerializeField] private float _emaAlpha = 0.60f;
 
        [Header("Calibration")]
        [Tooltip("Seconds to auto-calibrate the neutral baseline after the component starts.")]
        [SerializeField] private float _calibrationDuration = 2f;
 
        // ── Private ───────────────────────────────────────────────────────────────
        private float[] _rawMetrics    = new float[6];
        private float[] _neutralBaseline = null; // set during calibration
        private float   _calibrationTimer = 0f;
        private bool    _calibrated = false;
 
        private bool _hasNewData = false;
 
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
                    for (int i = 0; i < 6; i++)
                        _neutralBaseline[i] += (_rawMetrics[i] - _neutralBaseline[i]) * 0.1f; // slow accumulation
 
                if (_calibrationTimer >= _calibrationDuration)
                {
                    _calibrated = true;
                    Debug.Log($"[FaceLandmarkReader] ✓ Neutral baseline calibrated: " +
                              $"mouth={_neutralBaseline[0]:F3} smile={_neutralBaseline[4]:F3} furrow={_neutralBaseline[5]:F3}");
                }
            }
 
            // ── EMA smoothing ─────────────────────────────────────────────────
            for (int i = 0; i < 6; i++)
                NormalizedMetrics[i] = Mathf.Lerp(_rawMetrics[i], NormalizedMetrics[i], _emaAlpha);
        }
 
        #endregion
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Landmark processing
 
        private void HandleFaceDetected(NormalizedLandmarks faceLandmarks)
        {
            var lm = faceLandmarks.landmarks;
            if (lm == null || lm.Count < 400) return;
 
            // ── Face reference dimensions ─────────────────────────────────────
            // Vertical: chin (152) → top of forehead (10)
            float faceHeight = Mathf.Max(0.001f, Dist2D(lm, 10, 152));
            // Horizontal: left ear (234) → right ear (454)
            float faceWidth  = Mathf.Max(0.001f, Dist2D(lm, 234, 454));
 
            // ── [0] Mouth Openness ────────────────────────────────────────────
            // Inner upper lip (13) vs inner lower lip (14)
            _rawMetrics[0] = Dist2D(lm, 13, 14) / faceHeight;
 
            // ── [1] Eye Openness Left ─────────────────────────────────────────
            // Top eyelid (159), bottom eyelid (145), inner (33), outer (133)
            float eyeLW = Mathf.Max(0.001f, Dist2D(lm, 33, 133));
            _rawMetrics[1] = Dist2D(lm, 159, 145) / eyeLW;
 
            // ── [2] Eye Openness Right ────────────────────────────────────────
            // Top (386), bottom (374), inner (362), outer (263)
            float eyeRW = Mathf.Max(0.001f, Dist2D(lm, 362, 263));
            _rawMetrics[2] = Dist2D(lm, 386, 374) / eyeRW;
 
            // ── [3] Brow Raise ────────────────────────────────────────────────
            // Measure how far brows sit above the top eyelid on each side.
            // Left brow mid (52), Left eye top (159); Right brow mid (282), Right eye top (386)
            // In screen coords y increases downward, so brow_y < eye_y when raised.
            float browLiftL = (lm[159].y - lm[52].y) / faceHeight;  // positive = raised
            float browLiftR = (lm[386].y - lm[282].y) / faceHeight;
            _rawMetrics[3] = (browLiftL + browLiftR) * 0.5f;
 
            // ── [4] Smile Score (lip corner raise) ────────────────────────────
            // Left corner (61), Right corner (291), lip vertical midpoint
            // A genuine smile lifts the corners above the lip centre line.
            float lipMidY    = (lm[13].y + lm[14].y) * 0.5f;
            float smileL     = (lipMidY - lm[61].y)  / faceHeight;  // positive = corner above mid
            float smileR     = (lipMidY - lm[291].y) / faceHeight;
            _rawMetrics[4] = (smileL + smileR) * 0.5f;
 
            // ── [5] Brow Furrow ───────────────────────────────────────────────
            // Distance between inner brow points (65 left, 295 right), normalized by face width.
            // Anger pulls them together → smaller distance → higher furrow score.
            float innerBrowDist = Dist2D(lm, 65, 295);
            // Invert: wider apart = 0, slammed together ≈ 1
            _rawMetrics[5] = 1f - Mathf.Clamp01(innerBrowDist / (faceWidth * 0.35f));
 
            // ── Head-pose confidence ──────────────────────────────────────────
            // Estimate yaw via horizontal asymmetry of nose tip (1) between both ears.
            // If nose is strongly off-centre, metrics are unreliable.
            float noseCentreX   = (lm[234].x + lm[454].x) * 0.5f;
            float noseTipOffset = Mathf.Abs(lm[1].x - noseCentreX) / (faceWidth * 0.5f);
            HeadConfidence = Mathf.Clamp01(1f - noseTipOffset * 2f);
 
            _hasNewData = true;
        }
 
        #endregion
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Neutral baseline helpers
 
        /// <summary>
        /// Returns a metric value relative to the calibrated neutral baseline.
        /// Positive = above neutral, negative = below neutral.
        /// Falls back to raw NormalizedMetrics when not yet calibrated.
        /// </summary>
        public float GetRelativeMetric(int index)
        {
            if (!_calibrated || _neutralBaseline == null) return NormalizedMetrics[index];
            return NormalizedMetrics[index] - _neutralBaseline[index];
        }
 
        /// <summary>Force a new calibration pass (e.g. call at start of each game round).</summary>
        public void ResetCalibration()
        {
            _calibrated       = false;
            _calibrationTimer = 0f;
            _neutralBaseline  = null;
            Debug.Log("[FaceLandmarkReader] Calibration reset.");
        }
 
        #endregion
 
        // ─────────────────────────────────────────────────────────────────────────
        #region Utility
 
        private void TrySubscribeFaceEvents()
        {
            if (MediaPipeController.Instance == null)
            {
                Debug.LogWarning("[FaceLandmarkReader] MediaPipeController instance is NULL");
                return;
            }
            MediaPipeController.Instance.OnFaceDetected -= HandleFaceDetected;
            MediaPipeController.Instance.OnFaceDetected += HandleFaceDetected;
            Debug.Log("[FaceLandmarkReader] Successfully subscribed to OnFaceDetected");
        }
 
        /// <summary>2-D Euclidean distance between two landmarks (ignores Z).</summary>
        private static float Dist2D(System.Collections.Generic.IList<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> lm, int a, int b)
        {
            float dx = lm[a].x - lm[b].x;
            float dy = lm[a].y - lm[b].y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
 
        #endregion
    }
}