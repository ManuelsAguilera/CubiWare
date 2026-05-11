using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ARcadeRush.Core;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace CubiWare.Core
{
    /// <summary>
    /// Per-scene lifecycle manager for minigames.
    /// Place this MonoBehaviour in each minigame scene. It discovers the IMiniGame
    /// implementer, owns the session lifecycle (start, end, pause, resume, time warning),
    /// and reports completed session data back to the GameManager.
    ///
    /// The minigame itself does NOT need to implement IMiniGameLifecycle — MiniGameManager
    /// handles that contract. If the minigame does implement IMiniGameLifecycle, lifecycle
    /// events are forwarded to it.
    /// </summary>
    public class MiniGameManager : MonoBehaviour, IMiniGameLifecycle
    {
        /// <summary>The IMiniGame found in the current scene.</summary>
        private IMiniGame _miniGame;

        /// <summary>Tracks the current session data as it accumulates.</summary>
        private MinigameSessionData _currentSession;

        /// <summary>Time.time when the session started (for DurationSeconds calculation).</summary>
        private float _gameStartTime;

        /// <summary>Whether the session is actively tracking.</summary>
        private bool _sessionActive;

        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        // ── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            // Discover the IMiniGame in the scene
            _miniGame = FindFirstObjectByType<MonoBehaviour>() as IMiniGame;
            if (_miniGame != null)
            {
                _logger.LogInfo("MiniGameManager", $"Discovered IMiniGame: {_miniGame.GetType().Name}");
            }
            else
            {
                _logger.LogError("MiniGameManager",
                    "No IMiniGame found in scene — MiniGameManager will be inactive.",
                    ServiceErrorCode.MinigameDependencyMissing);
            }
        }

        private void Start()
        {
            // Begin session tracking
            _currentSession = new MinigameSessionData
            {
                StartTime = DateTime.Now,
                MinigameName = _miniGame?.GetType().Name ?? "Unknown",
                Score = 0,
                Completed = false,
                DurationSeconds = 0f,
                CustomStats = null
            };
            _gameStartTime = Time.time;
            _sessionActive = true;

            _logger.LogInfo("MiniGameManager",
                $"Session started for '{_currentSession.MinigameName}' at {_currentSession.StartTime:HH:mm:ss}");
        }

        private void OnEnable()
        {
            // Subscribe to scene load events for lifecycle awareness
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_sessionActive && _miniGame != null)
            {
                _logger.LogInfo("MiniGameManager",
                    $"MiniGameManager destroyed while session active — auto-ending session for '{_currentSession.MinigameName}'.");
                EndSession(0, false);
                ReportSessionToGameManager();
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _logger.LogInfo("MiniGameManager", $"Scene loaded: '{scene.name}' (mode={mode})");
        }

        // ── IMiniGameLifecycle Implementation ──────────────────────────────

        /// <summary>
        /// Called when the game is paused. Forwards to the minigame if it implements
        /// <see cref="IMiniGameLifecycle"/>.
        /// </summary>
        public void OnPause()
        {
            _logger.LogInfo("MiniGameManager", "OnPause called — forwarding to minigame if applicable.");

            if (_miniGame is IMiniGameLifecycle lifecycle)
            {
                lifecycle.OnPause();
            }
        }

        /// <summary>
        /// Called when the game resumes. Forwards to the minigame if it implements
        /// <see cref="IMiniGameLifecycle"/>.
        /// </summary>
        public void OnResume()
        {
            _logger.LogInfo("MiniGameManager", "OnResume called — forwarding to minigame if applicable.");

            if (_miniGame is IMiniGameLifecycle lifecycle)
            {
                lifecycle.OnResume();
            }
        }

        /// <summary>
        /// Called when the game timer is about to expire.
        /// Forwards to the minigame if it implements <see cref="IMiniGameLifecycle"/>.
        /// </summary>
        /// <param name="remainingSeconds">The number of seconds remaining.</param>
        public void OnTimeWarning(float remainingSeconds)
        {
            _logger.LogInfo("MiniGameManager",
                $"OnTimeWarning called — {remainingSeconds:F1}s remaining. Forwarding to minigame if applicable.");

            if (_miniGame is IMiniGameLifecycle lifecycle)
            {
                lifecycle.OnTimeWarning(remainingSeconds);
            }
        }

        // ── Session Management ─────────────────────────────────────────────

        /// <summary>
        /// Ends the current session with the given final score and completion status.
        /// Populates the <see cref="MinigameSessionData"/> with timing information.
        /// </summary>
        /// <param name="finalScore">The final score achieved.</param>
        /// <param name="completed">Whether the minigame was completed (as opposed to aborted).</param>
        /// <returns>The completed <see cref="MinigameSessionData"/> struct.</returns>
        public MinigameSessionData EndSession(int finalScore, bool completed)
        {
            if (!_sessionActive)
            {
                _logger.LogWarning("MiniGameManager", "EndSession called but no active session to end.");
                return _currentSession;
            }

            _sessionActive = false;
            _currentSession.Score = finalScore;
            _currentSession.Completed = completed;
            _currentSession.EndTime = DateTime.Now;
            _currentSession.DurationSeconds = Time.time - _gameStartTime;

            _logger.LogInfo("MiniGameManager",
                $"Session ended: Score={finalScore}, Completed={completed}, Duration={_currentSession.DurationSeconds:F1}s");

            return _currentSession;
        }

        /// <summary>
        /// Reports the current (completed) session data to <see cref="GameManager.CollectMinigameData"/>.
        /// Should be called after <see cref="EndSession"/>.
        /// </summary>
        public void ReportSessionToGameManager()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CollectMinigameData(_currentSession);
                _logger.LogInfo("MiniGameManager",
                    $"Session data reported to GameManager for '{_currentSession.MinigameName}'.");
            }
            else
            {
                _logger.LogError("MiniGameManager",
                    "GameManager.Instance is null — cannot report session data. Ensure the Bootstrap scene is active.",
                    ServiceErrorCode.MinigameDependencyMissing);
            }
        }
    }
}
