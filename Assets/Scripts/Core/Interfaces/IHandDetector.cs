using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Represents landmark data for a detected hand.
    /// </summary>
    public struct HandLandmarkData
    {
        /// <summary>
        /// Normalized hand landmark positions (coordinates in 0-1 range).
        /// </summary>
        public List<Vector2> Landmarks;

        /// <summary>
        /// Detection confidence score (0 to 1).
        /// </summary>
        public float Confidence;

        /// <summary>
        /// Timestamp of the detection in milliseconds.
        /// </summary>
        public long TimestampMs;
    }

    /// <summary>
    /// Interface for hand detection via MediaPipe, decoupling consumers from MediaPipeController.
    /// </summary>
    public interface IHandDetector
    {
        /// <summary>
        /// Fired when hand landmarks are detected.
        /// </summary>
        event Action<HandLandmarkData> OnHandDetected;

        /// <summary>
        /// Fired when the hand is no longer detected.
        /// </summary>
        event Action OnHandLost;

        /// <summary>
        /// Whether the detector is currently tracking hands.
        /// </summary>
        bool IsDetecting { get; }

        /// <summary>
        /// Current detection confidence (0 to 1).
        /// </summary>
        float DetectionConfidence { get; }

        /// <summary>
        /// Initializes the hand detector (e.g., loads MediaPipe model).
        /// </summary>
        /// <returns>True if initialization succeeded.</returns>
        Task<bool> InitializeAsync();

        /// <summary>
        /// Shuts down the hand detector and releases resources.
        /// </summary>
        Task ShutdownAsync();
    }
}
