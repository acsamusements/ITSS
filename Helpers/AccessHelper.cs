using Microsoft.Extensions.Logging;

using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace ITSS.Helpers;

/// <summary>
/// Configuration options for <see cref="AccessHelper"/>.
/// Pass an action to <see cref="AccessHelper.Initialize"/> to set application-wide defaults.
/// All settings can be overridden on a per-call basis.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AccessHelperOptions
{
    /// <summary>
    /// Default OleDb connection string used when none is supplied per-call.
    /// <para>Example: <c>Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\path\to\db.accdb;</c></para>
    /// </summary>
    public string? DefaultConnectionString { get; set; }

    /// <summary>Optional logger. When <c>null</c>, all logging is silently skipped.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Default OleDb command timeout in seconds. Default: 30.</summary>
    public int DefaultCommandTimeout { get; set; } = 30;

    /// <summary>
    /// When <c>true</c>, all exceptions are re-thrown after logging.
    /// When <c>false</c> (default), exceptions are swallowed and a safe default value is returned.
    /// Individual methods accept a per-call <c>throwOnError</c> override.
    /// </summary>
    public bool ThrowOnError { get; set; } = false;
}

/// <summary>
/// A reusable static Microsoft Access (OleDb) helper for any C# project targeting .NET 8+.
/// <para>
/// Call <see cref="Initialize"/> once at application startup to configure defaults.
/// Every method accepts optional per-call <c>connectionString</c>, <c>commandTimeout</c>,
/// and <c>throwOnError</c> overrides that take precedence over the initialized defaults.
/// </para>
/// <para>
/// <b>Parameter placeholders:</b> Access/OleDb uses positional <c>?</c> placeholders — not named
/// parameters. When using <see cref="OleDbParameter"/> arrays the order of parameters must match
/// the order of <c>?</c> markers in the SQL string.
/// </para>
/// <para>Required NuGet: <c>System.Data.OleDb</c></para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class AccessHelper
{
    #region Initialization

    private static AccessHelperOptions _options = new();

    /// <summary>
    /// Configures <see cref="AccessHelper"/> with application-wide defaults.
    /// Safe to call multiple times; the last call wins.
    /// </summary>
    /// <param name="configure">Action that receives and populates a fresh <see cref="AccessHelperOptions"/> instance.</param>
    public static void Initialize(Action<AccessHelperOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var opts = new AccessHelperOptions();
        configure(opts);
        _options = opts;
    }

    #endregion

    #region Private Helpers

    private static string ResolveConnectionString(string? perCallOverride)
    {
        var cs = perCallOverride ?? _options.DefaultConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "No connection string was provided. Either pass one per-call or set " +
                $"{nameof(AccessHelperOptions.DefaultConnectionString)} via {nameof(AccessHelper)}.{nameof(Initialize)}().");
        return cs;
    }

    /// <summary>Clones parameters onto the command to avoid "parameter already belongs to another command" errors on reuse.</summary>
    private static void AddParameters(OleDbCommand command, OleDbParameter[]? parameters)
    {
        if (parameters is null || parameters.Length == 0) return;
        foreach (var p in parameters)
        {
            command.Parameters.Add(new OleDbParameter(p.ParameterName, p.Value ?? DBNull.Value)
            {
                OleDbType = p.OleDbType,
                Size      = p.Size,
                Direction = p.Direction,
                Precision = p.Precision,
                Scale     = p.Scale
            });
        }
    }

    private static void AddParameters(OleDbCommand command, IList<object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return;
        foreach (var value in parameters)
            command.Parameters.AddWithValue(string.Empty, value ?? DBNull.Value);
    }

    /// <summary>
    /// Logs a message with caller context captured at compile time via
    /// <see cref="CallerMemberNameAttribute"/> and <see cref="CallerFilePathAttribute"/>.
    /// The injected names always reflect the <see cref="AccessHelper"/> method, never the external caller.
    /// </summary>
    private static void Log(
        LogLevel   level,
        Exception? ex,
        string     message,
        [CallerMemberName] string callerMethod = "",
        [CallerFilePath]   string callerFile   = "")
    {
        if (_options.Logger is null) return;
        var context = $"[{Path.GetFileNameWithoutExtension(callerFile)}.{callerMethod}]";
        if (ex is not null)
            _options.Logger.Log(level, ex, "{Context} {Message}", context, message);
        else
            _options.Logger.Log(level, "{Context} {Message}", context, message);
    }

    private static T? ConvertScalar<T>(object? raw)
    {
        if (raw is null || raw == DBNull.Value) return default;
        if (raw is T typed) return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            if (targetType == typeof(Guid))   return (T)(object)Guid.Parse(raw.ToString()!);
            if (targetType == typeof(byte[])) return raw is byte[] b ? (T)(object)b : default;
            return (T)Convert.ChangeType(raw, targetType);
        }
        catch { return default; }
    }

    private static readonly DateTime AccessMinDate = new(100, 1, 1);

    #endregion

    #region Async Methods

    // ── ExecuteScalarAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first column of the first row,
    /// or <c>null</c> when the result is SQL <c>NULL</c> or an error occurs (see <paramref name="throwOnError"/>).
    /// </summary>
    [DebuggerStepThrough]
    public static Task<object?> ExecuteScalarAsync(
        string             sql,
        OleDbParameter[]?  parameters        = null,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <inheritdoc cref="ExecuteScalarAsync(string,OleDbParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<object?> ExecuteScalarAsync(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first column of the first row
    /// cast to <typeparamref name="T"/>. Returns <c>default</c> when the result is SQL <c>NULL</c>
    /// or the conversion fails.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<T?> ExecuteScalarAsync<T>(
        string             sql,
        OleDbParameter[]?  parameters        = null,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var raw = await ExecuteScalarAsync(sql, parameters, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return ConvertScalar<T>(raw);
    }

    /// <inheritdoc cref="ExecuteScalarAsync{T}(string,OleDbParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<T?> ExecuteScalarAsync<T>(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var raw = await ExecuteScalarAsync(sql, parameters, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return ConvertScalar<T>(raw);
    }

    // ── ExecuteNonQueryAsync ─────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL command asynchronously and returns the number of rows affected.
    /// Returns <c>-1</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static Task<int> ExecuteNonQueryAsync(
        string             sql,
        OleDbParameter[]?  parameters        = null,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <inheritdoc cref="ExecuteNonQueryAsync(string,OleDbParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<int> ExecuteNonQueryAsync(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    // ── GetDataTableAsync ────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns the results as a <see cref="DataTable"/>.
    /// Returns an empty <see cref="DataTable"/> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static Task<DataTable> GetDataTableAsync(
        string             sql,
        OleDbParameter[]?  parameters        = null,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <inheritdoc cref="GetDataTableAsync(string,OleDbParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<DataTable> GetDataTableAsync(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    // ── GetListAsync<T> ──────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and maps each row to an instance of <typeparamref name="T"/>.
    /// Returns an empty list on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<List<T>> GetListAsync<T>(
        string             sql,
        OleDbParameter[]?  parameters        = null,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var dt = await GetDataTableAsync(sql, parameters, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return dt.ToList<T>();
    }

    /// <inheritdoc cref="GetListAsync{T}(string,OleDbParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<List<T>> GetListAsync<T>(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        bool?              throwOnError      = null,
        CancellationToken  cancellationToken = default)
    {
        var dt = await GetDataTableAsync(sql, parameters, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return dt.ToList<T>();
    }

    // ── GetDataReaderAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns an <see cref="OleDbDataReader"/>.
    /// <para>
    /// <b>Always throws on error</b> — there is no safe default value for a missing reader.
    /// The underlying connection is automatically closed when the reader is disposed
    /// (<see cref="CommandBehavior.CloseConnection"/>). Always wrap in a <c>using</c> block.
    /// </para>
    /// </summary>
    [DebuggerStepThrough]
    public static Task<OleDbDataReader> GetDataReaderAsync(
        string             sql,
        OleDbParameter[]?  parameters        = null,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, cancellationToken);
    }

    /// <inheritdoc cref="GetDataReaderAsync(string,OleDbParameter[],string,CommandType,int?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<OleDbDataReader> GetDataReaderAsync(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString  = null,
        CommandType        commandType       = CommandType.Text,
        int?               commandTimeout    = null,
        CancellationToken  cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, cancellationToken);
    }

    #endregion

    #region Sync Methods

    // ── ExecuteScalar ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and returns the first column of the first row,
    /// or <c>null</c> when the result is SQL <c>NULL</c> or an error occurs (see <paramref name="throwOnError"/>).
    /// </summary>
    [DebuggerStepThrough]
    public static object? ExecuteScalar(
        string             sql,
        OleDbParameter[]?  parameters       = null,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <inheritdoc cref="ExecuteScalar(string,OleDbParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static object? ExecuteScalar(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <summary>
    /// Executes a SQL query and returns the first column of the first row cast to <typeparamref name="T"/>.
    /// Returns <c>default</c> when the result is SQL <c>NULL</c> or the conversion fails.
    /// </summary>
    [DebuggerStepThrough]
    public static T? ExecuteScalar<T>(
        string             sql,
        OleDbParameter[]?  parameters       = null,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
        => ConvertScalar<T>(ExecuteScalar(sql, parameters, connectionString, commandType, commandTimeout, throwOnError));

    /// <inheritdoc cref="ExecuteScalar{T}(string,OleDbParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static T? ExecuteScalar<T>(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
        => ConvertScalar<T>(ExecuteScalar(sql, parameters, connectionString, commandType, commandTimeout, throwOnError));

    // ── ExecuteNonQuery ──────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL command and returns the number of rows affected.
    /// Returns <c>-1</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static int ExecuteNonQuery(
        string             sql,
        OleDbParameter[]?  parameters       = null,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <inheritdoc cref="ExecuteNonQuery(string,OleDbParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static int ExecuteNonQuery(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    // ── GetDataTable ─────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and returns the results as a <see cref="DataTable"/>.
    /// Returns an empty <see cref="DataTable"/> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static DataTable GetDataTable(
        string             sql,
        OleDbParameter[]?  parameters       = null,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <inheritdoc cref="GetDataTable(string,OleDbParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static DataTable GetDataTable(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    // ── GetList<T> ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and maps each row to an instance of <typeparamref name="T"/>.
    /// Returns an empty list on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static List<T> GetList<T>(
        string             sql,
        OleDbParameter[]?  parameters       = null,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
        => GetDataTable(sql, parameters, connectionString, commandType, commandTimeout, throwOnError).ToList<T>();

    /// <inheritdoc cref="GetList{T}(string,OleDbParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static List<T> GetList<T>(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null,
        bool?              throwOnError     = null)
        => GetDataTable(sql, parameters, connectionString, commandType, commandTimeout, throwOnError).ToList<T>();

    // ── GetDataReader ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and returns an <see cref="OleDbDataReader"/>.
    /// <para>
    /// <b>Always throws on error</b> — there is no safe default value for a missing reader.
    /// The underlying connection is automatically closed when the reader is disposed
    /// (<see cref="CommandBehavior.CloseConnection"/>). Always wrap in a <c>using</c> block.
    /// </para>
    /// </summary>
    [DebuggerStepThrough]
    public static OleDbDataReader GetDataReader(
        string             sql,
        OleDbParameter[]?  parameters       = null,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCore(sql, cmd => AddParameters(cmd, parameters), cs, commandType, timeout);
    }

    /// <inheritdoc cref="GetDataReader(string,OleDbParameter[],string,CommandType,int?)"/>
    [DebuggerStepThrough]
    public static OleDbDataReader GetDataReader(
        string             sql,
        IList<object?>?    parameters,
        string?            connectionString = null,
        CommandType        commandType      = CommandType.Text,
        int?               commandTimeout   = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCore(sql, cmd => AddParameters(cmd, parameters), cs, commandType, timeout);
    }

    #endregion

    #region Parameter Helpers

    /// <summary>
    /// Builds an <see cref="OleDbParameter"/> array from an object's public instance properties.
    /// <para>
    /// <see cref="DateTime"/> properties are only included when the value exceeds Access's
    /// minimum date (0100-01-01). <c>null</c> properties are omitted entirely.
    /// </para>
    /// <para>
    /// <b>Important:</b> OleDb uses positional <c>?</c> parameters. Ensure the order of
    /// properties matches the order of <c>?</c> markers in your SQL string.
    /// </para>
    /// </summary>
    /// <param name="obj">Source object whose public declared instance properties are mapped.</param>
    [DebuggerStepThrough]
    public static OleDbParameter[] GetOleDbParameters(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var list  = new List<OleDbParameter>();
        var props = obj.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var prop in props)
        {
            if (prop.Name.Equals("Item", StringComparison.OrdinalIgnoreCase)) continue;

            var value = prop.GetValue(obj, null);

            if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
            {
                var dt = value as DateTime? ?? (value is DateTime d ? d : (DateTime?)null);
                if (dt.HasValue && dt.Value > AccessMinDate)
                    list.Add(new OleDbParameter(prop.Name, dt.Value));
            }
            else if (value is not null)
            {
                list.Add(new OleDbParameter(prop.Name, value));
            }
        }

        return [.. list];
    }

    /// <summary>
    /// Builds a positional parameter value list from an object's public instance properties.
    /// <para>
    /// <see cref="DateTime"/> properties are only included when the value exceeds Access's
    /// minimum date (0100-01-01). <c>null</c> properties are omitted entirely.
    /// </para>
    /// </summary>
    /// <param name="obj">Source object whose public declared instance properties are mapped.</param>
    [DebuggerStepThrough]
    public static List<object?> GetParametersFromObject(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var parms = new List<object?>();
        var props = obj.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var prop in props)
        {
            if (prop.Name.Equals("Item", StringComparison.OrdinalIgnoreCase)) continue;

            var value = prop.GetValue(obj, null);

            if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
            {
                var dt = value as DateTime? ?? (value is DateTime d ? d : (DateTime?)null);
                if (dt.HasValue && dt.Value > AccessMinDate)
                    parms.Add(dt.Value);
            }
            else if (value is not null)
            {
                parms.Add(value);
            }
        }

        return parms;
    }

    #endregion

    #region Private Core Implementations

    private static async Task<object?> ExecuteScalarCoreAsync(
        string              sql,
        Action<OleDbCommand> addParams,
        string              cs,
        CommandType         commandType,
        int                 timeout,
        bool                throwOnError,
        CancellationToken   cancellationToken)
    {
        try
        {
            await using var connection = new OleDbConnection(cs);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result == DBNull.Value ? null : result;
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
            return null;
        }
    }

    private static async Task<int> ExecuteNonQueryCoreAsync(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout,
        bool                 throwOnError,
        CancellationToken    cancellationToken)
    {
        try
        {
            await using var connection = new OleDbConnection(cs);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing non-query: {sql}");
            var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Log(LogLevel.Debug, null, $"Rows affected: {rows}");
            return rows;
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
            return -1;
        }
    }

    private static async Task<DataTable> GetDataTableCoreAsync(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout,
        bool                 throwOnError,
        CancellationToken    cancellationToken)
    {
        var result = new DataTable();
        try
        {
            await using var connection = new OleDbConnection(cs);
            await using var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            result.Load(reader);
            Log(LogLevel.Debug, null, $"Loaded {result.Rows.Count} row(s).");
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
        }
        return result;
    }

    private static async Task<OleDbDataReader> GetDataReaderCoreAsync(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout,
        CancellationToken    cancellationToken)
    {
        var connection = new OleDbConnection(cs);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing reader: {sql}");
            return (OleDbDataReader)await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static object? ExecuteScalarCore(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout,
        bool                 throwOnError)
    {
        try
        {
            using var connection = new OleDbConnection(cs);
            connection.Open();
            using var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            var result = command.ExecuteScalar();
            return result == DBNull.Value ? null : result;
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
            return null;
        }
    }

    private static int ExecuteNonQueryCore(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout,
        bool                 throwOnError)
    {
        try
        {
            using var connection = new OleDbConnection(cs);
            connection.Open();
            using var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing non-query: {sql}");
            var rows = command.ExecuteNonQuery();
            Log(LogLevel.Debug, null, $"Rows affected: {rows}");
            return rows;
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
            return -1;
        }
    }

    private static DataTable GetDataTableCore(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout,
        bool                 throwOnError)
    {
        var result = new DataTable();
        try
        {
            using var connection = new OleDbConnection(cs);
            using var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            connection.Open();
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            using var reader = command.ExecuteReader();
            result.Load(reader);
            Log(LogLevel.Debug, null, $"Loaded {result.Rows.Count} row(s).");
        }
        catch (Exception ex) when (!throwOnError)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
        }
        return result;
    }

    private static OleDbDataReader GetDataReaderCore(
        string               sql,
        Action<OleDbCommand> addParams,
        string               cs,
        CommandType          commandType,
        int                  timeout)
    {
        var connection = new OleDbConnection(cs);
        try
        {
            connection.Open();
            var command = new OleDbCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing reader: {sql}");
            return (OleDbDataReader)command.ExecuteReader(CommandBehavior.CloseConnection);
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, ex, $"Failed: {sql}");
            connection.Dispose();
            throw;
        }
    }

    #endregion
}
