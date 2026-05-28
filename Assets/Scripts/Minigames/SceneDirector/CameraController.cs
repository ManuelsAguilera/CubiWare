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
    ///   • In TESTING MODE: keyboard keys (H / S / A / N) simulate emotion changes
    ///     for mask swapping while EmotionClassifier is not yet wired.
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach to the Camera UI panel GameObject.
    ///   2. Assign _cameraDisplay (RawImage that will show the webcam feed).
    ///   3. Assign _maskController (child GameObject with MaskController).
    ///   4. Set _testingMode = true while EmotionClassifier is disabled.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        [Header("References")]
        [SerializeField] private RawImage      _cameraDisplay;
        [SerializeField] private MaskController _maskController;

        [Header("Testing Mode")]
        [Tooltip("While true, keyboard keys H/S/A/N simulate emotion changes for mask testing.")]
        [SerializeField] private bool _testingMode = true;

        private const string LogServiceName = "CameraController";

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
            if (!_testingMode) return;

            // TODO: Remove this block and wire EmotionClassifier.OnEmotionChanged instead.
            EmotionLabel? sim = null;
            if (Input.GetKeyDown(KeyCode.H)) sim = EmotionLabel.Happy;
            if (Input.GetKeyDown(KeyCode.S)) sim = EmotionLabel.Surprised;
            if (Input.GetKeyDown(KeyCode.A)) sim = EmotionLabel.Angry;
            if (Input.GetKeyDown(KeyCode.N)) sim = EmotionLabel.Neutral;

            if (sim.HasValue)
                OnEmotionDetected(sim.Value);
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
        /// Single entry point for a detected emotion — drives both the AR mask and the approval bar.
        /// In testing mode called by the keyboard stub above.
        /// TODO: Wire to EmotionClassifier.OnEmotionChanged in SceneDirectorGame when classifier is live.
        /// </summary>
        public void OnEmotionDetected(EmotionLabel emotion)
        {
            _maskController?.SetEmotion(emotion);
            ApprovalBarController.Instance?.SetDetectedEmotion(emotion);
        }
    }
}
