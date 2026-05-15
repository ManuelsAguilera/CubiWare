using System;
using System.Collections;
using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.UI;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Shooter minigame — the player aims with their index finger and shoots by closing their fist.
    /// Uses pre-placed scene targets managed by TargetManager.
    /// Progresses through difficulty rows (Easy → Medium → Hard) based on score thresholds.
    /// Hits bandits for points (varies by row), hits innocents for penalties.
    /// 90-second timer. On timeout, shows Game Over screen with final score and restart/menu options.
    /// Implements IMiniGame for scene-independent orchestration.
    /// 
    /// Initialization flow:
    /// 1. SceneLoader loads the scene and calls <see cref="OnStart(MiniGameDependencies)"/>
    /// 2. OnStart registers with GameManager, subscribes to events, shows start menu
    /// 3. On timeout/completion, <see cref="OnEnd"/> creates MinigameSessionData and reports to GameManager
    /// </summary>
    public class ShooterGame : MonoBehaviour, IMiniGame
    {
        /// <summary>Last game's final score, read by MainMenuController to display on the menu.</summary>
        public static int LastScore { get; private set; }

        [Header("References")]
        [SerializeField] private ShooterHandController _handController;
        [SerializeField] private GunController _gunController;
        [SerializeField] private TargetManager _targetManager;
        [SerializeField] private HUDController _hudController;
        [SerializeField] private GameAudioController _audioController;

        [Header("Game Settings")]
        [SerializeField] private int _gameDuration = 90;
        [SerializeField] private int _mainMenuSceneIndex = 1;
        [SerializeField] private int _thisSceneIndex = 3;

        [Header("Wave Progression")]
        [SerializeField] private string[] _rowOrder = { "Easy", "Medium", "Hard" };
        [SerializeField] private int[] _scoreThresholds = { 30, 70, int.MaxValue };
        [SerializeField] private float[] _batchIntervals = { 3f, 2.5f, 2f };
        [SerializeField] private float _waveTransitionDelay = 1f;

        [Header("Debug")]
        [SerializeField] private bool _debugSkipStartMenu = false;
        [SerializeField] private int _debugScoreIncrement = 10;
        [SerializeField] private KeyCode _debugAddScoreKey = KeyCode.KeypadPlus;

        /// <summary>Must match Shooter in Build Settings (index 3).</summary>
        public int SceneIndex => _thisSceneIndex;

        [Header("State (read-only)")]
        [SerializeField] private int _score;
        [SerializeField] private int _timeRemaining;
        [SerializeField] private bool _isPlaying = false;
        [SerializeField] private bool _isGamePaused = false;

        private MiniGameDependencies _deps;
        private Coroutine _waveCo;
        private bool _hasGameEnded;
        private bool _gameReady; // true after setup, false until first unpause
        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        // ── IMiniGame Entry Point ───────────────────────────────────────────

        /// <summary>
        /// IMiniGame entry point called by SceneLoader after scene load completes.
        /// This is the primary initialization path — receives dependencies from Bootstrap/GameManager.
        /// </summary>
        public void OnStart(MiniGameDependencies deps)
        {
            _hasGameEnded = false;
            _hudController?.HideStartMenu();

            _gunController?.SetAimPreviewActive(true);
            _gunController?.SetDebugInputAllowed(true);

            _deps = deps;
            _score = 0;
            _timeRemaining = _gameDuration;

            // Register with GameManager so it owns the game state
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.StartGame(this);
                _logger.LogInfo("ShooterGame", "Registered with GameManager via StartGame().");

                // Inject GameManager reference into audio controller
                if (_audioController != null)
                {
                    _audioController.Initialize(_deps.GameManager);
                    _logger.LogInfo("ShooterGame", "GameManager injected into GameAudioController.");
                }
            }
            else
            {
                _logger.LogWarning("ShooterGame", "No GameManager in deps — running without global state tracking.");
            }

            if (_targetManager == null)
            {
                _logger.LogError("ShooterGame", "TargetManager not assigned!", ServiceErrorCode.MinigameDependencyMissing);
            }

            // Subscribe to GameManager events
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.OnScoreChanged += HandleScoreChanged;
                _deps.GameManager.OnGamePaused += HandleGamePaused;
                _deps.GameManager.OnGameResumed += HandleGameResumed;
            }

            // Subscribe to gun ammo/reload events and target hit
            if (_gunController != null)
            {
                _gunController.OnAmmoChanged += HandleAmmoChanged;
                _gunController.OnReloadStarted += HandleReloadStarted;
                _gunController.OnReloadCompleted += HandleReloadCompleted;
                _gunController.OnTargetHit += HandleTargetHit;
            }

            // Initial HUD update
            UpdateHUD();

            // Show start menu with pause music
            _isPlaying = true;
            _gameReady = false;
            _isGamePaused = true;
            _hudController?.SetHUDVisible(false);
            _hudController?.ShowStartMenu("SHOOTER", LastScore > 0 ? LastScore : null, "PRESS SPACE TO START");
            _audioController?.PauseMusic();

            _logger.LogInfo("ShooterGame", "OnStart completed — start menu shown.");
        }

        /// <summary>
        /// Called when the game ends (timeout, victory, etc.).
        /// Reports session data to GameManager via MinigameSessionData.
        /// </summary>
        public void OnEnd()
        {
            if (_hasGameEnded) return;
            _hasGameEnded = true;

            _gunController?.SetAimPreviewActive(false);
            _gunController?.SetDebugInputAllowed(false);

            // Stop all music immediately (no fade — game over)
            _audioController?.StopAllMusic();

            // Store final score for MainMenu to display
            LastScore = _score;

            // ── Create and report session data ──────────────────────────────
            var sessionData = new MinigameSessionData
            {
                MinigameName = "Shooter",
                Score = _score,
                Completed = _timeRemaining <= 0, // Completed if timer ran out (played full duration)
                StartTime = DateTime.Now.AddSeconds(-(_gameDuration - _timeRemaining)),
                EndTime = DateTime.Now,
                DurationSeconds = _gameDuration - _timeRemaining,
                CustomStats = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "TimeRemaining", _timeRemaining },
                    { "GameDuration", _gameDuration }
                }
            };

            if (GameManager.Instance != null)
            {
                GameManager.Instance.CollectMinigameData(sessionData);
                _logger.LogInfo("ShooterGame",
                    $"Session data reported: Score={sessionData.Score}, Duration={sessionData.DurationSeconds:F1}s");
            }
            else
            {
                _logger.LogWarning("ShooterGame", "GameManager.Instance is null — cannot report session data.");
            }

            _isPlaying = false;
            _gameReady = false;

            if (_waveCo != null)
            {
                StopCoroutine(_waveCo);
                _waveCo = null;
            }

            CancelInvoke(nameof(TimerTick));

            // Deactivate all targets
            if (_targetManager != null)
            {
                _targetManager.DeactivateAll();
            }

            // Unsubscribe from GameManager
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.OnScoreChanged -= HandleScoreChanged;
                _deps.GameManager.OnGamePaused -= HandleGamePaused;
                _deps.GameManager.OnGameResumed -= HandleGameResumed;
                _deps.GameManager.EndGame();
            }

            // Unsubscribe from gun events
            if (_gunController != null)
            {
                _gunController.OnAmmoChanged -= HandleAmmoChanged;
                _gunController.OnReloadStarted -= HandleReloadStarted;
                _gunController.OnReloadCompleted -= HandleReloadCompleted;
                _gunController.OnTargetHit -= HandleTargetHit;
            }

            // Show start/game-over menu in-scene with final score
            _hudController?.SetHUDVisible(false);
            _hudController?.HidePauseOverlay();
            _hudController?.ShowStartMenu("GAME OVER", LastScore, "PRESS SPACE TO RESTART");

            // Play pause music for the game over screen
            _audioController?.PauseMusic();

            _logger.LogInfo("ShooterGame", $"Game ended. Final score: {_score}");
        }

        // ── Unity Lifecycle (Editor-Only) ───────────────────────────────────

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only auto-initialization. When <see cref="_debugSkipStartMenu"/> is enabled,
        /// bypasses the start menu and begins gameplay immediately for rapid iteration.
        /// In production builds, the game is initialized via <see cref="OnStart"/> by SceneLoader.
        /// </summary>
        private void Start()
        {
            _hasGameEnded = false;

            if (_debugSkipStartMenu)
            {
                _logger.LogInfo("ShooterGame", "Debug skip: bypassing start menu (UNITY_EDITOR only).");

                _gunController?.SetAimPreviewActive(true);
                _gunController?.SetDebugInputAllowed(true);

                _score = 0;
                _timeRemaining = _gameDuration;

                // Subscribe to gun events
                if (_gunController != null)
                {
                    _gunController.OnAmmoChanged += HandleAmmoChanged;
                    _gunController.OnReloadStarted += HandleReloadStarted;
                    _gunController.OnReloadCompleted += HandleReloadCompleted;
                    _gunController.OnTargetHit += HandleTargetHit;
                }

                UpdateHUD();

                _isPlaying = true;
                _gameReady = false;
                _isGamePaused = true;

                BeginGame();

                _logger.LogInfo("ShooterGame", "Debug skip: start menu bypassed, game began immediately.");
            }
            // Normal flow is handled by SceneLoader → OnStart(deps). No auto-init here.
        }

        /// <summary>
        /// Debug/testing entry point. Right-click the component → "Start Game (Debug)".
        /// Fully self-contained — no Bootstrap/GameManager needed.
        /// Wrapped in UNITY_EDITOR to prevent bypassing the GameManager lifecycle in production builds.
        /// </summary>
        [ContextMenu("Start Game (Debug)")]
        public void DebugStartGame()
        {
            if (_isPlaying) return;

            _logger.LogInfo("ShooterGame", "DebugStartGame invoked from editor context menu.");

            _hasGameEnded = false;
            _hudController?.HideStartMenu();

            _gunController?.SetAimPreviewActive(true);
            _gunController?.SetDebugInputAllowed(true);

            _score = 0;
            _timeRemaining = _gameDuration;

            if (_targetManager == null)
            {
                _logger.LogError("ShooterGame", "TargetManager not assigned!", ServiceErrorCode.MinigameDependencyMissing);
            }

            // Subscribe to gun events
            if (_gunController != null)
            {
                _gunController.OnAmmoChanged += HandleAmmoChanged;
                _gunController.OnReloadStarted += HandleReloadStarted;
                _gunController.OnReloadCompleted += HandleReloadCompleted;
                _gunController.OnTargetHit += HandleTargetHit;
            }

            // Initial HUD update
            UpdateHUD();

            // Show start menu with pause music
            _isPlaying = true;
            _gameReady = false;
            _isGamePaused = true;
            _hudController?.SetHUDVisible(false);
            _hudController?.ShowStartMenu("SHOOTER", LastScore > 0 ? LastScore : null, "PRESS SPACE TO START");
            _audioController?.PauseMusic();

            _logger.LogInfo("ShooterGame", "DebugStartGame complete — start menu shown.");
        }
#endif

        // ── Button Handlers ─────────────────────────────────────────────────

        /// <summary>
        /// Called when the player clicks the start button on the start/game-over panel.
        /// </summary>
        private void HandleStartClicked()
        {
            if (!_isPlaying && _hasGameEnded)
            {
                // Game over screen — restart the game
                _logger.LogInfo("ShooterGame", "Restarting via start button...");
                if (_deps != null)
                    OnStart(_deps);
#if UNITY_EDITOR
                else
                    DebugStartGame();
#endif
                return;
            }

            if (_isGamePaused)
            {
                if (!_gameReady)
                {
                    // First-time unpause — begin the game
                    BeginGame();
                }
                else if (_deps?.GameManager != null)
                {
                    // Subsequent pause/resume via GameManager
                    _deps.GameManager.ResumeGame();
                }
            }
        }

        /// <summary>Load the main menu scene (e.g., from a UI button).</summary>
        public void LoadMainMenu()
        {
            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadScene(_mainMenuSceneIndex);
            }
            else
            {
                _logger.LogWarning("ShooterGame", "SceneLoader.Instance is null — cannot load MainMenu.");
            }
        }

        /// <summary>Add score directly (used by debug mode and by Target when hit).</summary>
        public void AddScore(int delta)
        {
            _score += delta;
            UpdateHUD();
        }

        // ── Wave Progression ───────────────────────────────────────────────

        private IEnumerator CoWaveProgression()
        {
            int waveCount = Mathf.Min(_rowOrder.Length, _scoreThresholds.Length, _batchIntervals.Length);

            for (int wave = 0; wave < waveCount && _isPlaying; wave++)
            {
                string rowLabel = _rowOrder[wave];
                int threshold = _scoreThresholds[wave];
                float interval = _batchIntervals[wave];

                // Show wave announcement on HUD
                _hudController?.ShowWave(rowLabel);

                // Set music intensity when entering a new wave
                int intensity = _rowOrder[wave] switch
                {
                    "Easy" => 1,
                    "Medium" => 2,
                    "Hard" => 3,
                    _ => 3
                };
                _audioController?.SetIntensity(intensity);

                yield return CoRunRowWave(rowLabel, threshold, interval);

                if (!_isPlaying) yield break;

                if (wave < _rowOrder.Length - 1)
                {
                    yield return new WaitForSeconds(_waveTransitionDelay);
                }
            }
        }

        private IEnumerator CoRunRowWave(string rowLabel, int scoreThreshold, float batchInterval)
        {
            while (_isPlaying)
            {
                if (_score >= scoreThreshold)
                    yield break;

                // Try to activate a batch
                if (_targetManager != null)
                {
                    _targetManager.ActivateBatch(rowLabel);
                }

                // Wait before next activation attempt
                float elapsed = 0f;
                while (elapsed < batchInterval && _isPlaying)
                {
                    if (_score >= scoreThreshold)
                        yield break;

                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
        }

        // ── Timer ───────────────────────────────────────────────────────────

        private void TimerTick()
        {
            if (!_isPlaying) return;

            _timeRemaining--;
            UpdateHUD();

            if (_timeRemaining <= 0)
            {
                HandleTimeout();
            }
        }

        private void HandleTimeout()
        {
            _logger.LogInfo("ShooterGame", "Time ran out!");
            OnEnd();
        }

        // ── HUD ─────────────────────────────────────────────────────────────

        private void UpdateHUD()
        {
            if (_hudController == null) return;

            _hudController.UpdateTimer(_timeRemaining);
            _hudController.UpdateScore(_score);
            if (_audioController != null)
                _hudController.UpdateMusicLabel(_audioController.CurrentIntensityLabel);
        }

        private void HandleScoreChanged(int newScore)
        {
            _score = newScore;
            if (_hudController != null)
            {
                _hudController.UpdateScore(newScore);
            }
        }

        private void HandleAmmoChanged(int current, int max)
        {
            if (_hudController != null)
            {
                _hudController.UpdateAmmo(current, max);
            }
        }

        private void HandleReloadStarted()
        {
            if (_hudController != null)
            {
                _hudController.ShowReloading(true);
            }
        }

        private void HandleReloadCompleted()
        {
            if (_hudController != null)
            {
                _hudController.ShowReloading(false);
            }
        }

        /// <summary>Called when GameManager pauses the game — hide all HUD elements.</summary>
        private void HandleGamePaused()
        {
            _isGamePaused = true;
            _hudController?.SetHUDVisible(false);
            _hudController?.ShowPauseOverlay("PAUSED");
        }

        /// <summary>Called when GameManager resumes the game — show all HUD elements.</summary>
        private void HandleGameResumed()
        {
            _isGamePaused = false;
            _hudController?.HidePauseOverlay();
            _hudController?.SetHUDVisible(true);
        }

        private void HandleTargetHit(Target target)
        {
            if (target == null) return;

            _score += target.HitScore;

            // In Bootstrap mode, also report to GameManager for global tracking
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.AddScore(target.HitScore);
            }

            _logger.LogInfo("ShooterGame", $"Target hit! Score: {target.HitScore} (total: {_score})");
            UpdateHUD();

            // Trigger score shake effect on the HUD
            _hudController?.PlayScoreShake();
        }

        // ── Unpause / Begin Game ────────────────────────────────────────────

        /// <summary>
        /// Called once when the player unpauses for the first time (or after a GameManager pause/resume cycle).
        /// Starts wave progression and the countdown timer.
        /// </summary>
        private void BeginGame()
        {
            if (_gameReady) return;
            _gameReady = true;
            _isGamePaused = false;

            _hudController?.HideStartMenu();
            _hudController?.HidePauseOverlay();
            _hudController?.SetHUDVisible(true);

            // Start music now that the game is actually beginning
            _audioController?.SetIntensity(1);

            // Start wave progression
            _waveCo = StartCoroutine(CoWaveProgression());

            // Start timer
            InvokeRepeating(nameof(TimerTick), 1f, 1f);

            _logger.LogInfo("ShooterGame", "BeginGame — music, wave progression, and timer started.");
        }

        // ── Input ───────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_isPlaying) return;

            // Debug score increment (only when game is active, not paused)
            if (_gameReady && !_isGamePaused)
            {
                if (Input.GetKeyDown(_debugAddScoreKey) || Input.GetKeyDown(KeyCode.Equals))
                {
                    AddScore(_debugScoreIncrement);
                }
            }
        }
    }
}
