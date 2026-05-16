using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;
using Mediapipe.Tasks.Vision.FaceLandmarker;

namespace CubiWare.Core.Services
{
    /// <summary>
    /// Service implementation of <see cref="IFaceDetector"/> that extends
    /// <see cref="AbstractMediaPipeLLM"/> for face landmark detection via MediaPipe.
    /// </summary>
    public class FaceDetectorService : AbstractMediaPipeLLM, IFaceDetector
    {
        /// <summary>
        /// Fired when face landmarks are detected.
        /// </summary>
        public event Action<FaceLandmarkData> OnFaceDetected;

        /// <summary>
        /// Fired when the face is no longer detected.
        /// </summary>
        public event Action OnFaceLost;

        /// <summary>
        /// Whether the detector is currently tracking a face.
        /// </summary>
        public bool IsDetecting { get; private set; }

        /// <summary>
        /// Gets the human-readable service name for logging.
        /// </summary>
        public override string ServiceName => "FaceDetectorService";

        /// <summary>
        /// Placeholder initialization. Override to perform real MediaPipe model loading.
        /// </summary>
        protected override Task<bool> OnInitializeAsync()
        {
            LogInfo("FaceDetectorService initializing (placeholder).");
            return Task.FromResult(true);
        }

        /// <summary>
        /// Placeholder shutdown. Override to release MediaPipe resources.
        /// </summary>
        protected override Task OnShutdownAsync()
        {
            LogInfo("FaceDetectorService shutting down (placeholder).");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes a <see cref="FaceLandmarkerResult"/> from MediaPipe, converts it
        /// into a <see cref="FaceLandmarkData"/> struct, and invokes <see cref="OnFaceDetected"/>.
        /// </summary>
        /// <param name="result">The MediaPipe face landmarker result to process.</param>
        public void ProcessLandmarks(FaceLandmarkerResult result)
        {
            try
            {
                if (result.Equals(default(FaceLandmarkerResult)) || result.faceLandmarks == null || result.faceLandmarks.Count == 0)
                {
                    IsDetecting = false;
                    OnFaceLost?.Invoke();
                    return;
                }

                var landmarks = result.faceLandmarks[0];
                if (landmarks.landmarks == null || landmarks.landmarks.Count == 0)
                {
                    IsDetecting = false;
                    OnFaceLost?.Invoke();
                    return;
                }

                var landmarkData = new FaceLandmarkData
                {
                    Landmarks = new List<Vector2>(landmarks.landmarks.Count),
                    Blendshapes = ExtractBlendshapes(result),
                    TimestampMs = (long)(Time.realtimeSinceStartup * 1000)
                };

                foreach (var lm in landmarks.landmarks)
                {
                    landmarkData.Landmarks.Add(new Vector2(lm.x, lm.y));
                }

                IsDetecting = true;
                OnFaceDetected?.Invoke(landmarkData);
            }
            catch (Exception ex)
            {
                LogError($"Error processing face landmarks: {ex.Message}", ServiceErrorCode.FaceDetectionFailed);
            }
        }

        /// <summary>
        /// Extracts blendshape weights from the face landmarker result, if available.
        /// </summary>
        private static float[] ExtractBlendshapes(FaceLandmarkerResult result)
        {
            try
            {
                if (result.faceBlendshapes == null || result.faceBlendshapes.Count == 0)
                    return null;

                var blendshapes = result.faceBlendshapes[0];
                if (blendshapes.categories == null || blendshapes.categories.Count == 0)
                    return null;

                float[] weights = new float[blendshapes.categories.Count];
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = blendshapes.categories[i].score;
                }

                return weights;
            }
            catch
            {
                return null;
            }
        }
    }
}
