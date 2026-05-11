using System.Collections.Generic;
using CubiWare.Core.Interfaces;

namespace CubiWare.Core.Services
{
    /// <summary>
    /// Static helper class providing factory methods for creating common
    /// <see cref="InteractionEventData"/> instances with pre-populated fields.
    /// </summary>
    public static class InteractionEvent
    {
        /// <summary>
        /// Creates an event indicating that a minigame has started.
        /// </summary>
        /// <param name="minigameName">The name of the minigame being started.</param>
        /// <returns>An <see cref="InteractionEventData"/> with event type "game_start".</returns>
        public static InteractionEventData GameStartEvent(string minigameName)
        {
            return new InteractionEventData
            {
                EventType = "game_start",
                SceneName = minigameName,
                Timestamp = UnityEngine.Time.realtimeSinceStartup,
                Parameters = new Dictionary<string, object>
                {
                    { "minigame_name", minigameName }
                }
            };
        }

        /// <summary>
        /// Creates an event indicating that a minigame has ended.
        /// </summary>
        /// <param name="minigameName">The name of the minigame that ended.</param>
        /// <param name="score">The final score achieved.</param>
        /// <param name="duration">The duration of the game session in seconds.</param>
        /// <returns>An <see cref="InteractionEventData"/> with event type "game_end".</returns>
        public static InteractionEventData GameEndEvent(string minigameName, int score, float duration)
        {
            return new InteractionEventData
            {
                EventType = "game_end",
                SceneName = minigameName,
                Timestamp = UnityEngine.Time.realtimeSinceStartup,
                Parameters = new Dictionary<string, object>
                {
                    { "minigame_name", minigameName },
                    { "score", score },
                    { "duration_seconds", duration }
                }
            };
        }

        /// <summary>
        /// Creates an event indicating that a gesture was detected.
        /// </summary>
        /// <param name="gestureName">The name of the detected gesture.</param>
        /// <param name="confidence">The detection confidence (0 to 1).</param>
        /// <returns>An <see cref="InteractionEventData"/> with event type "gesture_detected".</returns>
        public static InteractionEventData GestureDetectedEvent(string gestureName, float confidence)
        {
            return new InteractionEventData
            {
                EventType = "gesture_detected",
                Timestamp = UnityEngine.Time.realtimeSinceStartup,
                Parameters = new Dictionary<string, object>
                {
                    { "gesture_name", gestureName },
                    { "confidence", confidence }
                }
            };
        }

        /// <summary>
        /// Creates an event indicating that an emotion was detected.
        /// </summary>
        /// <param name="emotion">The detected emotion label.</param>
        /// <param name="confidence">The detection confidence (0 to 1).</param>
        /// <returns>An <see cref="InteractionEventData"/> with event type "emotion_detected".</returns>
        public static InteractionEventData EmotionDetectedEvent(string emotion, float confidence)
        {
            return new InteractionEventData
            {
                EventType = "emotion_detected",
                Timestamp = UnityEngine.Time.realtimeSinceStartup,
                Parameters = new Dictionary<string, object>
                {
                    { "emotion", emotion },
                    { "confidence", confidence }
                }
            };
        }

        /// <summary>
        /// Creates an event indicating a scene transition.
        /// </summary>
        /// <param name="fromScene">The name of the source scene.</param>
        /// <param name="toScene">The name of the destination scene.</param>
        /// <returns>An <see cref="InteractionEventData"/> with event type "scene_transition".</returns>
        public static InteractionEventData SceneTransitionEvent(string fromScene, string toScene)
        {
            return new InteractionEventData
            {
                EventType = "scene_transition",
                Timestamp = UnityEngine.Time.realtimeSinceStartup,
                Parameters = new Dictionary<string, object>
                {
                    { "from_scene", fromScene },
                    { "to_scene", toScene }
                }
            };
        }
    }
}
