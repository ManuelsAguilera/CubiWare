using System;
using UnityEngine;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace CubiWare.Core.Services
{
    /// <summary>
    /// Service implementation of <see cref="ICameraFeed"/> that wraps a
    /// <see cref="WebCamTexture"/> for camera feed management. This is a plain
    /// C# class, not a MonoBehaviour.
    /// </summary>
    public class CameraFeedProvider : ICameraFeed, IDisposable
    {
        private WebCamTexture _webCamTexture;
        private bool _isRunning;
        private Vector2Int _resolution;

        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        /// <summary>
        /// Fired whenever a new camera frame is captured. Can be invoked from
        /// an <c>Update()</c>-like call or left for manual invocation.
        /// </summary>
        public event Action<Texture> OnFrameCaptured;

        /// <summary>
        /// Whether the camera is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// The requested or actual camera resolution.
        /// </summary>
        public Vector2Int Resolution => _resolution;

        /// <summary>
        /// Initializes a new instance of <see cref="CameraFeedProvider"/> with
        /// a default resolution of 640x480.
        /// </summary>
        public CameraFeedProvider()
        {
            _resolution = new Vector2Int(640, 480);
        }

        /// <summary>
        /// Initializes a new instance with a custom resolution.
        /// </summary>
        /// <param name="width">Requested camera width.</param>
        /// <param name="height">Requested camera height.</param>
        public CameraFeedProvider(int width, int height)
        {
            _resolution = new Vector2Int(width, height);
        }

        /// <summary>
        /// Starts the camera feed by enumerating available devices, picking the
        /// first one, creating a <see cref="WebCamTexture"/>, and starting playback.
        /// </summary>
        public void StartCamera()
        {
            try
            {
                _logger.LogInfo(nameof(CameraFeedProvider), "StartCamera called.");

                WebCamDevice[] devices = WebCamTexture.devices;
                if (devices.Length == 0)
                {
                    _logger.LogError(nameof(CameraFeedProvider), "No camera devices found.", ServiceErrorCode.CameraInitFailed);
                    return;
                }

                string deviceName = devices[0].name;
                _logger.LogInfo(nameof(CameraFeedProvider), $"Using camera device: {deviceName}");

                _webCamTexture = new WebCamTexture(deviceName, _resolution.x, _resolution.y, 30);
                _webCamTexture.Play();
                _isRunning = true;

                _logger.LogInfo(nameof(CameraFeedProvider), $"Camera started: {deviceName}, Resolution: {_webCamTexture.width}x{_webCamTexture.height}");
            }
            catch (Exception ex)
            {
                _logger.LogError(nameof(CameraFeedProvider), $"Failed to start camera: {ex.Message}", ServiceErrorCode.CameraInitFailed);
                _isRunning = false;
            }
        }

        /// <summary>
        /// Stops the camera feed and releases the <see cref="WebCamTexture"/>.
        /// </summary>
        public void StopCamera()
        {
            try
            {
                if (_webCamTexture != null)
                {
                    if (_webCamTexture.isPlaying)
                    {
                        _webCamTexture.Stop();
                    }

                    if (_webCamTexture != null)
                    {
                        UnityEngine.Object.Destroy(_webCamTexture);
                        _webCamTexture = null;
                    }
                }

                _isRunning = false;
                _logger.LogInfo(nameof(CameraFeedProvider), "Camera stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogError(nameof(CameraFeedProvider), $"Error stopping camera: {ex.Message}", ServiceErrorCode.CameraFrameReadError);
            }
        }

        /// <summary>
        /// Gets the current frame from the camera as a <see cref="Texture"/>.
        /// </summary>
        /// <returns>The current video frame texture, or null if the camera is not running.</returns>
        public Texture GetCurrentFrame()
        {
            return _webCamTexture;
        }

        /// <summary>
        /// Invokes the <see cref="OnFrameCaptured"/> event with the current frame.
        /// Call this from an <c>Update()</c>-like loop to propagate frame updates.
        /// </summary>
        public void DispatchFrame()
        {
            if (_isRunning && _webCamTexture != null && _webCamTexture.isPlaying)
            {
                OnFrameCaptured?.Invoke(_webCamTexture);
            }
        }

        /// <summary>
        /// Releases the camera resources.
        /// </summary>
        public void Dispose()
        {
            StopCamera();
            _webCamTexture = null;
        }
    }
}
