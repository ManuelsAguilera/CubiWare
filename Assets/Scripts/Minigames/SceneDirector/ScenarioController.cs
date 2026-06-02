using System;
using UnityEngine;
using CubiWare.Core.Logging;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Controls the theater curtain overlay. Opens at round start, closes at round end.
    ///
    /// Setup in the Unity Editor:
    ///   1. Attach this script to a Canvas/UI GameObject that holds the curtain image.
    ///   2. Assign the Animator in the Inspector.
    ///   3. Create two Animator triggers: "Open" and "Close".
    ///   4. At the LAST frame of the Open clip add an Animation Event calling OnOpenComplete_AnimEvent().
    ///   5. At the LAST frame of the Close clip add an Animation Event calling OnCloseComplete_AnimEvent().
    /// </summary>
    public class ScenarioController : MonoBehaviour
    {
        public static ScenarioController Instance { get; private set; }

        /// <summary>Fired once the curtain-open animation finishes. Start the round here.</summary>
        public event Action OnOpenComplete;

        /// <summary>Fired once the curtain-close animation finishes. Clean up or return to menu here.</summary>
        public event Action OnCloseComplete;

        [Header("References")]
        [SerializeField] private Animator _animator;

        private static readonly int TriggerOpen  = Animator.StringToHash("Open");
        private static readonly int TriggerClose = Animator.StringToHash("Close");

        private const string LogServiceName = "ScenarioController";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Code-based discovery if Inspector wiring is missing
            if (_animator == null) _animator = GetComponent<Animator>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Plays the curtain-open animation.</summary>
        public void Open()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain opening.");
            _animator.SetTrigger(TriggerOpen);
        }

        /// <summary>Plays the curtain-close animation.</summary>
        public void Close()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain closing.");
            _animator.SetTrigger(TriggerClose);
        }

        // ── Animation Events ──────────────────────────────────────────────────────
        // These must be wired in the Animator clip editor, not called from code.

        public void OnOpenComplete_AnimEvent()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain fully open.");
            OnOpenComplete?.Invoke();
        }

        public void OnCloseComplete_AnimEvent()
        {
            ServiceLogger.Instance.LogInfo(LogServiceName, "Curtain fully closed.");
            OnCloseComplete?.Invoke();
        }
    }
}
