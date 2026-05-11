namespace CubiWare.Core.Logging
{
    /// <summary>
    /// Defines error codes for all services in the CubiWare system.
    /// Each category groups related error conditions for camera, MediaPipe,
    /// LLM, data store, scene, minigame, and general operations.
    /// </summary>
    public enum ServiceErrorCode
    {
        // ──────────────────────────────
        // Camera Errors
        // ──────────────────────────────

        /// <summary>
        /// The camera device failed to initialize. This may indicate missing permissions,
        /// incompatible hardware, or a conflict with another application using the camera.
        /// </summary>
        CameraInitFailed,

        /// <summary>
        /// The application was denied access to the camera device. This typically occurs
        /// when the user has not granted camera permissions in the OS settings.
        /// </summary>
        CameraAccessDenied,

        /// <summary>
        /// An error occurred while reading a frame from the camera feed. This may be
        /// caused by a dropped frame, device disconnection, or hardware failure.
        /// </summary>
        CameraFrameReadError,

        /// <summary>
        /// The camera operation timed out. Indicates that a camera command took longer
        /// than the configured timeout period to complete.
        /// </summary>
        CameraTimeout,

        // ──────────────────────────────
        // MediaPipe Errors
        // ──────────────────────────────

        /// <summary>
        /// MediaPipe framework failed to initialize. May indicate missing models,
        /// incompatible runtime, or insufficient GPU resources.
        /// </summary>
        MediaPipeInitFailed,

        /// <summary>
        /// Hand detection processing failed. Typically indicates an issue with the
        /// hand landmark model or input frame quality.
        /// </summary>
        HandDetectionFailed,

        /// <summary>
        /// Face detection processing failed. Typically indicates an issue with the
        /// face detection model or input frame quality.
        /// </summary>
        FaceDetectionFailed,

        /// <summary>
        /// A MediaPipe model failed to load. May indicate a missing, corrupted, or
        /// incompatible model file.
        /// </summary>
        ModelLoadFailed,

        /// <summary>
        /// MediaPipe processing exceeded the configured timeout. The detection or
        /// tracking pipeline took too long to produce results.
        /// </summary>
        ProcessingTimeout,

        // ──────────────────────────────
        // LLM Errors
        // ──────────────────────────────

        /// <summary>
        /// Failed to establish a connection to the LLM service. May indicate network
        /// issues, an invalid endpoint URL, or the service being unavailable.
        /// </summary>
        LLMConnectionFailed,

        /// <summary>
        /// The LLM request exceeded the configured timeout period. The remote service
        /// did not respond within the expected timeframe.
        /// </summary>
        LLMRequestTimeout,

        /// <summary>
        /// Failed to parse the LLM service response. The response format was unexpected
        /// or malformed.
        /// </summary>
        LLMResponseParseError,

        /// <summary>
        /// LLM service authentication failed. The API key or credentials provided are
        /// invalid, expired, or lack the required permissions.
        /// </summary>
        LLMAuthenticationFailed,

        /// <summary>
        /// The LLM service rate limit has been exceeded. The application should back off
        /// and retry after the rate limit window expires.
        /// </summary>
        LLMRateLimited,

        // ──────────────────────────────
        // DataStore Errors
        // ──────────────────────────────

        /// <summary>
        /// A read operation on the data store failed. May indicate corruption, disk I/O
        /// errors, or an invalid data format.
        /// </summary>
        DataStoreReadFailed,

        /// <summary>
        /// A write operation on the data store failed. May indicate insufficient space,
        /// permission issues, or disk I/O errors.
        /// </summary>
        DataStoreWriteFailed,

        /// <summary>
        /// The requested key was not found in the data store. The data may not exist
        /// or may have been evicted.
        /// </summary>
        DataStoreKeyNotFound,

        /// <summary>
        /// The data store has been detected as corrupted. Data integrity checks failed
        /// and the store may need to be rebuilt or restored from a backup.
        /// </summary>
        DataStoreCorrupted,

        // ──────────────────────────────
        // Scene Errors
        // ──────────────────────────────

        /// <summary>
        /// Failed to load a scene. May indicate the scene is missing from the build
        /// settings, or an error occurred during scene initialization.
        /// </summary>
        SceneLoadFailed,

        /// <summary>
        /// The specified scene was not found. The scene name or path may be incorrect,
        /// or the scene has not been added to the build settings.
        /// </summary>
        SceneNotFound,

        /// <summary>
        /// A scene transition failed. The application was unable to complete a cross-scene
        /// operation such as data transfer or additive loading.
        /// </summary>
        SceneTransitionFailed,

        // ──────────────────────────────
        // Minigame Errors
        // ──────────────────────────────

        /// <summary>
        /// A minigame failed to initialize. May indicate missing dependencies,
        /// incorrect configuration, or an unsupported state.
        /// </summary>
        MinigameInitFailed,

        /// <summary>
        /// A minigame entered an invalid or unexpected state. The game logic may have
        /// encountered a condition it cannot recover from.
        /// </summary>
        MinigameStateError,

        /// <summary>
        /// A minigame operation timed out. The game logic took longer than expected
        /// to complete a critical operation.
        /// </summary>
        MinigameTimeout,

        /// <summary>
        /// A required dependency for a minigame is missing. The minigame cannot start
        /// because a service or component it depends on is not available.
        /// </summary>
        MinigameDependencyMissing,

        // ──────────────────────────────
        // General Errors
        // ──────────────────────────────

        /// <summary>
        /// An unknown or unexpected error occurred. This catch-all code is used when
        /// the specific error cannot be determined.
        /// </summary>
        UnknownError,

        /// <summary>
        /// An operation was cancelled, typically via a cancellation token. This is not
        /// a failure state but a controlled interruption.
        /// </summary>
        OperationCancelled,

        /// <summary>
        /// The system is in an invalid state for the requested operation. The operation
        /// was called when prerequisites were not met.
        /// </summary>
        InvalidState,

        /// <summary>
        /// An operation was attempted but the service has not been initialized. Call
        /// Initialize() or InitializeAsync() before using the service.
        /// </summary>
        NotInitialized
    }
}
