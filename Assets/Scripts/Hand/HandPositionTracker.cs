using System.Collections.Generic;
using UnityEngine;
using Mediapipe.Tasks.Components.Containers;
using ARcadeRush.Core;

namespace ARcadeRush.Hand
{
    /// <summary>
    /// Tracks the hand's center point (based on palm landmarks) and provides 
    /// a public interface for other scripts to use.
    /// </summary>
    public class HandPositionTracker : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private Vector2 _currentHandPosition;
        public Vector2 CurrentHandPosition => _currentHandPosition;

        [Header("Settings")]
        public bool IsMirrored = false;

        [Header("Smoothing")]
        [SerializeField] private int _smoothingFrames = 5;
        private Queue<Vector2> _positionBuffer = new Queue<Vector2>();

        private void Start()
        {
            // Sync with PlayerPrefs
            IsMirrored = PlayerPrefs.GetInt("IsMirrored", 0) == 1;
            
            StartCoroutine(WaitForMediaPipe());
        }

        private System.Collections.IEnumerator WaitForMediaPipe()
        {
            while (MediaPipeController.Instance == null) yield return new WaitForSeconds(0.5f);
            MediaPipeController.Instance.OnHandDetected += HandleHandDetected;
        }

        private void OnDestroy()
        {
            if (MediaPipeController.Instance != null)
                MediaPipeController.Instance.OnHandDetected -= HandleHandDetected;
        }

        private void HandleHandDetected(NormalizedLandmarks data)
        {
            Vector2 sum = Vector2.zero;
            int[] palmIndices = { 0, 5, 9, 13, 17 };
            foreach (int index in palmIndices)
            {
                sum.x += data.landmarks[index].x;
                sum.y += (1f - data.landmarks[index].y);
            }
            Vector2 rawPos = sum / palmIndices.Length;

            // Apply Mirroring
            if (IsMirrored) rawPos.x = 1f - rawPos.x;

            // Add to buffer
            _positionBuffer.Enqueue(rawPos);
            if (_positionBuffer.Count > _smoothingFrames) _positionBuffer.Dequeue();

            // Calculate average
            Vector2 average = Vector2.zero;
            foreach (var pos in _positionBuffer) average += pos;
            _currentHandPosition = average / _positionBuffer.Count;
        }
    }
}
