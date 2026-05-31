using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using CubiWare.Core;
using CubiWare.Core.Logging;

namespace ARcadeRush.Core
{
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }

        private BootstrapManager _bootstrapManager;
        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
        }

        // ── Bootstrap Initialization ───────────────────────────────────────

        /// <summary>
        /// Called by BootstrapManager to register itself for coordinated startup.
        /// </summary>
        public void Initialize(BootstrapManager bootstrap)
        {
            _bootstrapManager = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
            _logger.LogInfo("SceneLoader", "Initialized by BootstrapManager.");
        }

        // ── Legacy Load (build index) ──────────────────────────────────────

        public void LoadScene(int index, LoadSceneMode mode = LoadSceneMode.Single)
        {
            _logger.LogInfo("SceneLoader", $"LoadScene called: buildIndex={index}, mode={mode}");
            SceneManager.LoadSceneAsync(index, mode);
        }

        // ── Delayed Load (legacy) ──────────────────────────────────────────

        public void LoadSceneDelayed(int index, float delay, LoadSceneMode mode = LoadSceneMode.Single)
        {
            _logger.LogInfo("SceneLoader", $"LoadSceneDelayed called: buildIndex={index}, delay={delay:F2}s");
            StartCoroutine(CoLoadSceneDelayed(index, delay, mode));
        }

        private IEnumerator CoLoadSceneDelayed(int index, float delay, LoadSceneMode mode)
        {
            yield return new WaitForSeconds(delay);
            _logger.LogInfo("SceneLoader", $"Delayed load executing: buildIndex={index}");
            SceneManager.LoadSceneAsync(index, mode);
        }

        // ── Async Scene Load with Callback ─────────────────────────────────

        /// <summary>
        /// Loads a scene by name asynchronously. When the scene finishes loading,
        /// it finds any IMiniGame in the new scene, calls OnStart(deps),
        /// then invokes the optional completion callback.
        ///
        /// This is the primary scene-loading entry point used by BootstrapManager
        /// and fixes the init bridge: IMiniGame.OnStart() is now actually called.
        /// </summary>
        public void LoadSceneAsync(string sceneName, Action onComplete = null)
        {
            _logger.LogInfo("SceneLoader", $"LoadSceneAsync called: scene='{sceneName}'");
            StartCoroutine(CoLoadSceneAsync(sceneName, onComplete));
        }

        // Scenes loaded from non-minigame contexts (Bootstrap, returns) don't need a loading overlay.
        private static readonly System.Collections.Generic.HashSet<string> _silentScenes =
            new() { "MainMenu", "Bootstrap" };

        private IEnumerator CoLoadSceneAsync(string sceneName, Action onComplete)
        {
            _logger.LogInfo("SceneLoader", $"Starting async load for scene: '{sceneName}'");

            bool showLoading = !_silentScenes.Contains(sceneName);
            if (showLoading)
                ARcadeRush.UI.LoadingScreenManager.Instance?.ShowTransition(sceneName);

            AsyncOperation asyncOp = SceneManager.LoadSceneAsync(sceneName);
            if (asyncOp == null)
            {
                _logger.LogError("SceneLoader", $"LoadSceneAsync returned null for scene '{sceneName}'. Is it in Build Settings?",
                    ServiceErrorCode.NotInitialized);
                if (showLoading) ARcadeRush.UI.LoadingScreenManager.Instance?.Hide();
                yield break;
            }

            // Wait for the scene to fully load, reporting progress each frame.
            // asyncOp.progress caps at 0.9 until scene activation; map to 0–100%.
            while (!asyncOp.isDone)
            {
                float pct = Mathf.Clamp01(asyncOp.progress / 0.9f);
                if (showLoading)
                    ARcadeRush.UI.LoadingScreenManager.Instance?.SetProgress(pct);
                yield return null;
            }

            if (showLoading)
            {
                ARcadeRush.UI.LoadingScreenManager.Instance?.SetProgress(1f);
                yield return null; // one extra frame so user sees 100%
                ARcadeRush.UI.LoadingScreenManager.Instance?.Hide();
            }

            _logger.LogInfo("SceneLoader", $"Scene '{sceneName}' loaded successfully.");

            // ── Init bridge fix: Find IMiniGame and call OnStart(deps) ──────
            var miniGame = FindFirstMiniGameInScene();
            if (miniGame != null)
            {
                var deps = new MiniGameDependencies
                {
                    GameManager   = GameManager.Instance,
                    Camera        = CameraFeedCtrl.Instance,
                    MediaPipe     = MediaPipeController.Instance,
                    LLM           = LLMConnector.Instance,
                    EmotionBridge = ARcadeRush.EmotionDetection.EmotionGameBridge.Instance,
                };

                _logger.LogInfo("SceneLoader", $"Found IMiniGame '{miniGame.GetType().Name}' — calling OnStart(deps).");
                miniGame.OnStart(deps);
            }
            else
            {
                _logger.LogInfo("SceneLoader", $"No IMiniGame found in scene '{sceneName}'.");
            }

            // Invoke the completion callback
            onComplete?.Invoke();
        }

        /// <summary>
        /// Finds the first active IMiniGame implementer in the currently loaded scene.
        /// </summary>
        private static IMiniGame FindFirstMiniGameInScene()
        {
            // Search all root GameObjects for an IMiniGame component
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var miniGame = root.GetComponentInChildren<IMiniGame>(true);
                if (miniGame != null)
                {
                    return miniGame;
                }
            }
            return null;
        }
    }
}
