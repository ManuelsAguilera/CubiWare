using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CubiWare.Core.Logging
{
    /// <summary>
    /// Encapsulates all contextual data for a single log event. Provides a structured
    /// way to capture service name, method, severity, message, and optional metadata
    /// such as error codes and correlation IDs.
    /// </summary>
    public struct LogContext
    {
        /// <summary>
        /// The name of the service that produced this log entry.
        /// Example: "CameraFeedProvider", "HandDetectorService".
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// The name of the method that produced this log entry.
        /// Automatically populated via <see cref="CallerMemberNameAttribute"/> when
        /// using convenience methods on <see cref="ServiceLogger"/>.
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// An optional error code set when the log entry represents an error condition.
        /// <c>null</c> for non-error log levels.
        /// </summary>
        public ServiceErrorCode? ErrorCode { get; }

        /// <summary>
        /// The severity level of this log entry.
        /// </summary>
        public LogLevel Level { get; }

        /// <summary>
        /// A human-readable message describing the log event.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// An optional correlation identifier for tracing a request or operation
        /// across multiple services. Useful for debugging distributed workflows.
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// The timestamp from <see cref="Time.realtimeSinceStartup"/> captured when
        /// this log entry was created. Provides high-resolution timing relative to
        /// application start.
        /// </summary>
        public float Timestamp { get; }

        /// <summary>
        /// An optional dictionary of additional key-value pairs for attaching
        /// extra contextual data to this log entry.
        /// </summary>
        public Dictionary<string, string> AdditionalData { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="LogContext"/> with the minimum
        /// required fields. Use the static factory methods <see cref="CreateInfo"/>
        /// and <see cref="CreateError"/> for common log patterns.
        /// </summary>
        /// <param name="serviceName">The name of the service producing the log.</param>
        /// <param name="level">The severity level of this log entry.</param>
        /// <param name="message">A human-readable message describing the event.</param>
        /// <param name="methodName">The calling method name. Automatically populated.</param>
        /// <param name="errorCode">An optional error code for error-level entries.</param>
        public LogContext(
            string serviceName,
            LogLevel level,
            string message,
            [CallerMemberName] string methodName = "",
            ServiceErrorCode? errorCode = null)
        {
            ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
            Level = level;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            MethodName = methodName;
            ErrorCode = errorCode;
            CorrelationId = null;
            Timestamp = Time.realtimeSinceStartup;
            AdditionalData = null;
        }

        /// <summary>
        /// Creates an informational <see cref="LogContext"/> with <see cref="LogLevel.Info"/>.
        /// </summary>
        /// <param name="service">The name of the service producing the log.</param>
        /// <param name="message">A human-readable message describing the event.</param>
        /// <returns>A new <see cref="LogContext"/> instance at Info level.</returns>
        public static LogContext CreateInfo(string service, string message)
        {
            return new LogContext(service, LogLevel.Info, message);
        }

        /// <summary>
        /// Creates an error <see cref="LogContext"/> with <see cref="LogLevel.Error"/>
        /// and the specified error code.
        /// </summary>
        /// <param name="service">The name of the service producing the log.</param>
        /// <param name="message">A human-readable message describing the error.</param>
        /// <param name="errorCode">The specific error code for this error condition.</param>
        /// <returns>A new <see cref="LogContext"/> instance at Error level.</returns>
        public static LogContext CreateError(string service, string message, ServiceErrorCode errorCode)
        {
            return new LogContext(service, LogLevel.Error, message, errorCode: errorCode);
        }

        /// <summary>
        /// Returns a formatted string representation of this log context in the format:
        /// <c>[Timestamp] [Level] [ServiceName.MethodName] Message</c>
        /// </summary>
        /// <returns>A formatted log line string.</returns>
        public override string ToString()
        {
            return $"[{Timestamp:F2}] [{Level}] [{ServiceName}.{MethodName}] {Message}";
        }
    }
}
