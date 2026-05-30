using System.Text;
using UnityEngine;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.EmotionDetection;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.EmotionTest
{
    /// <summary>
    /// Real-time debug display for the DeepFace emotion pipeline.
    /// Shows: connection status, dominant emotion, confidence, 7-emotion bars, history.
    /// Toggle visibility with the Emotion button in EmotionTestGame.
    /// </summary>
    public class EmotionTestDebugDisplay : MonoBehaviour
    {
        [Header("Debug Text Display")]
        [SerializeField] private TextMeshProUGUI _debugText;

        [Header("Settings")]
        [SerializeField] private bool _showDetailedScores = true;
        [SerializeField] private bool _showHistory = true;
        [SerializeField] private int  _historySize = 10;

        // Emotion → display color
        private static readonly System.Collections.Generic.Dictionary<EmotionType, string> _colors =
            new()
            {
                [EmotionType.Happy]    = "#00FF88",
                [EmotionType.Surprise] = "#FFD700",
                [EmotionType.Angry]    = "#FF4444",
                [EmotionType.Sad]      = "#00B4FF",
                [EmotionType.Fear]     = "#FF8800",
                [EmotionType.Disgust]  = "#AA44FF",
                [EmotionType.Neutral]  = "#888888",
                [EmotionType.Unknown]  = "#AAAAAA",
            };

        private MiniGameDependencies _deps;
        private EmotionGameBridge    _bridge;

        private int      _frameCount   = 0;
        private string[] _history;
        private int      _historyIdx   = 0;
        private bool     _isActive     = false;

        private readonly StringBuilder _sb = new(1024);
        private const string LogServiceName = "EmotionDebugDisplay";

        public void Initialize(MiniGameDependencies deps)
        {
            _deps    = deps;
            _bridge  = deps.EmotionBridge;
            _history = new string[_historySize];

            if (_bridge != null)
            {
                _bridge.OnEmotionChanged += OnEmotionChanged;
                ServiceLogger.Instance.LogInfo(LogServiceName, "EmotionGameBridge connected.");
            }
            else
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, "EmotionGameBridge not found in deps!");
            }
        }

        public void EnableModule(bool enable)
        {
            _isActive = enable;
            if (_debugText != null) _debugText.gameObject.SetActive(enable);
            if (!enable)
            {
                _frameCount = 0;
                if (_debugText != null)
                    _debugText.text = "<color=#888888>[EMOTION MODULE DISABLED]</color>";
            }
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Module {(enable ? "ENABLED" : "DISABLED")}");
        }

        public void Reset()
        {
            _frameCount = 0;
            System.Array.Clear(_history, 0, _history.Length);
            _historyIdx = 0;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Reset");
        }

        private void OnEmotionChanged(EmotionType emotion, float confidence)
        {
            if (!_isActive || _history == null) return;
            if (_showHistory)
            {
                _history[_historyIdx] = emotion.ToString();
                _historyIdx = (_historyIdx + 1) % _historySize;
            }
        }

        private void Update()
        {
            if (!_isActive || _debugText == null || _bridge == null) return;
            _frameCount++;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            var emotion    = _bridge.CurrentEmotion;
            var confidence = _bridge.Confidence;
            var connected  = _bridge.IsConnected;
            var faceDetected = _bridge.FaceDetected;

            _sb.Clear();
            _sb.Append("═══ DEEPFACE EMOTION DEBUG ═══\n");
            _sb.Append($"Frame: {_frameCount}  ");
            _sb.Append(connected
                ? "<color=#00FF88>● Connected</color>\n"
                : "<color=#FF4444>● Disconnected</color>\n");
            _sb.Append(faceDetected
                ? "<color=#00FF88>Face: detected</color>\n"
                : "<color=#888888>Face: not detected</color>\n");
            _sb.Append("\n");

            // Dominant emotion with color
            string color = _colors.TryGetValue(emotion, out var c) ? c : "#AAAAAA";
            _sb.Append($"Emotion: <color={color}><b>{emotion}</b></color>\n");

            // Confidence bar
            int barLen = Mathf.RoundToInt(confidence * 20f);
            _sb.Append($"Conf:    [{new string('█', barLen)}{new string('░', 20 - barLen)}] {confidence:P0}\n");

            if (_showDetailedScores && _bridge.IsConnected)
            {
                _sb.Append("\nAll scores:\n");
                AppendBar("Happy   ", _bridge.GetScore(EmotionType.Happy),   "#00FF88");
                AppendBar("Surprise", _bridge.GetScore(EmotionType.Surprise), "#FFD700");
                AppendBar("Angry   ", _bridge.GetScore(EmotionType.Angry),   "#FF4444");
                AppendBar("Sad     ", _bridge.GetScore(EmotionType.Sad),     "#00B4FF");
                AppendBar("Fear    ", _bridge.GetScore(EmotionType.Fear),    "#FF8800");
                AppendBar("Disgust ", _bridge.GetScore(EmotionType.Disgust), "#AA44FF");
                AppendBar("Neutral ", _bridge.GetScore(EmotionType.Neutral), "#888888");
            }

            if (_showHistory && _history != null)
            {
                _sb.Append("\nHistory:\n  ");
                int shown = 0;
                for (int i = 0; i < _historySize; i++)
                {
                    if (!string.IsNullOrEmpty(_history[i]))
                    {
                        _sb.Append(_history[i]);
                        shown++;
                        if (shown % 5 == 0) _sb.Append("\n  ");
                        else _sb.Append(" • ");
                    }
                }
            }

            _sb.Append("\n\nControls:\n  Toggle: Button\n  Reset: Button\n  Exit: Button / ESC");
            _debugText.text = _sb.ToString();
        }

        private void AppendBar(string label, float value, string color)
        {
            int filled = Mathf.RoundToInt(value * 10f);
            filled = Mathf.Clamp(filled, 0, 10);
            _sb.Append($"  {label} <color={color}>[{new string('█', filled)}{new string('░', 10 - filled)}]</color> {value:F2}\n");
        }

        private void OnDestroy()
        {
            if (_bridge != null)
                _bridge.OnEmotionChanged -= OnEmotionChanged;
        }
    }
}
