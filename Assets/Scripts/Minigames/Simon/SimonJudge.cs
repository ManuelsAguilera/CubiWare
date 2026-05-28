using System;
using UnityEngine;
using ARcadeRush.Hand;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// Monitors GestureDetector during the response phase.
    /// Phase 2: also monitors EmotionClassifier.
    /// Reports any detected action to SimonGame for evaluation.
    ///
    /// IMPORTANT — GestureDetector behavior (verified from source):
    ///   - Already has 5-frame debounce (_requiredStableFrames)
    ///   - Fires OnGestureDetected only on TRANSITIONS (not every frame)
    ///   - Fires "None" as a valid gesture transition
    ///   - Exposes CurrentDetectedGesture for polling
    ///
    /// This means SimonJudge does NOT need frame-level debounce, but MUST:
    ///   1. Filter out "None" events
    ///   2. Fire OnPlayerAction only ONCE per round (_actionAlreadyRegistered)
    ///   3. Capture pre-existing gesture at monitoring start
    /// </summary>
    public class SimonJudge : MonoBehaviour
    {
        [SerializeField] private GestureDetector _gestureDetector;
        // [SerializeField] private EmotionClassifier _emotionClassifier; // Phase 2

        /// <summary>Fired when the player performs any detectable action (once per round).</summary>
        public event Action<string> OnPlayerAction;

        /// <summary>Fired when the player returned to neutral after performing an action.</summary>
        public event Action OnPlayerReturnedToNeutral;

        private bool _isMonitoring;

        // v3 Fix (F2): prevents multiple firings per round
        private bool _actionAlreadyRegistered;

        // v3 Fix (F5): baseline gesture at monitoring start
        private string _baselineGesture = "None";

        /// <summary>
        /// Begin monitoring. Captures current gesture as baseline.
        /// If player is already holding a non-None gesture, it becomes the baseline
        /// and won't be reported — only a NEW gesture transition will fire.
        /// </summary>
        public void StartMonitoring()
        {
            _isMonitoring = true;
            _actionAlreadyRegistered = false;

            // v3 Fix (F5): capture current gesture as baseline
            _baselineGesture = _gestureDetector != null
                ? _gestureDetector.CurrentDetectedGesture
                : "None";

            // Subscribe to events
            if (_gestureDetector != null)
            {
                _gestureDetector.OnGestureDetected += HandleGestureDetected;
            }
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;

            // Unsubscribe from events
            if (_gestureDetector != null)
            {
                _gestureDetector.OnGestureDetected -= HandleGestureDetected;
            }
        }

        /// <summary>
        /// Resets internal state between rounds.
        /// </summary>
        public void ResetState()
        {
            _actionAlreadyRegistered = false;
            _baselineGesture = "None";
        }

        private void HandleGestureDetected(string gestureName)
        {
            if (!_isMonitoring) return;

            // v3 Fix (F4): filter out "None" — not a player action
            if (gestureName == "None")
            {
                // Player returned to neutral — only notify if they had acted
                if (_actionAlreadyRegistered)
                {
                    OnPlayerReturnedToNeutral?.Invoke();
                }
                return;
            }

            // v3 Fix (F5): if gesture matches baseline, it's pre-held — ignore
            if (gestureName == _baselineGesture)
            {
                return;
            }

            // v3 Fix (F2): fire only ONCE per monitoring session
            if (_actionAlreadyRegistered) return;
            _actionAlreadyRegistered = true;

            OnPlayerAction?.Invoke(gestureName);
        }

        // v3 Fix (F9): cleanup on destroy
        private void OnDestroy()
        {
            StopMonitoring();
        }
    }
}
