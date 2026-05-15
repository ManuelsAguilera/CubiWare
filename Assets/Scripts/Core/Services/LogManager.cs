using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;
using UnityEngine;
 
namespace CubiWare.Core.Services
{
    /// <summary>
    /// Singleton implementation of <see cref="IInteractionLogger"/> that buffers
    /// interaction events in memory and persists them via <see cref="IDataStore"/>
    /// when <see cref="FlushAsync"/> is called.
    /// </summary>
    public sealed class LogManager : IInteractionLogger
    {
        private static readonly Lazy<LogManager> _instance =
            new Lazy<LogManager>(() => new LogManager(), true);

        private readonly List<InteractionEventData> _events;
        private readonly string _sessionId;
        private readonly ServiceLogger _logger = ServiceLogger.Instance;
        private IDataStore _dataStore;

        /// <summary>
        /// Gets the singleton instance of <see cref="LogManager"/>.
        /// </summary>
        public static LogManager Instance => _instance.Value;

        /// <summary>
        /// The number of events queued and not yet flushed.
        /// </summary>
        public int PendingEventCount => _events.Count;

        /// <summary>
        /// The session identifier for grouping related events.
        /// </summary>
        public string SessionId => _sessionId;

        /// <summary>
        /// Prevents external instantiation. Use <see cref="Instance"/> to access
        /// the singleton.
        /// </summary>
        private LogManager()
        {
            _events = new List<InteractionEventData>();
            _sessionId = Guid.NewGuid().ToString();
            _logger.LogInfo(nameof(LogManager), $"LogManager initialized with session: {_sessionId}");
        }

        /// <summary>
        /// Sets the data store used for persisting interaction events.
        /// Called during bootstrap initialization by GameManager.
        /// </summary>
        /// <param name="dataStore">The data store implementation to use.</param>
        public void SetDataStore(IDataStore dataStore)
        {
            _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
            _logger.LogInfo(nameof(LogManager), "Data store assigned.");
        }

        /// <summary>
        /// Logs an interaction event by adding it to the in-memory buffer and
        /// recording it via <see cref="ServiceLogger"/>.
        /// </summary>
        /// <param name="eventData">The event data to log.</param>
        public void LogEvent(InteractionEventData eventData)
        {
            // Ensure session ID is set
            var data = eventData;
            if (string.IsNullOrEmpty(data.SessionId))
            {
                data.SessionId = _sessionId;
            }

            lock (_events)
            {
                _events.Add(data);
            }

            _logger.LogInfo(nameof(LogManager),
                $"Event logged: {data.EventType} | Scene: {data.SceneName} | Session: {data.SessionId}");
        }

        /// <summary>
        /// Flushes all pending events to persistent storage via the assigned
        /// <see cref="IDataStore"/>. Events are serialized as a JSON array
        /// and saved under the "interaction_log" key. The pending event list
        /// is cleared after a successful save.
        /// </summary>
        public async Task FlushAsync()
        {
            if (_dataStore == null)
            {
                _logger.LogWarning(nameof(LogManager), "FlushAsync called but no data store is assigned. Events will not be persisted.");
                return;
            }

            List<InteractionEventData> batch;
            lock (_events)
            {
                if (_events.Count == 0) return;
                batch = new List<InteractionEventData>(_events);
            }

            try
            {
                // Serialize each event manually since JsonUtility can't handle Dictionary<string, object>
                string jsonBatch = SerializeEventBatch(batch);
                await _dataStore.SaveAsync("interaction_log", jsonBatch);

                lock (_events)
                {
                    // Remove only the events we just saved (in case new events were added concurrently)
                    _events.RemoveRange(0, Math.Min(batch.Count, _events.Count));
                }

                _logger.LogInfo(nameof(LogManager), $"Flushed {batch.Count} events to data store. {_events.Count} events remain.");
            }
            catch (Exception ex)
            {
                _logger.LogError(nameof(LogManager), $"Failed to flush events: {ex.Message}", ServiceErrorCode.DataStoreWriteFailed);
            }
        }

        /// <summary>
        /// Manually serializes a list of InteractionEventData to a JSON array string.
        /// Uses StringBuilder for efficiency and handles Dictionary<string, object> by
        /// converting to a serializable entry list.
        /// </summary>
        private static string SerializeEventBatch(List<InteractionEventData> events)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = events[i];
                sb.Append("{");
                sb.Append("\"EventType\":").Append(JsonEncode(e.EventType)).Append(",");
                sb.Append("\"SceneName\":").Append(JsonEncode(e.SceneName)).Append(",");
                sb.Append("\"Timestamp\":").Append(e.Timestamp).Append(",");
                sb.Append("\"SessionId\":").Append(JsonEncode(e.SessionId)).Append(",");
                sb.Append("\"Parameters\":{");
                if (e.Parameters != null)
                {
                    bool first = true;
                    foreach (var kvp in e.Parameters)
                    {
                        if (!first) sb.Append(",");
                        first = false;
                        sb.Append(JsonEncode(kvp.Key)).Append(":");
                        if (kvp.Value is string s)
                            sb.Append(JsonEncode(s));
                        else if (kvp.Value is float f)
                            sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        else if (kvp.Value is double d)
                            sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        else if (kvp.Value is int iv)
                            sb.Append(iv);
                        else if (kvp.Value is bool b)
                            sb.Append(b ? "true" : "false");
                        else if (kvp.Value == null)
                            sb.Append("null");
                        else
                            sb.Append(JsonEncode(kvp.Value.ToString()));
                    }
                }
                sb.Append("}");
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>JSON-encodes a string value with proper escaping.</summary>
        private static string JsonEncode(string value)
        {
            if (value == null) return "null";
            var sb = new System.Text.StringBuilder();
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Retrieves the most recent events up to the specified count.
        /// </summary>
        /// <param name="count">The maximum number of events to return.</param>
        /// <returns>A read-only list of recent interaction events.</returns>
        public IReadOnlyList<InteractionEventData> GetRecentEvents(int count)
        {
            lock (_events)
            {
                int takeCount = Math.Min(count, _events.Count);
                int startIndex = _events.Count - takeCount;
                return _events.GetRange(startIndex, takeCount).AsReadOnly();
            }
        }
    }
}
