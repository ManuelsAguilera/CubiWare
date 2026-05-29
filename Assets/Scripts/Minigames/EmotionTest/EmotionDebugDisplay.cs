using System.Text;
using UnityEngine;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.EmotionTest
{
    /// <summary>
    /// Displays real-time emotion detection debug information.
    /// Shows: current emotion, metrics, frame count, detection history.
    /// Can be toggled on/off via button.
    /// </summary>
    public class EmotionTestDebugDisplay : MonoBehaviour
    {
        [Header("Debug Text Display")]
        [SerializeField] private TextMeshProUGUI _debugText;

        [Header("Settings")]
        [SerializeField] private bool _showDetailedMetrics = true;
        [SerializeField] private bool _showHistory = true;
        [SerializeField] private int _historySize = 10;

        private MiniGameDependencies _deps;
        private EmotionClassifier _emotionClassifier;
        private FaceLandmarkReader _faceLandmarkReader;

        private EmotionLabel _currentEmotion = EmotionLabel.Neutral;
        private int _frameCount = 0;
        private string[] _emotionHistory;
        private int _historyIndex = 0;
        private bool _isActive = false;

        private readonly StringBuilder _sb = new StringBuilder(1024);
        private const string LogServiceName = "EmotionDebugDisplay";

        public void Initialize(MiniGameDependencies deps)
        {
            _deps = deps;
            _emotionHistory = new string[_historySize];

            _emotionClassifier = FindFirstObjectByType<EmotionClassifier>();
            _faceLandmarkReader = FindFirstObjectByType<FaceLandmarkReader>();

            if (_emotionClassifier != null)
            {
                _emotionClassifier.OnEmotionChanged += OnEmotionChanged;
                ServiceLogger.Instance.LogInfo(LogServiceName, "EmotionClassifier found and subscribed");
            }
            else
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, "EmotionClassifier not found in scene!");
            }

            if (_faceLandmarkReader == null)
                ServiceLogger.Instance.LogWarning(LogServiceName, "FaceLandmarkReader not found in scene!");

            ServiceLogger.Instance.LogInfo(LogServiceName, "Initialized");
        }

        public void EnableModule(bool enable)
        {
            _isActive = enable;

            if (_debugText != null)
                _debugText.gameObject.SetActive(enable);

            if (!enable)
            {
                _frameCount = 0;
                if (_debugText != null)
                    _debugText.text = "[EMOTION MODULE DISABLED]";
            }

            ServiceLogger.Instance.LogInfo(LogServiceName, $"Module {(enable ? "ENABLED" : "DISABLED")}");
        }

        public void Reset()
        {
            _frameCount = 0;
            _currentEmotion = EmotionLabel.Neutral;
            System.Array.Clear(_emotionHistory, 0, _emotionHistory.Length);
            _historyIndex = 0;
            ServiceLogger.Instance.LogInfo(LogServiceName, "Reset");
        }

        private void OnEmotionChanged(EmotionLabel emotion)
        {
            if (!_isActive) return;

            _currentEmotion = emotion;

            if (_showHistory)
            {
                _emotionHistory[_historyIndex] = emotion.ToString();
                _historyIndex = (_historyIndex + 1) % _historySize;
            }

            ServiceLogger.Instance.LogInfo(LogServiceName, $"Emotion changed to: {emotion}");
        }

        private void Update()
        {
            if (!_isActive) return;
            _frameCount++;
            UpdateDebugDisplay();
        }

        private void UpdateDebugDisplay()
        {
            if (_debugText == null) return;

            _sb.Clear();
            _sb.Append("═══ EMOTION DETECTOR DEBUG ═══\n")
               .Append("Frame: ").Append(_frameCount).Append("\n\n")
               .Append("Detected Emotion: <b>").Append(_currentEmotion).Append("</b>")
               .Append(GetEmotionColorTag(_currentEmotion));

            if (_showDetailedMetrics && _faceLandmarkReader != null && _emotionClassifier != null)
            {
                float[] raw = _faceLandmarkReader.NormalizedMetrics;
                if (raw != null && raw.Length >= 10)
                {
                    _sb.Append("\nMetrics (Raw EMA | As-used by Classifier):\n");
                    AppendMetric("Mouth Open", raw[0], _faceLandmarkReader.GetRelativeMetric(0, 0.5f));
                    AppendMetric("Smile ratio",raw[4], _faceLandmarkReader.GetRatioMetric(4));
                    AppendMetric("Eye L     ", raw[1], _faceLandmarkReader.GetRelativeMetric(1));
                    AppendMetric("Eye R     ", raw[2], _faceLandmarkReader.GetRelativeMetric(2));
                    AppendMetric("Brow Raise", raw[3], _faceLandmarkReader.GetRelativeMetric(3, 0.5f));
                    AppendMetric("Furrow    ", raw[5], _faceLandmarkReader.GetRelativeMetric(5));
                    AppendMetric("Frown (n) ", raw[6], _faceLandmarkReader.GetRelativeMetric(6));
                    AppendMetric("Press     ", raw[7], _faceLandmarkReader.GetRelativeMetric(7));
                    AppendMetric("Funnel (O)", raw[8], _faceLandmarkReader.GetRelativeMetric(8, 0.5f));
                    AppendMetric("Squint    ", raw[9], _faceLandmarkReader.GetRelativeMetric(9));
                    AppendMetric("Blink avg ", _faceLandmarkReader.BlinkAverage, 0f);

                    _sb.Append("\nConfidence Scores (EMA Race):\n");
                    AppendScore("Neutral  ", _emotionClassifier.GetConfidence(EmotionLabel.Neutral));
                    AppendScore("Happy    ", _emotionClassifier.GetConfidence(EmotionLabel.Happy));
                    AppendScore("Surprised", _emotionClassifier.GetConfidence(EmotionLabel.Surprised));
                    AppendScore("Angry    ", _emotionClassifier.GetConfidence(EmotionLabel.Angry));
                }
            }

            if (_showHistory)
            {
                _sb.Append("\nHistory (last 10):\n");
                for (int i = 0; i < _historySize; i++)
                {
                    if (!string.IsNullOrEmpty(_emotionHistory[i]))
                    {
                        _sb.Append("  ").Append(_emotionHistory[i]);
                        if ((i + 1) % 5 == 0) _sb.Append('\n');
                    }
                }
            }

            _sb.Append("\nControls:\n")
               .Append("  Toggle: Button / E Key\n")
               .Append("  Reset: Button / R Key\n")
               .Append("  Exit: Button / ESC Key\n");

            _debugText.text = _sb.ToString();
        }

        private void AppendMetric(string name, float raw, float rel)
        {
            _sb.Append("  ").Append(name).Append(": ")
               .Append(raw.ToString("F3")).Append(" | ")
               .Append(rel.ToString("F3")).Append('\n');
        }

        private void AppendScore(string name, float score)
        {
            _sb.Append("  ").Append(name).Append(": ")
               .Append(score.ToString("F3")).Append('\n');
        }

        private string GetEmotionColorTag(EmotionLabel emotion)
        {
            return emotion switch
            {
                EmotionLabel.Happy     => "\n<color=#27C464>✓ HAPPY (Green)</color>",
                EmotionLabel.Surprised => "\n<color=#0096C8>✓ SURPRISED (Blue)</color>",
                EmotionLabel.Angry     => "\n<color=#E24B4A>✓ ANGRY (Red)</color>",
                _                      => "\n<color=#808080>○ NEUTRAL (Gray)</color>"
            };
        }

        private void OnDestroy()
        {
            if (_emotionClassifier != null)
                _emotionClassifier.OnEmotionChanged -= OnEmotionChanged;
        }
    }
}
