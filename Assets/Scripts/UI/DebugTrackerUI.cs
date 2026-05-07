using UnityEngine;
using TMPro;
using ARcadeRush.Hand;
using ARcadeRush.Face;

namespace ARcadeRush.UI
{
    public class DebugTrackerUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _debugText;
        [SerializeField] private GestureDetector _gestureDetector;
        [SerializeField] private EmotionClassifier _emotionClassifier;

        private string _lastGesture = "None";
        private string _lastEmotion = "Neutral";

        private void OnEnable()
        {
            if (_gestureDetector != null)
            {
                _gestureDetector.OnOpenHand += HandleOpenHand;
                _gestureDetector.OnClosedFist += HandleClosedFist;
                _gestureDetector.OnPoint += HandlePoint;
                _gestureDetector.OnPinch += HandlePinch;
                _gestureDetector.OnPinchRelease += HandlePinchRelease;
            }

            if (_emotionClassifier != null)
            {
                _emotionClassifier.OnEmotionChanged += HandleEmotionChanged;
            }
        }

        private void OnDisable()
        {
            if (_gestureDetector != null)
            {
                _gestureDetector.OnOpenHand -= HandleOpenHand;
                _gestureDetector.OnClosedFist -= HandleClosedFist;
                _gestureDetector.OnPoint -= HandlePoint;
                _gestureDetector.OnPinch -= HandlePinch;
                _gestureDetector.OnPinchRelease -= HandlePinchRelease;
            }

            if (_emotionClassifier != null)
            {
                _emotionClassifier.OnEmotionChanged -= HandleEmotionChanged;
            }
        }

        private void HandleOpenHand() => UpdateGesture("OpenHand");
        private void HandleClosedFist() => UpdateGesture("ClosedFist");
        private void HandlePoint() => UpdateGesture("Point");
        private void HandlePinch() => UpdateGesture("Pinch");
        private void HandlePinchRelease() => UpdateGesture("Pinch Released");
        private void HandleEmotionChanged(EmotionLabel emotion) => UpdateEmotion(emotion.ToString());

        private void UpdateGesture(string gesture)
        {
            _lastGesture = gesture;
            RefreshText();
        }

        private void UpdateEmotion(string emotion)
        {
            _lastEmotion = emotion;
            RefreshText();
        }

        private void RefreshText()
        {
            if (_debugText != null)
            {
                _debugText.text = $"<color=green>Gesture:</color> {_lastGesture}\n<color=blue>Emotion:</color> {_lastEmotion}";
            }
        }
    }
}
