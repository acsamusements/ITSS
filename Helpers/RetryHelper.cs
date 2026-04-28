using Microsoft.Extensions.Logging;

namespace ITSS.Helpers;

/// <summary>
/// Lightweight retry helper for transient failures.
/// Supports sync and async operations with configurable attempt counts,
/// delay, exponential back-off, and per-exception filtering.
/// </summary>
public static class RetryHelper
{
    /// <summary>
    /// Executes <paramref name="action"/> up to <paramref name="maxAttempts"/> times,
    /// waiting <paramref name="delayMs"/> milliseconds between attempts.
    /// </summary>
    /// <param name="action">The operation to attempt.</param>
    /// <param name="maxAttempts">Total number of attempts (including the first). Default: 3.</param>
    /// <param name="delayMs">Milliseconds to wait between attempts. Default: 200.</param>
    /// <param name="exponentialBackoff">When <c>true</c>, delay doubles on each retry.</param>
    /// <param name="retryOn">Optional predicate to determine which exceptions should trigger a retry. When <c>null</c>, all exceptions are retried.</param>
    /// <param name="logger">Optional logger for retry warnings.</param>
    /// <exception cref="Exception">The exception from the final failed attempt.</exception>
    public static void Retry(
        Action action,
        int maxAttempts       = 3,
        int delayMs           = 200,
        bool exponentialBackoff = false,
        Func<Exception, bool>? retryOn = null,
        ILogger? logger       = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        RetryCore<object?>(
            () => { action(); return null; },
            maxAttempts, delayMs, exponentialBackoff, retryOn, logger);
    }

    /// <summary>
    /// Executes <paramref name="func"/> up to <paramref name="maxAttempts"/> times,
    /// returning its result on success.
    /// </summary>
    public static T Retry<T>(
        Func<T> func,
        int maxAttempts         = 3,
        int delayMs             = 200,
        bool exponentialBackoff = false,
        Func<Exception, bool>? retryOn = null,
        ILogger? logger         = null)
    {
        ArgumentNullException.ThrowIfNull(func);
        return RetryCore(func, maxAttempts, delayMs, exponentialBackoff, retryOn, logger);
    }

    /// <summary>
    /// Executes <paramref name="action"/> asynchronously up to <paramref name="maxAttempts"/> times.
    /// </summary>
    public static Task RetryAsync(
        Func<Task> action,
        int maxAttempts         = 3,
        int delayMs             = 200,
        bool exponentialBackoff = false,
        Func<Exception, bool>? retryOn = null,
        ILogger? logger         = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RetryCoreAsync<object?>(
            async () => { await action().ConfigureAwait(false); return null; },
            maxAttempts, delayMs, exponentialBackoff, retryOn, logger, cancellationToken);
    }

    /// <summary>
    /// Executes <paramref name="func"/> asynchronously up to <paramref name="maxAttempts"/> times,
    /// returning its result on success.
    /// </summary>
    public static Task<T> RetryAsync<T>(
        Func<Task<T>> func,
        int maxAttempts         = 3,
        int delayMs             = 200,
        bool exponentialBackoff = false,
        Func<Exception, bool>? retryOn = null,
        ILogger? logger         = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return RetryCoreAsync(func, maxAttempts, delayMs, exponentialBackoff, retryOn, logger, cancellationToken);
    }

    // ── Core implementations ──────────────────────────────────────────────────

    private static T RetryCore<T>(
        Func<T> func,
        int maxAttempts,
        int delayMs,
        bool exponentialBackoff,
        Func<Exception, bool>? retryOn,
        ILogger? logger)
    {
        int currentDelay = delayMs;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return func();
            }
            catch (Exception ex) when (attempt < maxAttempts && ShouldRetry(ex, retryOn))
            {
                logger?.LogWarning(ex, "Attempt {Attempt}/{Max} failed. Retrying in {Delay}ms.", attempt, maxAttempts, currentDelay);
                Thread.Sleep(currentDelay);
                if (exponentialBackoff) currentDelay *= 2;
            }
        }
        return func(); // final attempt — let exception propagate
    }

    private static async Task<T> RetryCoreAsync<T>(
        Func<Task<T>> func,
        int maxAttempts,
        int delayMs,
        bool exponentialBackoff,
        Func<Exception, bool>? retryOn,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        int currentDelay = delayMs;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await func().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && ShouldRetry(ex, retryOn)
                                       && !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "Attempt {Attempt}/{Max} failed. Retrying in {Delay}ms.", attempt, maxAttempts, currentDelay);
                await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);
                if (exponentialBackoff) currentDelay *= 2;
            }
        }
        return await func().ConfigureAwait(false);
    }

    private static bool ShouldRetry(Exception ex, Func<Exception, bool>? retryOn)
        => retryOn is null || retryOn(ex);
}
