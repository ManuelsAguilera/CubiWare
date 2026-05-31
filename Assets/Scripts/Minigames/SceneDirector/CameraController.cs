using UnityEngine;
using UnityEngine.UI;
using ARcadeRush.Core;
using ARcadeRush.Face;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Manages the Camera UI element for Scene Director.
    /// Responsibilities:
    ///   • Routes the live webcam feed to the RawImage display.
    ///   • Owns the MaskController reference and forwards emotion changes to it.
    ///   • In editor simulation mode: keyboard keys (H / S / A / N) update SimulatedEmotion.
    ///     The simulation is consumed by SceneDirectorGame.Update() — this controller
    ///     no longer drives the approval bar directly.
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach to the Camera UI panel GameObject.
    ///   2. Assign _cameraDisplay (RawImage that will show the webcam feed).
    ///   3. Assign _maskController (child GameObject with MaskController).
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        [Header("References")]
        [SerializeField] private RawImage      _cameraDisplay;
        [SerializeField] private MaskController _maskController;

        private const string LogServiceName = "CameraController";

        // ── Emotion simulation (editor-only) ───────────────────────────────────

        /// <summary>
        /// The last simulated emotion from keyboard input.
        /// Read by SceneDirectorGame.Update() when _editorSimulation is active.
        /// </summary>
        public EmotionLabel SimulatedEmotion { get; private set; } = EmotionLabel.Neutral;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
#if UNITY_EDITOR
            // Keyboard simulation: H=Happy S=Surprised A=Angry N=Neutral
            EmotionLabel? sim = null;
            if (Input.GetKeyDown(KeyCode.H)) sim = EmotionLabel.Happy;
            if (Input.GetKeyDown(KeyCode.S)) sim = EmotionLabel.Surprised;
            if (Input.GetKeyDown(KeyCode.A)) sim = EmotionLabel.Angry;
            if (Input.GetKeyDown(KeyCode.N)) sim = EmotionLabel.Neutral;

            if (sim.HasValue)
            {
                SimulatedEmotion = sim.Value;
                // Immediately swap mask on key press (mask should be responsive)
                SetMaskEmotion(sim.Value);
            }
#endif
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds the live webcam feed to this element's RawImage.
        /// Call from SceneDirectorGame.OnStart() after camera is confirmed playing.
        /// </summary>
        public void BindFeed(CameraFeedCtrl cameraFeed)
        {
            if (cameraFeed == null || _cameraDisplay == null) return;
            cameraFeed.SetOutputImage(_cameraDisplay);
            ServiceLogger.Instance.LogInfo(LogServiceName, "Webcam feed bound to display.");
        }

        /// <summary>
        /// Drives only the AR mask swap. Approval bar polling is handled
        /// centrally by SceneDirectorGame.Update() to avoid conflicts.
        /// </summary>
        public void SetMaskEmotion(EmotionLabel emotion)
        {
            _maskController?.SetEmotion(emotion);
        }
    }
}
