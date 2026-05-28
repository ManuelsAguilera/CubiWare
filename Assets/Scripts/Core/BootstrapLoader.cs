using UnityEngine;
using UnityEngine.SceneManagement;
using ARcadeRush.Core;
using CubiWare.Core.Logging;

namespace ARcadeRush.Core
{
    /// <summary>
    /// Helper script for standalone minigame scene testing.
    /// If GameManager.Instance is null (meaning we didn't start from the Bootstrap scene),
    /// this script additively loads the Bootstrap scene (build index 0) to ensure
    /// all core services (Logger, DataStore, etc.) are initialized.
    /// </summary>
    public class BootstrapLoader : MonoBehaviour
    {
        private const string BootstrapSceneName = "Bootstrap";
        private static bool _isLoading = false;

        private void Awake()
        {
            if (GameManager.Instance == null && !_isLoading)
            {
                Debug.Log("[BootstrapLoader] GameManager.Instance is null. Loading Bootstrap scene additively...");
                _isLoading = true;
                
                // Additive load prevents the current scene from being destroyed
                SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Additive);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
