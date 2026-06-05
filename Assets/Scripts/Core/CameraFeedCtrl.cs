using UnityEngine;
using CubiWare.Core.Services;
using CubiWare.Core.Logging;

namespace ARcadeRush.Core
{
    public class CameraFeedCtrl : MonoBehaviour
    {
        public static CameraFeedCtrl Instance { get; private set; }

        [SerializeField] private int _cameraIndex = 0;
        [SerializeField] private UnityEngine.UI.RawImage _outputImage;

        [Header("Camera Resolution Settings")]
        [SerializeField] private int _requestedWidth = 640;
        [SerializeField] private int _requestedHeight = 480;
        [SerializeField] private int _requestedFPS = 30;

        public bool DidUpdateThisFrame { get; private set; }
        public bool IsPlaying => _webCamTexture != null && _webCamTexture.isPlaying;
        public WebCamTexture ActiveWebCamTexture => _webCamTexture;
        public float CurrentFPS { get; private set; }

        private WebCamTexture _webCamTexture;
        private int _frameCount;
        private float _fpsTimer;
        private bool _disposed = false;
        private CameraFeedProvider _provider;
        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[CamFeed] Duplicate CameraFeedCtrl detected. Destroying new instance.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            try
            {
                _provider = new CameraFeedProvider(_requestedWidth, _requestedHeight);
            }
            catch (System.Exception e)
            {
                _logger.LogError("CameraFeedCtrl", $"Failed to initialize CameraFeedProvider: {e.Message}", ServiceErrorCode.CameraInitFailed);
            }
        }

#if UNITY_EDITOR
        private void OnEnable() => UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        private void OnDisable() => UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _logger.LogInfo("CameraFeedCtrl", "Exiting Play Mode! Force releasing camera handle.");
                ReleaseCamera();
            }
        }
#endif

        public void StartCamera()
        {
            if (IsPlaying) return;
            _logger.LogInfo("CameraFeedCtrl", "StartCamera called.");
            StartCoroutine(StartCameraRoutine());
        }

        private System.Collections.IEnumerator StartCameraRoutine()
        {
            _logger.LogInfo("CameraFeedCtrl", $"StartCameraRoutine started on instance {GetEntityId()}");

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

                float permissionTimeout = 10f;
                float permissionElapsed = 0f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera)
                       && permissionElapsed < permissionTimeout)
                {
                    permissionElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    _logger.LogError("CameraFeedCtrl", "Camera permission denied by user!", ServiceErrorCode.CameraAccessDenied);
                    yield break;
                }
            }
#endif

            if (_webCamTexture == null)
            {
                // Yield one frame before querying devices — on Linux/v4l2 this call
                // can block 5-30s on first use. The yield lets the loading screen
                // render at least one frame before we potentially freeze.
                yield return null;

                WebCamDevice[] devices = WebCamTexture.devices;
                for (int i = 0; i < devices.Length; i++)
                {
                    // Debug.Log($"[CamFeed] Found camera [{i}]: {devices[i].name}");
                    _logger.LogInfo("CameraFeedCtrl", $"Found camera [{i}]: {devices[i].name}");
                }

                if (devices.Length == 0) {yield break;}
                if (_cameraIndex < 0 || _cameraIndex >= devices.Length) _cameraIndex = 0;

                if (_requestedWidth > 0 && _requestedHeight > 0)
                {
                    _webCamTexture = new WebCamTexture(devices[_cameraIndex].name, _requestedWidth, _requestedHeight, _requestedFPS);
                }
                else
                {
                    _webCamTexture = new WebCamTexture(devices[_cameraIndex].name);
                }

        _logger.LogInfo("CameraFeedCtrl", $"WebCamTexture created for: {_webCamTexture.deviceName}");
            }

            if (_outputImage != null)
                _outputImage.texture = _webCamTexture;

            _webCamTexture.Play();
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
                elapsed += Time.unscaledDeltaTime;
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
            
            if (_outputImage != null)
            {
                _outputImage.texture = null;
                //_outputImage.enabled = false;
            }

            _provider?.StopCamera();
        }

        public void SwitchCamera(string deviceName)
        {
            bool wasPlaying = IsPlaying;

            StopCamera();

            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            // ← ONLY THIS CHANGES:
            if (_requestedWidth > 0 && _requestedHeight > 0)
                _webCamTexture = new WebCamTexture(deviceName, _requestedWidth, _requestedHeight, _requestedFPS);
            else
                _webCamTexture = new WebCamTexture(deviceName);

            _logger.LogInfo("CameraFeedCtrl", $"Switched to '{deviceName}' (not started yet)");

            if (wasPlaying)
                StartCamera();
        }

        public static string[] GetDeviceNames()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            string[] names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log($"[CamFeed] GetDeviceNames found camera [{i}]: {devices[i].name}");
                names[i] = devices[i].name;
            }
            return names;
        }

        public string ActiveDeviceName => _webCamTexture != null ? _webCamTexture.deviceName : "";

        public void SetOutputImage(UnityEngine.UI.RawImage newOutput)
        {
            Debug.Log($"[CamFeed] SetOutputImage called. New output: {newOutput?.name ?? "null"}");
            _outputImage = newOutput;
            if (_outputImage != null && _webCamTexture != null)
            {
                _outputImage.texture = _webCamTexture;
                // Respect global mirror setting from CameraConfigUI
                bool isMirrored = PlayerPrefs.GetInt("IsMirrored", 0) == 1;
                _outputImage.uvRect = isMirrored
                    ? new Rect(1, 0, -1, 1)   // mirrored: flip horizontally
                    : new Rect(0, 0, 1, 1);   // not mirrored: normal
            }
        }

        private void Update()
        {
            DidUpdateThisFrame = _webCamTexture != null && _webCamTexture.isPlaying && _webCamTexture.didUpdateThisFrame;

            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _frameCount++;
                _fpsTimer += Time.unscaledDeltaTime;
                if (_fpsTimer >= 1f)
                {
                    CurrentFPS = _frameCount / _fpsTimer;
                    //Debug.Log($"[CamFeed] Actual camera FPS: {CurrentFPS:F1} (requested: {_requestedFPS})");
                    _frameCount = 0;
                    _fpsTimer = 0f;
                }
            }
            else
            {
                CurrentFPS = 0f;
                _frameCount = 0;
                _fpsTimer = 0f;
            }
        }

        private void ReleaseCamera()
        {
            if (_disposed) return;
            _disposed = true;

            StopCamera();
            _provider?.Dispose();

            if (_webCamTexture != null)
            {
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }
        }

        private void OnApplicationQuit()
        {
            _logger.LogInfo("CameraFeedCtrl", "OnApplicationQuit called. Force releasing camera handle.");
            ReleaseCamera();
        }

        private void OnDestroy()
        {
            _logger.LogInfo("CameraFeedCtrl", $"OnDestroy called. Stack:\n{System.Environment.StackTrace}");
            if (Instance == this) Instance = null;
            ReleaseCamera();
        }
    }
}