using System;
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Components.Containers;
using CubiWare.Core.Services;
using CubiWare.Core.Logging;
using CubiWare.Core.Interfaces;

namespace ARcadeRush.Core
{
    /// <summary>
    /// MediaPipe hand gesture detection.
    /// Face detection has been removed — emotion classification is now handled
    /// by DeepFace via EmotionGameBridge / EmotionWebSocketClient.
    /// </summary>
    public class MediaPipeController : MonoBehaviour
    {
        public static MediaPipeController Instance { get; private set; }

        public event Action<NormalizedLandmarks> OnHandDetected;
        public event Action                      OnHandLost;

        private HandDetectorService _handService;

        public IHandDetector HandDetector => _handService;

        private HandLandmarker _handLandmarker;
        private bool           _isReady          = false;
        private long           _currentTimestampMs = 0;
        private int            _handsLostFrames  = 0;

        private Color32[]                              _pixelBuffer;
        private Unity.Collections.NativeArray<byte>   _nativePixels;

        private readonly ConcurrentQueue<PendingHandOutcome> _handOutcomeQueue = new();

        private struct PendingHandOutcome
        {
            public bool               HasLandmarks;
            public NormalizedLandmarks Landmarks;
        }

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
            _handService = new HandDetectorService();
        }

        private async void Start()
        {
            await _handService.InitializeAsync();
            InitMediapipe();
        }

        private void InitMediapipe()
        {
            string handModelPath = Path.Combine(Application.streamingAssetsPath, "mediapipe", "hand_landmarker.task");
            var handBaseOptions = new Mediapipe.Tasks.Core.BaseOptions(modelAssetPath: handModelPath);
            var handOptions = new HandLandmarkerOptions(
                handBaseOptions,
                runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numHands: 1,
                resultCallback: OnHandLandmarksCallback
            );
            _handLandmarker = HandLandmarker.CreateFromOptions(handOptions);

            _isReady = true;
            ServiceLogger.Instance.LogInfo("MediaPipeController", "Hand tracking initialized.");
        }

        private void Update()
        {
            if (!_isReady) return;

            FlushPendingMediapipeResults();

            if (CameraFeedCtrl.Instance == null) return;
            var webCamTex = CameraFeedCtrl.Instance.ActiveWebCamTexture;
            if (webCamTex == null || !webCamTex.isPlaying) return;

            int width      = webCamTex.width;
            int height     = webCamTex.height;
            int pixelCount = width * height;

            if (_pixelBuffer == null || _pixelBuffer.Length != pixelCount)
            {
                _pixelBuffer = new Color32[pixelCount];
                if (_nativePixels.IsCreated) _nativePixels.Dispose();
                _nativePixels = new Unity.Collections.NativeArray<byte>(pixelCount * 4, Unity.Collections.Allocator.Persistent);
            }

            webCamTex.GetPixels32(_pixelBuffer);
            for (int i = 0; i < pixelCount; i++)
            {
                _nativePixels[i * 4]     = _pixelBuffer[i].r;
                _nativePixels[i * 4 + 1] = _pixelBuffer[i].g;
                _nativePixels[i * 4 + 2] = _pixelBuffer[i].b;
                _nativePixels[i * 4 + 3] = _pixelBuffer[i].a;
            }

            long newTimestamp = (long)(Time.realtimeSinceStartup * 1000);
            if (newTimestamp <= _currentTimestampMs)
                newTimestamp = _currentTimestampMs + 1;
            _currentTimestampMs = newTimestamp;

            using var mpImage = new Mediapipe.Image(Mediapipe.ImageFormat.Types.Format.Srgba, width, height, width * 4, _nativePixels);
            _handLandmarker.DetectAsync(mpImage, _currentTimestampMs);
        }

        private void OnHandLandmarksCallback(HandLandmarkerResult result, Mediapipe.Image image, long timestamp)
        {
            if (result.handLandmarks != null && result.handLandmarks.Count > 0)
            {
                var src   = result.handLandmarks[0];
                var copy  = NormalizedLandmarks.Alloc(src.landmarks?.Count ?? 0);
                src.CloneTo(ref copy);
                _handOutcomeQueue.Enqueue(new PendingHandOutcome { HasLandmarks = true, Landmarks = copy });
            }
            else
            {
                _handOutcomeQueue.Enqueue(new PendingHandOutcome { HasLandmarks = false });
            }
        }

        private void FlushPendingMediapipeResults()
        {
            while (_handOutcomeQueue.TryDequeue(out PendingHandOutcome outcome))
            {
                if (outcome.HasLandmarks)
                {
                    _handsLostFrames = 0;
                    OnHandDetected?.Invoke(outcome.Landmarks);
                }
                else
                {
                    _handsLostFrames++;
                    if (_handsLostFrames >= 3)
                        OnHandLost?.Invoke();
                }
            }
        }

        private void OnDestroy()
        {
            if (_handLandmarker != null) _handLandmarker.Close();
            if (_nativePixels.IsCreated) _nativePixels.Dispose();
            if (_handService != null) _ = _handService.ShutdownAsync();
        }
    }
}
