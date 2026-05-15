using System;
using System.Collections.Generic;
using CubiWare.Core.Interfaces;

namespace CubiWare.Core.Data
{
    /// <summary>
    /// Aggregate user data class used for JSON serialization via IDataStore.
    /// Stores persistent data across all minigame sessions, including score history,
    /// high scores, preferences, and last-played date.
    /// </summary>
    [System.Serializable]
    public class UserData
    {
        /// <summary>
        /// The most recent score achieved across all minigames.
        /// </summary>
        public int LastScore;

        /// <summary>
        /// The highest score ever achieved.
        /// </summary>
        public int HighScore;

        /// <summary>
        /// History of past minigame sessions for analytics and replay tracking.
        /// </summary>
        public List<MinigameSessionData> SessionHistory = new List<MinigameSessionData>();

        /// <summary>
        /// The date and time the user last played any minigame.
        /// </summary>
        public DateTime LastPlayedDate;

        /// <summary>
        /// User preferences dictionary for future customization options.
        /// </summary>
        public Dictionary<string, object> Preferences = new Dictionary<string, object>();
    }
}
