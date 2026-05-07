using UnityEngine;
using ARcadeRush.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Components.Containers;

namespace ARcadeRush.Face
{
    public class FaceLandmarkReader : MonoBehaviour
    {
        // 0: Mouth Openness, 1: Eye Openness L, 2: Eye Openness R, 3: Brow Raise
        public float[] NormalizedMetrics { get; private set; } = new float[4];

        private void OnEnable()
        {
            TrySubscribeFaceEvents();
        }

        private void Start()
        {
            TrySubscribeFaceEvents();
        }

        private void OnDisable()
        {
            if (MediaPipeController.Instance != null)
            {
                MediaPipeController.Instance.OnFaceDetected -= HandleFaceDetected;
            }
        }

        private void TrySubscribeFaceEvents()
        {
            if (MediaPipeController.Instance == null) return;
            MediaPipeController.Instance.OnFaceDetected -= HandleFaceDetected;
            MediaPipeController.Instance.OnFaceDetected += HandleFaceDetected;
        }

        private void HandleFaceDetected(NormalizedLandmarks faceLandmarks)
        {
            var landmarks = faceLandmarks.landmarks;

            // Face height for normalization
            float faceHeight = Vector2.Distance(
                new Vector2(landmarks[10].x, landmarks[10].y),
                new Vector2(landmarks[152].x, landmarks[152].y)
            );

            // Mouth Openness
            float mouthDist = Vector2.Distance(
                new Vector2(landmarks[13].x, landmarks[13].y),
                new Vector2(landmarks[14].x, landmarks[14].y)
            );
            NormalizedMetrics[0] = mouthDist / faceHeight;

            // Eye Openness Left (Screen right, since mirrored)
            float eyeLeftWidth = Vector2.Distance(
                new Vector2(landmarks[33].x, landmarks[33].y),
                new Vector2(landmarks[133].x, landmarks[133].y)
            );
            float eyeLeftHeight = Vector2.Distance(
                new Vector2(landmarks[159].x, landmarks[159].y),
                new Vector2(landmarks[145].x, landmarks[145].y)
            );
            NormalizedMetrics[1] = eyeLeftHeight / eyeLeftWidth;

            // Eye Openness Right
            float eyeRightWidth = Vector2.Distance(
                new Vector2(landmarks[362].x, landmarks[362].y),
                new Vector2(landmarks[263].x, landmarks[263].y)
            );
            float eyeRightHeight = Vector2.Distance(
                new Vector2(landmarks[386].x, landmarks[386].y),
                new Vector2(landmarks[374].x, landmarks[374].y)
            );
            NormalizedMetrics[2] = eyeRightHeight / eyeRightWidth;

            // Brow Raise
            float browLeftY = (landmarks[70].y + landmarks[300].y) / 2f;
            float browBaseY = (landmarks[21].y + landmarks[251].y) / 2f;
            
            // Note: y goes down in screen coords, so base - tip is the raise amount
            float browRaiseDist = browBaseY - browLeftY;
            NormalizedMetrics[3] = browRaiseDist / faceHeight;
        }
    }
}
