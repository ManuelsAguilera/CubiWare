using System;
using UnityEngine;
using TMPro;
using ARcadeRush.Face;

namespace ARcadeRush.UI
{
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private TMP_Text _timerText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _emotionLabelText;

        public void UpdateTimer(float seconds)
        {
            if (_timerText != null)
            {
                _timerText.text = TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
            }
        }

        public void UpdateScore(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"Score: {score}";
            }
        }

        public void ShowEmotion(EmotionLabel label)
        {
            if (_emotionLabelText == null) return;

            _emotionLabelText.text = label.ToString();

            switch (label)
            {
                case EmotionLabel.Happy:
                    _emotionLabelText.color = Color.green;
                    break;
                case EmotionLabel.Surprised:
                    _emotionLabelText.color = Color.blue;
                    break;
                case EmotionLabel.Angry:
                    _emotionLabelText.color = Color.red;
                    break;
                default:
                    _emotionLabelText.color = Color.gray;
                    break;
            }
        }
    }
}
