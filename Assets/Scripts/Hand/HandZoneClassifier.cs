using System;
using UnityEngine;

namespace ARcadeRush.Hand
{
    /// <summary>
    /// Six-zone classification of the camera frame in normalized [0,1] space.
    /// MediaPipe coordinates: Origin (0,0) = TOP-LEFT of camera image.
    /// X increases rightward, Y increases DOWNWARD.
    /// So "Up" = small Y (hand high), "Down" = large Y (hand low).
    /// </summary>
    public enum HandZone
    {
        None,           // Hand not detected or in dead zone between thresholds
        UpLeft,         // X < leftThreshold, Y < upThreshold (small Y = high)
        UpRight,        // X > rightThreshold, Y < upThreshold
        DownLeft,       // X < leftThreshold, Y > downThreshold (large Y = low)
        DownRight,      // X > rightThreshold, Y > downThreshold
        Center          // Within centerRange rectangle of (0.5, 0.5)
    }

    /// <summary>
    /// Shared component that classifies a normalized [0,1] hand position into
    /// one of six directional zones. Consumes HandPositionTracker.CurrentHandPosition
    /// (polled, not duplicate). Uses configurable thresholds with dead zones to
    /// prevent flickering at boundaries.
    ///
    /// Dependency resolution: Finds HandPositionTracker via FindAnyObjectByType
    /// (same pattern as Fruit Ninja's Blade.cs).
    ///
    /// Intended consumers:
    ///   - SimonJudge (position validation for Simon Dice)
    ///   - PositionInstructor (visual/audio cues)
    ///   - Future minigames needing directional zone awareness
    /// </summary>
    public class HandZoneClassifier : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandPositionTracker _positionTracker;

        [Header("Zone Thresholds — Midpoint-Based (v2)")]
        [Tooltip("X values BELOW this are 'Left'. Default 0.5 = midpoint of the X axis.")]
        [SerializeField] [Range(0f, 1f)] private float _leftThreshold = 0.5f;

        [Tooltip("X values ABOVE this are 'Right'. Default 0.5 = midpoint of the X axis.")]
        [SerializeField] [Range(0f, 1f)] private float _rightThreshold = 0.5f;

        [Tooltip("Y values BELOW this are 'Up' (MediaPipe: small Y = high). Default 0.5 = midpoint of the Y axis.")]
        [SerializeField] [Range(0f, 1f)] private float _upThreshold = 0.5f;

        [Tooltip("Y values ABOVE this are 'Down' (MediaPipe: large Y = low). Default 0.5 = midpoint of the Y axis.")]
        [SerializeField] [Range(0f, 1f)] private float _downThreshold = 0.5f;

        [Tooltip("Radius around (0.5, 0.5) considered 'Center'. Kept for backward compatibility.")]
        [SerializeField] [Range(0.05f, 0.30f)] private float _centerRadius = 0.15f;

        [Header("Center Zone")]
        [Tooltip("How far from absolute center (0.5,0.5) in X and Y the hand can be to classify as Center. Center always takes priority over quadrants.")]
        [SerializeField] [Range(0.05f, 0.3f)] private float _centerRange = 0.15f;

        [Header("Debounce")]
        [Tooltip("How many consecutive frames in a zone before confirming transition. Prevents flickering.")]
        [SerializeField] [Range(1, 10)] private int _debounceFrames = 4;

        [Header("Debug")]
        [SerializeField] private HandZone _currentZone = HandZone.None;
        [SerializeField] private HandZone _previousZone = HandZone.None;

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>Current confirmed zone (debounced). Use for UI elements to prevent flicker.</summary>
        public HandZone CurrentZone => _currentZone;

        /// <summary>
        /// v7: Raw, un-debounced zone classification for the current frame.
        /// Use for judging/validation where immediate zone awareness is needed
        /// and a single-frame flicker is acceptable (judge fires only once per round).
        /// The debounced CurrentZone is better for persistent UI to prevent flicker.
        /// </summary>
        public HandZone RawZone { get; private set; } = HandZone.None;

        /// <summary>Fires when the confirmed zone changes. (oldZone, newZone).</summary>
        public event Action<HandZone, HandZone> OnZoneChanged;

        /// <summary>Configurable thresholds — exposed for runtime tuning. v2: all thresholds default to 0.5 (midpoint).</summary>
        public float LeftThreshold  { get => _leftThreshold;  set => _leftThreshold  = Mathf.Clamp(value, 0f, 1f); }
        public float RightThreshold { get => _rightThreshold; set => _rightThreshold = Mathf.Clamp(value, 0f, 1f); }
        public float UpThreshold    { get => _upThreshold;    set => _upThreshold    = Mathf.Clamp(value, 0f, 1f); }
        public float DownThreshold  { get => _downThreshold;  set => _downThreshold  = Mathf.Clamp(value, 0f, 1f); }
        public float CenterRadius   { get => _centerRadius;   set => _centerRadius   = Mathf.Clamp(value, 0.05f, 0.30f); }
        public float CenterRange    { get => _centerRange;    set => _centerRange    = Mathf.Clamp(value, 0.05f, 0.3f); }

        // ── Debounce State ──────────────────────────────────────────────

        private HandZone _pendingZone = HandZone.None;
        private int _zoneFrameCount = 0;

        // ── Unity Lifecycle ─────────────────────────────────────────────

        private void Start()
        {
            // Resolve HandPositionTracker if not assigned in Inspector
            // Same pattern as Fruit Ninja's Blade.cs
            if (_positionTracker == null)
            {
                _positionTracker = FindAnyObjectByType<HandPositionTracker>();
                if (_positionTracker == null)
                {
                    Debug.LogWarning("[HandZoneClassifier] No HandPositionTracker found in scene. Zone classification disabled.");
                }
            }
        }

        private void Update()
        {
            if (_positionTracker == null) return;

            Vector2 handPos = _positionTracker.CurrentHandPosition;
            HandZone rawZone = ClassifyPosition(handPos);

            // v7: expose raw zone for immediate judging (no debounce lag)
            RawZone = rawZone;

            // Debounce: must stay in same raw zone for _debounceFrames before confirming
            if (rawZone == _pendingZone)
            {
                _zoneFrameCount++;
            }
            else
            {
                _pendingZone = rawZone;
                _zoneFrameCount = 1;
            }

            if (_zoneFrameCount >= _debounceFrames && rawZone != _currentZone)
            {
                HandZone oldZone = _currentZone;
                _currentZone = rawZone;
                _previousZone = oldZone;
                OnZoneChanged?.Invoke(oldZone, rawZone);
            }
        }

        // ── Classification Logic ────────────────────────────────────────

        /// <summary>
        /// v2.1: Midpoint-based classification with independent Up/Left/Down/Right flags.
        /// Corners take priority over Center — quadrant check runs first.
        ///
        /// MediaPipe coordinates: (0,0)=top-left, X increases right, Y increases DOWN.
        /// So "Up" = small Y (hand high in image), "Down" = large Y (hand low in image).
        ///
        /// Classification order:
        ///   1. Out of bounds → None
        ///   2. Quadrant classification: Up/Left/Down/Right are ALL determined independently
        ///      against the midpoint 0.5. Each quadrant is the intersection of one vertical
        ///      flag and one horizontal flag. Corners win over Center.
        ///   3. If only one axis is extreme (e.g., only Up but neither Left nor Right) → Center
        ///   4. If position is at exact 0.5 on both axes → Center
        ///
        /// isLeft/isRight are independent booleans. isUp/isDown are also independent.
        /// There are no "dead zones" — every valid position maps to a zone.
        /// </summary>
        public HandZone ClassifyPosition(Vector2 position)
        {
            // Out of bounds
            if (position.x < 0f || position.x > 1f || position.y < 0f || position.y > 1f)
                return HandZone.None;

            // ── v2.1: Independent axis flags — threshold at 0.5 midpoint ──────
            // Each flag is determined independently against the configured threshold.
            bool isLeft  = position.x < _leftThreshold;
            bool isRight = position.x > _rightThreshold;

            // MediaPipe: small Y = hand is high (up), large Y = hand is low (down)
            bool isUp    = position.y < _upThreshold;
            bool isDown  = position.y > _downThreshold;

            // ── v2.1: Quadrants first (corners override Center) ─────
            // If both axes are extreme → classify as a corner quadrant
            if (isUp && isLeft)   return HandZone.UpLeft;
            if (isUp && isRight)  return HandZone.UpRight;
            if (isDown && isLeft)  return HandZone.DownLeft;
            if (isDown && isRight) return HandZone.DownRight;

            // If only one axis is extreme or neither → Center
            return HandZone.Center;
        }

        /// <summary>
        /// Returns true if the current confirmed zone matches the target zone.
        /// </summary>
        public bool IsInZone(HandZone targetZone)
        {
            return _currentZone == targetZone && targetZone != HandZone.None;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw zone boundaries when selected in the Editor.
            // Note: this draws in world space at the GameObject's position for reference.
            // The actual zones are in normalized [0,1] image space, so this is approximate.
            Gizmos.color = new Color(1f, 1f, 1f, 0.4f);

            float scale = 5f;
            Vector3 origin = transform.position - new Vector3(scale * 0.5f, scale * 0.5f, 0f);

            // Left/Right threshold vertical lines
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f); // orange
            Gizmos.DrawLine(origin + new Vector3(_leftThreshold * scale, 0f, 0f),
                            origin + new Vector3(_leftThreshold * scale, scale, 0f));
            Gizmos.DrawLine(origin + new Vector3(_rightThreshold * scale, 0f, 0f),
                            origin + new Vector3(_rightThreshold * scale, scale, 0f));

            // Up/Down threshold horizontal lines
            Gizmos.color = new Color(0f, 1f, 1f, 0.4f); // cyan
            Gizmos.DrawLine(origin + new Vector3(0f, _upThreshold * scale, 0f),
                            origin + new Vector3(scale, _upThreshold * scale, 0f));
            Gizmos.DrawLine(origin + new Vector3(0f, _downThreshold * scale, 0f),
                            origin + new Vector3(scale, _downThreshold * scale, 0f));

            // Center rectangle (new independent CenterRange)
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f); // green
            Vector3 centerPoint = origin + new Vector3(0.5f * scale, 0.5f * scale, 0f);
            float halfRange = _centerRange * scale;
            // Draw rectangle for Center zone
            Gizmos.DrawWireCube(centerPoint, new Vector3(halfRange * 2f, halfRange * 2f, 0f));

            // Also draw the legacy centerRadius circle (fainter, for reference)
            Gizmos.color = new Color(0f, 1f, 0f, 0.1f);
            Gizmos.DrawWireSphere(centerPoint, _centerRadius * scale);

            // Zone labels
            Gizmos.color = Color.white;
            float labelScale = scale * 0.9f;
            DrawGizmoLabel(origin + new Vector3(0.0f, _upThreshold * scale * 0.5f, 0f), "UpLeft");
            DrawGizmoLabel(origin + new Vector3(labelScale, _upThreshold * scale * 0.5f, 0f), "UpRight");
            DrawGizmoLabel(origin + new Vector3(0.0f, scale - (_downThreshold * scale * 0.3f), 0f), "DownLeft");
            DrawGizmoLabel(origin + new Vector3(labelScale, scale - (_downThreshold * scale * 0.3f), 0f), "DownRight");
            DrawGizmoLabel(centerPoint + Vector3.up * (halfRange + 0.3f), "Center");
        }

        private void DrawGizmoLabel(Vector3 position, string text)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.Label(position, text);
#endif
        }
#endif
    }
}
