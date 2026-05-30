using UnityEngine;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Positions and swaps the 3D theatrical AR mask.
    ///
    /// NOTE: Face positioning (FaceCenterNormalized / FaceScale) was previously driven
    /// by FaceLandmarkReader (MediaPipe). That system has been replaced by DeepFace.
    /// Face tracking for AR mask placement is currently DISABLED pending integration
    /// with a new face position source (e.g. from the DeepFace server's region data
    /// or a separate lightweight face detector).
    ///
    /// Mask swapping via SetEmotion() still works — it is driven by EmotionGameBridge
    /// from SceneDirectorGame once the game loop is implemented.
    /// </summary>
    public class MaskController : MonoBehaviour
    {
        [Header("Mask Meshes")]
        [Tooltip("Indexed by (int)EmotionLabel: [0]=Neutral [1]=Happy [2]=Surprised [3]=Angry.")]
        [SerializeField] private GameObject[] _maskObjects = new GameObject[4];

        [Header("Positioning")]
        [SerializeField] private Transform _maskRoot;
        [Tooltip("The camera that renders the Director 3D stage.")]
        [SerializeField] private Camera    _stageCamera;
        [SerializeField] private float     _maskDepth = 1.5f;
        [SerializeField] private float     _scaleMultiplier = 3.5f;

        // TODO: Replace with face position data from DeepFace region or a lightweight
        // face detector. Previously sourced from FaceLandmarkReader.FaceCenterNormalized
        // and FaceLandmarkReader.FaceScale (MediaPipe — now removed).

        private EmotionLabel _currentEmotion = EmotionLabel.Neutral;
        private const string LogServiceName  = "MaskController";

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>Swaps the active mask mesh to match the given emotion.</summary>
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

        // ── Internal ───────────────────────────────────────────────────────────────

        private void SwapMesh(EmotionLabel emotion)
        {
            for (int i = 0; i < _maskObjects.Length; i++)
                if (_maskObjects[i] != null)
                    _maskObjects[i].SetActive(i == (int)emotion);
        }
    }
}
