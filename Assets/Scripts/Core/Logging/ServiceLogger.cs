using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CubiWare.Core.Logging
{
    /// <summary>
    /// Singleton service for structured logging across the CubiWare application.
    /// Provides convenience methods for common log levels, an event for external
    /// consumers (UI, file sinks), and a circular buffer of recent log entries.
    /// </summary>
    public sealed class ServiceLogger
    {
        private const int MaxRecentLogs = 100;

        private readonly List<LogContext> _recentLogs;
        private readonly object _lock = new object();

        private static readonly Lazy<ServiceLogger> _instance =
            new Lazy<ServiceLogger>(() => new ServiceLogger(), true);

        /// <summary>
        /// Gets the singleton instance of <see cref="ServiceLogger"/>.
        /// The instance is lazily initialized and thread-safe.
        /// </summary>
        public static ServiceLogger Instance => _instance.Value;

        /// <summary>
        /// Gets or sets the minimum log level that will be processed.
        /// Messages below this threshold are silently discarded.
        /// Defaults to <see cref="LogLevel.Info"/>.
        /// </summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        /// <summary>
        /// Occurs when a log entry is emitted. Subscribe to this event to route logs
        /// to UI displays, file writers, or external sinks.
        /// </summary>
        public event Action<LogContext> OnLogEmitted;

        /// <summary>
        /// Prevents external instantiation. Use <see cref="Instance"/> to access
        /// the singleton.
        /// </summary>
        private ServiceLogger()
        {
            _recentLogs = new List<LogContext>(MaxRecentLogs);
        }

        /// <summary>
        /// Logs a message with the specified context. If the context's level is below
        /// <see cref="MinimumLevel"/>, the message is discarded. Otherwise, it is
        /// added to the circular buffer and the <see cref="OnLogEmitted"/> event is fired.
        /// </summary>
        /// <param name="context">The fully populated log context.</param>
        public void Log(LogContext context)
        {
            if (context.Level < MinimumLevel)
                return;

            // Filter out specific verbose shutdown logs
            if (context.Message.Contains("Shutdown completed successfully"))
                return;

            lock (_lock)
            {
                _recentLogs.Add(context);

                // Maintain circular buffer of MaxRecentLogs
                if (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.RemoveRange(0, _recentLogs.Count - MaxRecentLogs);
                }
            }

            OnLogEmitted?.Invoke(context);

            // Write to Unity's console as a fallback
            switch (context.Level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log(context.ToString());
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(context.ToString());
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    UnityEngine.Debug.LogError(context.ToString());
                    break;
            }
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="service">The name of the service producing the log.</param>
        /// <param name="message">A human-readable message describing the event.</param>
        /// <param name="methodName">The calling method name. Automatically populated.</param>
        public void LogInfo(string service, string message, [CallerMemberName] string methodName = "")
        {
            var context = new LogContext(service, LogLevel.Info, message, methodName);
            Log(context);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="service">The name of the service producing the log.</param>
        /// <param name="message">A human-readable message describing the warning.</param>
        /// <param name="methodName">The calling method name. Automatically populated.</param>
        public void LogWarning(string service, string message, [CallerMemberName] string methodName = "")
        {
            var context = new LogContext(service, LogLevel.Warning, message, methodName);
            Log(context);
        }

        /// <summary>
        /// Logs an error message with the specified error code.
        /// </summary>
        /// <param name="service">The name of the service producing the log.</param>
        /// <param name="message">A human-readable message describing the error.</param>
        /// <param name="errorCode">The specific error code for this error condition.</param>
        /// <param name="methodName">The calling method name. Automatically populated.</param>
        public void LogError(string service, string message, ServiceErrorCode errorCode, [CallerMemberName] string methodName = "")
        {
            var context = new LogContext(service, LogLevel.Error, message, methodName, errorCode);
            Log(context);
        }

        /// <summary>
        /// Retrieves a read-only view of the most recent log entries, up to the
        /// specified count. Returns logs in chronological order.
        /// </summary>
        /// <param name="count">The maximum number of recent log entries to return.</param>
        /// <returns>A read-only list of recent log contexts.</returns>
        public IReadOnlyList<LogContext> GetRecentLogs(int count)
        {
            lock (_lock)
            {
                int startIndex = Math.Max(0, _recentLogs.Count - count);
                int takeCount = Math.Min(count, _recentLogs.Count - startIndex);
                return _recentLogs.GetRange(startIndex, takeCount).AsReadOnly();
            }
        }

        /// <summary>
        /// Flushes pending log entries. Currently a placeholder for future
        /// file/network sink integration. In the current implementation, logs
        /// are written synchronously so no explicit flush is needed.
        /// </summary>
        public void Flush()
        {
            // Placeholder: future implementation may buffer log entries
            // before writing to file or network sinks.
        }
    }
}
