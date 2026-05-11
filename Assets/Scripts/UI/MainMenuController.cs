using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARcadeRush.Core;
using ARcadeRush.Minigames.Shooter;

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

        [Header("Score Display")]
        [SerializeField] private TMP_Text _lastScoreText;
        [SerializeField] private string _scorePrefix = "Last Score: ";

        private void Start()
        {
            if (_startTestingSceneBtn != null)
            {
                // Optional testing scene — ensure camera is running before loading.
                _startTestingSceneBtn.onClick.AddListener(() => LoadScene(2));
            }
            if (_startShooterBtn != null)
            {
                // Scene 2 is MG_Shooter in final Build Settings.
                _startShooterBtn.onClick.AddListener(() => LoadScene(3));
            }

            if (_startFruitNinjaBtn != null)
            {
                // Scene 2 is MG_Shooter in final Build Settings.
                _startFruitNinjaBtn.onClick.AddListener(() => LoadScene(2));
            }

            if (_startDirectorBtn != null)
            {
                // Optional testing scene — ensure camera is running before loading.
                _startDirectorBtn.onClick.AddListener(() => LoadScene(3));
            }
            if (_startSimonBtn != null)
            {
                // Scene 2 is MG_Shooter in final Build Settings.
                _startSimonBtn.onClick.AddListener(() => LoadScene(2));
            }

            // Display last game score if returning from a completed game
            if (_lastScoreText != null)
            {
                int lastScore = ShooterGame.LastScore;
                if (lastScore > 0 || ShooterGame.LastScore != 0)
                {
                    _lastScoreText.text = $"{_scorePrefix}{lastScore}";
                    _lastScoreText.gameObject.SetActive(true);
                }
                else
                {
                    _lastScoreText.gameObject.SetActive(false);
                }
            }
        }

        private void LoadScene(int buildIndex)
        {
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
