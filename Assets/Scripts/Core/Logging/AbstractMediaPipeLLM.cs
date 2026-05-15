using System;
using System.Threading;
using System.Threading.Tasks;
using CubiWare.Core.Interfaces;

namespace CubiWare.Core.Logging
{
    /// <summary>
    /// Abstract base class for MediaPipe and LLM services. Provides a common
    /// lifecycle pattern (initialize, shutdown, reset), integrated logging via
    /// <see cref="ServiceLogger"/>, service-level cancellation, and configurable
    /// retry logic with exponential backoff.
    /// </summary>
    /// <remarks>
    /// Derived classes should override <see cref="OnInitializeAsync"/> and
    /// <see cref="OnShutdownAsync"/> to provide service-specific logic. The public
    /// <see cref="InitializeAsync"/> and <see cref="ShutdownAsync"/> methods
    /// handle logging, retries, and state tracking automatically.
    /// </remarks>
    public abstract class AbstractMediaPipeLLM
    {
        private CancellationTokenSource _cts;

        /// <summary>
        /// Gets the <see cref="ServiceLogger"/> instance used for all logging
        /// within this service and its derived classes.
        /// </summary>
        protected ServiceLogger Logger => ServiceLogger.Instance;

        /// <summary>
        /// Gets a cancellation token linked to this service's lifecycle.
        /// The token is cancelled when <see cref="Reset"/> is called or when
        /// the service is shut down. Use this token for cooperative cancellation
        /// in long-running or async operations.
        /// </summary>
        protected CancellationToken ServiceToken => _cts.Token;

        /// <summary>
        /// Gets or sets whether this service has been successfully initialized.
        /// Set to <c>true</c> at the end of a successful <see cref="OnInitializeAsync"/>
        /// call; set to <c>false</c> during shutdown.
        /// </summary>
        public bool IsInitialized { get; protected set; }

        /// <summary>
        /// Gets the human-readable name of this service. Used in log messages
        /// and error contexts. Must be implemented by derived classes.
        /// </summary>
        public abstract string ServiceName { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="AbstractMediaPipeLLM"/>.
        /// Creates the initial <see cref="CancellationTokenSource"/> for
        /// service-level cancellation.
        /// </summary>
        protected AbstractMediaPipeLLM()
        {
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Initializes the service by calling <see cref="OnInitializeAsync"/>
        /// with retry logic. Logs the start, success, or failure of initialization.
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous initialization operation.
        /// The result is <c>true</c> if initialization succeeded; <c>false</c> otherwise.
        /// </returns>
        public async Task<bool> InitializeAsync()
        {
            LogInfo("Initialization started.");

            try
            {
                bool success = await RetryAsync(
                    async () =>
                    {
                        if (_cts.Token.IsCancellationRequested)
                        {
                            LogWarning("Initialization cancelled before starting.");
                            return false;
                        }

                        return await OnInitializeAsync();
                    },
                    maxRetries: 3,
                    baseDelayMs: 100
                );

                if (success)
                {
                    IsInitialized = true;
                    LogInfo("Initialization completed successfully.");
                }
                else
                {
                    LogError("Initialization failed after all retries.", ServiceErrorCode.NotInitialized);
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                LogWarning("Initialization was cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Initialization threw an unexpected exception: {ex.Message}", ServiceErrorCode.UnknownError);
                return false;
            }
        }

        /// <summary>
        /// Shuts down the service by calling <see cref="OnShutdownAsync"/>.
        /// Resets the cancellation token source after shutdown completes.
        /// </summary>
        /// <returns>A task that represents the asynchronous shutdown operation.</returns>
        public async Task ShutdownAsync()
        {
            if (!IsInitialized)
            {
                LogWarning("Shutdown requested but service was not initialized.");
                return;
            }

            LogInfo("Shutdown started.");

            try
            {
                await OnShutdownAsync();
                IsInitialized = false;
                LogInfo("Shutdown completed successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Shutdown threw an unexpected exception: {ex.Message}", ServiceErrorCode.UnknownError);
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>
        /// Resets the service by cancelling the current cancellation token source
        /// and creating a new one. This allows the service to be re-initialized
        /// after a shutdown or failure.
        /// </summary>
        public void Reset()
        {
            var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
            oldCts?.Cancel();
            oldCts?.Dispose();
        }

        /// <summary>
        /// Executes the specified asynchronous operation with retry logic using
        /// exponential backoff. The operation is retried up to <paramref name="maxRetries"/>
        /// times with a delay that doubles each attempt.
        /// </summary>
        /// <typeparam name="T">The return type of the operation.</typeparam>
        /// <param name="operation">The asynchronous operation to retry.</param>
        /// <param name="maxRetries">The maximum number of retry attempts. Defaults to 3.</param>
        /// <param name="baseDelayMs">The base delay in milliseconds for the first retry. Each subsequent retry doubles this delay. Defaults to 100.</param>
        /// <returns>A task that represents the asynchronous retry operation, containing the result.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="operation"/> is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the operation is cancelled via <see cref="ServiceToken"/>.</exception>
        protected async Task<T> RetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3, float baseDelayMs = 100)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            int attempt = 0;

            while (true)
            {
                _cts.Token.ThrowIfCancellationRequested();

                try
                {
                    return await operation();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempt++;

                    if (attempt > maxRetries)
                    {
                        LogError($"Operation failed after {maxRetries} retries. Last error: {ex.Message}", ServiceErrorCode.UnknownError);
                        throw;
                    }

                    float delayMs = baseDelayMs * (float)Math.Pow(2, attempt - 1);
                    LogWarning($"Operation failed (attempt {attempt}/{maxRetries}). Retrying in {delayMs}ms. Error: {ex.Message}");

                    await Task.Delay((int)delayMs, _cts.Token);
                }
            }
        }

        /// <summary>
        /// Service-specific initialization logic. Override this method to implement
        /// the actual initialization for your derived service.
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous initialization operation.
        /// The result is <c>true</c> if initialization succeeded; <c>false</c> otherwise.
        /// </returns>
        protected virtual Task<bool> OnInitializeAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Service-specific shutdown logic. Override this method to implement
        /// the actual shutdown for your derived service, such as releasing
        /// unmanaged resources or cancelling pending operations.
        /// </summary>
        /// <returns>A task that represents the asynchronous shutdown operation.</returns>
        protected virtual Task OnShutdownAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Logs a debug-level message for this service.
        /// </summary>
        /// <param name="message">The debug message to log.</param>
        protected void LogDebug(string message)
        {
            Logger.LogInfo(ServiceName, $"[DEBUG] {message}");
        }

        /// <summary>
        /// Logs an informational message for this service.
        /// </summary>
        /// <param name="message">The informational message to log.</param>
        protected void LogInfo(string message)
        {
            Logger.LogInfo(ServiceName, message);
        }

        /// <summary>
        /// Logs a warning message for this service.
        /// </summary>
        /// <param name="message">The warning message to log.</param>
        protected void LogWarning(string message)
        {
            Logger.LogWarning(ServiceName, message);
        }

        /// <summary>
        /// Logs an error message with the specified error code for this service.
        /// </summary>
        /// <param name="message">The error message to log.</param>
        /// <param name="errorCode">The specific error code describing the error condition.</param>
        protected void LogError(string message, ServiceErrorCode errorCode)
        {
            Logger.LogError(ServiceName, message, errorCode);
        }
    }
}
