using System;
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Components.Containers;

namespace ARcadeRush.Core
{
    public class MediaPipeController : MonoBehaviour
    {
        public static MediaPipeController Instance { get; private set; }

        public event Action<NormalizedLandmarks> OnHandDetected;
        public event Action OnHandLost;
        public event Action<NormalizedLandmarks> OnFaceDetected;

        private HandLandmarker _handLandmarker;
        private FaceLandmarker _faceLandmarker;
        private bool _isReady = false;
        private long _currentTimestampMs = 0;
        private int _handsLostFrames = 0;

        private readonly ConcurrentQueue<PendingHandOutcome> _handOutcomeQueue = new ConcurrentQueue<PendingHandOutcome>();
        private readonly ConcurrentQueue<NormalizedLandmarks> _faceOutcomeQueue = new ConcurrentQueue<NormalizedLandmarks>();

        private struct PendingHandOutcome
        {
            public bool HasLandmarks;
            public NormalizedLandmarks Landmarks;
        }

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

            string faceModelPath = Path.Combine(Application.streamingAssetsPath, "mediapipe", "face_landmarker.task");
            var faceBaseOptions = new Mediapipe.Tasks.Core.BaseOptions(modelAssetPath: faceModelPath);
            var faceOptions = new FaceLandmarkerOptions(
                faceBaseOptions,
                runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numFaces: 1,
                resultCallback: OnFaceLandmarksCallback
            );
            _faceLandmarker = FaceLandmarker.CreateFromOptions(faceOptions);

            _isReady = true;
            Debug.Log("MediaPipeController: Hand and Face tracking initialized.");
        }

        private void Update()
        {
            if (!_isReady) return;

            FlushPendingMediapipeResults();

            if (CameraFeedCtrl.Instance == null) return;
            var webCamTex = CameraFeedCtrl.Instance.ActiveWebCamTexture;
            if (webCamTex == null || !webCamTex.isPlaying) return;

            Color32[] pixels = webCamTex.GetPixels32();
            int width = webCamTex.width;
            int height = webCamTex.height;

            var pixelData = new Unity.Collections.NativeArray<byte>(pixels.Length * 4, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixelData[i * 4] = pixels[i].r;
                pixelData[i * 4 + 1] = pixels[i].g;
                pixelData[i * 4 + 2] = pixels[i].b;
                pixelData[i * 4 + 3] = pixels[i].a;
            }

            long newTimestamp = (long)(Time.realtimeSinceStartup * 1000);
            if (newTimestamp <= _currentTimestampMs)
            {
                newTimestamp = _currentTimestampMs + 1;
            }
            _currentTimestampMs = newTimestamp;

            using (var mpImage = new Mediapipe.Image(Mediapipe.ImageFormat.Types.Format.Srgba, width, height, width * 4, pixelData))
            {
                _handLandmarker.DetectAsync(mpImage, _currentTimestampMs);
            }

            using (var mpImage2 = new Mediapipe.Image(Mediapipe.ImageFormat.Types.Format.Srgba, width, height, width * 4, pixelData))
            {
                _faceLandmarker.DetectAsync(mpImage2, _currentTimestampMs);
            }

            pixelData.Dispose();
        }

        /// <summary>
        /// Mediapipe LIVE_STREAM invokes this off the Unity main thread — enqueue only.
        /// </summary>
        private void OnHandLandmarksCallback(HandLandmarkerResult result, Mediapipe.Image image, long timestamp)
        {
            if (result.handLandmarks != null && result.handLandmarks.Count > 0)
            {
                var src = result.handLandmarks[0];
                int count = src.landmarks?.Count ?? 0;
                var copy = NormalizedLandmarks.Alloc(count);
                src.CloneTo(ref copy);
                _handOutcomeQueue.Enqueue(new PendingHandOutcome { HasLandmarks = true, Landmarks = copy });
            }
            else
            {
                _handOutcomeQueue.Enqueue(new PendingHandOutcome { HasLandmarks = false });
            }
        }

        /// <summary>
        /// Mediapipe LIVE_STREAM invokes this off the Unity main thread — enqueue only.
        /// </summary>
        private void OnFaceLandmarksCallback(FaceLandmarkerResult result, Mediapipe.Image image, long timestamp)
        {
            if (result.faceLandmarks != null && result.faceLandmarks.Count > 0)
            {
                var src = result.faceLandmarks[0];
                int count = src.landmarks?.Count ?? 0;
                var copy = NormalizedLandmarks.Alloc(count);
                src.CloneTo(ref copy);
                _faceOutcomeQueue.Enqueue(copy);
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
                    {
                        OnHandLost?.Invoke();
                    }
                }
            }

            while (_faceOutcomeQueue.TryDequeue(out NormalizedLandmarks faceCopy))
            {
                OnFaceDetected?.Invoke(faceCopy);
            }
        }

        private void OnDestroy()
        {
            if (_handLandmarker != null) _handLandmarker.Close();
            if (_faceLandmarker != null) _faceLandmarker.Close();
        }
    }
}
