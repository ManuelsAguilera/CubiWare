using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;
using Mediapipe.Tasks.Vision.HandLandmarker;

namespace CubiWare.Core.Services
{
    /// <summary>
    /// Service implementation of <see cref="IHandDetector"/> that extends
    /// <see cref="AbstractMediaPipeLLM"/> for hand landmark detection via MediaPipe.
    /// </summary>
    public class HandDetectorService : AbstractMediaPipeLLM, IHandDetector
    {
        private float _detectionConfidence;

        /// <summary>
        /// Fired when hand landmarks are detected.
        /// </summary>
        public event Action<HandLandmarkData> OnHandDetected;

        /// <summary>
        /// Fired when the hand is no longer detected.
        /// </summary>
        public event Action OnHandLost;

        /// <summary>
        /// Whether the detector is currently tracking hands.
        /// </summary>
        public bool IsDetecting { get; private set; }

        /// <summary>
        /// Current detection confidence (0 to 1).
        /// </summary>
        public float DetectionConfidence => _detectionConfidence;

        /// <summary>
        /// Gets the human-readable service name for logging.
        /// </summary>
        public override string ServiceName => "HandDetectorService";

        /// <summary>
        /// Placeholder initialization. Override to perform real MediaPipe model loading.
        /// </summary>
        protected override Task<bool> OnInitializeAsync()
        {
            LogInfo("HandDetectorService initializing (placeholder).");
            return Task.FromResult(true);
        }

        /// <summary>
        /// Placeholder shutdown. Override to release MediaPipe resources.
        /// </summary>
        protected override Task OnShutdownAsync()
        {
            LogInfo("HandDetectorService shutting down (placeholder).");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes a <see cref="HandLandmarkerResult"/> from MediaPipe, converts it
        /// into a <see cref="HandLandmarkData"/> struct, and invokes <see cref="OnHandDetected"/>.
        /// </summary>
        /// <param name="result">The MediaPipe hand landmarker result to process.</param>
        public void ProcessLandmarks(HandLandmarkerResult result)
        {
            try
            {
                if (result.Equals(default(HandLandmarkerResult)) || result.handLandmarks == null || result.handLandmarks.Count == 0)
                {
                    IsDetecting = false;
                    OnHandLost?.Invoke();
                    return;
                }

                var landmarks = result.handLandmarks[0];
                if (landmarks.landmarks == null || landmarks.landmarks.Count == 0)
                {
                    IsDetecting = false;
                    OnHandLost?.Invoke();
                    return;
                }

                var landmarkData = new HandLandmarkData
                {
                    Landmarks = new List<Vector2>(landmarks.landmarks.Count),
                    Confidence = 1.0f,
                    TimestampMs = (long)(Time.realtimeSinceStartup * 1000)
                };

                foreach (var lm in landmarks.landmarks)
                {
                    landmarkData.Landmarks.Add(new Vector2(lm.x, lm.y));
                }

                _detectionConfidence = landmarkData.Confidence;
                IsDetecting = true;

                OnHandDetected?.Invoke(landmarkData);
            }
            catch (Exception ex)
            {
                LogError($"Error processing hand landmarks: {ex.Message}", ServiceErrorCode.HandDetectionFailed);
            }
        }
    }
}
