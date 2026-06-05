using System.Collections.Generic;
using UnityEngine;
using Mediapipe.Tasks.Components.Containers;
using ARcadeRush.Core;

namespace ARcadeRush.Hand
{
    /// <summary>
    /// Tracks the hand's center point (based on palm landmarks) and provides 
    /// a public interface for other scripts to use.
    ///
    /// Velocity extrapolation: when MediaPipe drops detection for a short burst,
    /// the last known velocity is used to extrapolate the position for up to
    /// _ghostFrameMax frames (with per-frame decay) before fully stopping.
    /// </summary>
    public class HandPositionTracker : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private Vector2 _currentHandPosition;
        public Vector2 CurrentHandPosition => _currentHandPosition;

        public bool IsHandDetected { get; private set; }

        /// <summary>True while in the ghost extrapolation window after hand loss.</summary>
        public bool IsExtrapolating => _isExtrapolating;

        [Header("Settings")]
        public bool IsMirrored = false;

        [Header("Smoothing")]
        [SerializeField] private int _smoothingFrames = 5;
        private Queue<Vector2> _positionBuffer = new Queue<Vector2>();
        private Vector2 _runningBufferSum = Vector2.zero;

        [Header("Velocity Extrapolation")]
        [SerializeField] private int _ghostFrameMax = 3;
        [SerializeField] [Range(0f, 1f)] private float _velocityDecay = 0.75f;

        private Vector2 _lastVelocity = Vector2.zero;
        private Vector2 _previousSmoothedPosition = Vector2.zero;
        private bool _isExtrapolating = false;
        private int _ghostFramesRemaining = 0;

        private static readonly int[] PalmIndices = { 0, 5, 9, 13, 17 };

        private void Start()
        {
            // Sync with PlayerPrefs
            IsMirrored = PlayerPrefs.GetInt("IsMirrored", 0) == 1;
            
            StartCoroutine(WaitForMediaPipe());
        }

        private void Update()
        {
            if (!_isExtrapolating) return;

            _ghostFramesRemaining--;
            _lastVelocity *= _velocityDecay;
            _currentHandPosition += _lastVelocity;
            _currentHandPosition = new Vector2(
                Mathf.Clamp01(_currentHandPosition.x),
                Mathf.Clamp01(_currentHandPosition.y)
            );

            if (_ghostFramesRemaining <= 0)
                HardStop();
        }

        private System.Collections.IEnumerator WaitForMediaPipe()
        {
            while (MediaPipeController.Instance == null) yield return new WaitForSeconds(0.5f);
            MediaPipeController.Instance.OnHandDetected += HandleHandDetected;
            MediaPipeController.Instance.OnHandLost += HandleHandLost;
        }

        private void OnDestroy()
        {
            if (MediaPipeController.Instance != null)
            {
                MediaPipeController.Instance.OnHandDetected -= HandleHandDetected;
                MediaPipeController.Instance.OnHandLost -= HandleHandLost;
            }
        }

        private void HandleHandDetected(NormalizedLandmarks data)
        {
            // Cancel any active extrapolation — real data has arrived
            _isExtrapolating = false;
            IsHandDetected = true;

            Vector2 sum = Vector2.zero;
            foreach (int index in PalmIndices)
            {
                sum.x += data.landmarks[index].x;
                sum.y += (1f - data.landmarks[index].y);
            }
            Vector2 rawPos = sum / PalmIndices.Length;

            // Apply Mirroring
            if (IsMirrored) rawPos.x = 1f - rawPos.x;

            // Add to buffer
            _positionBuffer.Enqueue(rawPos);
            _runningBufferSum += rawPos;

            if (_positionBuffer.Count > _smoothingFrames)
            {
                _runningBufferSum -= _positionBuffer.Dequeue();
            }

            // Calculate average without allocation
            Vector2 newSmoothed = _runningBufferSum / _positionBuffer.Count;

            // Track velocity as delta between consecutive smoothed positions
            _lastVelocity = newSmoothed - _previousSmoothedPosition;
            _previousSmoothedPosition = newSmoothed;
            _currentHandPosition = newSmoothed;
        }

        private void HandleHandLost()
        {
            // If hand was moving, extrapolate for a few frames before stopping
            if (_lastVelocity.sqrMagnitude > 0.0001f)
            {
                _isExtrapolating = true;
                _ghostFramesRemaining = _ghostFrameMax;
                // IsHandDetected stays true — blade keeps moving
            }
            else
            {
                // Hand was stationary — no useful velocity, stop immediately
                HardStop();
            }
        }

        private void HardStop()
        {
            IsHandDetected = false;
            _isExtrapolating = false;
            _ghostFramesRemaining = 0;
            _positionBuffer.Clear();
            _runningBufferSum = Vector2.zero;
            _currentHandPosition = Vector2.zero;
            _lastVelocity = Vector2.zero;
            _previousSmoothedPosition = Vector2.zero;
        }
    }
}
