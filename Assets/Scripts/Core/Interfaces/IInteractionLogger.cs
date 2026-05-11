using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Represents a single interaction event for logging and analytics.
    /// </summary>
    public struct InteractionEventData
    {
        /// <summary>
        /// The type/category of the event (e.g., "gesture", "button_click", "voice_command").
        /// </summary>
        public string EventType;

        /// <summary>
        /// The scene name where the event occurred.
        /// </summary>
        public string SceneName;

        /// <summary>
        /// The time at which the event occurred (Unix timestamp or game time).
        /// </summary>
        public float Timestamp;

        /// <summary>
        /// Optional key-value parameters providing additional event context.
        /// </summary>
        public Dictionary<string, object> Parameters;

        /// <summary>
        /// The session identifier for grouping related events.
        /// </summary>
        public string SessionId;
    }

    /// <summary>
    /// Interface for logging user interaction events.
    /// Decouples analytics/logging consumers from any specific backend implementation.
    /// </summary>
    public interface IInteractionLogger
    {
        /// <summary>
        /// Logs an interaction event for later processing or upload.
        /// </summary>
        /// <param name="eventData">The event data to log.</param>
        void LogEvent(InteractionEventData eventData);

        /// <summary>
        /// Flushes all pending events to persistent storage or network.
        /// </summary>
        Task FlushAsync();

        /// <summary>
        /// Retrieves the most recent events.
        /// </summary>
        /// <param name="count">The maximum number of events to return.</param>
        /// <returns>A read-only list of recent interaction events.</returns>
        IReadOnlyList<InteractionEventData> GetRecentEvents(int count);

        /// <summary>
        /// The number of events queued and not yet flushed.
        /// </summary>
        int PendingEventCount { get; }
    }
}
