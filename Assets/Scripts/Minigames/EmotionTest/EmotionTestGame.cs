using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Face;
using CubiWare.Core.Logging;
 
namespace ARcadeRush.Minigames.EmotionTest
{
    /// <summary>
    /// Emotion Classifier Debug Scene
    /// Purpose: Test and debug the emotion detection pipeline
    /// Features: Camera feed, real-time emotion metrics, toggleable modules
    /// Scalable: Can add Hand Gesture Detection, LLM Integration, etc.
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

        [Header("Settings")]
        [SerializeField] private int _mainMenuSceneIndex = 1;

        /// <summary>Must match MG_EmotionTest in Build Settings</summary>
        public int SceneIndex => 4;

        private const string LogServiceName = "EmotionTestGame";
        private MiniGameDependencies _deps;
        private bool _isPlaying = false;
        private bool _emotionModuleActive = false;

        public void OnStart(MiniGameDependencies deps)
        {
            _deps = deps;
            
            _isPlaying = true;

            ServiceLogger.Instance.LogInfo(LogServiceName, "Starting Emotion Debug Scene");

            // Setup Camera
            SetupCamera();

            // Setup UI
            SetupUI();

            // Setup Emotion Debug Display
            if (_emotionDebugDisplay != null)
            {
                _emotionDebugDisplay.Initialize(_deps);
                _emotionDebugDisplay.EnableModule(false);
                _emotionModuleActive = false;
            }
        }

        public void OnEnd()
        {
            _isPlaying = false;

            ServiceLogger.Instance.LogInfo(LogServiceName, "Exiting Emotion Debug Scene");

            if (_deps?.GameManager != null)
            {
                _deps.GameManager.EndGame();
            }

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadSceneDelayed(_mainMenuSceneIndex, 0.5f);
            }
        }

        private void SetupCamera()
        {
            if (_deps?.Camera == null)
            {
                Debug.LogWarning("[EmotionTestGame] Camera not available in dependencies!");
                return;
            }

            if (_cameraDisplay == null)
            {
                Debug.LogWarning("[EmotionTestGame] Camera display RawImage not assigned!");
            }

            _deps.Camera.SetOutputImage(_cameraDisplay);

            if (!_deps.Camera.IsPlaying)
            {
                _deps.Camera.StartCamera();
                Debug.Log("[EmotionTestGame] Camera started.");
            }
            else
            {
                // Camera already running from another scene — manually push the
                // existing WebCamTexture into the new scene's RawImage right now.
                if (_deps.Camera.ActiveWebCamTexture != null)
                {
                    _cameraDisplay.texture = _deps.Camera.ActiveWebCamTexture;
                    Debug.Log("[EmotionTestGame] Camera already active, texture reassigned to new display.");
                }
                else
                {
                    // Texture not ready yet for some reason, start fresh
                    _deps.Camera.StartCamera();
                    Debug.Log("[EmotionTestGame] Camera active but texture null, restarting.");
                }
            }
        }

        private void SetupUI()
        {
            if (_toggleEmotionBtn != null)
            {
                _toggleEmotionBtn.onClick.AddListener(OnToggleEmotion);
            }

            if (_resetBtn != null)
            {
                _resetBtn.onClick.AddListener(OnReset);
            }

            if (_exitBtn != null)
            {
                _exitBtn.onClick.AddListener(OnExit);
            }
        }

        private void OnToggleEmotion()
        {
            _emotionModuleActive = !_emotionModuleActive;

            var classifier = FindAnyObjectByType<EmotionClassifier>();
            if (classifier != null)
                classifier.SetEnabled(_emotionModuleActive);

            if (_emotionDebugDisplay != null)
                _emotionDebugDisplay.EnableModule(_emotionModuleActive);

            ServiceLogger.Instance.LogInfo(LogServiceName, $"Emotion module {(_emotionModuleActive ? "ENABLED" : "DISABLED")}");
        }

        private void OnReset()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Reset button pressed");

            if (_emotionDebugDisplay != null)
                _emotionDebugDisplay.Reset();

            var reader = FindAnyObjectByType<FaceLandmarkReader>();
            if (reader != null)
                reader.ResetCalibration();

            var classifier = FindAnyObjectByType<EmotionClassifier>();
            if (classifier != null)
                classifier.ResetState();
        }

        private void OnExit()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Exit button pressed");
            OnEnd();
        }

        private void Update()
        {
            if (!_isPlaying || _deps?.Camera == null)
                return;

            // Keep camera active
            if (_cameraDisplay != null && !_deps.Camera.IsPlaying)
            {
                ServiceLogger.Instance.LogWarning(LogServiceName, "Camera stopped unexpectedly. Restarting...");
                _deps.Camera.StartCamera();
            }

            // ESC key to exit
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnExit();
            }
        }

        private void Awake()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Awake");
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                ServiceLogger.Instance.LogInfo(LogServiceName, "Registering with GameManager...");
                GameManager.Instance.StartGame(this);
            }
            else
            {
                ServiceLogger.Instance.LogError(LogServiceName, "GameManager not found! Start from Bootstrap scene.", ServiceErrorCode.NotInitialized);
            }
        }

        private void OnDestroy()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "OnDestroy");
        }
    }
}

