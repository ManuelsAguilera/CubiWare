using System;
using UnityEngine;

namespace ARcadeRush.Face
{
    public enum EmotionLabel { Neutral, Happy, Surprised, Angry }

    [RequireComponent(typeof(FaceLandmarkReader))]
    public class EmotionClassifier : MonoBehaviour
    {
        public event Action<EmotionLabel> OnEmotionChanged;

        private FaceLandmarkReader _reader;
        private EmotionLabel _currentEmotion = EmotionLabel.Neutral;

        private EmotionLabel _candidateEmotion = EmotionLabel.Neutral;
        private int _framesHeld = 0;
        private const int REQUIRED_FRAMES = 8;

        private void Awake()
        {
            _reader = GetComponent<FaceLandmarkReader>();
        }

        private void Update()
        {
            float[] metrics = _reader.NormalizedMetrics;
            
            float mouthOpenness = metrics[0];
            float eyeAvg = (metrics[1] + metrics[2]) / 2f;
            float browRaise = metrics[3];

            EmotionLabel detected = EmotionLabel.Neutral;

            if (mouthOpenness > 0.08f && browRaise < 0.1f)
            {
                detected = EmotionLabel.Happy;
            }
            else if (mouthOpenness > 0.12f && eyeAvg > 0.35f && browRaise > 0.15f)
            {
                detected = EmotionLabel.Surprised;
            }
            else if (mouthOpenness < 0.04f && browRaise < 0.05f && eyeAvg < 0.25f)
            {
                detected = EmotionLabel.Angry;
            }

            // Temporal Smoothing
            if (detected == _candidateEmotion)
            {
                _framesHeld++;
                if (_framesHeld >= REQUIRED_FRAMES && _currentEmotion != detected)
                {
                    _currentEmotion = detected;
                    OnEmotionChanged?.Invoke(_currentEmotion);
                }
            }
            else
            {
                _candidateEmotion = detected;
                _framesHeld = 1;
            }
        }
    }
}
