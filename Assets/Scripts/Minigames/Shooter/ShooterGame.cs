using System.Collections;
using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.UI;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Shooter minigame — the player aims with their index finger and shoots by closing their fist.
    /// Uses pre-placed scene targets managed by TargetManager.
    /// Progresses through difficulty rows (Easy → Medium → Hard) based on score thresholds.
    /// Hits bandits for points (varies by row), hits innocents for penalties.
    /// 90-second timer. On timeout, shows Game Over screen with final score and restart/menu options.
    /// Implements IMiniGame for scene-independent orchestration.
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
        [SerializeField] private int _thisSceneIndex = 2;

        [Header("Wave Progression")]
        [SerializeField] private string[] _rowOrder = { "Easy", "Medium", "Hard" };
        [SerializeField] private int[] _scoreThresholds = { 30, 70, int.MaxValue };
        [SerializeField] private float[] _batchIntervals = { 3f, 2.5f, 2f };
        [SerializeField] private float _waveTransitionDelay = 1f;

        [Header("Debug")]
        [SerializeField] private bool _debugSkipStartMenu = false;
        [SerializeField] private int _debugScoreIncrement = 10;
        [SerializeField] private KeyCode _debugAddScoreKey = KeyCode.KeypadPlus;

        /// <summary>Must match MG_Shooter in Build Settings (index 2).</summary>
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

        // ------ Auto-Initialization ------

        private void Start()
        {
            _hasGameEnded = false;

            if (_debugSkipStartMenu)
            {
                // Skip the start menu — go directly into gameplay for rapid testing.
                _gunController?.SetAimPreviewActive(true);
                _gunController?.SetDebugInputAllowed(true);

                _score = 0;
                _timeRemaining = _gameDuration;

                // Subscribe to gun events (same as DebugStartGame/OnStart)
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

                Debug.Log("[ShooterGame] Debug skip: start menu bypassed, game began immediately.");
                return;
            }

            // Normal flow — show start menu, wait for button click to play.
            _hudController?.SetHUDVisible(false);
            _hudController?.ShowStartMenu("SHOOTER", LastScore > 0 ? LastScore : null, "PRESS SPACE TO START");
            _audioController?.PauseMusic();

            _isPlaying = true;
            _gameReady = false;
            _isGamePaused = true;

            // Subscribe to button events from the start menu panel
            if (_hudController != null)
            {
                _hudController.OnStartClicked += HandleStartClicked;
                _hudController.OnMainMenuClicked += LoadMainMenu;
            }
        }

        /// <summary>
        /// Called when the player clicks the start button on the start/game-over panel.
        /// </summary>
        private void HandleStartClicked()
        {
            if (!_isPlaying && _hasGameEnded)
            {
                // Game over screen — restart the game
                Debug.Log("[ShooterGame] Restarting via start button...");
                if (_deps != null)
                    OnStart(_deps);
                else
                    DebugStartGame();
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

        /// <summary>
        /// Debug/testing entry point. Right-click the component → "Start Game (Debug)".
        /// Fully self-contained, no Bootstrap/GameManager needed.
        /// </summary>
        [ContextMenu("Start Game (Debug)")]
        public void DebugStartGame()
        {
            if (_isPlaying) return;

            _hasGameEnded = false;
            _hudController?.HideStartMenu();

            _gunController?.SetAimPreviewActive(true);
            _gunController?.SetDebugInputAllowed(true);

            _score = 0;
            _timeRemaining = _gameDuration;

            if (_targetManager == null)
            {
                Debug.LogError("[ShooterGame] TargetManager not assigned!");
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

            Debug.Log("[ShooterGame] Game started — start menu shown with pause music.");
        }

        /// <summary>IMiniGame entry point (used when Bootstrap/GameManager are active).</summary>
        public void OnStart(MiniGameDependencies deps)
        {
            _hasGameEnded = false;
            _hudController?.HideStartMenu();

            _gunController?.SetAimPreviewActive(true);
            _gunController?.SetDebugInputAllowed(true);

            _deps = deps;
            _score = 0;
            _timeRemaining = _gameDuration;

            if (_targetManager == null)
            {
                Debug.LogError("[ShooterGame] TargetManager not assigned!");
            }

            // Subscribe to score changes for HUD updates
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.OnScoreChanged += HandleScoreChanged;
                _deps.GameManager.OnGamePaused += HandleGamePaused;
                _deps.GameManager.OnGameResumed += HandleGameResumed;
            }

            // Subscribe to gun ammo/reload events and target hit (scoring handled via HandleTargetHit)
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

            Debug.Log("[ShooterGame] Game started — start menu shown with pause music.");
        }

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

            // Unsubscribe from GameManager (only if Bootstrap path)
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
        }

        /// <summary>Add score directly (used by debug mode and by Target when hit).</summary>
        public void AddScore(int delta)
        {
            _score += delta;
            UpdateHUD();
        }

        // --- Wave Progression ---

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
                // Maps from row label so intensity follows the row order (up and down):
                // Easy → 1, Medium → 2, Hard → 3
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

        // --- Timer ---

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
            Debug.Log("[ShooterGame] Time ran out!");
            OnEnd();
        }

        // --- HUD ---

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

            Debug.Log($"[ShooterGame] Target hit! Score: {target.HitScore} (total: {_score})");
            UpdateHUD();

            // Trigger score shake effect on the HUD
            _hudController?.PlayScoreShake();
        }

        // --- Unpause / Begin Game ---

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

            Debug.Log("[ShooterGame] Game unpaused — music, wave progression, and timer started.");
        }

        // --- Input ---

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

        /// <summary>Load the main menu scene (e.g., from a UI button).</summary>
        public void LoadMainMenu()
        {
            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadScene(_mainMenuSceneIndex);
            }
            else
            {
                Debug.LogWarning("[ShooterGame] SceneLoader.Instance is null — cannot load MainMenu.");
            }
        }
    }
}
