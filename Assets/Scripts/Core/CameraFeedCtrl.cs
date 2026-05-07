using UnityEngine;

namespace ARcadeRush.Core
{
    /// <summary>
    /// Camera feed controller. Based on the original working CameraController.cs.
    /// Singleton + DontDestroyOnLoad.
    /// </summary>
    public class CameraFeedCtrl : MonoBehaviour
    {
        public static CameraFeedCtrl Instance { get; private set; }

        [SerializeField] private int _cameraIndex = 0;
        [SerializeField] private UnityEngine.UI.RawImage _outputImage;

        public bool DidUpdateThisFrame { get; private set; }
        public bool IsPlaying => _webCamTexture != null && _webCamTexture.isPlaying;

        public WebCamTexture ActiveWebCamTexture => _webCamTexture;

        private WebCamTexture _webCamTexture;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log($"[CamFeed] Destroying duplicate instance {GetInstanceID()}. Winning instance is {Instance.GetInstanceID()}");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
            Debug.Log($"[CamFeed] Awake: Instance {GetInstanceID()} protected with DontDestroyOnLoad.");
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Debug.Log("[CamFeed] Exiting Play Mode! Force releasing camera handle.");
                StopCamera();
                if (_webCamTexture != null)
                {
                    Destroy(_webCamTexture);
                    _webCamTexture = null;
                }
            }
        }
#endif

        /// <summary>
        /// Call this to start the camera (e.g. from a button or from another script).
        /// Mirrors the original CameraController.StartCamera() that worked.
        /// </summary>
        public void StartCamera()
        {
            StartCoroutine(StartCameraRoutine());
        }

        // Note: For Android, ensure AndroidManifest.xml includes: <uses-permission android:name="android.permission.CAMERA" />
        private System.Collections.IEnumerator StartCameraRoutine()
        {
            Debug.Log($"[CamFeed] StartCamera called on instance {GetInstanceID()}");

            #if UNITY_IOS || UNITY_WEBGL
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("[CamFeed] Camera permission denied by user!");
                yield break;
            }
            #elif UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                // Wait until the user has either granted or denied the permission (this can take more than one frame, but we'll wait one frame as requested)
                yield return null; 
                // Technically it's safer to wait until the permission dialog is dismissed, but per prompt:
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    Debug.LogError("[CamFeed] Camera permission denied by user!");
                    yield break;
                }
            }
            #endif

            if (_webCamTexture == null)
            {
                WebCamDevice[] devices = WebCamTexture.devices;
                Debug.Log($"[CamFeed] Found {devices.Length} device(s)");
                for (int i = 0; i < devices.Length; i++)
                    Debug.Log($"[CamFeed] Found camera [{i}]: {devices[i].name}");

                if (devices.Length == 0)
                {
                    Debug.LogError("[CamFeed] No cameras found!");
                    yield break;
                }

                if (_cameraIndex < 0 || _cameraIndex >= devices.Length)
                    _cameraIndex = 0;

                _webCamTexture = new WebCamTexture(devices[_cameraIndex].name);
                
                if (_webCamTexture == null)
                    Debug.LogError("[CamFeed] WebCamTexture failed to instantiate!");
                else
                    Debug.Log($"[CamFeed] WebCamTexture created for: {_webCamTexture.deviceName}");
            }

            if (_outputImage != null)
            {
                _outputImage.texture = _webCamTexture;
            }

            _webCamTexture.Play();
            Debug.Log($"[CamFeed] Camera started: {_webCamTexture.deviceName}, isPlaying={_webCamTexture.isPlaying}");
            StartCoroutine(PollCameraState());
        }

        private System.Collections.IEnumerator PollCameraState()
        {
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                if (_webCamTexture == null)
                {
                    Debug.LogError($"[CamFeed] Polling: _webCamTexture became null after {elapsed:F2}s!");
                    yield break;
                }
                if (_webCamTexture.isPlaying)
                {
                    Debug.Log($"[CamFeed] Polling: Camera is playing after {elapsed:F2}s!");
                    yield break;
                }
                Debug.Log($"[CamFeed] Polling: isPlaying=False at {elapsed:F2}s...");
                elapsed += Time.deltaTime;
                yield return null;
            }
            Debug.LogError("[CamFeed] Polling: Camera failed to start playing after 3 seconds.");
        }

        public void StopCamera()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
                Debug.Log("[CamFeed] Camera stopped.");
            }
        }

        /// <summary>
        /// Switch to a different camera by device name.
        /// </summary>
        public void SwitchCamera(string deviceName)
        {
            bool wasPlaying = IsPlaying;

            StopCamera();
            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            _webCamTexture = new WebCamTexture(deviceName);
            Debug.Log($"[CamFeed] Switched to '{deviceName}' (not started yet)");

            // Only auto-start if camera was already running
            if (wasPlaying)
            {
                StartCamera();
            }
        }

        public static string[] GetDeviceNames()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string[] names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++)
                names[i] = devices[i].name;
            return names;
        }

        public string ActiveDeviceName => _webCamTexture != null ? _webCamTexture.deviceName : "";

        public void SetOutputImage(UnityEngine.UI.RawImage newOutput)
        {
            _outputImage = newOutput;
            if (_outputImage != null && _webCamTexture != null)
            {
                _outputImage.texture = _webCamTexture;
            }
        }

        private void Update()
        {
            DidUpdateThisFrame = _webCamTexture != null && _webCamTexture.isPlaying && _webCamTexture.didUpdateThisFrame;
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[CamFeed] OnApplicationQuit called. Force releasing camera handle.");
            StopCamera();
            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }

        private void OnDestroy()
        {
            Debug.Log($"[CamFeed] OnDestroy called. Stack:\n{System.Environment.StackTrace}");
            if (Instance == this) Instance = null;
            StopCamera();
            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }
    }
}
