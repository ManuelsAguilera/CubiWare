
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;
using CubiWare.Core.Services;
 
namespace ARcadeRush.Core
{
    [Serializable]
    public class UserData
    {
        public int LastScore;
    }

    public enum GameState { Idle, Playing, Paused, Results }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Idle;
        public int CurrentScore { get; private set; }
        public int LastScore { get; private set; }
        public IMiniGame CurrentMiniGame { get; private set; }

        public event Action OnGameStarted;
        public event Action OnGameEnded;
        public event Action<int> OnScoreChanged;
        public event Action OnGamePaused;
        public event Action OnGameResumed;

        // ── Bootstrap-managed dependencies ────────────────────────────────
        private IDataStore _dataStore;
        private SceneLoader _sceneLoader;
        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        /// <summary>Exposes the data store for other components to resolve.</summary>
        public IDataStore DataStore => _dataStore;

        // ── Session history ────────────────────────────────────────────────
        private readonly List<MinigameSessionData> _sessionHistory = new List<MinigameSessionData>();

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ── Bootstrap Initialization ───────────────────────────────────────

        /// <summary>
        /// Called by BootstrapManager after SceneLoader is initialized.
        /// Loads persisted user data and sets the initial game state.
        /// </summary>
        public async void Initialize(IDataStore dataStore, SceneLoader sceneLoader)
        {
            _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));

            _logger.LogInfo("GameManager", "Initializing with data store and scene loader.");

            // Wire up LogManager with the same data store for interaction event persistence
            LogManager.Instance.SetDataStore(_dataStore);

            await LoadUserDataAsync();

            State = GameState.Idle;
            _logger.LogInfo("GameManager", $"Initialized. LastScore={LastScore}, State={State}");
        }

        // ── Minigame Session Recording ─────────────────────────────────────

        /// <summary>
        /// Records the result of a completed minigame session.
        /// Stores it in the session history and updates LastScore.
        /// </summary>
        public void CollectMinigameData(MinigameSessionData sessionData)
        {
            _sessionHistory.Add(sessionData);
            LastScore = sessionData.Score;
            _logger.LogInfo("GameManager",
                $"Collected minigame data: '{sessionData.MinigameName}' Score={sessionData.Score}, " +
                $"Duration={sessionData.DurationSeconds:F1}s, HistoryCount={_sessionHistory.Count}");
        }

        // ── User Data Persistence ──────────────────────────────────────────

        /// <summary>
        /// Saves user data (last score, session history) through the IDataStore.
        /// </summary>
        public async Task SaveUserDataAsync()
        {
            if (_dataStore == null)
            {
                _logger.LogError("GameManager", "Cannot save — _dataStore is null.", ServiceErrorCode.NotInitialized);
                return;
            }

            try
            {
                var userData = new UserData
                {
                    LastScore = LastScore
                };

                await _dataStore.SaveAsync("user_data", userData);
                _logger.LogInfo("GameManager", $"User data saved. LastScore={LastScore}");
            }
            catch (Exception ex)
            {
                _logger.LogError("GameManager", $"Failed to save user data: {ex.Message}", ServiceErrorCode.DataStoreWriteFailed);
            }
        }

        /// <summary>
        /// Loads user data from the IDataStore and restores LastScore.
        /// </summary>
        public async Task LoadUserDataAsync()
        {
            if (_dataStore == null)
            {
                _logger.LogError("GameManager", "Cannot load — _dataStore is null.", ServiceErrorCode.NotInitialized);
                return;
            }

            try
            {
                UserData userData = await _dataStore.LoadAsync<UserData>("user_data");
                if (userData != null)
                {
                    LastScore = userData.LastScore;
                    _logger.LogInfo("GameManager", $"User data loaded. LastScore={LastScore}");
                }
                else
                {
                    LastScore = 0;
                    _logger.LogInfo("GameManager", "No saved user data found — starting fresh.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("GameManager", $"Failed to load user data: {ex.Message}", ServiceErrorCode.DataStoreReadFailed);
                LastScore = 0;
            }
        }

        // ── Self-Destruct ──────────────────────────────────────────────────

        /// <summary>
        /// Saves user data and destroys this GameObject.
        /// Called when transitioning away from the bootstrap-managed lifecycle.
        /// </summary>
        public async void SelfDestruct()
        {
            _logger.LogInfo("GameManager", "SelfDestruct called — saving data and destroying.");
            await SaveUserDataAsync();
            Destroy(gameObject);
        }

        // ── Existing Game State API ────────────────────────────────────────

        /// <summary>
        /// Full lifecycle entry: sets state and calls <see cref="IMiniGame.OnStart"/>.
        /// Use when GameManager is the primary entry point (e.g. Unity Start(),
        /// BootstrapManager direct load).
        /// Do NOT call from inside an existing OnStart() — use RegisterGame() instead.
        /// </summary>
        public void StartGame(IMiniGame game)
        {
            if (game == null)
            {
                _logger.LogError("GameManager", "Cannot start a null minigame.", ServiceErrorCode.InvalidState);
                return;
            }

            CurrentMiniGame = game;
            State = GameState.Playing;
            CurrentScore = 0;
            
            var deps = new MiniGameDependencies
            {
                GameManager   = this,
                Camera        = CameraFeedCtrl.Instance ?? FindFirstObjectByType<CameraFeedCtrl>(),
                MediaPipe     = MediaPipeController.Instance,
                LLM           = LLMConnector.Instance,
                EmotionBridge = ARcadeRush.EmotionDetection.EmotionGameBridge.Instance,
            };

            _logger.LogInfo("GameManager",
                $"StartGame: Camera={deps.Camera != null}, " +
                $"MediaPipe={deps.MediaPipe != null}, " +
                $"LLM={deps.LLM != null}, " +
                $"EmotionBridge={deps.EmotionBridge != null}");

            game.OnStart(deps);

            OnScoreChanged?.Invoke(CurrentScore);
            OnGameStarted?.Invoke();
            
        }

        /// <summary>
        /// Registers an already-active minigame with the GameManager without
        /// re-invoking <see cref="IMiniGame.OnStart"/>. Use when the minigame was
        /// entered through SceneLoader (which already called OnStart).
        /// Sets state, fires events, and establishes scoring ownership.
        /// </summary>
        public void RegisterGame(IMiniGame game)
        {
            if (game == null)
            {
                _logger.LogError("GameManager", "Cannot register a null minigame.", ServiceErrorCode.InvalidState);
                return;
            }

            CurrentMiniGame = game;
            State = GameState.Playing;
            CurrentScore = 0;

            OnScoreChanged?.Invoke(CurrentScore);
            OnGameStarted?.Invoke();

            _logger.LogInfo("GameManager", $"Minigame '{game.GetType().Name}' registered (OnStart was already called by SceneLoader).");
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
                OnGamePaused?.Invoke();
            }
        }

        public void ResumeGame()
        {
            if (State == GameState.Paused)
            {
                State = GameState.Playing;
                Time.timeScale = 1f;
                OnGameResumed?.Invoke();
            }
        }
    }
}
