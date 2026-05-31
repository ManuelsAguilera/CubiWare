using UnityEngine;
using ARcadeRush.EmotionDetection;

namespace ARcadeRush.EmotionDetection
{
    /// <summary>
    /// Developer diagnostic component — attach to any GameObject in a scene
    /// to get a live console feed of the emotion detection pipeline.
    ///
    /// HOW THE EMOTION PIPELINE WORKS (integration reference):
    /// ────────────────────────────────────────────────────────
    ///
    /// 1. TRANSPORT (EmotionWebSocketClient)
    ///    • Every 0.1 s: captures WebCamTexture, scales to 320×240, encodes to JPEG
    ///    • Sends JPEG bytes → ws://localhost:8765/ws (Python server)
    ///    • Receives JSON text → enqueues in ConcurrentQueue (thread-safe)
    ///    • Main thread dequeues each frame and fires OnEmotionReceived
    ///
    /// 2. CLASSIFICATION (Python / DeepFace)
    ///    • Worker thread runs DeepFace.analyze() on each incoming frame
    ///    • Returns 7 emotion scores (0-1): angry, disgust, fear, happy, sad, surprise, neutral
    ///    • Applies rolling average over last 4 results (anti-flicker)
    ///    • Face detection heuristic via --strict-detection flag
    ///
    /// 3. BRIDGE (EmotionGameBridge)
    ///    • Translates raw scores into game-facing EmotionType enum
    ///    • Exposes two MODES:
    ///
    ///      AutoInterval — CurrentEmotion updates on every incoming message.
    ///                     OnEmotionChanged fires when dominant emotion changes.
    ///                     Use for: real-time reactive feedback, HUD displays.
    ///
    ///      Window       — Silent until OpenDetectionWindow(seconds) is called.
    ///                     Accumulates time-votes per emotion during the window.
    ///                     OnWindowClosed fires once with the dominant emotion.
    ///                     Use for: timed challenges, Director de Escena rounds.
    ///
    /// 4. MINIGAME INTEGRATION PATTERN
    ///    Every IMiniGame receives EmotionGameBridge via deps.EmotionBridge in OnStart().
    ///
    ///    Minimal integration example:
    ///    ─────────────────────────────
    ///    public void OnStart(MiniGameDependencies deps)
    ///    {
    ///        _bridge = deps.EmotionBridge;
    ///        _bridge.SetEnabled(true);
    ///        _bridge.SetMode(EmotionDetectionMode.AutoInterval);
    ///        _bridge.OnEmotionChanged += HandleEmotion;
    ///    }
    ///
    ///    private void HandleEmotion(EmotionType emotion, float confidence)
    ///    {
    ///        // Called on main thread — safe to access Unity APIs
    ///        Debug.Log($"Emotion: {emotion} ({confidence:P0})");
    ///    }
    ///
    ///    Window mode example (e.g. Director de Escena):
    ///    ────────────────────────────────────────────────
    ///    _bridge.SetMode(EmotionDetectionMode.Window);
    ///    _bridge.OnWindowClosed += dominant =>
    ///    {
    ///        bool success = (dominant == _targetEmotion);
    ///    };
    ///    _bridge.OpenDetectionWindow(3f);  // 3-second reading window
    ///
    ///    Available queries (any mode, any frame):
    ///    ─────────────────────────────────────────
    ///    _bridge.CurrentEmotion               // EmotionType
    ///    _bridge.Confidence                   // float 0-1
    ///    _bridge.FaceDetected                 // bool
    ///    _bridge.IsConnected                  // bool (WebSocket)
    ///    _bridge.GetScore(EmotionType.Happy)  // float 0-1 raw score
    ///    _bridge.IsEmotionActive(EmotionType.Happy, 0.55f)  // bool
    ///    _bridge.LatestData                   // full EmotionData struct
    ///
    /// ────────────────────────────────────────────────────────
    /// </summary>
    public class EmotionStatusLogger : MonoBehaviour
    {
        [Header("Logging")]
        [Tooltip("Log every time the dominant emotion changes.")]
        [SerializeField] private bool _logEmotionChanges = true;

        [Tooltip("Log when a detection window opens or closes.")]
        [SerializeField] private bool _logWindowEvents = true;

        [Tooltip("Print a full status summary every N seconds. 0 = disabled.")]
        [SerializeField] private float _statusInterval = 5f;

        [Tooltip("Log individual raw scores in the status summary.")]
        [SerializeField] private bool _logRawScores = true;

        private EmotionGameBridge _bridge;
        private float             _statusTimer;
        private const string      TAG = "EmotionPipeline";

        private void Start()
        {
            _bridge = EmotionGameBridge.Instance;
            if (_bridge == null)
            {
                Debug.LogWarning($"[{TAG}] EmotionGameBridge.Instance is null. " +
                                 "Start from Bootstrap scene to initialize the pipeline.");
                return;
            }

            if (_logEmotionChanges)
                _bridge.OnEmotionChanged += OnEmotionChanged;

            if (_logWindowEvents)
            {
                _bridge.OnWindowClosed += OnWindowClosed;
            }

            Debug.Log($"[{TAG}] Logger attached. " +
                      $"Mode={_bridge.DetectionMode} | " +
                      $"Connected={_bridge.IsConnected} | " +
                      $"Enabled={_bridge.IsEnabled}");
        }

        private void Update()
        {
            if (_bridge == null || _statusInterval <= 0f) return;

            _statusTimer += Time.deltaTime;
            if (_statusTimer < _statusInterval) return;
            _statusTimer = 0f;
            PrintStatusSummary();
        }

        private void OnDestroy()
        {
            if (_bridge == null) return;
            _bridge.OnEmotionChanged -= OnEmotionChanged;
            _bridge.OnWindowClosed  -= OnWindowClosed;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnEmotionChanged(EmotionType emotion, float confidence)
        {
            string faceTag = _bridge.FaceDetected ? "" : " [no face]";
            Debug.Log($"[{TAG}] EMOTION CHANGED → {emotion} ({confidence:P0}){faceTag}");
        }

        private void OnWindowClosed(EmotionType dominant)
        {
            Debug.Log($"[{TAG}] WINDOW CLOSED → dominant={dominant} | " +
                      $"confidence={_bridge.Confidence:P0}");
        }

        // ── Status summary ────────────────────────────────────────────────────

        private void PrintStatusSummary()
        {
            if (_bridge == null) return;

            var d = _bridge.LatestData;

            string connStatus = _bridge.IsConnected ? "CONNECTED" : "DISCONNECTED";
            string modeStatus = _bridge.DetectionMode == EmotionDetectionMode.Window
                ? $"Window (open={_bridge.IsWindowOpen})"
                : "AutoInterval";

            Debug.Log(
                $"[{TAG}] ── Status ──────────────────────────────────\n" +
                $"  Server : {connStatus}\n" +
                $"  Mode   : {modeStatus}\n" +
                $"  Enabled: {_bridge.IsEnabled}\n" +
                $"  Face   : {(_bridge.FaceDetected ? "detected" : "NOT detected")}\n" +
                $"  Current: {_bridge.CurrentEmotion} ({_bridge.Confidence:P0})\n" +
                (_logRawScores ? FormatScores(d.scores) : "") +
                $"  ─────────────────────────────────────────────");
        }

        private static string FormatScores(EmotionScores s)
        {
            return
                $"  Scores : happy={s.happy:F2}  surprise={s.surprise:F2}  angry={s.angry:F2}\n" +
                $"           sad={s.sad:F2}    fear={s.fear:F2}     disgust={s.disgust:F2}  neutral={s.neutral:F2}\n";
        }
    }
}
