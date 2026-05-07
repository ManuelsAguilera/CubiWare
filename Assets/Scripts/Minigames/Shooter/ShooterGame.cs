using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.UI;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Shooter minigame — the player aims with their index finger and shoots by closing their fist.
    /// Hits bandits for +10 points, hits innocents for -20 points.
    /// 90-second timer. On timeout, returns to MainMenu.
    /// Implements IMiniGame for scene-independent orchestration.
    /// </summary>
    public class ShooterGame : MonoBehaviour, IMiniGame
    {
        [Header("References")]
        [SerializeField] private ShooterHandController _handController;
        [SerializeField] private TargetSpawner _targetSpawner;
        [SerializeField] private HUDController _hudController;

        [Header("Game Settings")]
        [SerializeField] private int _gameDuration = 90;
        [SerializeField] private int _mainMenuSceneIndex = 1;

        /// <summary>Must match MG_Shooter in Build Settings (index 2 after cleanup).</summary>
        public int SceneIndex => 2;

        private MiniGameDependencies _deps;
        private int _timeRemaining;
        private bool _isPlaying = false;

        public void OnStart(MiniGameDependencies deps)
        {
            _deps = deps;
            _isPlaying = true;
            _timeRemaining = _gameDuration;

            Debug.Log($"[ShooterGame] Game started! Duration: {_gameDuration}s");

            // Start spawning targets
            if (_targetSpawner != null)
            {
                _targetSpawner.StartSpawning();
            }

            // Subscribe to score changes for HUD updates
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.OnScoreChanged += HandleScoreChanged;
            }

            // Initial HUD update
            UpdateHUD();

            // Start timer
            InvokeRepeating(nameof(TimerTick), 1f, 1f);
        }

        public void OnEnd()
        {
            _isPlaying = false;
            CancelInvoke(nameof(TimerTick));

            // Stop spawning and clear targets
            if (_targetSpawner != null)
            {
                _targetSpawner.StopSpawning();
            }

            // Unsubscribe
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.OnScoreChanged -= HandleScoreChanged;
            }

            Debug.Log($"[ShooterGame] Game ended. Final score: {(_deps?.GameManager?.CurrentScore ?? 0)}");

            // End game via GameManager
            if (_deps?.GameManager != null)
            {
                _deps.GameManager.EndGame();
            }

            // Return to MainMenu after 2-second delay
            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadSceneDelayed(_mainMenuSceneIndex, 2f);
            }
        }

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

        private void UpdateHUD()
        {
            if (_hudController == null) return;

            _hudController.UpdateTimer(_timeRemaining);

            if (_deps?.GameManager != null)
            {
                _hudController.UpdateScore(_deps.GameManager.CurrentScore);
            }
        }

        private void HandleScoreChanged(int newScore)
        {
            if (_hudController != null)
            {
                _hudController.UpdateScore(newScore);
            }
        }

        private void HandleTimeout()
        {
            Debug.Log("[ShooterGame] Time ran out!");
            OnEnd();
        }
    }
}
