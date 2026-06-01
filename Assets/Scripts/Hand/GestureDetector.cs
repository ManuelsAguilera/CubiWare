using System;
using System.Collections.Generic;
using UnityEngine;
using Mediapipe.Tasks.Components.Containers;
using CubiWare.Core.Interfaces;
using ARcadeRush.Core;

namespace ARcadeRush.Hand
{
    public enum GestureType { None, OpenHand, ClosedFist, Point, Pinch, ThumbDown, Custom }

    /// <summary>
    /// Handedness assumption for palm-orientation validation.
    /// Auto skips the check when MediaPipe handedness is unavailable.
    /// </summary>
    public enum HandednessAssumption { Right, Left, Auto }

    public class GestureDetector : MonoBehaviour
    {
        [Header("Activation")]
        [Tooltip("If false, this detector will remain idle until EnableDetector() is called.")]
        [SerializeField] private bool _enabledByDefault = true;
        
        [Header("Palm Orientation")]
        [Tooltip("If true, OpenHand is only accepted when palm faces the camera (thumb outside palm).")]
        [SerializeField] private bool _requirePalmFacingCamera = true;

        [Tooltip("Handedness assumption for palm check. 'Right' means thumb should be on right side of index MCP in mirrored view.")]
        [SerializeField] private HandednessAssumption _palmHandAssumption = HandednessAssumption.Auto;

        [Header("Filtering")]
        [SerializeField] private List<string> _enabledGestures = new List<string>();
        [SerializeField] private string _currentDetectedGesture = "None";
        /// <summary>Debounced gesture (5 stable frames). Use for baseline capture and UI.</summary>
        public string CurrentDetectedGesture => _currentDetectedGesture;

        /// <summary>
        /// v7 Fix 5: Raw, un-debounced gesture classification for the current frame.
        /// Use for judging/validation where immediate gesture awareness is needed.
        /// The debounced CurrentDetectedGesture is better for baseline capture and UI.
        /// </summary>
        public string RawGesture { get; private set; } = "None";

        [Header("Debug Finger States")]
        [SerializeField] private string[] _currentFingerStates = new string[5];
        
        public event Action OnOpenHand;
        public event Action OnClosedFist;
        public event Action OnPoint;
        public event Action OnPinch;
        public event Action OnPinchRelease;
        public event Action OnThumbDown;
        
        public event Action<string> OnGestureDetected;

        [Header("Configuration")]
        [SerializeField] private string _heuristicsCsvName = "GestureHeuristics";

        private string _previousGestureName = "None";
        private List<GestureHeuristic> _heuristics = new List<GestureHeuristic>();
        private bool _isProcessing = false;
        private List<Vector2> _lastLandmarks;
        
        // Smoothing
        private string _pendingGestureName = "None";
        private int _gestureFrameCount = 0;
        private const int _requiredStableFrames = 5;

        private void Start()
        {
            Debug.Log("[GestureDetector] STARTING UP!");
            _isProcessing = _enabledByDefault;
            LoadHeuristics();
            StartCoroutine(WaitForMediaPipe());

            // Phase 1: Default to OpenHand + ClosedFist only for Simon game compatibility.
            // Other minigames can call SetEnabledGestures() to override.
            if (_enabledGestures.Count == 0)
            {
                SetEnabledGestures(new List<string> { "OpenHand", "ClosedFist" });
            }
        }

        private System.Collections.IEnumerator WaitForMediaPipe()
        {
            Debug.Log("[GestureDetector] Waiting for MediaPipeController.Instance...");
            while (MediaPipeController.Instance == null)
            {
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log($"[GestureDetector] Connected to MediaPipeController! Subscribing to OnHandDetected...");
            MediaPipeController.Instance.OnHandDetected += HandleHandDetected;
        }

        private void HandleHandDetected(Mediapipe.Tasks.Components.Containers.NormalizedLandmarks data)
        {
            if (_lastLandmarks == null) Debug.Log("[GestureDetector] RECEIVED FIRST SET OF LANDMARKS!");
            
            var landmarks = new List<Vector2>();
            foreach (var lm in data.landmarks) landmarks.Add(new Vector2(lm.x, lm.y));
            _lastLandmarks = landmarks;
        }

        public void SetDetectionActive(bool active) => _isProcessing = active;

        /// <summary>
        /// Restricts gesture detection to only the specified gesture names.
        /// Pass an empty list or null to allow all gestures.
        /// </summary>
        public void SetEnabledGestures(List<string> gestureNames)
        {
            _enabledGestures = gestureNames ?? new List<string>();
            Debug.Log($"[GestureDetector] Enabled gestures: {(_enabledGestures.Count > 0 ? string.Join(", ", _enabledGestures) : "ALL")}");
        }

        private void LoadHeuristics()
        {
            TextAsset csvFile = Resources.Load<TextAsset>(_heuristicsCsvName);
            if (csvFile == null) return;
            string[] lines = csvFile.text.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var h = GestureHeuristic.FromCsvLine(lines[i]);
                if (h != null) _heuristics.Add(h);
            }
        }

        private void Update()
        {
            if (!_isProcessing) return;

            if (_lastLandmarks == null || _lastLandmarks.Count < 21)
            {
                if (_currentDetectedGesture != "None")
                {
                    _currentDetectedGesture = "None";
                    _previousGestureName = "None";
                    OnGestureDetected?.Invoke("None");
                }
                return;
            }

            // 1. Detect raw finger states (Inverted logic: y > joint means UP)
            bool[] fingerStates = new bool[5];
            fingerStates[0] = _lastLandmarks[4].y > _lastLandmarks[2].y; 
            fingerStates[1] = _lastLandmarks[8].y > _lastLandmarks[5].y; 
            fingerStates[2] = _lastLandmarks[12].y > _lastLandmarks[9].y;
            fingerStates[3] = _lastLandmarks[16].y > _lastLandmarks[13].y;
            fingerStates[4] = _lastLandmarks[20].y > _lastLandmarks[17].y;

            for (int i = 0; i < 5; i++) _currentFingerStates[i] = fingerStates[i] ? "UP" : "DOWN";

            float pinchDist = Vector2.Distance(_lastLandmarks[4], _lastLandmarks[8]);
            
            string currentGestureName = "None";
            foreach (var h in _heuristics)
            {
                if (_enabledGestures.Count > 0 && !_enabledGestures.Contains(h.GestureName)) continue;
                if (IsMatch(h, fingerStates, pinchDist, _lastLandmarks))
                {
                    currentGestureName = h.GestureName;
                    break;
                }
            }

            // v7 Fix 5: expose raw gesture for immediate judging (no debounce lag)
            RawGesture = currentGestureName;

            // Smoothing/Debouncing
            if (currentGestureName == _pendingGestureName) _gestureFrameCount++;
            else { _pendingGestureName = currentGestureName; _gestureFrameCount = 1; }

            if (_gestureFrameCount >= _requiredStableFrames)
            {
                if (currentGestureName != _previousGestureName)
                {
                    if (_previousGestureName == "Pinch" && currentGestureName != "Pinch") OnPinchRelease?.Invoke();
                    
                    _currentDetectedGesture = currentGestureName;

                    // Don't fire events for "None" — it's not a gesture, just absence.
                    if (currentGestureName != "None")
                    {
                        InvokeGestureEvent(currentGestureName);
                        OnGestureDetected?.Invoke(currentGestureName);
                    }
                    _previousGestureName = currentGestureName;
                }
            }
        }

        private bool IsMatch(GestureHeuristic h, bool[] states, float pinchDist, List<Vector2> landmarks)
        {
            if (h.PinchThreshold < 1.0f && pinchDist > h.PinchThreshold) return false;

            if (!CheckFinger(h.Thumb, states[0])) return false;

            // EitherUp Logic: Index OR Middle must be UP
            if (h.Index == FingerStateRequirement.EitherUp || h.Middle == FingerStateRequirement.EitherUp)
            {
                if (!(states[1] || states[2])) return false;
            }
            else
            {
                if (!CheckFinger(h.Index, states[1])) return false;
                if (!CheckFinger(h.Middle, states[2])) return false;
            }

            if (!CheckFinger(h.Ring, states[3])) return false;
            if (!CheckFinger(h.Pinky, states[4])) return false;

            // Custom Rules
            switch (h.CustomRule)
            {
                case CustomHeuristicRule.ThumbTipBelowIP:
                    if (!(landmarks[4].y > landmarks[3].y)) return false;
                    break;
                case CustomHeuristicRule.ThumbAboveMCP:
                    if (!(landmarks[4].y < landmarks[2].y)) return false;
                    break;
                case CustomHeuristicRule.ThumbExtended:
                    if (!(Mathf.Abs(landmarks[4].x - landmarks[2].x) > 0.15f)) return false;
                    break;
                case CustomHeuristicRule.ThumbTucked:
                    if (!(Vector2.Distance(landmarks[4], landmarks[2]) < 0.1f)) return false;
                    break;
                case CustomHeuristicRule.ThumbInsidePalm:
                    if (!(landmarks[4].x < landmarks[5].x)) return false;
                    break;
                case CustomHeuristicRule.ThumbOutsidePalm:
                    if (!(landmarks[4].x > landmarks[5].x)) return false;
                    break;
            }

            // Phase 1: Palm-orientation validation for OpenHand.
            // If palm faces away from camera, treat as not-OpenHand.
            if (_requirePalmFacingCamera && h.GestureName == "OpenHand")
            {
                if (!IsPalmFacingCamera(landmarks)) return false;
            }

            return true;
        }

        private bool CheckFinger(FingerStateRequirement req, bool isUp)
        {
            if (req == FingerStateRequirement.Any || req == FingerStateRequirement.EitherUp) return true;
            if (req == FingerStateRequirement.Up && !isUp) return false;
            if (req == FingerStateRequirement.Down && isUp) return false;
            return true;
        }

        private void InvokeGestureEvent(string name)
        {
            // Phase 1: Secondary whitelist guard — suppress events for non-whitelisted gestures
            // even if they somehow pass heuristic matching.
            if (_enabledGestures.Count > 0 && !_enabledGestures.Contains(name))
            {
                Debug.LogWarning($"[GestureDetector] InvokeGestureEvent blocked for '{name}' — not in enabled list.");
                return;
            }

            switch (name)
            {
                case "OpenHand": OnOpenHand?.Invoke(); break;
                case "ClosedFist": OnClosedFist?.Invoke(); break;
                case "Point": OnPoint?.Invoke(); break;
                case "Pinch": OnPinch?.Invoke(); break;
                case "ThumbDown": OnThumbDown?.Invoke(); break;
            }
        }

        /// <summary>
        /// Validates that the palm is facing the camera (not the back of the hand).
        /// Heuristic: For a right hand in mirrored webcam view, the thumb tip (4)
        /// should be to the RIGHT of the index MCP (5) when palm faces camera.
        /// For left hand, the opposite. Auto mode skips the check.
        /// </summary>
        private bool IsPalmFacingCamera(List<Vector2> landmarks)
        {
            if (landmarks == null || landmarks.Count < 6) return true; // no data, allow

            float thumbX = landmarks[4].x;
            float indexMcpX = landmarks[5].x;

            switch (_palmHandAssumption)
            {
                case HandednessAssumption.Right:
                    // Right hand facing camera (mirrored): thumb on RIGHT side
                    return thumbX > indexMcpX;
                case HandednessAssumption.Left:
                    // Left hand facing camera (mirrored): thumb on LEFT side
                    return thumbX < indexMcpX;
                case HandednessAssumption.Auto:
                default:
                    // Can't determine without MediaPipe handedness — allow both
                    return true;
            }
        }
    }
}
