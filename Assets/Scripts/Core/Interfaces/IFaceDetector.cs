using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Represents landmark data for a detected face.
    /// </summary>
    public struct FaceLandmarkData
    {
        /// <summary>
        /// Normalized face landmark positions (coordinates in 0-1 range).
        /// </summary>
        public List<Vector2> Landmarks;

        /// <summary>
        /// Optional facial expression blendshape weights. May be null if not available.
        /// </summary>
        public float[] Blendshapes;

        /// <summary>
        /// Timestamp of the detection in milliseconds.
        /// </summary>
        public long TimestampMs;
    }

    /// <summary>
    /// Interface for face detection via MediaPipe, decoupling consumers from MediaPipeController.
    /// </summary>
    public interface IFaceDetector
    {
        /// <summary>
        /// Fired when face landmarks are detected.
        /// </summary>
        event Action<FaceLandmarkData> OnFaceDetected;

        /// <summary>
        /// Fired when the face is no longer detected.
        /// </summary>
        event Action OnFaceLost;

        /// <summary>
        /// Whether the detector is currently tracking a face.
        /// </summary>
        bool IsDetecting { get; }

        /// <summary>
        /// Initializes the face detector (e.g., loads MediaPipe model).
        /// </summary>
        /// <returns>True if initialization succeeded.</returns>
        Task<bool> InitializeAsync();

        /// <summary>
        /// Shuts down the face detector and releases resources.
        /// </summary>
        Task ShutdownAsync();
    }
}
