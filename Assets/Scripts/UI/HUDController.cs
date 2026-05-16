using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Face;

namespace ARcadeRush.UI
{
    public class HUDController : MonoBehaviour
    {
        [Header("HUD Panel")]
        [SerializeField] private GameObject _hudPanel;

        [SerializeField] private TMP_Text _timerText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _emotionLabelText;

        [Header("Music")]
        [SerializeField] private TMP_Text _musicLabelText;

        [Header("Ammo / Reload")]
        [SerializeField] private TMP_Text _ammoText;
        [SerializeField] private GameObject _reloadingIndicator;

        [Header("Wave Announcement")]
        [SerializeField] private TMP_Text _waveAnnouncementText;
        [SerializeField] private float _waveAnnouncementHoldDuration = 1.5f;
        [SerializeField] private float _waveAnnouncementFadeDuration = 0.5f;

        [Header("Score Shake")]
        [SerializeField] private float _shakeIntensity = 6f;
        [SerializeField] private float _shakeDuration = 0.15f;

        [Header("Start Menu")]
        [SerializeField] private GameObject _startMenuPanel;
        [SerializeField] private TMP_Text _startMenuTitleText;
        [SerializeField] private TMP_Text _startMenuScoreText;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _mainMenuButton;

        /// <summary>Fired when the player clicks the start button.</summary>
        public Action OnStartClicked;

        /// <summary>Fired when the player clicks the main menu button.</summary>
        public Action OnMainMenuClicked;

        [Header("Pause Overlay")]
        [SerializeField] private GameObject _pauseOverlay;
        [SerializeField] private TMP_Text _pauseText;

        private Coroutine _waveAnnouncementCo;
        private Coroutine _scoreShakeCo;
        private Vector3 _scoreTextOriginalAnchoredPos;

        // --- Unity Lifecycle ---

        private void Awake()
        {
            if (_scoreText != null)
            {
                _scoreTextOriginalAnchoredPos = _scoreText.rectTransform.anchoredPosition;
            }

            // Ensure all panels and HUD start hidden.
            // ShooterGame.Start() will show the correct UI for the current state.
            if (_pauseOverlay != null)
            {
                _pauseOverlay.SetActive(false);
            }

            if (_startMenuPanel != null)
            {
                _startMenuPanel.SetActive(false);
            }

            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(false);
            }

            if (_mainMenuButton != null)
            {
                _mainMenuButton.gameObject.SetActive(false);
            }

            // Hide all HUD elements at init — ShooterGame or BeginGame toggles them.
            SetHUDVisible(false);
        }

        // --- Timer ---

        public void UpdateTimer(float seconds)
        {
            if (_timerText != null)
            {
                _timerText.text = TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
            }
        }

        // --- Score ---

        public void UpdateScore(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"Score: {score}";
            }
        }

        /// <summary>Update score text and trigger the shake effect.</summary>
        public void UpdateScoreWithShake(int score)
        {
            UpdateScore(score);
            PlayScoreShake();
        }

        // --- Ammo / Reload ---

        /// <summary>Update the ammo display (e.g. "5 / 6").</summary>
        public void UpdateAmmo(int current, int max)
        {
            if (_ammoText != null)
            {
                _ammoText.text = $"{current} / {max}";
            }
        }

        /// <summary>Show or hide the reloading indicator.</summary>
        public void ShowReloading(bool isReloading)
        {
            if (_reloadingIndicator != null)
            {
                _reloadingIndicator.SetActive(isReloading);
            }
        }

        // --- Music Label ---

        /// <summary>Display the currently active music track name.</summary>
        public void UpdateMusicLabel(string label)
        {
            if (_musicLabelText != null)
            {
                _musicLabelText.text = $"Music: {label}";
            }
        }

        // --- Visibility ---

        /// <summary>
        /// Show or hide the HUD panel (and all its children).
        /// When _hudPanel is assigned, the parent panel is toggled directly.
        /// Otherwise falls back to toggling individual elements.
        /// </summary>
        public void SetHUDVisible(bool visible)
        {
            if (_hudPanel != null)
            {
                _hudPanel.SetActive(visible);
                return;
            }

            // Fallback: toggle individual elements if no panel reference
            if (_timerText != null) _timerText.gameObject.SetActive(visible);
            if (_scoreText != null) _scoreText.gameObject.SetActive(visible);
            if (_ammoText != null) _ammoText.gameObject.SetActive(visible);
            if (_reloadingIndicator != null) _reloadingIndicator.SetActive(visible);
            if (_musicLabelText != null) _musicLabelText.gameObject.SetActive(visible);
            if (_emotionLabelText != null) _emotionLabelText.gameObject.SetActive(visible);
            if (_waveAnnouncementText != null) _waveAnnouncementText.gameObject.SetActive(visible);
        }

        /// <summary>Show the pause overlay with a custom message.</summary>
        public void ShowPauseOverlay(string message)
        {
            if (_pauseOverlay != null) _pauseOverlay.SetActive(true);
            if (_pauseText != null) _pauseText.text = message;
        }

        /// <summary>Hide the pause overlay.</summary>
        public void HidePauseOverlay()
        {
            if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
        }

        // --- Start Menu ---

        /// <summary>
        /// Show the start menu / game over panel.
        /// If <paramref name="lastScore"/> is null, the score display is hidden.
        /// </summary>
        public void ShowStartMenu(string title, int? lastScore, string promptMessage)
        {
            if (_startMenuPanel != null)
            {
                _startMenuPanel.SetActive(true);
            }

            if (_startMenuTitleText != null)
            {
                _startMenuTitleText.text = title;
            }

            if (_startMenuScoreText != null)
            {
                if (lastScore.HasValue)
                {
                    _startMenuScoreText.text = $"Last Score: {lastScore.Value}";
                    _startMenuScoreText.gameObject.SetActive(true);
                }
                else
                {
                    _startMenuScoreText.gameObject.SetActive(false);
                }
            }

            // Wire the start button to fire OnStartClicked
            if (_startButton != null)
            {
                _startButton.onClick.RemoveAllListeners();
                _startButton.onClick.AddListener(() => OnStartClicked?.Invoke());
                _startButton.gameObject.SetActive(true);
            }

            // Wire the main menu button to fire OnMainMenuClicked
            if (_mainMenuButton != null)
            {
                _mainMenuButton.onClick.RemoveAllListeners();
                _mainMenuButton.onClick.AddListener(() => OnMainMenuClicked?.Invoke());
                _mainMenuButton.gameObject.SetActive(true);
            }
        }

        /// <summary>Hide the start menu panel, pause overlay, and buttons.</summary>
        public void HideStartMenu()
        {
            if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
            if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
            if (_startButton != null)
            {
                _startButton.onClick.RemoveAllListeners();
                _startButton.gameObject.SetActive(false);
            }
            if (_mainMenuButton != null)
            {
                _mainMenuButton.onClick.RemoveAllListeners();
                _mainMenuButton.gameObject.SetActive(false);
            }
        }

        // --- Emotion ---

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

        // --- Wave Announcement ---

        /// <summary>
        /// Display a wave name announcement (e.g. "Wave: Easy") that holds briefly then fades out.
        /// </summary>
        public void ShowWave(string waveName)
        {
            if (_waveAnnouncementText == null) return;

            if (_waveAnnouncementCo != null)
            {
                StopCoroutine(_waveAnnouncementCo);
            }

            _waveAnnouncementCo = StartCoroutine(CoShowWave(waveName));
        }

        private IEnumerator CoShowWave(string waveName)
        {
            _waveAnnouncementText.text = $"Wave: {waveName}";
            _waveAnnouncementText.alpha = 1f;

            // Hold fully visible
            yield return new WaitForSeconds(_waveAnnouncementHoldDuration);

            // Fade out
            float elapsed = 0f;
            while (elapsed < _waveAnnouncementFadeDuration)
            {
                elapsed += Time.deltaTime;
                _waveAnnouncementText.alpha = Mathf.Lerp(1f, 0f, elapsed / _waveAnnouncementFadeDuration);
                yield return null;
            }

            _waveAnnouncementText.alpha = 0f;
            _waveAnnouncementCo = null;
        }

        // --- Score Shake ---

        /// <summary>Trigger a brief shake animation on the score text.</summary>
        public void PlayScoreShake()
        {
            if (_scoreText == null) return;

            if (_scoreShakeCo != null)
            {
                StopCoroutine(_scoreShakeCo);
                // Reset position immediately to avoid drift
                _scoreText.rectTransform.anchoredPosition = _scoreTextOriginalAnchoredPos;
            }

            _scoreShakeCo = StartCoroutine(CoScoreShake());
        }

        private IEnumerator CoScoreShake()
        {
            RectTransform rect = _scoreText.rectTransform;
            Vector3 originalPos = _scoreTextOriginalAnchoredPos;
            float elapsed = 0f;

            while (elapsed < _shakeDuration)
            {
                elapsed += Time.deltaTime;
                float offsetX = UnityEngine.Random.Range(-_shakeIntensity, _shakeIntensity);
                float offsetY = UnityEngine.Random.Range(-_shakeIntensity, _shakeIntensity);
                rect.anchoredPosition = originalPos + new Vector3(offsetX, offsetY, 0f);
                yield return null;
            }

            rect.anchoredPosition = originalPos;
            _scoreShakeCo = null;
        }


    }
}
