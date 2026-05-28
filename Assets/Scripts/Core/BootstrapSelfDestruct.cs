using UnityEngine;
using UnityEngine.SceneManagement;
using CubiWare.Core.Logging;

namespace CubiWare.Core
{
    /// <summary>
    /// A helper MonoBehaviour that destroys the Bootstrap scene's root GameObject
    /// when a new non-Bootstrap scene loads. This prevents the Bootstrap scene's
    /// GameObjects (like BootstrapManager) from persisting into game scenes.
    ///
    /// Attach this to the Bootstrap scene's root GameObject.
    /// </summary>
    public class BootstrapSelfDestruct : MonoBehaviour
    {
        private const string BootstrapSceneName = "Bootstrap";
        private bool _hasSubscribed = false;

        [SerializeField] private GameObject _bootstrapSceneOnlyRoot; 

        private void Start()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _hasSubscribed = true;

            ServiceLogger.Instance.LogInfo(
                "BootstrapSelfDestruct",
                $"Listening to sceneLoaded — will self-destruct when a scene other than '{BootstrapSceneName}' loads."
            );
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Only self-destruct when entering a non-Bootstrap scene
            if (scene.name != BootstrapSceneName)
            {
                ServiceLogger.Instance.LogInfo(
                    "BootstrapSelfDestruct",
                    $"Scene '{scene.name}' loaded — destroying Bootstrap GameObject '{gameObject.name}'."
                );

                // Unsubscribe before destroying to avoid re-entry
                if (_hasSubscribed)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    _hasSubscribed = false;
                }

                if (_bootstrapSceneOnlyRoot != null)
                {
                    Destroy(_bootstrapSceneOnlyRoot);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up subscription if we're destroyed for any other reason
            if (_hasSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _hasSubscribed = false;
            }
        }
    }
}
