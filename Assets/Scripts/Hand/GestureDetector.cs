using System;
using UnityEngine;
using Mediapipe.Tasks.Components.Containers;

namespace ARcadeRush.Hand
{
    public enum GestureType { None, OpenHand, ClosedFist, Point, Pinch, ThumbDown }

    [RequireComponent(typeof(Hand3DProjector))]
    public class GestureDetector : MonoBehaviour
    {
        public event Action OnOpenHand;
        public event Action OnClosedFist;
        public event Action OnPoint;
        public event Action OnPinch;
        public event Action OnPinchRelease;
        /// <summary>Fires when index is pointing but thumb is lowered (finger-gun safety-off).</summary>
        public event Action OnThumbDown;

        [SerializeField] private float _pinchThreshold = 0.05f;

        private Hand3DProjector _projector;
        private GestureType _previousGesture = GestureType.None;

        private void Awake()
        {
            _projector = GetComponent<Hand3DProjector>();
        }

        private void Update()
        {
            var positions = _projector.LandmarkWorldPositions;
            if (positions[0] == Vector3.back)
            {
                if (_previousGesture == GestureType.Pinch) OnPinchRelease?.Invoke();
                _previousGesture = GestureType.None;
                return;
            }

            var norm = _projector.LastNormalizedLandmarks.landmarks;
            if (norm == null || norm.Count < 21) return;

            // ARcade Context §4.2: image space (fingertip "above" MCP => smaller y).
            bool thumbUp = norm[4].y < norm[2].y;
            bool indexUp = norm[8].y < norm[5].y;
            bool middleUp = norm[12].y < norm[9].y;
            bool ringUp = norm[16].y < norm[13].y;
            bool pinkyUp = norm[20].y < norm[17].y;

            float pinchDist = Vector2.Distance(new Vector2(norm[4].x, norm[4].y), new Vector2(norm[8].x, norm[8].y));
            bool isPinching = pinchDist < _pinchThreshold;

            // Thumb-down detection: index pointing AND thumb tip (4) is below thumb IP joint (3)
            // In image-space (y-down), "below" means larger y.
            bool thumbDown = indexUp && norm[4].y > norm[3].y;

            GestureType currentGesture = GestureType.None;

            if (isPinching)
            {
                currentGesture = GestureType.Pinch;
            }
            else if (thumbUp && indexUp && middleUp && ringUp && pinkyUp)
            {
                currentGesture = GestureType.OpenHand;
            }
            else if (!thumbUp && !indexUp && !middleUp && !ringUp && !pinkyUp)
            {
                currentGesture = GestureType.ClosedFist;
            }
            else if (indexUp && !middleUp && !ringUp && !pinkyUp && thumbDown)
            {
                // Index pointing + thumb lowered = ThumbDown (finger-gun safety-off)
                currentGesture = GestureType.ThumbDown;
            }
            else if (indexUp && !middleUp && !ringUp && !pinkyUp)
            {
                currentGesture = GestureType.Point;
            }

            if (currentGesture != _previousGesture)
            {
                if (_previousGesture == GestureType.Pinch)
                {
                    OnPinchRelease?.Invoke();
                }

                switch (currentGesture)
                {
                    case GestureType.OpenHand: OnOpenHand?.Invoke(); break;
                    case GestureType.ClosedFist: OnClosedFist?.Invoke(); break;
                    case GestureType.Point: OnPoint?.Invoke(); break;
                    case GestureType.Pinch: OnPinch?.Invoke(); break;
                    case GestureType.ThumbDown: OnThumbDown?.Invoke(); break;
                }

                _previousGesture = currentGesture;
            }
        }
    }
}
