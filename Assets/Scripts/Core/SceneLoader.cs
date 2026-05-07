using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARcadeRush.Core
{
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
        }

        private void Start()
        {
            // If we are currently in the Bootstrap scene (index 0), automatically load the Main Menu
            if (SceneManager.GetActiveScene().buildIndex == 0)
            {
                LoadSceneDelayed(1, 0.5f); //delay to let Camera & MediaPipe initialize
            }
        }

        public void LoadScene(int index, LoadSceneMode mode = LoadSceneMode.Single)
        {
            SceneManager.LoadSceneAsync(index, mode);
        }

        public void LoadSceneDelayed(int index, float delay, LoadSceneMode mode = LoadSceneMode.Single)
        {
            StartCoroutine(CoLoadSceneDelayed(index, delay, mode));
        }

        private IEnumerator CoLoadSceneDelayed(int index, float delay, LoadSceneMode mode)
        {
            yield return new WaitForSeconds(delay);
            SceneManager.LoadSceneAsync(index, mode);
        }
    }
}
