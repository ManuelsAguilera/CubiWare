using UnityEngine;
using ARcadeRush.Core;
using ARcadeRush.Face;

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

        private MiniGameDependencies _deps;
        private bool _isPlaying = false;
        private bool _emotionModuleActive = true;

        public void OnStart(MiniGameDependencies deps)
        {
            _deps = deps;
            _isPlaying = true;

            Debug.Log("[EmotionTestGame] Starting Emotion Debug Scene");

            // Setup Camera
            SetupCamera();

            // Setup UI
            SetupUI();

            // Setup Emotion Debug Display
            if (_emotionDebugDisplay != null)
            {
                _emotionDebugDisplay.Initialize(_deps);
                _emotionDebugDisplay.EnableModule(true);
                _emotionModuleActive = true;
            }
        }

        public void OnEnd()
        {
            _isPlaying = false;

            Debug.Log("[EmotionTestGame] Exiting Emotion Debug Scene");

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

            if (_cameraDisplay != null)
            {
                _deps.Camera.SetOutputImage(_cameraDisplay);

                if (!_deps.Camera.IsPlaying)
                {
                    _deps.Camera.StartCamera();
                    Debug.Log("[EmotionTestGame] Camera started");
                }
                else
                {
                    Debug.Log("[EmotionTestGame] Camera already active");
                }
            }
            else
            {
                Debug.LogWarning("[EmotionTestGame] Camera display RawImage not assigned!");
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

            if (_emotionDebugDisplay != null)
            {
                _emotionDebugDisplay.EnableModule(_emotionModuleActive);
            }

            Debug.Log($"[EmotionTestGame] Emotion module {(_emotionModuleActive ? "ENABLED" : "DISABLED")}");
        }

        private void OnReset()
        {
            Debug.Log("[EmotionTestGame] Reset button pressed");

            if (_emotionDebugDisplay != null)
            {
                _emotionDebugDisplay.Reset();
            }
        }

        private void OnExit()
        {
            Debug.Log("[EmotionTestGame] Exit button pressed");
            OnEnd();
        }

        private void Update()
        {
            if (!_isPlaying || _deps?.Camera == null)
                return;

            // Keep camera active
            if (_cameraDisplay != null && !_deps.Camera.IsPlaying)
            {
                Debug.LogWarning("[EmotionTestGame] Camera stopped unexpectedly. Restarting...");
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
            Debug.Log("[EmotionTestGame] Awake");
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                Debug.Log("[EmotionTestGame] Registering with GameManager...");
                GameManager.Instance.StartGame(this);
            }
            else
            {
                Debug.LogError("[EmotionTestGame] GameManager not found! Start from Bootstrap scene.");
            }
        }

        private void OnDestroy()
        {
            Debug.Log("[EmotionTestGame] OnDestroy");
        }
    }
}

