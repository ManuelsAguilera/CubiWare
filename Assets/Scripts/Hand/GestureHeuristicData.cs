using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARcadeRush.Hand
{
    public enum FingerStateRequirement { Up, Down, Any, EitherUp }
    public enum CustomHeuristicRule { None, ThumbTipBelowIP, ThumbExtended, ThumbTucked, ThumbAboveMCP, ThumbInsidePalm, ThumbOutsidePalm }

    [Serializable]
    public class GestureHeuristic
    {
        public string GestureName;
        public FingerStateRequirement Thumb = FingerStateRequirement.Any;
        public FingerStateRequirement Index = FingerStateRequirement.Any;
        public FingerStateRequirement Middle = FingerStateRequirement.Any;
        public FingerStateRequirement Ring = FingerStateRequirement.Any;
        public FingerStateRequirement Pinky = FingerStateRequirement.Any;
        public float PinchThreshold = 1.0f;
        public CustomHeuristicRule CustomRule = CustomHeuristicRule.None;

        public static GestureHeuristic FromCsvLine(string line)
        {
            string[] parts = line.Split(',');
            if (parts.Length < 8) return null;

            return new GestureHeuristic
            {
                GestureName = parts[0],
                Thumb = ParseState(parts[1]),
                Index = ParseState(parts[2]),
                Middle = ParseState(parts[3]),
                Ring = ParseState(parts[4]),
                Pinky = ParseState(parts[5]),
                PinchThreshold = float.Parse(parts[6]),
                CustomRule = (CustomHeuristicRule)Enum.Parse(typeof(CustomHeuristicRule), parts[7])
            };
        }

        private static FingerStateRequirement ParseState(string state)
        {
            return state.ToUpper() switch
            {
                "UP" => FingerStateRequirement.Up,
                "DOWN" => FingerStateRequirement.Down,
                _ => FingerStateRequirement.Any
            };
        }
    }

    /// <summary>
    /// Unity-friendly ScriptableObject version of the heuristics.
    /// Allows for visual editing and per-gesture event/effect assignment.
    /// </summary>
    [CreateAssetMenu(fileName = "GestureConfig", menuName = "ARcade/Gesture Config")]
    public class GestureConfig : ScriptableObject
    {
        public List<GestureHeuristic> Heuristics = new List<GestureHeuristic>();
    }
}
