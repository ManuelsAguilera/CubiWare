using System;
using UnityEngine;
using ARcadeRush.Hand;
using ARcadeRush.EmotionDetection;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// v7: Unified frame-by-frame evaluation of player input during the response phase.
    /// Supports two round types — gesture+position and emotion-only.
    ///
    /// Gesture rounds: Every frame reads CurrentDetectedGesture (GestureDetector) AND
    /// RawZone (HandZoneClassifier) atomically — eliminates race condition between
    /// gesture event timing and zone debounce. No event subscriptions.
    ///
    /// Emotion rounds: Every frame polls EmotionGameBridge.IsMatchingEmotion()
    /// directly — no timer gating, fires on first match.
    ///
    /// Key behaviors:
    ///   1. Filter out "None" gestures and baseline (pre-held) gestures
    ///   2. Fire OnPlayerAction only ONCE per round (_actionAlreadyRegistered guard)
    ///   3. Zone validation uses RawZone (zero-lag, undebounced)
    ///   4. Fire OnPlayerTricked when gesture+zone match but command didn't contain "simon dice"
    /// </summary>
    public class SimonJudge : MonoBehaviour
    {
        [SerializeField] private GestureDetector _gestureDetector;

        [Header("Position System (Phase 2)")]
        [SerializeField] private HandZoneClassifier _handZoneClassifier;

        [Header("Emotion System (v5)")]
        [SerializeField] private EmotionGameBridge _emotionBridge;

        [Header("Debug - Emotion (Read-Only)")]
        [SerializeField] private string _currentDominantEmotion = "—";
        [SerializeField] private string _targetEmotion = "—";
        [SerializeField] private bool _faceDetected = false;
        [SerializeField] private bool _bridgeConnected = false;
        [SerializeField] private float _emotionConfidence = 0f;

        /// <summary>Fired when the player performs any detectable action (once per round).</summary>
        public event Action<string> OnPlayerAction;

        /// <summary>Fired when the player returned to neutral after performing an action.</summary>
        public event Action OnPlayerReturnedToNeutral;

        /// <summary>
        /// Fired when the player performed the correct gesture+zone but the command
        /// did NOT contain "simon dice" — they got tricked into acting.
        /// </summary>
        public event Action<string> OnPlayerTricked;

        /// <summary>
        /// Fired when the player matches the target emotion (emotion rounds only).
        /// </summary>
        public event Action<string> OnEmotionMatched;

        private bool _isMonitoring;

        // v3 Fix (F2): prevents multiple firings per round
        private bool _actionAlreadyRegistered;

        // v3 Fix (F5): baseline gesture at monitoring start
        private string _baselineGesture = "None";

        // Phase 2: expected zone for this round
        private HandZone _expectedZone = HandZone.None;

        // Simon Dice trick flag: set by SimonGame before monitoring starts
        private bool _commandContainsSimonDice;

        // v6: expected gesture for continuous evaluation
        private SimonGestureTarget _expectedGesture;
        private bool _hasExpectedGesture;

        // v5: emotion round state
        private bool _isEmotionRound;
        private SimonEmotionTarget _expectedEmotion;

        /// <summary>
        /// v7: Begin monitoring. Captures current gesture as baseline.
        /// Detection is entirely frame-by-frame in Update() — no event subscriptions.
        /// For emotion rounds, ensures AutoInterval mode on the bridge.
        /// </summary>
        public void StartMonitoring()
        {
            _isMonitoring = true;
            _actionAlreadyRegistered = false;

            // Resolve HandZoneClassifier if not assigned in Inspector
            if (_handZoneClassifier == null)
            {
                _handZoneClassifier = FindAnyObjectByType<HandZoneClassifier>();
            }

            // Resolve EmotionGameBridge if not assigned in Inspector
            if (_emotionBridge == null)
            {
                _emotionBridge = EmotionGameBridge.Instance;
            }

            if (_isEmotionRound)
            {
                // Emotion round: ensure AutoInterval mode for polling
                if (_emotionBridge != null)
                {
                    _emotionBridge.SetMode(EmotionDetectionMode.AutoInterval);
                }
            }
            else
            {
                // Gesture round: capture baseline gesture (pre-held gestures ignored)
                _baselineGesture = _gestureDetector != null
                    ? _gestureDetector.CurrentDetectedGesture
                    : "None";
            }
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        /// <summary>
        /// Resets internal state between rounds.
        /// </summary>
        public void ResetState()
        {
            _actionAlreadyRegistered = false;
            _baselineGesture = "None";
            _expectedZone = HandZone.None;
            _commandContainsSimonDice = false;
            _isEmotionRound = false;
            _expectedEmotion = SimonEmotionTarget.Neutral;
            _hasExpectedGesture = false;
            _expectedGesture = SimonGestureTarget.OpenHand;
        }

        /// <summary>
        /// Sets the expected hand zone for this round.
        /// Called by SimonGame before StartMonitoring().
        /// </summary>
        public void SetExpectedZone(HandZone zone)
        {
            _expectedZone = zone;
        }

        /// <summary>
        /// v6: Sets the expected gesture for this round.
        /// Enables continuous evaluation in Update() to catch the race condition
        /// where GestureDetector fires before HandZoneClassifier settles.
        /// Called by SimonGame before StartMonitoring().
        /// </summary>
        public void SetExpectedGesture(SimonGestureTarget gesture)
        {
            _expectedGesture = gesture;
            _hasExpectedGesture = true;
        }

        /// <summary>
        /// Sets whether the command text contains "simon dice" (case-insensitive).
        /// Called by SimonGame before StartMonitoring().
        /// If false and the player performs the action, they get tricked (no points).
        /// </summary>
        public void SetSimonDiceFlag(bool containsSimonDice)
        {
            _commandContainsSimonDice = containsSimonDice;
        }

        /// <summary>
        /// v5: Configures this round as an emotion-only round.
        /// Called by SimonGame before StartMonitoring().
        /// </summary>
        public void SetEmotionRound(SimonEmotionTarget expectedEmotion)
        {
            _isEmotionRound = true;
            _expectedEmotion = expectedEmotion;
            _targetEmotion = SimonCommandGenerator.GetEmotionDisplayName(expectedEmotion);
        }

        /// <summary>
        /// Called by SimonGame to relay the EmotionGameBridge reference.
        /// </summary>
        public void SetEmotionBridge(EmotionGameBridge bridge)
        {
            _emotionBridge = bridge;
        }

        /// <summary>
        /// v5: Returns whether the current round is an emotion-only round.
        /// </summary>
        public bool IsEmotionRound => _isEmotionRound;

        private void Update()
        {
            // ── Debug: always update inspector read-only fields when bridge is available ──
            if (_emotionBridge != null)
            {
                _bridgeConnected = _emotionBridge.IsConnected;
                _faceDetected = _emotionBridge.FaceDetected;
                _currentDominantEmotion = _emotionBridge.GetCurrentDominantEmotion() ?? "—";
                _emotionConfidence = _emotionBridge.Confidence;
            }

            if (!_isMonitoring) return;

            if (_isEmotionRound)
            {
                EvaluateEmotionRound();
            }
            else
            {
                EvaluateGestureRound();
            }
        }

        /// <summary>
        /// v7: Evaluates emotion round every frame — no timer gating.
        /// Checks EmotionGameBridge directly each frame; fires event once via _actionAlreadyRegistered.
        /// </summary>
        /// <summary>
        /// v7 Fix 4: Evaluates emotion round every frame — no timer gating.
        /// When simon didn't say, ANY detected emotion triggers tricked (not just the target).
        /// When simon DID say, only the expected emotion counts.
        /// </summary>
        private void EvaluateEmotionRound()
        {
            if (_actionAlreadyRegistered) return;
            if (_emotionBridge == null || !_emotionBridge.IsConnected || !_emotionBridge.FaceDetected)
                return;

            // ── v7 Fix 4: When simon didn't say, ANY detected emotion = tricked ──
            if (!_commandContainsSimonDice)
            {
                string currentEmotion = _emotionBridge.GetCurrentDominantEmotion();
                if (!string.IsNullOrEmpty(currentEmotion) && _emotionBridge.Confidence >= 0.40f)
                {
                    _actionAlreadyRegistered = true;
                    Debug.Log($"[SimonJudge] Player TRICKED (any emotion)! Detected '{currentEmotion}' but command didn't contain 'simon dice'.");
                    OnPlayerTricked?.Invoke(currentEmotion);
                }
                return;
            }

            // ── Simon DID say — check expected emotion ──
            string targetEmotionStr = SimonCommandGenerator.GetEmotionEnglishName(_expectedEmotion);
            if (string.IsNullOrEmpty(targetEmotionStr)) return;

            if (!_emotionBridge.IsMatchingEmotion(targetEmotionStr))
                return;

            _actionAlreadyRegistered = true;

            Debug.Log($"[SimonJudge] Emotion matched! Target: {_expectedEmotion}, " +
                      $"Dominant: {_emotionBridge.GetCurrentDominantEmotion()}, " +
                      $"Confidence: {_emotionBridge.Confidence:F2}");

            OnEmotionMatched?.Invoke(targetEmotionStr);
        }

        /// <summary>
        /// v7 Fix 4: Unified frame-by-frame gesture+zone evaluation.
        /// Reads RawZone (undebounced) and CurrentDetectedGesture every frame atomically,
        /// eliminating the race condition between GestureDetector events and zone debounce.
        /// When simon didn't say, ANY gesture triggers tricked (not just the expected one).
        /// When simon DID say, only the expected gesture+zone counts.
        /// Fires event once via _actionAlreadyRegistered guard.
        /// </summary>
        private void EvaluateGestureRound()
        {
            if (!_hasExpectedGesture) return;
            if (_actionAlreadyRegistered) return;
            if (_gestureDetector == null) return;

            // v7 Fix 5: use RawGesture for zero-lag detection (not debounced CurrentDetectedGesture)
            string currentGesture = _gestureDetector.RawGesture;
            if (string.IsNullOrEmpty(currentGesture) || currentGesture == "None")
                return;
            if (currentGesture == _baselineGesture)
                return;

            // ── v7 Fix 4: When simon didn't say, ANY gesture = tricked ──
            if (!_commandContainsSimonDice)
            {
                _actionAlreadyRegistered = true;
                Debug.Log($"[SimonJudge] Player TRICKED (any gesture)! Gesture '{currentGesture}' but command didn't contain 'simon dice'.");
                OnPlayerTricked?.Invoke(currentGesture);
                return;
            }

            // ── Simon DID say — check expected gesture + zone ──
            string expectedGestureStr = _expectedGesture.ToString();
            if (!string.Equals(currentGesture, expectedGestureStr, StringComparison.OrdinalIgnoreCase))
                return;

            // Zone validation using RawZone — zero-lag, no debounce
            if (_handZoneClassifier != null && _expectedZone != HandZone.None)
            {
                if (_handZoneClassifier.RawZone != _expectedZone)
                    return;
            }

            // Both gesture and zone match atomically — fire!
            _actionAlreadyRegistered = true;

            Debug.Log($"[SimonJudge] Unified evaluation matched! Gesture='{currentGesture}', RawZone={_handZoneClassifier?.RawZone}, ExpectedZone={_expectedZone}");

            OnPlayerAction?.Invoke(currentGesture);
        }

        // v3 Fix (F9): cleanup on destroy
        private void OnDestroy()
        {
            StopMonitoring();
        }
    }
}
