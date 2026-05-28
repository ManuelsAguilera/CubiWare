using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ARcadeRush.Core;
using ARcadeRush.UI;
using ARcadeRush.Hand;
using CubiWare.Core;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// IMiniGame orchestrator for the Simón Dice minigame.
    /// Manages round progression, timing, LLM coordination, and UI state via SimonMenuManager.
    ///
    /// v3 Fixes applied:
    ///   - (F1)  Timer starts AFTER command generation completes (in OnCommandReady)
    ///   - (F3)  _roundAlreadyJudged guard prevents double evaluation
    ///   - (F9)  OnDestroy unsubscribes from all events
    ///   - (F10) SceneIndex uses SceneManager.GetActiveScene().buildIndex
    ///   - (F13) GamePhase enum prevents re-entrant StartRound() calls
    /// </summary>
    public class SimonGame : MonoBehaviour, IMiniGame
    {
        // ── Serialized Fields ──────────────────────────────────────────

        [Header("References")]
        [SerializeField] private SimonMenuManager _menuManager;
        [SerializeField] private SimonHUDController _hud;
        [SerializeField] private SimonJudge _judge;
        [SerializeField] private SimonCommandGenerator _commandGenerator;
        [SerializeField] private GestureDetector _gestureDetector;
        // [SerializeField] private EmotionClassifier _emotionClassifier; // Phase 2

        [Header("Game Settings")]
        [SerializeField] private int _maxRounds = 5;
        [SerializeField] private int _mainMenuSceneIndex = 1;

        [Header("Timing")]
        [SerializeField] private float _commandDisplayDuration = 2.5f;
        [SerializeField] private float _responseTimePerRound = 5f;
        [SerializeField] private float _feedbackDuration = 2f;
        [SerializeField] private float _roundTransitionDelay = 1.5f;

#if UNITY_EDITOR
        [Header("Debug (Editor Only)")]
        [Tooltip("Skip the start menu and begin gameplay immediately.")]
        [SerializeField] private bool _debugSkipStartMenu;
#endif

        // ── IMiniGame ───────────────────────────────────────────────────

        // v3 Fix (F10): fallback to runtime scene index
        public int SceneIndex => _cachedSceneIndex;
        private int _cachedSceneIndex;

        // ── Internal State ──────────────────────────────────────────────

        private int _currentRound;
        private int _correctStreak;
        private SimonCommand _currentCommand;
        private MiniGameDependencies _deps;

        // v3 Fix (F3): race condition guard
        private bool _roundAlreadyJudged;

        // v3 Fix (F13): internal phase prevents re-entrant calls
        private GamePhase _phase = GamePhase.Idle;

        private bool _hasGameEnded;
        private bool _isPaused;

        // Timing
        private float _responseTimer;
        private Coroutine _responseTimerCo;
        private Coroutine _displayTimerCo;
        private Coroutine _feedbackCo;

        // Logging
        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        // ── Unity Lifecycle ─────────────────────────────────────────────

        private void Awake()
        {
            _cachedSceneIndex = SceneManager.GetActiveScene().buildIndex;
        }

        private void OnDestroy()
        {
            // v3 Fix (F9): cleanup all event subscriptions
            if (_judge != null)
            {
                _judge.OnPlayerAction -= HandlePlayerAction;
                _judge.OnPlayerReturnedToNeutral -= HandlePlayerNeutral;
            }

            if (_menuManager != null)
            {
                _menuManager.OnStartClicked -= StartGame;
                _menuManager.OnMainMenuClicked -= LoadMainMenu;
                _menuManager.OnResumeClicked -= ResumeGame;
                _menuManager.OnRestartClicked -= RestartGame;
            }

            // Stop all coroutines
            if (_responseTimerCo != null) StopCoroutine(_responseTimerCo);
            if (_displayTimerCo != null) StopCoroutine(_displayTimerCo);
            if (_feedbackCo != null) StopCoroutine(_feedbackCo);
        }

        // ── IMiniGame Implementation ────────────────────────────────────

        public void OnStart(MiniGameDependencies deps)
        {
            _deps = deps ?? throw new ArgumentNullException(nameof(deps));
            _hasGameEnded = false;
            _phase = GamePhase.Idle;

            _logger.LogInfo("SimonGame", $"OnStart called. SceneIndex={_cachedSceneIndex}, " +
                $"LLM={_deps.LLM != null}, MediaPipe={_deps.MediaPipe != null}");

            // Register with GameManager (SceneLoader already called OnStart, so use RegisterGame)
            if (_deps.GameManager != null)
            {
                _deps.GameManager.RegisterGame(this);
            }

            // Wire judge events
            if (_judge != null)
            {
                _judge.OnPlayerAction += HandlePlayerAction;
                _judge.OnPlayerReturnedToNeutral += HandlePlayerNeutral;
            }

            // Wire menu events
            if (_menuManager != null)
            {
                _menuManager.OnStartClicked += StartGame;
                _menuManager.OnMainMenuClicked += LoadMainMenu;
                _menuManager.OnResumeClicked += ResumeGame;
                _menuManager.OnRestartClicked += RestartGame;

                // Show start menu
                _menuManager.SetState(SimonMenuState.StartMenu);
            }

            // Reset HUD
            _hud?.ResetAll();

            // Ensure gesture detection is active
            _gestureDetector?.SetDetectionActive(true);

#if UNITY_EDITOR
            if (_debugSkipStartMenu)
            {
                _logger.LogInfo("SimonGame", "Debug skip: bypassing start menu.");
                StartGame();
            }
#endif
        }

        public void OnEnd()
        {
            if (_hasGameEnded) return;
            _hasGameEnded = true;

            _logger.LogInfo("SimonGame", $"OnEnd called. Rounds completed: {_correctStreak}/{_maxRounds}");

            // Stop all running coroutines
            if (_responseTimerCo != null) { StopCoroutine(_responseTimerCo); _responseTimerCo = null; }
            if (_displayTimerCo != null) { StopCoroutine(_displayTimerCo); _displayTimerCo = null; }
            if (_feedbackCo != null) { StopCoroutine(_feedbackCo); _feedbackCo = null; }

            // Stop judge monitoring
            _judge?.StopMonitoring();

            // Create and report session data
            var sessionData = new MinigameSessionData
            {
                MinigameName = "Simon",
                Score = _correctStreak,
                Completed = _correctStreak >= _maxRounds,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                DurationSeconds = 0f, // Simon Dice doesn't have a fixed duration
                CustomStats = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "RoundsCompleted", _correctStreak },
                    { "MaxRounds", _maxRounds },
                    { "Won", _correctStreak >= _maxRounds }
                }
            };

            if (_deps?.GameManager != null)
            {
                _deps.GameManager.CollectMinigameData(sessionData);
                _deps.GameManager.EndGame();
            }

            _phase = GamePhase.Ended;
        }

        // ── Game Flow ───────────────────────────────────────────────────

        /// <summary>
        /// Called from StartMenu button. Begins the countdown sequence.
        /// </summary>
        private void StartGame()
        {
            if (_phase != GamePhase.Idle && _phase != GamePhase.Ended)
            {
                _logger.LogWarning("SimonGame", $"StartGame ignored — invalid phase: {_phase}");
                return;
            }

            _logger.LogInfo("SimonGame", "StartGame triggered.");

            _currentRound = 0;
            _correctStreak = 0;

            // Pre-plan the command distribution
            if (_commandGenerator != null)
            {
                _commandGenerator.ResetPlan();
                _commandGenerator.PlanGame(_maxRounds);
            }

            // Run countdown, then start first round
            if (_menuManager != null)
            {
                StartCoroutine(_menuManager.RunCountdown(() =>
                {
                    _phase = GamePhase.Countdown; // Will transition in StartRound
                    StartRound();
                }));
            }
            else
            {
                StartRound();
            }
        }

        /// <summary>
        /// Starts a new round. Guarded by GamePhase to prevent re-entrant calls.
        /// </summary>
        private void StartRound()
        {
            // v3 Fix (F13): guard against double StartRound calls
            if (_phase == GamePhase.Generating)
            {
                _logger.LogWarning("SimonGame", "StartRound ignored — already generating command.");
                return;
            }

            if (_hasGameEnded) return;

            _phase = GamePhase.Generating;
            _roundAlreadyJudged = false; // Reset for new round
            _judge?.ResetState();

            int displayRound = _currentRound + 1;
            _hud?.UpdateRoundCounter(displayRound, _maxRounds);
            _menuManager?.SetState(SimonMenuState.Playing);
            _hud?.HideDialogue();

            _logger.LogInfo("SimonGame", $"Starting round {displayRound}/{_maxRounds}. Generating command...");

            // Generate command asynchronously (LLM may take time)
            if (_commandGenerator != null)
            {
                _commandGenerator.GenerateCommand(_currentRound, _maxRounds, _deps?.LLM, OnCommandReady);
            }
            else
            {
                // Fallback without generator: create a simple command
                OnCommandReady(new SimonCommand
                {
                    SaysSimonDice = true,
                    ActionType = SimonActionType.Gesture,
                    GestureTarget = SimonGestureTarget.OpenHand,
                    DialogueText = "Simón dice: ¡mano abierta!"
                });
            }
        }

        /// <summary>
        /// Callback from SimonCommandGenerator when the command is ready (LLM or fallback).
        /// v3 Fix (F1): Timer starts HERE, not after a fixed delay.
        /// </summary>
        private void OnCommandReady(SimonCommand cmd)
        {
            _currentCommand = cmd;
            _phase = GamePhase.DisplayCommand;

            _logger.LogInfo("SimonGame", $"Command ready: saysSimonDice={cmd.SaysSimonDice}, " +
                $"gesture={cmd.GestureTarget}, text=\"{cmd.DialogueText}\"");

            // Show dialogue text
            _hud?.ShowDialogue(cmd.DialogueText, cmd.SaysSimonDice);

            // Wait for display duration, then begin response phase
            if (_displayTimerCo != null) StopCoroutine(_displayTimerCo);
            _displayTimerCo = StartCoroutine(CoDisplayPhase());
        }

        private IEnumerator CoDisplayPhase()
        {
            yield return new WaitForSeconds(_commandDisplayDuration);

            // v3 Fix (F1): Timer starts NOW, after display phase and LLM latency
            BeginResponsePhase();
        }

        /// <summary>
        /// Begins the player response phase: starts monitoring + timer.
        /// </summary>
        private void BeginResponsePhase()
        {
            _phase = GamePhase.WaitResponse;

            _logger.LogInfo("SimonGame", $"Response phase started. {_responseTimePerRound}s timer running.");

            // Start monitoring player gestures
            _judge?.StartMonitoring();

            // Reset timer display
            _responseTimer = _responseTimePerRound;
            _hud?.UpdateTimer(_responseTimer, _responseTimePerRound);

            // Start response timer coroutine
            if (_responseTimerCo != null) StopCoroutine(_responseTimerCo);
            _responseTimerCo = StartCoroutine(CoResponseTimer());
        }

        private IEnumerator CoResponseTimer()
        {
            while (_responseTimer > 0f)
            {
                if (!_isPaused)
                {
                    _responseTimer -= Time.deltaTime;
                    _hud?.UpdateTimer(_responseTimer, _responseTimePerRound);
                }
                yield return null;
            }

            // Timer expired — handle timeout
            _responseTimer = 0f;
            _hud?.UpdateTimer(0f, _responseTimePerRound);
            HandleTimeout();
        }

        // ── Player Input Handling ───────────────────────────────────────

        /// <summary>
        /// Called by SimonJudge when the player performs a gesture.
        /// </summary>
        private void HandlePlayerAction(string gestureName)
        {
            // v3 Fix (F3): prevent double evaluation
            if (_roundAlreadyJudged)
            {
                _logger.LogWarning("SimonGame", $"Player action '{gestureName}' ignored — round already judged.");
                return;
            }

            if (_phase != GamePhase.WaitResponse)
            {
                _logger.LogWarning("SimonGame", $"Player action '{gestureName}' ignored — not in WaitResponse phase (current: {_phase}).");
                return;
            }

            _logger.LogInfo("SimonGame", $"Player action detected: '{gestureName}'");
            _roundAlreadyJudged = true;
            _judge?.StopMonitoring();

            // Stop timer
            if (_responseTimerCo != null) { StopCoroutine(_responseTimerCo); _responseTimerCo = null; }

            JudgeRound(gestureName, timedOut: false);
        }

        /// <summary>
        /// Called by SimonJudge when the player returns to neutral.
        /// (Not used for judgment in Phase 1, but may be used in Phase 2.)
        /// </summary>
        private void HandlePlayerNeutral()
        {
            _logger.LogInfo("SimonGame", "Player returned to neutral.");
        }

        /// <summary>
        /// Called when the response timer expires.
        /// </summary>
        private void HandleTimeout()
        {
            // v3 Fix (F3): prevent double evaluation
            if (_roundAlreadyJudged)
            {
                _logger.LogWarning("SimonGame", "Timeout ignored — round already judged.");
                return;
            }

            if (_phase != GamePhase.WaitResponse)
            {
                _logger.LogWarning("SimonGame", $"Timeout ignored — not in WaitResponse phase (current: {_phase}).");
                return;
            }

            _logger.LogInfo("SimonGame", "Response timeout.");
            _roundAlreadyJudged = true;
            _judge?.StopMonitoring();

            JudgeRound(playerAction: null, timedOut: true);
        }

        // ── Judgment ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the Simon Says truth table to determine the round result.
        ///
        /// +--------------+------------------+---------------+
        /// |SaysSimonDice | Player Action    | Result        |
        /// +--------------+------------------+---------------+
        /// | TRUE         | Correct gesture  | CORRECT       |
        /// | TRUE         | Wrong gesture    | WrongGesture  |
        /// | TRUE         | Nothing/timeout  | Timeout       |
        /// | FALSE        | Stayed neutral   | CORRECT       |
        /// | FALSE        | Did ANY gesture  | WrongAction   |
        /// +--------------+------------------+---------------+
        /// </summary>
        private void JudgeRound(string playerAction, bool timedOut)
        {
            _phase = GamePhase.Judging;

            RoundResult result;

            if (_currentCommand == null)
            {
                _logger.LogWarning("SimonGame", "JudgeRound called with null command — treating as timeout.");
                result = RoundResult.Timeout;
            }
            else if (_currentCommand.SaysSimonDice)
            {
                // Simón dice — player MUST obey
                if (timedOut)
                {
                    result = RoundResult.Timeout;
                }
                else
                {
                    string expectedGesture = _currentCommand.GestureTarget.ToString();
                    if (string.Equals(playerAction, expectedGesture, StringComparison.OrdinalIgnoreCase))
                    {
                        result = RoundResult.Correct;
                    }
                    else
                    {
                        result = RoundResult.WrongGesture;
                    }
                }
            }
            else
            {
                // Simón did NOT say "Simón dice" — player must stay neutral
                if (timedOut)
                {
                    // Player did nothing = correct
                    result = RoundResult.Correct;
                }
                else
                {
                    // Player did something = wrong
                    result = RoundResult.WrongAction;
                }
            }

            _logger.LogInfo("SimonGame", $"Judgment: saysSimonDice={_currentCommand?.SaysSimonDice}, " +
                $"expected={_currentCommand?.GestureTarget}, playerAction={playerAction ?? "timeout"}, " +
                $"result={result}");

            // Process result
            if (result == RoundResult.Correct)
            {
                _correctStreak++;
                _currentRound++;
            }

            // Show feedback
            ShowFeedback(result);
        }

        private void ShowFeedback(RoundResult result)
        {
            bool correct = result == RoundResult.Correct;

            if (_menuManager != null)
            {
                _feedbackCo = StartCoroutine(_menuManager.ShowFeedback(correct, _feedbackDuration));
            }

            // After feedback duration, decide next step
            StartCoroutine(CoAfterFeedback(result));
        }

        private IEnumerator CoAfterFeedback(RoundResult result)
        {
            yield return new WaitForSeconds(_feedbackDuration);

            // Hide dialogue before next round or end screen
            _hud?.HideDialogue();

            if (result == RoundResult.Correct)
            {
                if (_currentRound >= _maxRounds)
                {
                    // Victory!
                    _logger.LogInfo("SimonGame", $"Victory! {_correctStreak}/{_maxRounds} rounds completed.");
                    EndGame(won: true);
                }
                else
                {
                    // Next round
                    yield return new WaitForSeconds(_roundTransitionDelay);
                    StartRound();
                }
            }
            else
            {
                // Any mistake = game over
                string reason = result switch
                {
                    RoundResult.Timeout => "No reaccionaste a tiempo",
                    RoundResult.WrongAction => "Hiciste un gesto cuando no debías",
                    RoundResult.WrongGesture => "Hiciste el gesto incorrecto",
                    _ => "Error"
                };

                _logger.LogInfo("SimonGame", $"Game Over: {reason}. {_correctStreak}/{_maxRounds} rounds.");
                EndGame(won: false, reason: reason);
            }
        }

        private void EndGame(bool won, string reason = null)
        {
            _phase = GamePhase.Ended;
            _hasGameEnded = true;

            // Stop all active coroutines
            if (_responseTimerCo != null) { StopCoroutine(_responseTimerCo); _responseTimerCo = null; }
            if (_displayTimerCo != null) { StopCoroutine(_displayTimerCo); _displayTimerCo = null; }

            _judge?.StopMonitoring();
            _hud?.HideDialogue();

            // Report session data
            if (_deps?.GameManager != null)
            {
                var sessionData = new MinigameSessionData
                {
                    MinigameName = "Simon",
                    Score = _correctStreak,
                    Completed = won,
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now,
                    DurationSeconds = 0f,
                    CustomStats = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "RoundsCompleted", _correctStreak },
                        { "MaxRounds", _maxRounds },
                        { "Won", won },
                        { "FinalResult", reason ?? "Victory" }
                    }
                };

                _deps.GameManager.CollectMinigameData(sessionData);
            }

            // Show appropriate end screen
            if (_menuManager != null)
            {
                if (won)
                {
                    _menuManager.ShowVictory(_correctStreak);
                }
                else
                {
                    _menuManager.ShowGameOver(reason ?? "Error desconocido", _correctStreak);
                }
            }

            _logger.LogInfo("SimonGame", $"Game ended. Won={won}, Rounds={_correctStreak}/{_maxRounds}");
        }

        // ── Pause / Resume ──────────────────────────────────────────────

        private void PauseGame()
        {
            if (_phase != GamePhase.WaitResponse || _isPaused) return;
            _isPaused = true;
            _menuManager?.SetState(SimonMenuState.Paused);
            _logger.LogInfo("SimonGame", "Game paused.");
        }

        private void ResumeGame()
        {
            if (!_isPaused) return;
            _isPaused = false;
            _menuManager?.SetState(SimonMenuState.Playing);
            _logger.LogInfo("SimonGame", "Game resumed.");
        }

        // ── Navigation ──────────────────────────────────────────────────

        private void RestartGame()
        {
            _logger.LogInfo("SimonGame", "RestartGame called.");

            // Reset all state
            _hasGameEnded = false;
            _currentRound = 0;
            _correctStreak = 0;
            _roundAlreadyJudged = false;
            _isPaused = false;

            if (_commandGenerator != null)
            {
                _commandGenerator.ResetPlan();
                _commandGenerator.PlanGame(_maxRounds);
            }

            _hud?.ResetAll();
            _menuManager?.SetState(SimonMenuState.StartMenu);
            _phase = GamePhase.Idle;
        }

        public void LoadMainMenu()
        {
            _logger.LogInfo("SimonGame", "Loading MainMenu scene.");

            _judge?.StopMonitoring();

            if (_deps?.GameManager != null)
            {
                _deps.GameManager.EndGame();
            }

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadScene(_mainMenuSceneIndex);
            }
            else
            {
                SceneManager.LoadScene(_mainMenuSceneIndex);
            }
        }

        // ── GamePhase Enum ──────────────────────────────────────────────

        /// <summary>
        /// Internal state tracked by SimonGame to prevent re-entrant calls.
        /// NOT the same as SimonMenuState (which controls UI panels).
        /// </summary>
        private enum GamePhase
        {
            Idle,           // Before game starts or after game ends
            Countdown,      // Countdown animation running
            Generating,     // Waiting for LLM/command generation (v3 F13)
            DisplayCommand, // Command text shown, player reading
            WaitResponse,   // Timer ticking, monitoring player input
            Judging,        // Evaluating round result
            Feedback,       // Showing correct/wrong feedback
            Ended           // Game over or victory
        }
    }
}
