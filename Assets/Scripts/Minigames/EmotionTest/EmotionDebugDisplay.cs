using UnityEngine;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.Face;

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
        [SerializeField] private TextMeshProUGUI _emotionLabelText;

        [Header("Settings")]
        [SerializeField] private bool _showDetailedMetrics = true;
        [SerializeField] private bool _showHistory = true;
        [SerializeField] private int _historySize = 10;

        private MiniGameDependencies _deps;
        private EmotionClassifier _emotionClassifier;
        private FaceLandmarkReader _faceLandmarkReader;

        private EmotionLabel _currentEmotion = EmotionLabel.Neutral;
        private float[] _lastMetrics = new float[4];
        private int _frameCount = 0;
        private string[] _emotionHistory;
        private int _historyIndex = 0;

        private bool _isActive = false;
        private Color _colorHappy = new Color32(39, 196, 100, 255);
        private Color _colorSurprised = new Color32(0, 150, 200, 255);
        private Color _colorAngry = new Color32(226, 75, 74, 255);
        private Color _colorNeutral = new Color32(128, 128, 128, 255);

        public void Initialize(MiniGameDependencies deps)
        {
            _deps = deps;
            _emotionHistory = new string[_historySize];

            // Find EmotionClassifier in scene
            _emotionClassifier = FindObjectOfType<EmotionClassifier>();
            _faceLandmarkReader = FindObjectOfType<FaceLandmarkReader>();

            if (_emotionClassifier != null)
            {
                _emotionClassifier.OnEmotionChanged += OnEmotionChanged;
                Debug.Log("[EmotionDebugDisplay] EmotionClassifier found and subscribed");
            }
            else
            {
                Debug.LogWarning("[EmotionDebugDisplay] EmotionClassifier not found in scene!");
            }

            if (_faceLandmarkReader == null)
            {
                Debug.LogWarning("[EmotionDebugDisplay] FaceLandmarkReader not found in scene!");
            }

            Debug.Log("[EmotionDebugDisplay] Initialized");
        }

        public void EnableModule(bool enable)
        {
            _isActive = enable;

            if (_debugText != null)
            {
                _debugText.gameObject.SetActive(enable);
            }

            if (_emotionLabelText != null)
            {
                _emotionLabelText.gameObject.SetActive(enable);
            }

            if (!enable)
            {
                _frameCount = 0;
                if (_debugText != null)
                {
                    _debugText.text = "[EMOTION MODULE DISABLED]";
                }
                if (_emotionLabelText != null)
                {
                    _emotionLabelText.text = "---";
                }
            }

            Debug.Log($"[EmotionDebugDisplay] Module {(enable ? "ENABLED" : "DISABLED")}");
        }

        public void Reset()
        {
            _frameCount = 0;
            _currentEmotion = EmotionLabel.Neutral;
            System.Array.Clear(_emotionHistory, 0, _emotionHistory.Length);
            _historyIndex = 0;

            Debug.Log("[EmotionDebugDisplay] Reset");
        }

        private void OnEmotionChanged(EmotionLabel emotion)
        {
            if (!_isActive) return;

            _currentEmotion = emotion;

            // Add to history
            if (_showHistory)
            {
                _emotionHistory[_historyIndex] = emotion.ToString();
                _historyIndex = (_historyIndex + 1) % _historySize;
            }

            Debug.Log($"[EmotionDebugDisplay] Emotion changed to: {emotion}");
        }

        private void Update()
        {
            if (!_isActive) return;

            _frameCount++;

            // Update debug display
            UpdateDebugDisplay();
        }

        private void UpdateDebugDisplay()
        {
            if (_debugText == null) return;

            string displayText = "";

            // Header
            displayText += "═══ EMOTION DETECTOR DEBUG ═══\n";
            displayText += $"Frame: {_frameCount}\n\n";

            // Current emotion
            displayText += $"Detected Emotion: <b>{_currentEmotion}</b>\n";
            displayText += GetEmotionColorTag(_currentEmotion);

            // Metrics if available
            if (_showDetailedMetrics && _faceLandmarkReader != null)
            {
                float[] metrics = _faceLandmarkReader.NormalizedMetrics;
                if (metrics != null && metrics.Length >= 4)
                {
                    _lastMetrics = metrics;

                    displayText += "\n";
                    displayText += "Metrics:\n";
                    displayText += $"  Mouth Openness:  {metrics[0]:F3}\n";
                    displayText += $"  Left Eye Open:   {metrics[1]:F3}\n";
                    displayText += $"  Right Eye Open:  {metrics[2]:F3}\n";
                    displayText += $"  Brow Raise:      {metrics[3]:F3}\n";

                    // Show thresholds
                    displayText += "\n";
                    displayText += "Thresholds:\n";
                    displayText += $"  Happy:      mouth > 0.08 && brow < 0.1\n";
                    displayText += $"  Surprised:  mouth > 0.12 && eyes > 0.35 && brow > 0.15\n";
                    displayText += $"  Angry:      mouth < 0.04 && brow < 0.05 && eyes < 0.25\n";
                }
            }

            // History
            if (_showHistory)
            {
                displayText += "\n";
                displayText += "History (last 10):\n";
                for (int i = 0; i < _historySize; i++)
                {
                    if (!string.IsNullOrEmpty(_emotionHistory[i]))
                    {
                        displayText += $"  {_emotionHistory[i]} ";
                        if ((i + 1) % 5 == 0) displayText += "\n";
                    }
                }
            }

            // Instructions
            displayText += "\n";
            displayText += "Controls:\n";
            displayText += "  Toggle: Button / E Key\n";
            displayText += "  Reset: Button / R Key\n";
            displayText += "  Exit: Button / ESC Key\n";

            _debugText.text = displayText;

            // Update emotion label (big centered text)
            if (_emotionLabelText != null)
            {
                _emotionLabelText.text = _currentEmotion.ToString().ToUpper();
                _emotionLabelText.color = GetEmotionColor(_currentEmotion);
            }
        }

        private string GetEmotionColorTag(EmotionLabel emotion)
        {
            return emotion switch
            {
                EmotionLabel.Happy => "\n<color=#27C464>✓ HAPPY (Green)</color>",
                EmotionLabel.Surprised => "\n<color=#0096C8>✓ SURPRISED (Blue)</color>",
                EmotionLabel.Angry => "\n<color=#E24B4A>✓ ANGRY (Red)</color>",
                _ => "\n<color=#808080>○ NEUTRAL (Gray)</color>"
            };
        }

        private Color GetEmotionColor(EmotionLabel emotion)
        {
            return emotion switch
            {
                EmotionLabel.Happy => _colorHappy,
                EmotionLabel.Surprised => _colorSurprised,
                EmotionLabel.Angry => _colorAngry,
                _ => _colorNeutral
            };
        }

        private void OnDestroy()
        {
            if (_emotionClassifier != null)
            {
                _emotionClassifier.OnEmotionChanged -= OnEmotionChanged;
            }
        }
    }
}
