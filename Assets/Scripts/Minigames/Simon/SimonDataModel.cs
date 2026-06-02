using System;
using UnityEngine;
using ARcadeRush.Hand;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// Data model types for the Simón Dice minigame.
    /// Separated from MonoBehaviour scripts for clean dependency chains.
    /// </summary>

    public enum SimonActionType
    {
        Gesture,  // Player must perform a hand gesture
        Emotion   // Player must show a facial emotion (Phase 3)
    }

    public enum SimonGestureTarget
    {
        OpenHand,
        ClosedFist
        // REMOVED for Phase 1: Point, Pinch, ThumbDown
    }

    public enum SimonEmotionTarget
    {
        Happy,
        Angry,
        Sad,
        Neutral,
        Surprise
        // Fear and Disgust excluded due to low DeepFace accuracy
    }

    public enum RoundResult
    {
        None,
        Correct,      // Player obeyed correctly (or stayed still when needed)
        WrongAction,  // Player did a gesture/emotion when Simón did NOT say "Simón dice"
        Timeout,      // Player didn't respond in time when Simón DID say "Simón dice"
        WrongGesture  // Player did the wrong gesture/emotion (not the one requested)
    }

    /// <summary>
    /// Command structure for a single Simón Dice round.
    /// Game-logic parameters are pre-determined; DialogueText is LLM-generated.
    /// v3: Changed from struct to class to avoid boxing with string field.
    /// </summary>
    public class SimonCommand
    {
        public bool SaysSimonDice;             // True = "Simón dice...", False = regular command
        public bool ContainsSimonDice;         // True if the dialogue text contains "simon dice" (case-insensitive). Set by generator.
        public SimonActionType ActionType;     // Gesture or Emotion
        public SimonGestureTarget GestureTarget; // Only valid if ActionType == Gesture
        public SimonEmotionTarget EmotionTarget; // Only valid if ActionType == Emotion
        public HandZone ExpectedZone;          // Phase 2: zone player must move hand to before gesture
        public string DialogueText;            // LLM-generated phrase

        /// <summary>True if this command includes a position requirement (Phase 2).</summary>
        public bool HasPositionTarget => ExpectedZone != HandZone.None;

        /// <summary>True if this command includes an emotion requirement (Phase 3).</summary>
        public bool HasEmotionTarget => ActionType == SimonActionType.Emotion;
    }
}
