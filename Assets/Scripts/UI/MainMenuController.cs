using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.Minigames.Shooter;
using CubiWare.Core;
using CubiWare.Core.Logging;

namespace ARcadeRush.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [Header("Menu Buttons")]
        
        [SerializeField] private Button _startTestingSceneBtn;
        [SerializeField] private Button _startShooterBtn;
        [SerializeField] private Button _startFruitNinjaBtn;
        [SerializeField] private Button _startDirectorBtn;
        [SerializeField] private Button _startSimonBtn;
        [SerializeField] private Button _startTest2Btn;

        [Header("Score Display")]
        [SerializeField] private TMP_Text _lastScoreText;
        [SerializeField] private string _scorePrefix = "Last Score: ";

        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        private void Start()
        {
            _logger.LogInfo("MainMenuController",
                "Buttons assigned: " +
                $"Test2={_startTest2Btn != null}, " +
                $"Director={_startDirectorBtn != null}");

            if (_startTestingSceneBtn != null)
            {
                // DummyTest — optional testing scene for rapid iteration
                _startTestingSceneBtn.onClick.AddListener(() =>
                    LoadSceneByName("DummyTest"));
            }

            if (_startShooterBtn != null)
            {
                // Shooter minigame — loaded via registry for decoupled build index
                _startShooterBtn.onClick.AddListener(() =>
                    LoadSceneByName("Shooter"));
            }

            if (_startFruitNinjaBtn != null)
            {
                // FruitNinja placeholder — currently points to DummyTest scene
                _startFruitNinjaBtn.onClick.AddListener(() =>
                    LoadSceneByName("FruitNinja"));
            }

            if (_startDirectorBtn != null)
            {
                // EmotionTest (Director) — loaded via registry
                _startDirectorBtn.onClick.AddListener(() =>
                    LoadSceneByName("EmotionTest"));
            }

            if (_startSimonBtn != null)
            {
                // Simon placeholder — currently points to DummyTest scene
                _startSimonBtn.onClick.AddListener(() =>
                    LoadSceneByName("Simon"));
            }
            
            if (_startTest2Btn != null)
            {
                // Test2 — fallback to legacy delayed load with scenario index
                _startTest2Btn.onClick.AddListener(() => LoadScene(4));
            }

            // Display last score from GameManager if available
            if (_lastScoreText != null && GameManager.Instance != null)
            {
                _lastScoreText.text = _scorePrefix + GameManager.Instance.LastScore;
            }
        }

        /// <summary>
        /// Loads a scene by its friendly name using <see cref="MiniGameRegistry"/>
        /// to resolve the build index, then delegates to <see cref="SceneLoader.LoadSceneAsync(string, System.Action)"/>.
        /// </summary>
        private void LoadSceneByName(string sceneName)
        {
            int buildIndex = MiniGameRegistry.GetSceneIndex(sceneName);
            if (buildIndex < 0)
            {
                _logger.LogError("MainMenuController",
                    $"Scene '{sceneName}' not found in build settings.",
                    ServiceErrorCode.SceneNotFound);
                return;
            }

            _logger.LogInfo("MainMenuController",
                $"Loading scene '{sceneName}' (build index {buildIndex}) via SceneLoader.");

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadSceneAsync(sceneName, () =>
                {
                    _logger.LogInfo("MainMenuController",
                        $"Scene '{sceneName}' loaded successfully.");
                });
            }
            else
            {
                _logger.LogError("MainMenuController",
                    "SceneLoader Instance is missing! Did you start from the Bootstrap scene?",
                    ServiceErrorCode.SceneLoadFailed);
            }
        }

        /// <summary>
        /// Legacy fallback: loads a scene by raw build index with a delay.
        /// Used for scenes not yet registered in <see cref="MiniGameRegistry"/>.
        /// </summary>
        private void LoadScene(int buildIndex)
        {
            _logger.LogInfo("MainMenuController",
                $"Loading scene by build index {buildIndex} (legacy path).");

            if (SceneLoader.Instance != null)
            {
                SceneLoader.Instance.LoadSceneDelayed(buildIndex, 0.5f);
            }
            else
            {
                Debug.LogError("MainMenuController: SceneLoader Instance is missing! Did you start from the Bootstrap scene?");
            }
        }
    }
}
