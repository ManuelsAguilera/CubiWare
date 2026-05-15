using System;
using System.Collections.Generic;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Data transfer object representing the results and metadata of a completed minigame session.
    /// </summary>
    public struct MinigameSessionData
    {
        /// <summary>
        /// The final score achieved in this session.
        /// </summary>
        public int Score;

        /// <summary>
        /// Total duration of the session in seconds.
        /// </summary>
        public float DurationSeconds;

        /// <summary>
        /// Whether the minigame was completed (as opposed to aborted/timed out).
        /// </summary>
        public bool Completed;

        /// <summary>
        /// When the session started.
        /// </summary>
        public DateTime StartTime;

        /// <summary>
        /// When the session ended.
        /// </summary>
        public DateTime EndTime;

        /// <summary>
        /// The name/identifier of the minigame.
        /// </summary>
        public string MinigameName;

        /// <summary>
        /// Optional custom statistics specific to the minigame type.
        /// </summary>
        public Dictionary<string, object> CustomStats;
    }
}
