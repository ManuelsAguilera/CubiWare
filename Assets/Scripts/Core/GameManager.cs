using System;
using UnityEngine;

namespace ARcadeRush.Core
{
    public enum GameState { Idle, Playing, Paused, Results }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Idle;
        public int CurrentScore { get; private set; }
        public IMiniGame CurrentMiniGame { get; private set; }

        public event Action OnGameStarted;
        public event Action OnGameEnded;
        public event Action<int> OnScoreChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
        }

        public void StartGame(IMiniGame game)
        {
            if (game == null)
            {
                Debug.LogError("GameManager: Cannot start a null minigame.");
                return;
            }

            CurrentMiniGame = game;
            State = GameState.Playing;
            CurrentScore = 0;
            
            OnScoreChanged?.Invoke(CurrentScore);
            OnGameStarted?.Invoke();
        }

        public void EndGame()
        {
            State = GameState.Results;
            OnGameEnded?.Invoke();
            CurrentMiniGame = null;
        }

        public void AddScore(int delta)
        {
            if (State != GameState.Playing) return;

            CurrentScore += delta;
            OnScoreChanged?.Invoke(CurrentScore);
        }

        public void PauseGame()
        {
            if (State == GameState.Playing)
            {
                State = GameState.Paused;
                Time.timeScale = 0f;
            }
        }

        public void ResumeGame()
        {
            if (State == GameState.Paused)
            {
                State = GameState.Playing;
                Time.timeScale = 1f;
            }
        }
    }
}
