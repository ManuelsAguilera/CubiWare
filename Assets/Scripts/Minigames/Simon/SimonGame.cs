using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ARcadeRush.Core;
using ARcadeRush.UI;
using ARcadeRush.Hand;
using ARcadeRush.EmotionDetection;
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
    ///
    /// v5: Emotion rounds alternate with gesture+position rounds.
    ///   - Emotion rounds skip position validation entirely.
    ///   - Judge polls EmotionGameBridge for emotion matching.
    ///   - OnEmotionMatched event triggers scoring like OnPlayerAction.
    /// </summary>
    public class SimonGame : MonoBehaviour, IMiniGame
    {
        // ── Serialized Fields ──────────────────────────────────────────

        [Header("Camera")]
        [SerializeField] private UnityEngine.UI.RawImage _cameraDisplay;

        [Header("References")]
        [SerializeField] private SimonMenuManager _menuManager;
        [SerializeField] private SimonHUDController _hud;
        [SerializeField] private SimonJudge _judge;
        [SerializeField] private SimonCommandGenerator _commandGenerator;
        [SerializeField] private GestureDetector _gestureDetector;

        [Header("Position System (Phase 2)")]
        [SerializeField] private PositionInstructor _positionInstructor;
        [SerializeField] private HandZoneClassifier _handZoneClassifier;

        [Header("Emotion System (v5)")]
        [SerializeField] private EmotionGameBridge _emotionBridge;

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
        private bool _timeoutDebugShown; // guard: only capture snapshot once per game

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
                _judge.OnPlayerTricked -= HandlePlayerTricked;
                _judge.OnEmotionMatched -= HandleEmotionMatched;
            }

            if (_menuManager != null)
            {
                _menuManager.OnStartClicked -= StartGame;
                _menuManager.OnMainMenuClicked -= LoadMainMenu;
                _menuManager.OnResumeClicked -= ResumeGame;
                _menuManager.OnRestartClicked -= RestartGame;
            }

            // Cleanup position instructor arrows
            if (_positionInstructor != null)
            {
                _positionInstructor.ClearInstruction();
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

            // Resolve EmotionGameBridge if not assigned in Inspector
            if (_emotionBridge == null)
            {
                _emotionBridge = EmotionGameBridge.Instance;
            }

            // Relay EmotionGameBridge to judge so it can poll emotions
            if (_judge != null && _emotionBridge != null)
            {
                _judge.SetEmotionBridge(_emotionBridge);
            }

            // Setup camera feed display
            SetupCamera();

            // Wire judge events
            if (_judge != null)
            {
                _judge.OnPlayerAction += HandlePlayerAction;
                _judge.OnPlayerReturnedToNeutral += HandlePlayerNeutral;
                _judge.OnPlayerTricked += HandlePlayerTricked;
                _judge.OnEmotionMatched += HandleEmotionMatched;
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

        /// <summary>
        /// Wires the camera feed to the RawImage display, following the
        /// same pattern as EmotionTestGame.SetupCamera().
        /// </summary>
        private void SetupCamera()
        {
            if (_deps?.Camera == null)
            {
                _logger.LogWarning("SimonGame", "Camera not available in dependencies.");
                return;
            }

            if (_cameraDisplay == null)
            {
                _logger.LogWarning("SimonGame", "Camera display RawImage not assigned in Inspector.");
                return;
            }

            _deps.Camera.SetOutputImage(_cameraDisplay);

            if (!_deps.Camera.IsPlaying)
            {
                // SetOutputImage() already wired texture + mirror uvRect.
                _deps.Camera.StartCamera();
                _logger.LogInfo("SimonGame", "Camera started for Simon scene.");
            }
            else
            {
                if (_deps.Camera.ActiveWebCamTexture != null)
                {
                    // Re-wire via SetOutputImage so it respects global mirror setting
                    _deps.Camera.SetOutputImage(_cameraDisplay);
                    _logger.LogInfo("SimonGame", "Camera already active, SetOutputImage reassigned.");
                }
                else
                {
                    _deps.Camera.StartCamera();
                    _logger.LogInfo("SimonGame", "Camera active but texture null, restarting.");
                }
            }
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
            _hud?.HideEmotionTarget();
            _positionInstructor?.ClearInstruction();

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
                    ContainsSimonDice = true,
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

            string roundType = cmd.ActionType == SimonActionType.Emotion ? "EMOTION" : "GESTURE+POSITION";
            _logger.LogInfo("SimonGame", $"Command ready [{roundType}]: saysSimonDice={cmd.SaysSimonDice}, " +
                $"gesture={cmd.GestureTarget}, emotion={cmd.EmotionTarget}, text=\"{cmd.DialogueText}\"");

            // Show dialogue text
            _hud?.ShowDialogue(cmd.DialogueText, cmd.SaysSimonDice);

            // Wait for display duration, then begin response phase
            if (_displayTimerCo != null) StopCoroutine(_displayTimerCo);
            _displayTimerCo = StartCoroutine(CoDisplayPhase());
        }

        private IEnumerator CoDisplayPhase()
        {
            yield return new WaitForSeconds(_commandDisplayDuration);

            // v5: Emotion rounds show emotion HUD; gesture rounds show position arrows as visual guide.
            // Timer starts IMMEDIATELY for both — position arrows are parallel hints, not blockers.
            if (_currentCommand != null && _currentCommand.ActionType == SimonActionType.Emotion)
            {
                string emotionName = SimonCommandGenerator.GetEmotionDisplayName(_currentCommand.EmotionTarget);
                _hud?.ShowEmotionTarget(emotionName);
            }
            else if (_positionInstructor != null && _currentCommand != null && _currentCommand.HasPositionTarget)
            {
                // Show position arrows in parallel (visual guide only — does NOT block timer)
                _positionInstructor.InstructZone(_currentCommand.ExpectedZone);
            }

            BeginResponsePhase();
        }

        /// <summary>
        /// Begins the player response phase: starts monitoring + timer IMMEDIATELY.
        /// The prompt is shown, the timer ticks down. If no valid action within
        /// _responseTimePerRound seconds → timeout → game over.
        /// If command doesn't say "simon dice" but player acts → tricked → game over.
        /// v5: Emotion rounds configure judge differently (no zone, poll emotions).
        /// </summary>
        private void BeginResponsePhase()
        {
            _phase = GamePhase.WaitResponse;

            string roundType = _currentCommand?.ActionType == SimonActionType.Emotion ? "EMOTION" : "GESTURE";
            _logger.LogInfo("SimonGame", $"Response phase started [{roundType}]. {_responseTimePerRound}s timer running.");

            // Configure judge for this round type
            if (_judge != null && _currentCommand != null)
            {
                if (_currentCommand.ActionType == SimonActionType.Emotion)
                {
                    _judge.SetEmotionRound(_currentCommand.EmotionTarget);
                    _judge.SetSimonDiceFlag(_currentCommand.ContainsSimonDice);
                    _judge.SetExpectedZone(HandZone.None);
                }
                else
                {
                    _judge.SetExpectedGesture(_currentCommand.GestureTarget);
                    _judge.SetExpectedZone(_currentCommand.ExpectedZone);
                    _judge.SetSimonDiceFlag(_currentCommand.ContainsSimonDice);
                }
            }

            // Start monitoring player gestures (or emotion polling)
            _judge?.StartMonitoring();

            // Reset timer display
            _responseTimer = _responseTimePerRound;
            _hud?.UpdateTimer(_responseTimer, _responseTimePerRound);

            // Start response timer coroutine — runs until action or timeout
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

            // ── Clear UI immediately (symmetry with HandleEmotionMatched) ──
            _positionInstructor?.ClearInstruction();
            _hud?.HideEmotionTarget();

            JudgeRound(gestureName, timedOut: false, isEmotion: false);
        }

        /// <summary>
        /// Called by SimonJudge when the player returns to neutral.
        /// </summary>
        private void HandlePlayerNeutral()
        {
            _logger.LogInfo("SimonGame", "Player returned to neutral.");
        }

        /// <summary>
        /// Called by SimonJudge when the player performed the correct gesture+zone
        /// but the command did NOT contain "simon dice" — they got tricked.
        /// </summary>
        private void HandlePlayerTricked(string gestureName)
        {
            if (_roundAlreadyJudged)
            {
                _logger.LogWarning("SimonGame", $"Tricked action '{gestureName}' ignored — round already judged.");
                return;
            }

            if (_phase != GamePhase.WaitResponse)
            {
                _logger.LogWarning("SimonGame", $"Tricked action '{gestureName}' ignored — not in WaitResponse phase (current: {_phase}).");
                return;
            }

            _logger.LogInfo("SimonGame", $"Player TRICKED! Gesture '{gestureName}' but command didn't say 'simon dice'.");
            _roundAlreadyJudged = true;
            _judge?.StopMonitoring();

            // Stop timer
            if (_responseTimerCo != null) { StopCoroutine(_responseTimerCo); _responseTimerCo = null; }

            // Show tricked feedback — no scoring
            ShowTrickedFeedback(gestureName);
        }

        /// <summary>
        /// v5: Called by SimonJudge when the player matches the target emotion.
        /// Similar to HandlePlayerAction but for emotion rounds.
        /// </summary>
        private void HandleEmotionMatched(string emotionName)
        {
            if (_roundAlreadyJudged)
            {
                _logger.LogWarning("SimonGame", $"Emotion match '{emotionName}' ignored — round already judged.");
                return;
            }

            if (_phase != GamePhase.WaitResponse)
            {
                _logger.LogWarning("SimonGame", $"Emotion match '{emotionName}' ignored — not in WaitResponse phase (current: {_phase}).");
                return;
            }

            _logger.LogInfo("SimonGame", $"Emotion matched: '{emotionName}'");
            _roundAlreadyJudged = true;
            _judge?.StopMonitoring();

            // Stop timer
            if (_responseTimerCo != null) { StopCoroutine(_responseTimerCo); _responseTimerCo = null; }

            // Hide emotion HUD
            _hud?.HideEmotionTarget();
            _positionInstructor?.ClearInstruction();

            JudgeRound(emotionName, timedOut: false, isEmotion: true);
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

            // ═══ ONE-SHOT TIMEOUT DEBUG SNAPSHOT ═══════════════════════════
            if (!_timeoutDebugShown)
            {
                _timeoutDebugShown = true;
                string zoneStr = _handZoneClassifier != null ? _handZoneClassifier.CurrentZone.ToString() : "no classifier";
                string gestureStr = _gestureDetector != null ? _gestureDetector.CurrentDetectedGesture : "no detector";
                string emotionStr = "no bridge";
                string faceStr = "n/a";
                string confStr = "n/a";
                string connStr = "n/a";
                if (_emotionBridge != null)
                {
                    emotionStr = _emotionBridge.GetCurrentDominantEmotion() ?? "null";
                    faceStr = _emotionBridge.FaceDetected.ToString();
                    confStr = _emotionBridge.Confidence.ToString("F3");
                    connStr = _emotionBridge.IsConnected.ToString();
                }
                string expectedStr = _currentCommand != null
                    ? $"type={_currentCommand.ActionType}, gesture={_currentCommand.GestureTarget}, emotion={_currentCommand.EmotionTarget}, zone={_currentCommand.ExpectedZone}, simonDice={_currentCommand.ContainsSimonDice}"
                    : "no command";

                Debug.LogWarning($"[SimonGame-TIMEOUT-SNAPSHOT]\n" +
                    $"  ROUND CONTEXT: round={_currentRound + 1}/{_maxRounds}, expected=[{expectedStr}]\n" +
                    $"  HAND ZONE:     {zoneStr}\n" +
                    $"  DETECTED GESTURE: {gestureStr}\n" +
                    $"  EMOTION:       dominant={emotionStr}, faceDetected={faceStr}, confidence={confStr}, connected={connStr}\n" +
                    $"  TIMER:         remaining={_responseTimer:F2}s / {_responseTimePerRound}s\n" +
                    $"  ── This snapshot fires ONCE per game session. ──");
            }
            // ═════════════════════════════════════════════════════════════════

            _logger.LogInfo("SimonGame", "Response timeout.");
            _roundAlreadyJudged = true;
            _judge?.StopMonitoring();

            // Hide emotion HUD on timeout
            _hud?.HideEmotionTarget();
            _positionInstructor?.ClearInstruction();

            bool isEmotion = _currentCommand?.ActionType == SimonActionType.Emotion;
            JudgeRound(playerAction: null, timedOut: true, isEmotion: isEmotion);
        }

        // ── Judgment ────────────────────────────────────────────────────

        /// <summary>
        /// Applies the Simon Says truth table to determine the round result.
        ///
        /// +------------------+------------------+---------------+
        /// |ContainsSimonDice | Player Action    | Result        |
        /// +------------------+------------------+---------------+
        /// | TRUE             | Correct gesture  | CORRECT       |
        /// | TRUE             | Wrong gesture    | WrongGesture  |
        /// | TRUE             | Nothing/timeout  | Timeout       |
        /// | FALSE            | Stayed neutral   | CORRECT       |
        /// | FALSE            | Did ANY gesture  | WrongAction   |
        /// +------------------+------------------+---------------+
        ///
        /// v5: Emotion rounds use the same truth table.
        /// NOTE: Tricked detection (player acts when ContainsSimonDice=false) is
        /// handled in HandlePlayerTricked BEFORE this method is called.
        /// Emotion match detection is handled in HandleEmotionMatched.
        /// </summary>
        private void JudgeRound(string playerAction, bool timedOut, bool isEmotion)
        {
            _phase = GamePhase.Judging;

            RoundResult result;

            if (_currentCommand == null)
            {
                _logger.LogWarning("SimonGame", "JudgeRound called with null command — treating as timeout.");
                result = RoundResult.Timeout;
            }
            else if (_currentCommand.ContainsSimonDice)
            {
                // Command contains "simon dice" — player MUST obey
                if (timedOut)
                {
                    result = RoundResult.Timeout;
                }
                else if (isEmotion)
                {
                    // Emotion round: check if matched emotion matches expected
                    // v7: use English name to match the value passed by HandleEmotionMatched
                    string expectedEmotion = SimonCommandGenerator.GetEmotionEnglishName(_currentCommand.EmotionTarget);
                    if (string.Equals(playerAction, expectedEmotion, StringComparison.OrdinalIgnoreCase))
                    {
                        result = RoundResult.Correct;
                    }
                    else
                    {
                        result = RoundResult.WrongGesture; // "wrong emotion" uses same enum
                    }
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
                // Command did NOT contain "simon dice" — player must stay neutral
                // Player action is only possible here if they did the WRONG thing
                // (correct action with no simon dice is caught by HandlePlayerTricked/HandleEmotionMatched → trick check)
                if (timedOut)
                {
                    // Player did nothing = correct
                    result = RoundResult.Correct;
                }
                else
                {
                    // Player did something but wrong action — still wrong
                    result = RoundResult.WrongAction;
                }
            }

            _logger.LogInfo("SimonGame", $"Judgment: containsSimonDice={_currentCommand?.ContainsSimonDice}, " +
                $"isEmotion={isEmotion}, expected={_currentCommand?.GestureTarget}/{_currentCommand?.EmotionTarget}, " +
                $"playerAction={playerAction ?? "timeout"}, result={result}");

            // Process result
            if (result == RoundResult.Correct)
            {
                _correctStreak++;
                _currentRound++;
            }

            // Show feedback
            ShowFeedback(result);
        }

        private void ShowTrickedFeedback(string gestureName)
        {
            _phase = GamePhase.Judging;

            _logger.LogInfo("SimonGame", $"Tricked! Player did '{gestureName}' but command didn't say 'simon dice'.");

            // Hide any emotion HUD
            _hud?.HideEmotionTarget();
            _positionInstructor?.ClearInstruction();

            // Show "tricked" HUD message
            _hud?.ShowTrickedMessage();

            // Do NOT increment score, do NOT increment round count for tricked
            // Show feedback then move to game over (trick = loss)
            if (_menuManager != null)
            {
                _feedbackCo = StartCoroutine(_menuManager.ShowFeedback(false, _feedbackDuration));
            }

            StartCoroutine(CoAfterTricked());
        }

        private IEnumerator CoAfterTricked()
        {
            yield return new WaitForSeconds(_feedbackDuration);
            _hud?.HideDialogue();
            _positionInstructor?.ClearInstruction();

            // Being tricked = game over
            EndGame(won: false, reason: "¡Te engañó Simón! Hiciste el gesto cuando no debías");
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

            // Hide dialogue, emotion HUD, and position arrows before next round or end screen
            _hud?.HideDialogue();
            _hud?.HideEmotionTarget();
            _positionInstructor?.ClearInstruction();

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
            _hud?.HideEmotionTarget();
            _positionInstructor?.ClearInstruction();

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
            _timeoutDebugShown = false;

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
        ///
        /// Round flow (v5):
        /// Generating → DisplayCommand → WaitResponse → Judging → Feedback
        /// Timer starts IMMEDIATELY after DisplayCommand. Position arrows are parallel visual hints.
        /// </summary>
        private enum GamePhase
        {
            Idle,               // Before game starts or after game ends
            Countdown,          // Countdown animation running
            Generating,         // Waiting for LLM/command generation
            DisplayCommand,     // Command text shown, player reading
            WaitResponse,       // Timer ticking, monitoring player input
            Judging,            // Evaluating round result
            Feedback,           // Showing correct/wrong feedback
            Ended               // Game over or victory
        }
    }
}
