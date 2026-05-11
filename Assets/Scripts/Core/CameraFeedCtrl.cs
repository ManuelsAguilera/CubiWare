using UnityEngine;
using CubiWare.Core.Services;
using CubiWare.Core.Logging;

namespace ARcadeRush.Core
{
    /// <summary>
    /// Camera feed controller. Based on the original working CameraController.cs.
    /// Singleton + DontDestroyOnLoad. Now delegates to <see cref="CameraFeedProvider"/>
    /// for service-layer camera management.
    /// </summary>
    public class CameraFeedCtrl : MonoBehaviour
    {
        public static CameraFeedCtrl Instance { get; private set; }

        [SerializeField] private int _cameraIndex = 0;
        [SerializeField] private UnityEngine.UI.RawImage _outputImage;

        // LINUX BROSKI (Cambio vicho)
        [Header("Camera Resolution Settings")]
        [SerializeField] private int _requestedWidth = 640;
        [SerializeField] private int _requestedHeight = 480;
        [SerializeField] private int _requestedFPS = 30;

        public bool DidUpdateThisFrame { get; private set; }
        public bool IsPlaying => _webCamTexture != null && _webCamTexture.isPlaying;

        public WebCamTexture ActiveWebCamTexture => _webCamTexture;

        private WebCamTexture _webCamTexture;

        /// <summary>
        /// Service-layer camera provider for decoupled camera management.
        /// </summary>
        private CameraFeedProvider _provider;

        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);

            // Initialize the service-layer provider
            _provider = new CameraFeedProvider(_requestedWidth, _requestedHeight);
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
                _logger.LogInfo("CameraFeedCtrl", "Exiting Play Mode! Force releasing camera handle.");
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
        /// Delegates to <see cref="CameraFeedProvider.StartCamera"/> after setup.
        /// </summary>
        public void StartCamera()
        {
            _logger.LogInfo("CameraFeedCtrl", "StartCamera called. Delegating to provider.");
            StartCoroutine(StartCameraRoutine());
        }

        // Note: For Android, ensure AndroidManifest.xml includes: <uses-permission android:name="android.permission.CAMERA" />
        private System.Collections.IEnumerator StartCameraRoutine()
        {
            _logger.LogInfo("CameraFeedCtrl", $"StartCameraRoutine started on instance {GetInstanceID()}");

            #if UNITY_IOS || UNITY_WEBGL
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                _logger.LogError("CameraFeedCtrl", "Camera permission denied by user!", ServiceErrorCode.CameraAccessDenied);
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
                    _logger.LogError("CameraFeedCtrl", "Camera permission denied by user!", ServiceErrorCode.CameraAccessDenied);
                    yield break;
                }
            }
            #endif

            if (_webCamTexture == null)
            {
                WebCamDevice[] devices = WebCamTexture.devices;
                _logger.LogInfo("CameraFeedCtrl", $"Found {devices.Length} camera device(s).");
                for (int i = 0; i < devices.Length; i++)
                    _logger.LogInfo("CameraFeedCtrl", $"Found camera [{i}]: {devices[i].name}");

                if (devices.Length == 0)
                {
                    _logger.LogError("CameraFeedCtrl", "No cameras found!", ServiceErrorCode.CameraInitFailed);
                    yield break;
                }

                if (_cameraIndex < 0 || _cameraIndex >= devices.Length)
                    _cameraIndex = 0;

                // Vicho cambio
                //_webCamTexture = new WebCamTexture(devices[_cameraIndex].name);
                
                _webCamTexture = new WebCamTexture(devices[_cameraIndex].name, _requestedWidth, _requestedHeight, _requestedFPS);

                if (_webCamTexture == null)
                    _logger.LogError("CameraFeedCtrl", "WebCamTexture failed to instantiate!", ServiceErrorCode.CameraInitFailed);
                else
                    _logger.LogInfo("CameraFeedCtrl", $"WebCamTexture created for: {_webCamTexture.deviceName}");
            }

            if (_outputImage != null)
            {
                _outputImage.texture = _webCamTexture;
            }

            _webCamTexture.Play();

            // Delegate to the service-layer provider
            _provider?.StartCamera();

            _logger.LogInfo("CameraFeedCtrl", $"Camera started: {_webCamTexture.deviceName}, isPlaying={_webCamTexture.isPlaying}");
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
                _logger.LogInfo("CameraFeedCtrl", "Camera stopped.");
            }

            // Delegate to the service-layer provider
            _provider?.StopCamera();
        }

        /// <summary>
        /// Switch to a different camera by device name.
        /// </summary>
        public void SwitchCamera(string deviceName)
        {
            bool wasPlaying = IsPlaying;

            StopCamera();

            _provider?.StopCamera();

            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            // vicho cambio
            //_webCamTexture = new WebCamTexture(deviceName);
            
            _webCamTexture = new WebCamTexture(deviceName, _requestedWidth, _requestedHeight, _requestedFPS);
            _logger.LogInfo("CameraFeedCtrl", $"Switched to '{deviceName}' (not started yet)");

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
            _logger.LogInfo("CameraFeedCtrl", "OnApplicationQuit called. Force releasing camera handle.");
            StopCamera();
            _provider?.Dispose();
            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }

        private void OnDestroy()
        {
            _logger.LogInfo("CameraFeedCtrl", $"OnDestroy called. Stack:\n{System.Environment.StackTrace}");
            if (Instance == this) Instance = null;
            StopCamera();
            _provider?.Dispose();
            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }
    }
}
