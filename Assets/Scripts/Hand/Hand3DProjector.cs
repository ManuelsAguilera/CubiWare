using System;
using UnityEngine;
using ARcadeRush.Core;
using Mediapipe.Tasks.Components.Containers;

namespace ARcadeRush.Hand
{
    public class Hand3DProjector : MonoBehaviour
    {
        private const float DefaultNearScale = 0.4f;
        private const float DefaultMidScale = 0.25f;
        private const float DefaultFarScale = 0.15f;

        [Header("Calibration Scales (Hand Size, normalized)")]
        [SerializeField] private float _nearScale = DefaultNearScale;
        [SerializeField] private float _midScale = DefaultMidScale;
        [SerializeField] private float _farScale = DefaultFarScale;

        [Header("Depth Ranges (Unity Z passed to Camera.ScreenPointToRay depth)")]
        [SerializeField] private float _nearZ = 2f;
        [SerializeField] private float _midZ = 10f;
        [SerializeField] private float _farZ = 20f;

        public Vector3 WristWorldPos { get; private set; }
        public Vector3[] LandmarkWorldPositions { get; private set; } = new Vector3[21];

        /// <summary>Same combined heuristic as legacy HandTracker (palm + middle finger).</summary>
        public float CurrentHandScale { get; private set; }

        /// <summary>Most recent normalized image-space hand; default when hand is lost.</summary>
        public NormalizedLandmarks LastNormalizedLandmarks { get; private set; }

        public event Action OnLost;

        private Camera _mainCamera;

        private void Awake()
        {
            _mainCamera = Camera.main;
            for (int i = 0; i < 21; i++) LandmarkWorldPositions[i] = Vector3.back;
        }

        private void OnEnable()
        {
            TrySubscribeHandEvents();
        }

        private void Start()
        {
            TrySubscribeHandEvents();
            if (MediaPipeController.Instance == null)
            {
                Debug.LogWarning("Hand3DProjector: MediaPipeController not found. Start from Bootstrap scene so singletons are alive.");
            }
        }

        private void OnDisable()
        {
            UnsubscribeHandEvents();
        }

        public void GetCalibrationScales(out float nearScale, out float midScale, out float farScale)
        {
            nearScale = _nearScale;
            midScale = _midScale;
            farScale = _farScale;
        }

        public void SetCalibrationScales(float nearScale, float midScale, float farScale)
        {
            _nearScale = nearScale;
            _midScale = midScale;
            _farScale = farScale;
        }

        public void ResetCalibrationScalesToDefaults()
        {
            _nearScale = DefaultNearScale;
            _midScale = DefaultMidScale;
            _farScale = DefaultFarScale;
        }

        private void TrySubscribeHandEvents()
        {
            if (MediaPipeController.Instance == null) return;
            MediaPipeController.Instance.OnHandDetected -= HandleHandDetected;
            MediaPipeController.Instance.OnHandLost -= HandleHandLost;
            MediaPipeController.Instance.OnHandDetected += HandleHandDetected;
            MediaPipeController.Instance.OnHandLost += HandleHandLost;
        }

        private void UnsubscribeHandEvents()
        {
            if (MediaPipeController.Instance == null) return;
            MediaPipeController.Instance.OnHandDetected -= HandleHandDetected;
            MediaPipeController.Instance.OnHandLost -= HandleHandLost;
        }

        /// <summary>Matches legacy HandTracker.GetCalibratedDepth().</summary>
        private float GetCalibratedDepth(float handScale)
        {
            float resultDepth = 10f;

            if (handScale <= 0f) return resultDepth;

            if (handScale >= _nearScale) resultDepth = _nearZ;
            else if (handScale <= _farScale) resultDepth = _farZ;
            else
            {
                if (handScale < _midScale)
                {
                    float t = (handScale - _farScale) / (_midScale - _farScale);
                    resultDepth = Mathf.Lerp(_farZ, _midZ, t);
                }
                else
                {
                    float t = (handScale - _midScale) / (_nearScale - _midScale);
                    resultDepth = Mathf.Lerp(_midZ, _nearZ, t);
                }
            }

            return resultDepth;
        }

        private void HandleHandDetected(NormalizedLandmarks handLandmarks)
        {
            var landmarks = handLandmarks.landmarks;
            if (landmarks == null || landmarks.Count < 21) return;

            LastNormalizedLandmarks = handLandmarks;

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            float palmHeight = Vector2.Distance(
                new Vector2(landmarks[0].x, landmarks[0].y),
                new Vector2(landmarks[9].x, landmarks[9].y)
            );
            float palmWidth = Vector2.Distance(
                new Vector2(landmarks[5].x, landmarks[5].y),
                new Vector2(landmarks[17].x, landmarks[17].y)
            );
            float middleFingerLength = Vector2.Distance(
                new Vector2(landmarks[9].x, landmarks[9].y),
                new Vector2(landmarks[12].x, landmarks[12].y)
            );

            float palmScale = Mathf.Max(palmHeight, palmWidth);
            CurrentHandScale = (palmScale * 0.4f) + (middleFingerLength * 0.6f);

            float baseDepth = GetCalibratedDepth(CurrentHandScale);

            // Same screen mapping + z offset as FingerVisualizer (mirrored X for webcam overlay parity).
            for (int i = 0; i < 21; i++)
            {
                Vector3 screenPos = new Vector3(
                    (1f - landmarks[i].x) * Screen.width,
                    (1f - landmarks[i].y) * Screen.height,
                    baseDepth + (landmarks[i].z * 0.5f)
                );

                LandmarkWorldPositions[i] = _mainCamera.ScreenToWorldPoint(screenPos);
            }

            WristWorldPos = LandmarkWorldPositions[0];
        }

        private void HandleHandLost()
        {
            CurrentHandScale = 0f;
            LastNormalizedLandmarks = default;
            WristWorldPos = Vector3.back;
            for (int i = 0; i < 21; i++) LandmarkWorldPositions[i] = Vector3.back;

            OnLost?.Invoke();
        }
    }
}
