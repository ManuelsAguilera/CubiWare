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

        /// <summary>Must match MG_EmotionTest in Build Settings</summary>
        public int SceneIndex => 4;

        private const string LogServiceName = "EmotionTestGame";
        private MiniGameDependencies _deps;
        private bool _isPlaying = false;
        private bool _emotionModuleActive = false;
        private bool _uiSetup = false;
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

            _deps?.GameManager?.EndGame();

            // Tell MainMenu to open the GameSelector panel directly instead of the main panel.
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
            if (_uiSetup) return;
            _uiSetup = true;

            if (_toggleEmotionBtn != null)
                _toggleEmotionBtn.onClick.AddListener(OnToggleEmotion);
            if (_resetBtn != null)
                _resetBtn.onClick.AddListener(OnReset);
            if (_exitBtn != null)
                _exitBtn.onClick.AddListener(OnExit);
        }

        private void OnToggleEmotion()
        {
            _emotionModuleActive = !_emotionModuleActive;

            var classifier = FindFirstObjectByType<EmotionClassifier>();
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

            var reader = FindFirstObjectByType<FaceLandmarkReader>();
            if (reader != null)
                reader.ResetCalibration();

            var classifier = FindFirstObjectByType<EmotionClassifier>();
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
            if (GameManager.Instance == null)
            {
                ServiceLogger.Instance.LogError(LogServiceName, "GameManager not found! Start from Bootstrap scene.", ServiceErrorCode.NotInitialized);
                return;
            }

            if (_deps != null)
            {
                // SceneLoader already called OnStart() — just register ownership without re-running setup.
                // Calling StartGame() here would invoke OnStart() a second time, doubling button listeners
                // and making toggle buttons cancel themselves on each click.
                GameManager.Instance.RegisterGame(this);
            }
            else
            {
                // Direct scene entry without SceneLoader (e.g. Editor Play from this scene).
                GameManager.Instance.StartGame(this);
            }
        }

        private void OnDestroy()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "OnDestroy");
        }
    }
}

