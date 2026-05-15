namespace CubiWare.Core.Logging
{
    /// <summary>
    /// Defines the severity levels for log messages. Levels are ordered from
    /// most verbose (Trace) to most critical (Fatal). Use the <see cref="LogLevelExtensions.IsAbove"/>
    /// extension method to filter messages by threshold.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Fine-grained diagnostic information, useful for debugging specific
        /// code paths. Typically disabled in production builds.
        /// </summary>
        Trace = 0,

        /// <summary>
        /// General diagnostic information useful for understanding application
        /// flow and state during development.
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Informational messages tracking normal application operations,
        /// such as service start/stop events or user actions.
        /// </summary>
        Info = 2,

        /// <summary>
        /// Potentially harmful situations that do not prevent the application
        /// from continuing but may indicate an underlying issue.
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Error events that cause a specific operation to fail but do not
        /// crash the entire application.
        /// </summary>
        Error = 4,

        /// <summary>
        /// Severe errors that cause the application to terminate or become
        /// unusable. Requires immediate investigation.
        /// </summary>
        Fatal = 5
    }

    /// <summary>
    /// Extension methods for the <see cref="LogLevel"/> enum.
    /// </summary>
    public static class LogLevelExtensions
    {
        /// <summary>
        /// Determines whether this log level is at or above the specified threshold.
        /// Use this to filter log messages against the configured minimum level.
        /// </summary>
        /// <param name="level">The current log level to evaluate.</param>
        /// <param name="threshold">The minimum threshold to compare against.</param>
        /// <returns>
        /// <c>true</c> if <paramref name="level"/> is greater than or equal to
        /// <paramref name="threshold"/>; otherwise <c>false</c>.
        /// </returns>
        /// <example>
        /// <code>
        /// LogLevel level = LogLevel.Warning;
        /// bool shouldLog = level.IsAbove(LogLevel.Info); // true
        /// </code>
        /// </example>
        public static bool IsAbove(this LogLevel level, LogLevel threshold)
        {
            return level >= threshold;
        }
    }
}
