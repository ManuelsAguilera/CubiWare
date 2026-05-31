using UnityEngine;
using TMPro;
using ARcadeRush.Hand;
using ARcadeRush.EmotionDetection;

namespace ARcadeRush.UI
{
    /// <summary>
    /// Debug overlay showing gesture and emotion state.
    /// Emotion now sourced from EmotionGameBridge (DeepFace).
    /// </summary>
    public class DebugTrackerUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text        _debugText;
        [SerializeField] private GestureDetector _gestureDetector;

        private string _lastGesture = "None";
        private string _lastEmotion = "Unknown";

        private void OnEnable()
        {
            if (_gestureDetector != null)
            {
                _gestureDetector.OnOpenHand    += HandleOpenHand;
                _gestureDetector.OnClosedFist  += HandleClosedFist;
                _gestureDetector.OnPoint       += HandlePoint;
                _gestureDetector.OnPinch       += HandlePinch;
                _gestureDetector.OnPinchRelease += HandlePinchRelease;
            }

            if (EmotionGameBridge.Instance != null)
                EmotionGameBridge.Instance.OnEmotionChanged += HandleEmotionChanged;
        }

        private void OnDisable()
        {
            if (_gestureDetector != null)
            {
                _gestureDetector.OnOpenHand    -= HandleOpenHand;
                _gestureDetector.OnClosedFist  -= HandleClosedFist;
                _gestureDetector.OnPoint       -= HandlePoint;
                _gestureDetector.OnPinch       -= HandlePinch;
                _gestureDetector.OnPinchRelease -= HandlePinchRelease;
            }

            if (EmotionGameBridge.Instance != null)
                EmotionGameBridge.Instance.OnEmotionChanged -= HandleEmotionChanged;
        }

        private void HandleOpenHand()       => UpdateGesture("OpenHand");
        private void HandleClosedFist()     => UpdateGesture("ClosedFist");
        private void HandlePoint()          => UpdateGesture("Point");
        private void HandlePinch()          => UpdateGesture("Pinch");
        private void HandlePinchRelease()   => UpdateGesture("Pinch Released");

        private void HandleEmotionChanged(EmotionType emotion, float confidence)
            => UpdateEmotion($"{emotion} ({confidence:P0})");

        private void UpdateGesture(string gesture) { _lastGesture = gesture; RefreshText(); }
        private void UpdateEmotion(string emotion)  { _lastEmotion = emotion; RefreshText(); }

        private void RefreshText()
        {
            if (_debugText != null)
                _debugText.text = $"<color=green>Gesture:</color> {_lastGesture}\n<color=#00B4FF>Emotion:</color> {_lastEmotion}";
        }
    }
}
