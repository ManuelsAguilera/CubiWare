using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Interface for LLM (Large Language Model) query services, decoupling consumers from LLMConnector.
    /// Supports both single-response and streaming queries.
    /// </summary>
    public interface ILLMService
    {
        /// <summary>
        /// Sends a prompt and returns the full text response.
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM.</param>
        /// <param name="ct">Cancellation token to cancel the request.</param>
        /// <returns>The response text from the LLM.</returns>
        Task<string> QueryAsync(string prompt, CancellationToken ct = default);

        /// <summary>
        /// Sends a prompt and streams the response tokens as they arrive.
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM.</param>
        /// <param name="ct">Cancellation token to cancel the stream.</param>
        /// <returns>An async enumerable of response text chunks.</returns>
        IAsyncEnumerable<string> QueryStreamAsync(string prompt, CancellationToken ct = default);

        /// <summary>
        /// Whether the service is initialized and ready to accept queries.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Average response time in milliseconds for recent queries.
        /// </summary>
        float AverageResponseTimeMs { get; }

        /// <summary>
        /// Fired when a complete response is received from a non-streaming query.
        /// </summary>
        event Action<string> OnResponseReceived;

        /// <summary>
        /// Fired when an error occurs during query processing.
        /// </summary>
        event Action<string> OnError;
    }
}
