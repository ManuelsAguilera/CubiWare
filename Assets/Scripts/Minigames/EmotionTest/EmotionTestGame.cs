using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.EmotionDetection;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.EmotionTest
{
    /// <summary>
    /// EmotionTest debug scene — test environment for the DeepFace emotion pipeline.
    /// Activate the Emotion button to start sending frames to the Python server.
    /// Use the Inspector on EmotionGameBridge to see live detection results.
    /// </summary>
    public class EmotionTestGame : MonoBehaviour, IMiniGame
    {
        [Header("Camera")]
        [SerializeField] private UnityEngine.UI.RawImage _cameraDisplay;

        [Header("Debug UI")]
        [SerializeField] private EmotionTestDebugDisplay _emotionDebugDisplay;

        [Header("Control Buttons")]
        [SerializeField] private UnityEngine.UI.Button _toggleEmotionBtn;
        [SerializeField] private UnityEngine.UI.Button _resetBtn;
        [SerializeField] private UnityEngine.UI.Button _exitBtn;

        /// <summary>Must match EmotionTest in Build Settings</summary>
        public int SceneIndex => 4;

        private const string LogServiceName = "EmotionTestGame";
        private MiniGameDependencies _deps;
        private bool _isPlaying        = false;
        private bool _emotionModuleActive = false;
        private bool _uiSetup          = false;
        private bool _displayInitialized = false;

        public void OnStart(MiniGameDependencies deps)
        {
            _deps = deps;
            _isPlaying = true;

            ServiceLogger.Instance.LogInfo(LogServiceName, "Starting Emotion Debug Scene");

            SetupCamera();
            SetupUI();

            if (_emotionDebugDisplay != null && !_displayInitialized)
            {
                _displayInitialized = true;
                _emotionDebugDisplay.Initialize(_deps);
                _emotionDebugDisplay.EnableModule(false);
                _emotionModuleActive = false;
            }
        }

        public void OnEnd()
        {
            _isPlaying = false;

            ServiceLogger.Instance.LogInfo(LogServiceName, "Exiting Emotion Debug Scene");

            _deps?.EmotionBridge?.SetEnabled(false);
            _deps?.GameManager?.EndGame();

            UnityEngine.PlayerPrefs.SetInt("ReturnToGameSelector", 1);
            SceneLoader.Instance?.LoadSceneAsync("MainMenu");
        }

        private void SetupCamera()
        {
            if (_deps?.Camera == null)
            {
                Debug.LogWarning("[EmotionTestGame] Camera not available in dependencies!");
                return;
            }

            if (_cameraDisplay == null)
                Debug.LogWarning("[EmotionTestGame] Camera display RawImage not assigned!");

            _deps.Camera.SetOutputImage(_cameraDisplay);

            if (!_deps.Camera.IsPlaying)
            {
                _deps.Camera.StartCamera();
                Debug.Log("[EmotionTestGame] Camera started.");
            }
            else
            {
                if (_deps.Camera.ActiveWebCamTexture != null)
                {
                    _cameraDisplay.texture = _deps.Camera.ActiveWebCamTexture;
                    Debug.Log("[EmotionTestGame] Camera already active, texture reassigned.");
                }
                else
                {
                    _deps.Camera.StartCamera();
                    Debug.Log("[EmotionTestGame] Camera active but texture null, restarting.");
                }
            }
        }

        private void SetupUI()
        {
            if (_uiSetup) return;
            _uiSetup = true;

            if (_toggleEmotionBtn != null) _toggleEmotionBtn.onClick.AddListener(OnToggleEmotion);
            if (_resetBtn != null)         _resetBtn.onClick.AddListener(OnReset);
            if (_exitBtn != null)          _exitBtn.onClick.AddListener(OnExit);
        }

        private void OnToggleEmotion()
        {
            _emotionModuleActive = !_emotionModuleActive;

            _deps?.EmotionBridge?.SetEnabled(_emotionModuleActive);

            if (_emotionDebugDisplay != null)
                _emotionDebugDisplay.EnableModule(_emotionModuleActive);

            ServiceLogger.Instance.LogInfo(LogServiceName,
                $"Emotion module {(_emotionModuleActive ? "ENABLED" : "DISABLED")}");
        }

        private void OnReset()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Reset button pressed");

            _deps?.EmotionBridge?.ResetState();

            if (_emotionDebugDisplay != null)
                _emotionDebugDisplay.Reset();
        }

        private void OnExit()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Exit button pressed");
            OnEnd();
        }

        private void Update()
        {
            if (!_isPlaying || _deps?.Camera == null) return;

            if (_cameraDisplay != null && !_deps.Camera.IsPlaying)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, "Camera stopped unexpectedly. Restarting...");
                _deps.Camera.StartCamera();
            }

            if (Input.GetKeyDown(KeyCode.Escape)) OnExit();
        }

        private void Awake()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Awake");
        }

        private void Start()
        {
            if (GameManager.Instance == null)
            {
                ServiceLogger.Instance.LogError(LogServiceName, "GameManager not found! Start from Bootstrap scene.", ServiceErrorCode.NotInitialized);
                return;
            }

            if (_deps != null)
                GameManager.Instance.RegisterGame(this);
            else
                GameManager.Instance.StartGame(this);
        }

        private void OnDestroy()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "OnDestroy");
        }
    }
}
