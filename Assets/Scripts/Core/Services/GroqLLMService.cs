using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace CubiWare.Core.Services
{
    /// <summary>
    /// Service implementation of <see cref="ILLMService"/> that extends
    /// <see cref="AbstractMediaPipeLLM"/> for Groq API-based LLM queries.
    /// Currently uses placeholder implementations for initialization and streaming.
    /// </summary>
    public class GroqLLMService : AbstractMediaPipeLLM, ILLMService
    {
        private string _apiKey;
        private float _averageResponseTimeMs;

        /// <summary>
        /// Gets the human-readable service name for logging.
        /// </summary>
        public override string ServiceName => "GroqLLMService";

        /// <summary>
        /// Whether the service is initialized and ready to accept queries.
        /// </summary>
        public bool IsReady => IsInitialized;

        /// <summary>
        /// Average response time in milliseconds for recent queries.
        /// </summary>
        public float AverageResponseTimeMs => _averageResponseTimeMs;

        /// <summary>
        /// Fired when a complete response is received from a non-streaming query.
        /// </summary>
        public event Action<string> OnResponseReceived;

        /// <summary>
        /// Fired when an error occurs during query processing.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Placeholder initialization. In a real implementation, this would load
        /// the API key from configuration and validate connectivity.
        /// </summary>
        protected override Task<bool> OnInitializeAsync()
        {
            LogInfo("GroqLLMService initializing (placeholder).");
            // TODO: Load API key from Resources/GroqConfig or similar
            _apiKey = string.Empty;
            _averageResponseTimeMs = 0f;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Placeholder shutdown.
        /// </summary>
        protected override Task OnShutdownAsync()
        {
            LogInfo("GroqLLMService shutting down (placeholder).");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends a prompt and returns a mock response. Real implementation would
        /// call the Groq API endpoint.
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM.</param>
        /// <param name="ct">Cancellation token to cancel the request.</param>
        /// <returns>A mock response string.</returns>
        public async Task<string> QueryAsync(string prompt, CancellationToken ct = default)
        {
            if (!IsReady)
            {
                var errorMsg = "GroqLLMService is not initialized.";
                LogError(errorMsg, ServiceErrorCode.NotInitialized);
                OnError?.Invoke(errorMsg);
                return string.Empty;
            }

            try
            {
                LogInfo($"QueryAsync called with prompt: {prompt}");

                var startTime = TimeSpan.FromTicks(DateTime.UtcNow.Ticks).TotalMilliseconds;

                // Placeholder: simulate network latency
                await Task.Delay(100, ct);

                string mockResponse = "Mock response for: " + prompt;

                var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks).TotalMilliseconds - startTime;
                _averageResponseTimeMs = (float)elapsed;

                OnResponseReceived?.Invoke(mockResponse);
                LogInfo($"QueryAsync completed in {_averageResponseTimeMs:F0}ms.");

                return mockResponse;
            }
            catch (OperationCanceledException)
            {
                LogWarning("QueryAsync was cancelled.");
                OnError?.Invoke("Query cancelled.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                LogError($"QueryAsync failed: {ex.Message}", ServiceErrorCode.LLMConnectionFailed);
                OnError?.Invoke(ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Sends a prompt and streams mock response tokens. Real implementation
        /// would use server-sent events (SSE) from the Groq API.
        /// </summary>
        /// <param name="prompt">The prompt to send to the LLM.</param>
        /// <param name="ct">Cancellation token to cancel the stream.</param>
        /// <returns>An async enumerable of mock response text chunks.</returns>
        public async IAsyncEnumerable<string> QueryStreamAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!IsReady)
            {
                var errorMsg = "GroqLLMService is not initialized.";
                LogError(errorMsg, ServiceErrorCode.NotInitialized);
                OnError?.Invoke(errorMsg);
                yield break;
            }

            LogInfo($"QueryStreamAsync called with prompt: {prompt}");

            string mockResponse = "Mock response for: " + prompt;
            string[] words = mockResponse.Split(' ');

            await foreach (var token in EnumerateTokens(words, ct))
            {
                yield return token;
            }

            LogInfo("QueryStreamAsync completed.");
        }

        /// <summary>
        /// Helper enumerator that yields tokens without a try-catch block,
        /// avoiding CS1626 (cannot yield in try block with catch clauses).
        /// </summary>
        private async IAsyncEnumerable<string> EnumerateTokens(string[] words, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (string word in words)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return word + " ";
            }
        }
    }
}
