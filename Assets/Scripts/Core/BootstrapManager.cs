using System;
using System.Collections;
using UnityEngine;
using CubiWare.Core.Logging;
using CubiWare.Core.Services;
using CubiWare.Core.Interfaces;

namespace CubiWare.Core
{
    /// <summary>
    /// BootstrapManager is a MonoBehaviour singleton that orchestrates the entire
    /// application initialization sequence. It lives in the Bootstrap scene and
    /// ensures all services are initialized in the correct order before loading
    /// the MainMenu scene.
    ///
    /// This fixes the "init bridge" problem — previously IMiniGame.OnStart() had
    /// no caller and GameManager.StartGame() was never invoked.
    /// </summary>
    public class BootstrapManager : MonoBehaviour
    {
        public static BootstrapManager Instance { get; private set; }

        public enum BootstrapState
        {
            NotStarted,
            Initializing,
            Initialized,
            ShuttingDown,
            ShutDown
        }

        public BootstrapState State { get; private set; }

        private ServiceLogger _logger;
        private PlayerPrefsDataStore _dataStore;

        // ── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            State = BootstrapState.NotStarted;

            Debug.Log("[BootstrapManager] Awake — Instance set, DontDestroyOnLoad applied.");
        }

        private void Start()
        {
            StartCoroutine(InitializeAsync());
        }

        private void OnApplicationQuit()
        {
            StartCoroutine(ShutdownAsync());
        }

        // ── Initialization Sequence ────────────────────────────────────────

        /// <summary>
        /// Runs the complete bootstrap initialization sequence as a coroutine.
        /// Each step is yielded to allow async services to begin their own init.
        /// </summary>
        private IEnumerator InitializeAsync()
        {
            State = BootstrapState.Initializing;

            // ── Step 1: Logger ─────────────────────────────────────────────
            _logger = ServiceLogger.Instance;
            _logger.LogInfo("BootstrapManager", "Bootstrap starting — State=Initializing");

            yield return null;

            // ── Step 2: Create data store ──────────────────────────────────
            _dataStore = new PlayerPrefsDataStore();
            _logger.LogInfo("BootstrapManager", "PlayerPrefsDataStore created.");

            yield return null;

            // ── Step 3: Initialize SceneLoader ─────────────────────────────
            SceneLoader.Instance.Initialize(this);
            _logger.LogInfo("BootstrapManager", "SceneLoader initialized.");

            yield return null;

            // ── Step 4: Initialize GameManager ─────────────────────────────
            GameManager.Instance.Initialize(_dataStore, SceneLoader.Instance);
            _logger.LogInfo("BootstrapManager", "GameManager initialized with data store and scene loader.");

            yield return null;

            // ── Step 5: CameraFeedProvider (via CameraFeedCtrl) ────────────
            // CameraFeedCtrl already creates its provider in Awake, which runs
            // before this coroutine. We just log confirmation.
            if (CameraFeedCtrl.Instance != null)
            {
                _logger.LogInfo("BootstrapManager", "CameraFeedProvider ready (initialized in CameraFeedCtrl.Awake).");
            }
            else
            {
                _logger.LogError("BootstrapManager", "CameraFeedCtrl.Instance is null!", ServiceErrorCode.NotInitialized);
            }

            yield return null;

            // ── Step 6: HandDetectorService (via MediaPipeController) ──────
            if (MediaPipeController.Instance != null)
            {
                _logger.LogInfo("BootstrapManager", "HandDetectorService will be initialized by MediaPipeController.Start().");
            }
            else
            {
                _logger.LogError("BootstrapManager", "MediaPipeController.Instance is null!", ServiceErrorCode.NotInitialized);
            }

            yield return null;

            // ── Step 7: FaceDetectorService (via MediaPipeController) ──────
            // Same MediaPipeController handles both hand and face detection.
            _logger.LogInfo("BootstrapManager", "FaceDetectorService will be initialized by MediaPipeController.Start().");

            yield return null;

            // ── Step 8: GroqLLMService (via LLMConnector) ─────────────────
            if (LLMConnector.Instance != null)
            {
                _logger.LogInfo("BootstrapManager", "GroqLLMService initialized by LLMConnector.Awake().");
            }
            else
            {
                _logger.LogError("BootstrapManager", "LLMConnector.Instance is null!", ServiceErrorCode.NotInitialized);
            }

            yield return null;

            // ── Step 9-10: Mark as Initialized ─────────────────────────────
            State = BootstrapState.Initialized;
            _logger.LogInfo("BootstrapManager", "Bootstrap complete — State=Initialized");

            yield return null;

            // ── Step 11-12: Load MainMenu ──────────────────────────────────
            _logger.LogInfo("BootstrapManager", "Bootstrap complete, loading MainMenu");
            SceneLoader.Instance.LoadSceneAsync("MainMenu", () =>
            {
                _logger.LogInfo("BootstrapManager", "MainMenu loaded.");
            });
        }

        // ── Shutdown Sequence ──────────────────────────────────────────────

        /// <summary>
        /// Runs the reverse shutdown sequence, saving data and releasing
        /// resources in the correct order.
        /// </summary>
        public IEnumerator ShutdownAsync()
        {
            if (State == BootstrapState.ShuttingDown || State == BootstrapState.ShutDown)
                yield break;

            State = BootstrapState.ShuttingDown;
            _logger.LogInfo("BootstrapManager", "Shutdown starting — State=ShuttingDown");

            yield return null;

            // ── Reverse Step 8: Shutdown GroqLLMService ────────────────────
            _logger.LogInfo("BootstrapManager", "Shutting down GroqLLMService...");
            // GroqLLMService shutdown is handled by its own OnDestroy
            yield return null;

            // ── Reverse Step 7: Shutdown FaceDetectorService ───────────────
            _logger.LogInfo("BootstrapManager", "Shutting down FaceDetectorService...");
            // Shutdown handled by MediaPipeController.OnDestroy
            yield return null;

            // ── Reverse Step 6: Shutdown HandDetectorService ───────────────
            _logger.LogInfo("BootstrapManager", "Shutting down HandDetectorService...");
            // Shutdown handled by MediaPipeController.OnDestroy
            yield return null;

            // ── Reverse Step 5: Shutdown CameraFeedProvider ────────────────
            _logger.LogInfo("BootstrapManager", "Shutting down CameraFeedProvider...");
            if (CameraFeedCtrl.Instance != null)
            {
                CameraFeedCtrl.Instance.StopCamera();
            }
            yield return null;

            // ── Reverse Step 4: Shutdown GameManager (save data) ──────────
            _logger.LogInfo("BootstrapManager", "Shutting down GameManager...");
            if (GameManager.Instance != null)
            {
                // Save user data before shutdown
                _ = GameManager.Instance.SaveUserDataAsync();
            }
            yield return null;

            State = BootstrapState.ShutDown;
            _logger.LogInfo("BootstrapManager", "Shutdown complete — State=ShutDown");
        }
    }
}
