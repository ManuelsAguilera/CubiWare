using System;
using UnityEngine;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Interface for camera feed management, decoupling consumers from CameraFeedCtrl.
    /// Provides methods to start/stop the camera and access the current frame.
    /// </summary>
    public interface ICameraFeed
    {
        /// <summary>
        /// Starts the camera feed with the configured resolution and framerate.
        /// </summary>
        void StartCamera();

        /// <summary>
        /// Stops the camera feed and releases resources.
        /// </summary>
        void StopCamera();

        /// <summary>
        /// Gets the current frame from the camera as a Texture.
        /// </summary>
        /// <returns>The current video frame texture, or null if the camera is not running.</returns>
        Texture GetCurrentFrame();

        /// <summary>
        /// Whether the camera is currently running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// The requested or actual camera resolution.
        /// </summary>
        Vector2Int Resolution { get; }

        /// <summary>
        /// Fired whenever a new camera frame is captured.
        /// </summary>
        event Action<Texture> OnFrameCaptured;
    }
}
