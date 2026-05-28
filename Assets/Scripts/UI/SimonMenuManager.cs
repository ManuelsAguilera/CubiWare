using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ARcadeRush.UI
{
    /// <summary>
    /// Centralized UI state manager for the Simón Dice minigame.
    /// Knows the current SimonMenuState and activates/deactivates the correct
    /// UI panels/canvases. Acts as a bridge between SimonGame (logic) and
    /// SimonHUDController (content).
    ///
    /// UI Hierarchy:
    /// Canvas_Simon
    /// ├── StartMenuPanel
    /// │   ├── TitleText ("SIMÓN DICE")
    /// │   ├── StartButton
    /// │   └── MainMenuButton
    /// ├── CountdownPanel
    /// │   └── CountdownText ("3", "2", "1", "¡YA!")
    /// ├── GameplayHUD
    /// │   ├── DialoguePanel
    /// │   │   ├── DialogueBackground (Image — speech bubble style)
    /// │   │   └── DialogueText (TMP_Text — Simon's words)
    /// │   ├── RoundCounter (TMP_Text — "Ronda 3 / 5")
    /// │   ├── TimerBar (Image fill)
    /// │   └── TimerText (TMP_Text — "4.2s")
    /// ├── FeedbackPanel
    /// │   ├── CheckmarkCard (GameObject — green ✓)
    /// │   └── CrossCard (GameObject — red ✗)
    /// ├── PausePanel
    /// │   ├── PauseText ("PAUSA")
    /// │   ├── ResumeButton
    /// │   └── MainMenuButton
    /// ├── VictoryPanel
    /// │   ├── VictoryText ("¡GANASTE!")
    /// │   ├── StatsText ("5/5 rondas")
    /// │   └── RestartButton
    /// └── GameOverPanel
    ///     ├── GameOverText ("¡PERDISTE!")
    ///     ├── ReasonText ("Hiciste un gesto cuando no debías")
    ///     └── RestartButton
    /// </summary>
    public class SimonMenuManager : MonoBehaviour
    {
        // ── Serialized Fields ──────────────────────────────────────────

        [Header("Panels")]
        [SerializeField] private GameObject _startMenuPanel;
        [SerializeField] private GameObject _countdownPanel;
        [SerializeField] private GameObject _gameplayHUDPanel;
        [SerializeField] private GameObject _feedbackPanel;
        [SerializeField] private GameObject _pausePanel;
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private GameObject _gameOverPanel;

        [Header("Feedback Cards")]
        [SerializeField] private GameObject _checkmarkCard;
        [SerializeField] private GameObject _crossCard;

        [Header("Start Menu")]
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _mainMenuButton_Start;

        [Header("Pause")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _mainMenuButton_Pause;

        [Header("Game Over")]
        [SerializeField] private TMP_Text _gameOverReasonText;
        [SerializeField] private Button _restartButton_GameOver;

        [Header("Victory")]
        [SerializeField] private TMP_Text _victoryStatsText;
        [SerializeField] private Button _restartButton_Victory;

        [Header("Countdown")]
        [SerializeField] private TMP_Text _countdownText;

        [Header("HUD Reference")]
        [SerializeField] private SimonHUDController _hudController;

        // ── Events (forwarded to SimonGame) ─────────────────────────────

        public event Action OnStartClicked;
        public event Action OnMainMenuClicked;
        public event Action OnResumeClicked;
        public event Action OnRestartClicked;

        // ── Public API ───────────────────────────────────────────────────

        public SimonMenuState CurrentState { get; private set; } = SimonMenuState.StartMenu;

        /// <summary>
        /// Transition to a new menu state. Deactivates all panels, then activates
        /// only the panel relevant to the new state.
        /// </summary>
        public void SetState(SimonMenuState newState)
        {
            CurrentState = newState;
            DeactivateAllPanels();

            switch (newState)
            {
                case SimonMenuState.StartMenu:
                    if (_startMenuPanel != null) _startMenuPanel.SetActive(true);
                    break;
                case SimonMenuState.Countdown:
                    if (_countdownPanel != null) _countdownPanel.SetActive(true);
                    break;
                case SimonMenuState.Playing:
                    if (_gameplayHUDPanel != null) _gameplayHUDPanel.SetActive(true);
                    break;
                case SimonMenuState.Feedback:
                    if (_feedbackPanel != null) _feedbackPanel.SetActive(true);
                    // Also keep HUD visible during feedback so player sees context
                    if (_gameplayHUDPanel != null) _gameplayHUDPanel.SetActive(true);
                    break;
                case SimonMenuState.Paused:
                    if (_gameplayHUDPanel != null) _gameplayHUDPanel.SetActive(true);
                    if (_pausePanel != null) _pausePanel.SetActive(true);
                    break;
                case SimonMenuState.Victory:
                    if (_victoryPanel != null) _victoryPanel.SetActive(true);
                    break;
                case SimonMenuState.GameOver:
                    if (_gameOverPanel != null) _gameOverPanel.SetActive(true);
                    break;
            }
        }

        /// <summary>
        /// Runs the countdown sequence (3, 2, 1, ¡YA!) and invokes onComplete when done.
        /// </summary>
        public IEnumerator RunCountdown(Action onComplete)
        {
            SetState(SimonMenuState.Countdown);

            string[] steps = { "3", "2", "1", "¡YA!" };
            float stepDuration = 0.8f;

            for (int i = 0; i < steps.Length; i++)
            {
                if (_countdownText != null)
                    _countdownText.text = steps[i];

                yield return new WaitForSeconds(stepDuration);
            }

            onComplete?.Invoke();
        }

        /// <summary>
        /// Shows the correct/wrong feedback card for a given duration.
        /// </summary>
        public IEnumerator ShowFeedback(bool correct, float duration)
        {
            SetState(SimonMenuState.Feedback);

            if (_checkmarkCard != null) _checkmarkCard.SetActive(correct);
            if (_crossCard != null) _crossCard.SetActive(!correct);

            yield return new WaitForSeconds(duration);

            if (_checkmarkCard != null) _checkmarkCard.SetActive(false);
            if (_crossCard != null) _crossCard.SetActive(false);
        }

        /// <summary>
        /// Shows game over screen with a reason and rounds completed.
        /// </summary>
        public void ShowGameOver(string reason, int roundsCompleted)
        {
            SetState(SimonMenuState.GameOver);

            if (_gameOverReasonText != null)
                _gameOverReasonText.text = reason;
        }

        /// <summary>
        /// Shows victory screen with rounds completed.
        /// </summary>
        public void ShowVictory(int roundsCompleted)
        {
            SetState(SimonMenuState.Victory);

            if (_victoryStatsText != null)
                _victoryStatsText.text = $"{roundsCompleted}/{roundsCompleted} rondas completadas";
        }

        // ── Internal ─────────────────────────────────────────────────────

        private void DeactivateAllPanels()
        {
            if (_startMenuPanel != null) _startMenuPanel.SetActive(false);
            if (_countdownPanel != null) _countdownPanel.SetActive(false);
            if (_gameplayHUDPanel != null) _gameplayHUDPanel.SetActive(false);
            if (_feedbackPanel != null) _feedbackPanel.SetActive(false);
            if (_pausePanel != null) _pausePanel.SetActive(false);
            if (_victoryPanel != null) _victoryPanel.SetActive(false);
            if (_gameOverPanel != null) _gameOverPanel.SetActive(false);

            // Also hide feedback cards
            if (_checkmarkCard != null) _checkmarkCard.SetActive(false);
            if (_crossCard != null) _crossCard.SetActive(false);
        }

        private void Awake()
        {
            WireButtonEvents();
        }

        private void OnDestroy()
        {
            UnwireButtonEvents();
        }

        private void WireButtonEvents()
        {
            if (_startButton != null)
                _startButton.onClick.AddListener(() => OnStartClicked?.Invoke());
            if (_mainMenuButton_Start != null)
                _mainMenuButton_Start.onClick.AddListener(() => OnMainMenuClicked?.Invoke());
            if (_resumeButton != null)
                _resumeButton.onClick.AddListener(() => OnResumeClicked?.Invoke());
            if (_mainMenuButton_Pause != null)
                _mainMenuButton_Pause.onClick.AddListener(() => OnMainMenuClicked?.Invoke());
            if (_restartButton_GameOver != null)
                _restartButton_GameOver.onClick.AddListener(() => OnRestartClicked?.Invoke());
            if (_restartButton_Victory != null)
                _restartButton_Victory.onClick.AddListener(() => OnRestartClicked?.Invoke());
        }

        private void UnwireButtonEvents()
        {
            if (_startButton != null)
                _startButton.onClick.RemoveAllListeners();
            if (_mainMenuButton_Start != null)
                _mainMenuButton_Start.onClick.RemoveAllListeners();
            if (_resumeButton != null)
                _resumeButton.onClick.RemoveAllListeners();
            if (_mainMenuButton_Pause != null)
                _mainMenuButton_Pause.onClick.RemoveAllListeners();
            if (_restartButton_GameOver != null)
                _restartButton_GameOver.onClick.RemoveAllListeners();
            if (_restartButton_Victory != null)
                _restartButton_Victory.onClick.RemoveAllListeners();
        }
    }

    /// <summary>
    /// UI state enum for the Simón Dice minigame.
    /// Used by SimonMenuManager to determine which panel(s) to display.
    /// </summary>
    public enum SimonMenuState
    {
        StartMenu,   // Title + start button visible
        Countdown,   // "3, 2, 1..." before first round
        Playing,     // Active round — HUD visible (dialogue, timer, round counter)
        Feedback,    // Brief correct/wrong feedback card
        Paused,      // Pause overlay
        GameOver,    // Failure screen with reason
        Victory      // Success screen with stats
    }
}
