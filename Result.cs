namespace ITSS;

/// <summary>
/// Represents the outcome of an operation that can either succeed with a value
/// or fail with an error message and optional exception.
/// <para>
/// Use <see cref="Result{T}.Ok"/> to create a success result and
/// <see cref="Result{T}.Fail"/> to create a failure result.
/// Pairs naturally with <c>SqlHelper</c>, <c>HttpHelper</c>, and other helpers
/// that may produce or consume typed results.
/// </para>
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        _value    = value;
        Error     = error;
        Exception = exception;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Returns <c>true</c> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The success value.
    /// <exception cref="InvalidOperationException">Thrown when accessed on a failure result.</exception>
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access {nameof(Value)} on a failed result. Error: {Error}");

    /// <summary>The error message when <see cref="IsFailure"/> is <c>true</c>; otherwise <c>null</c>.</summary>
    public string? Error { get; }

    /// <summary>The underlying exception when <see cref="IsFailure"/> is <c>true</c>; otherwise <c>null</c>.</summary>
    public Exception? Exception { get; }

    // ── Factory Methods ───────────────────────────────────────────────────────

    /// <summary>Creates a successful result with the given <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new(true, value, null, null);

    /// <summary>Creates a failure result with the given <paramref name="error"/> message.</summary>
    public static Result<T> Fail(string error) => new(false, default, error, null);

    /// <summary>Creates a failure result from an <paramref name="exception"/>.</summary>
    public static Result<T> Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(false, default, exception.Message, exception);
    }

    /// <summary>Creates a failure result with both a message and the originating <paramref name="exception"/>.</summary>
    public static Result<T> Fail(string error, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(false, default, error, exception);
    }

    // ── Functional Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns <paramref name="fallback"/> when this result is a failure.
    /// </summary>
    public T ValueOrDefault(T fallback = default!) => IsSuccess ? _value! : fallback;

    /// <summary>
    /// Executes <paramref name="onSuccess"/> when the result is successful.
    /// </summary>
    public Result<T> OnSuccess(Action<T> onSuccess)
    {
        if (IsSuccess) onSuccess(_value!);
        return this;
    }

    /// <summary>
    /// Executes <paramref name="onFailure"/> when the result is a failure.
    /// </summary>
    public Result<T> OnFailure(Action<string?> onFailure)
    {
        if (IsFailure) onFailure(Error);
        return this;
    }

    /// <summary>
    /// Projects the success value to a new type using <paramref name="map"/>.
    /// Propagates failures without calling <paramref name="map"/>.
    /// </summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Ok(map(_value!)) : Result<TOut>.Fail(Error!, Exception!);
    }

    /// <summary>
    /// Chains a second operation that itself returns a <see cref="Result{TOut}"/>.
    /// Short-circuits on failure.
    /// </summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return IsSuccess ? bind(_value!) : Result<TOut>.Fail(Error!, Exception!);
    }

    /// <summary>
    /// Wraps <paramref name="func"/> in a try/catch and returns <see cref="Ok"/> or <see cref="Fail"/>.
    /// </summary>
    public static Result<T> Try(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        try { return Ok(func()); }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>
    /// Wraps an async <paramref name="func"/> in a try/catch and returns <see cref="Ok"/> or <see cref="Fail"/>.
    /// </summary>
    public static async Task<Result<T>> TryAsync(Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        try { return Ok(await func().ConfigureAwait(false)); }
        catch (Exception ex) { return Fail(ex); }
    }

    // ── Implicit Conversions ──────────────────────────────────────────────────

    /// <summary>Implicitly wraps a value in a successful <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    // ── Deconstruct ───────────────────────────────────────────────────────────

    /// <summary>Enables tuple deconstruction: <c>var (ok, value, error) = result;</c></summary>
    public void Deconstruct(out bool isSuccess, out T? value, out string? error)
    {
        isSuccess = IsSuccess;
        value     = _value;
        error     = Error;
    }

    public override string ToString() =>
        IsSuccess ? $"Ok({_value})" : $"Fail({Error})";
}

/// <summary>
/// Non-generic <see cref="Result"/> for operations that have no return value.
/// </summary>
public sealed class Result
{
    private Result(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Error     = error;
        Exception = exception;
    }

    /// <summary>Returns <c>true</c> when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Returns <c>true</c> when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The error message when <see cref="IsFailure"/> is <c>true</c>; otherwise <c>null</c>.</summary>
    public string? Error { get; }

    /// <summary>The underlying exception when <see cref="IsFailure"/> is <c>true</c>; otherwise <c>null</c>.</summary>
    public Exception? Exception { get; }

    /// <summary>A cached successful result (no allocation on success).</summary>
    public static readonly Result Success = new(true, null, null);

    /// <summary>Creates a failure result with the given <paramref name="error"/> message.</summary>
    public static Result Fail(string error) => new(false, error, null);

    /// <summary>Creates a failure result from an <paramref name="exception"/>.</summary>
    public static Result Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(false, exception.Message, exception);
    }

    /// <summary>Creates a failure result with both a message and the originating exception.</summary>
    public static Result Fail(string error, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(false, error, exception);
    }

    /// <summary>
    /// Wraps <paramref name="action"/> in a try/catch and returns <see cref="Success"/> or <see cref="Fail"/>.
    /// </summary>
    public static Result Try(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try { action(); return Success; }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>
    /// Wraps an async <paramref name="action"/> in a try/catch.
    /// </summary>
    public static async Task<Result> TryAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try { await action().ConfigureAwait(false); return Success; }
        catch (Exception ex) { return Fail(ex); }
    }

    public void Deconstruct(out bool isSuccess, out string? error)
    {
        isSuccess = IsSuccess;
        error     = Error;
    }

    public override string ToString() => IsSuccess ? "Ok" : $"Fail({Error})";
}
