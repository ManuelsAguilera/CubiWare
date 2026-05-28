using System;
using UnityEngine;

namespace ARcadeRush.Minigames.Simon
{
    /// <summary>
    /// Data model types for the Simón Dice minigame.
    /// Separated from MonoBehaviour scripts for clean dependency chains.
    /// </summary>

    public enum SimonActionType
    {
        Gesture,  // Player must perform a hand gesture
        Emotion   // Player must show a facial emotion (Phase 2)
    }

    public enum SimonGestureTarget
    {
        OpenHand,
        ClosedFist,
        Point,
        Pinch,
        ThumbDown
    }

    public enum SimonEmotionTarget
    {
        Happy,
        Surprised,
        Angry
        // Neutral is implicitly the "don't do anything" state
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
        public SimonActionType ActionType;     // Gesture or Emotion
        public SimonGestureTarget GestureTarget; // Only valid if ActionType == Gesture
        public SimonEmotionTarget EmotionTarget; // Only valid if ActionType == Emotion
        public string DialogueText;            // LLM-generated phrase
    }
}
