using UnityEngine;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Positions and swaps the 3D theatrical AR mask to follow the player's face.
    ///
    /// HOW POSITIONING WORKS
    /// ─────────────────────
    /// The Director scene uses a dedicated Stage Camera that looks at a background
    /// quad displaying the webcam texture. The mask is a 3D mesh sitting in world
    /// space between that camera and the quad. Face position is converted from
    /// MediaPipe normalised image space [0..1] to Unity viewport space, then to
    /// world space via Camera.ViewportToWorldPoint.
    ///
    /// Setup in the Unity Editor:
    ///   1. Create three theatrical mask prefabs (Happy, Surprised, Angry) and
    ///      assign them to _maskObjects[1], [2], [3]. Index = (int)EmotionLabel.
    ///      [0] Neutral = no mask (leave null or use a neutral blank mesh).
    ///   2. Place all mask meshes as children of _maskRoot. Only one is active at a time.
    ///   3. Assign _stageCamera — the camera that renders the Director stage (NOT the UI camera).
    ///   4. Assign _reader (FaceLandmarkReader in the scene).
    ///   5. Tune _maskDepth and _scaleMultiplier until the mask sits correctly over the face.
    ///
    /// COORDINATE NOTE
    /// ───────────────
    /// MediaPipe landmark Y increases top→bottom; Unity viewport Y increases bottom→top.
    /// This script flips Y automatically.
    /// </summary>
    public class MaskController : MonoBehaviour
    {
        [Header("Mask Meshes")]
        [Tooltip("Indexed by (int)EmotionLabel: [0]=Neutral [1]=Happy [2]=Surprised [3]=Angry. Leave [0] null for no mask at neutral.")]
        [SerializeField] private GameObject[] _maskObjects = new GameObject[4];

        [Header("Positioning")]
        [SerializeField] private Transform _maskRoot;
        [Tooltip("The camera that renders the Director 3D stage (not the UI camera).")]
        [SerializeField] private Camera    _stageCamera;
        [Tooltip("Depth in camera-space units at which the mask is placed (should sit in front of the background quad).")]
        [SerializeField] private float     _maskDepth = 1.5f;
        [Tooltip("Multiplied by FaceScale (normalised face width) to get world-space mask size.")]
        [SerializeField] private float     _scaleMultiplier = 3.5f;

        [Header("References")]
        [SerializeField] private FaceLandmarkReader _reader;

        private EmotionLabel _currentEmotion = EmotionLabel.Neutral;
        private const string LogServiceName  = "MaskController";

        private void Awake()
        {
            if (_reader == null)
                _reader = FindAnyObjectByType<FaceLandmarkReader>();

            // Validate Stage Camera setup early — missing RenderTexture is a silent failure
            if (_stageCamera != null && _stageCamera.targetTexture == null)
                ServiceLogger.Instance.LogWarning(LogServiceName,
                    "Stage Camera has no Target Texture assigned. Masks will not be visible.");
        }

        private void Update()
        {
            if (_reader == null || !_reader.HasFreshData) return;
            if (_reader.HeadConfidence < 0.2f) return; // face too far sideways — hide mask

            UpdatePosition();
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Swaps the active mask mesh to the one matching the given emotion.</summary>
        public void SetEmotion(EmotionLabel emotion)
        {
            if (emotion == _currentEmotion) return;
            _currentEmotion = emotion;
            SwapMesh(emotion);
            ServiceLogger.Instance.LogInfo(LogServiceName, $"Mask → {emotion}");
        }

        /// <summary>Hides all mask meshes.</summary>
        public void HideMask()
        {
            foreach (var m in _maskObjects)
                if (m != null) m.SetActive(false);
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        private void UpdatePosition()
        {
            if (_maskRoot == null || _stageCamera == null) return;

            Vector2 faceNorm = _reader.FaceCenterNormalized;

            // Convert MediaPipe image space → Unity viewport space (flip Y).
            Vector3 viewportPoint = new Vector3(
                faceNorm.x,
                1f - faceNorm.y,
                _maskDepth
            );

            _maskRoot.position   = _stageCamera.ViewportToWorldPoint(viewportPoint);
            _maskRoot.localScale = Vector3.one * (_reader.FaceScale * _scaleMultiplier);

            // TODO: Apply head rotation for better 3D alignment.
            // When facialTransformationMatrixes is exposed from FaceLandmarkReader,
            // extract the rotation component and apply it here:
            //   _maskRoot.rotation = ExtractRotation(_reader.HeadPoseMatrix);
        }

        private void SwapMesh(EmotionLabel emotion)
        {
            for (int i = 0; i < _maskObjects.Length; i++)
                if (_maskObjects[i] != null)
                    _maskObjects[i].SetActive(i == (int)emotion);
        }
    }
}
