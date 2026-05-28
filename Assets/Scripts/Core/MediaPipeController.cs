using System;
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Components.Containers;
using CubiWare.Core.Services;
using CubiWare.Core.Logging;
using CubiWare.Core.Interfaces;
 
namespace ARcadeRush.Core
{
    public class MediaPipeController : MonoBehaviour
    {
        public static MediaPipeController Instance { get; private set; }
 
        public event Action<NormalizedLandmarks> OnHandDetected;
        public event Action OnHandLost;
 
        // ── CAMBIO 1: el evento ahora entrega el resultado completo ──────────
        // Antes: Action<NormalizedLandmarks>
        // Ahora: Action<FaceLandmarkerResult>  (incluye landmarks + blendshapes)
        public event Action<FaceLandmarkerResult> OnFaceDetected;
 
        /// <summary>
        /// Service-layer hand detector for decoupled hand detection.
        /// Exposed as <see cref="IHandDetector"/> for dependency-injected consumers.
        /// </summary>
        private HandDetectorService _handService;

        /// <summary>
        /// Gets the service-layer hand detector as <see cref="IHandDetector"/>.
        /// Consumers (e.g., ShooterHandController) should resolve this interface
        /// instead of relying on MediaPipeController.Instance directly.
        /// </summary>
        public IHandDetector HandDetector => _handService;
 
        /// <summary>
        /// Service-layer face detector for decoupled face detection.
        /// </summary>
        private FaceDetectorService _faceService;
 
        private HandLandmarker _handLandmarker;
        private FaceLandmarker _faceLandmarker;
        private bool _isReady = false;
        private long _currentTimestampMs = 0;
        private int _handsLostFrames = 0;

        // Persistent pixel buffers — allocated once on first use, reused every frame to eliminate GC pressure.
        private Color32[] _pixelBuffer;
        private Unity.Collections.NativeArray<byte> _nativePixels;
 
        private readonly ConcurrentQueue<PendingHandOutcome> _handOutcomeQueue = new ConcurrentQueue<PendingHandOutcome>();
 
        // ── CAMBIO 2: la queue ahora almacena FaceLandmarkerResult ───────────
        // Antes: ConcurrentQueue<NormalizedLandmarks>
        // Ahora: ConcurrentQueue<FaceLandmarkerResult>
        private readonly ConcurrentQueue<FaceLandmarkerResult> _faceOutcomeQueue = new ConcurrentQueue<FaceLandmarkerResult>();
 
        private struct PendingHandOutcome
        {
            public bool HasLandmarks;
            public NormalizedLandmarks Landmarks;
        }
 
        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject);

            _handService = new HandDetectorService();
            _faceService = new FaceDetectorService();
        }
 
        private async void Start()
        {
            // Initialize service-layer providers
            await _handService.InitializeAsync();
            await _faceService.InitializeAsync();
 
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
                // ── Habilitar blendshapes en las opciones del modelo ─────────
                outputFaceBlendshapes: true,
                resultCallback: OnFaceLandmarksCallback
            );
            _faceLandmarker = FaceLandmarker.CreateFromOptions(faceOptions);
 
            _isReady = true;
            ServiceLogger.Instance.LogInfo("MediaPipeController", "Hand and Face tracking initialized.");
        }
 
        private void Update()
        {
            if (!_isReady) return;
 
            FlushPendingMediapipeResults();
 
            if (CameraFeedCtrl.Instance == null)
            {
                // Log only occasionally or once
                return;
            }
            var webCamTex = CameraFeedCtrl.Instance.ActiveWebCamTexture;
            if (webCamTex == null || !webCamTex.isPlaying)
            {
                // This might be the culprit.
                return;
            }
            
            // Log if detected
            // Debug.Log($"[MediaPipeController] Processing frame...");

            Color32[] pixels = webCamTex.GetPixels32();
            int width  = webCamTex.width;
            int height = webCamTex.height;
 
            var pixelData = new Unity.Collections.NativeArray<byte>(pixels.Length * 4, Unity.Collections.Allocator.Temp);
            for (int i = 0; i < pixels.Length; i++)
            {
                _pixelBuffer = new Color32[pixelCount];
                if (_nativePixels.IsCreated) _nativePixels.Dispose();
                _nativePixels = new Unity.Collections.NativeArray<byte>(pixelCount * 4, Unity.Collections.Allocator.Persistent);
            }

            // Fill the cached Color32 array in-place — no managed allocation.
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

            // Each detector needs its own Image wrapper — DetectAsync transfers native pointer ownership,
            // disposing the wrapper as a side effect. Both wrap the same _nativePixels buffer.
            using (var mpImage = new Mediapipe.Image(Mediapipe.ImageFormat.Types.Format.Srgba, width, height, width * 4, _nativePixels))
                _handLandmarker.DetectAsync(mpImage, _currentTimestampMs);

            using (var mpImage = new Mediapipe.Image(Mediapipe.ImageFormat.Types.Format.Srgba, width, height, width * 4, _nativePixels))
                _faceLandmarker.DetectAsync(mpImage, _currentTimestampMs);
        }
 
        // Sin cambios — manos siguen igual
        private void OnHandLandmarksCallback(HandLandmarkerResult result, Mediapipe.Image image, long timestamp)
        {
            if (result.handLandmarks != null && result.handLandmarks.Count > 0)
            {
                var src   = result.handLandmarks[0];
                int count = src.landmarks?.Count ?? 0;
                // Debug.Log($"[MediaPipeController] Hand detected! Landmarks count: {count}");

                var copy  = NormalizedLandmarks.Alloc(count);
                src.CloneTo(ref copy);
                _handOutcomeQueue.Enqueue(new PendingHandOutcome { HasLandmarks = true, Landmarks = copy });
            }
            else
            {
                _handOutcomeQueue.Enqueue(new PendingHandOutcome { HasLandmarks = false });
            }
        }

 
        // ── CAMBIO 3: encolar el FaceLandmarkerResult completo ───────────────
        // Ahora: se clona el resultado para evitar que MediaPipe reutilice la memoria
        // de las listas internas (landmarks/blendshapes) antes de ser procesado en Update.
        private void OnFaceLandmarksCallback(FaceLandmarkerResult result, Mediapipe.Image image, long timestamp)
        {
            if (result.faceLandmarks == null || result.faceLandmarks.Count == 0) return;

            // Clonado profundo necesario para thread-safety
            var faceCopy = FaceLandmarkerResult.Alloc(
                result.faceLandmarks.Count, 
                result.faceBlendshapes != null && result.faceBlendshapes.Count > 0,
                result.facialTransformationMatrixes != null && result.facialTransformationMatrixes.Count > 0
            );
            result.CloneTo(ref faceCopy);
            
            _faceOutcomeQueue.Enqueue(faceCopy);
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
 
            // Only the most recent face result matters — drop any backlog built up during frame-rate dips.
            bool hasFaceResult = false;
            FaceLandmarkerResult latestFaceResult = default;
            while (_faceOutcomeQueue.TryDequeue(out FaceLandmarkerResult fr))
            {
                latestFaceResult = fr;
                hasFaceResult = true;
            }
            if (hasFaceResult)
                OnFaceDetected?.Invoke(latestFaceResult);
        }
 
        private void OnDestroy()
        {
            if (_handLandmarker != null) _handLandmarker.Close();
            if (_faceLandmarker != null) _faceLandmarker.Close();
            if (_nativePixels.IsCreated) _nativePixels.Dispose();
 
            // Shutdown service-layer providers
            if (_handService != null)
            {
                _ = _handService.ShutdownAsync();
            }
            if (_faceService != null)
            {
                _ = _faceService.ShutdownAsync();
            }
        }
    }
}