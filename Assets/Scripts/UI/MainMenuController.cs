using UnityEngine;
using UnityEngine.UI;
using ARcadeRush.Core;

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
