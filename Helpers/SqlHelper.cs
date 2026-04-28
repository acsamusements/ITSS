using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ITSS.Helpers;

/// <summary>
/// Configuration options for <see cref="SqlHelper"/>.
/// Pass an action to <see cref="SqlHelper.Initialize"/> to set application-wide defaults.
/// All settings can be overridden on a per-call basis.
/// </summary>
public sealed class SqlHelperOptions
{
    /// <summary>Default connection string used when none is supplied per-call.</summary>
    public string? DefaultConnectionString { get; set; }

    /// <summary>Optional logger. When <c>null</c>, all logging is silently skipped.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Default SQL command timeout in seconds. Default: 30.</summary>
    public int DefaultCommandTimeout { get; set; } = 30;

    /// <summary>
    /// When <c>true</c>, all exceptions are re-thrown after logging.
    /// When <c>false</c> (default), exceptions are swallowed and a safe default value is returned.
    /// Individual methods accept a per-call <c>throwOnError</c> override.
    /// </summary>
    public bool ThrowOnError { get; set; } = false;
}

/// <summary>
/// A reusable static SQL Server helper for any C# project targeting .NET 8+.
/// <para>
/// Call <see cref="Initialize"/> once at application startup to configure defaults.
/// Every method accepts optional per-call <c>connectionString</c>, <c>commandTimeout</c>,
/// and <c>throwOnError</c> overrides that take precedence over the initialized defaults.
/// </para>
/// <para>Required NuGet: <c>Microsoft.Data.SqlClient</c></para>
/// </summary>
public static class SqlHelper
{
    #region Initialization

    private static SqlHelperOptions _options = new();

    /// <summary>
    /// Configures <see cref="SqlHelper"/> with application-wide defaults.
    /// Safe to call multiple times; the last call wins.
    /// </summary>
    /// <param name="configure">Action that receives and populates a fresh <see cref="SqlHelperOptions"/> instance.</param>
    public static void Initialize(Action<SqlHelperOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var opts = new SqlHelperOptions();
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
                $"{nameof(SqlHelperOptions.DefaultConnectionString)} via {nameof(SqlHelper)}.{nameof(Initialize)}().");
        return cs;
    }

    /// <summary>Clones parameters onto the command to avoid "parameter already belongs to another command" errors on reuse.</summary>
    private static void AddParameters(SqlCommand command, SqlParameter[]? parameters)
    {
        if (parameters is null || parameters.Length == 0) return;
        foreach (var p in parameters)
        {
            command.Parameters.Add(new SqlParameter(p.ParameterName, p.Value ?? DBNull.Value)
            {
                SqlDbType = p.SqlDbType,
                Size      = p.Size,
                Direction = p.Direction,
                Precision = p.Precision,
                Scale     = p.Scale
            });
        }
    }

    private static void AddParameters(SqlCommand command, Dictionary<string, object?>? parameters, bool prefixAt)
    {
        if (parameters is null || parameters.Count == 0) return;
        foreach (var (key, value) in parameters)
        {
            var name = prefixAt && !key.StartsWith('@') ? "@" + key : key;
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    /// <summary>
    /// Logs a message with caller context captured at compile time via
    /// <see cref="CallerMemberNameAttribute"/> and <see cref="CallerFilePathAttribute"/>.
    /// The injected names always reflect the <see cref="SqlHelper"/> method, never the external caller.
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
            if (targetType == typeof(Guid))    return (T)(object)Guid.Parse(raw.ToString()!);
            if (targetType == typeof(byte[]))  return raw is byte[] b ? (T)(object)b : default;
            return (T)Convert.ChangeType(raw, targetType);
        }
        catch { return default; }
    }

    private static readonly DateTime SqlMinDate = new(1753, 1, 1);

    #endregion

    #region Async Methods

    // ── ExecuteScalarAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first column of the first row,
    /// or <c>null</c> when the result is SQL <c>NULL</c> or an error occurs (see <paramref name="throwOnError"/>).
    /// </summary>
    [DebuggerStepThrough]
    public static Task<object?> ExecuteScalarAsync(
        string            sql,
        SqlParameter[]?   parameters       = null,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <inheritdoc cref="ExecuteScalarAsync(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<object?> ExecuteScalarAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt         = true,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCoreAsync(sql, cmd => AddParameters(cmd, parameters, prefixAt),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first column of the first row
    /// cast to <typeparamref name="T"/>. Returns <c>default</c> when the result is SQL <c>NULL</c>
    /// or the conversion fails.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<T?> ExecuteScalarAsync<T>(
        string            sql,
        SqlParameter[]?   parameters       = null,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var raw = await ExecuteScalarAsync(sql, parameters, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return ConvertScalar<T>(raw);
    }

    /// <inheritdoc cref="ExecuteScalarAsync{T}(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<T?> ExecuteScalarAsync<T>(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt         = true,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var raw = await ExecuteScalarAsync(sql, parameters, prefixAt, connectionString,
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
        string            sql,
        SqlParameter[]?   parameters       = null,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <inheritdoc cref="ExecuteNonQueryAsync(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<int> ExecuteNonQueryAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt         = true,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCoreAsync(sql, cmd => AddParameters(cmd, parameters, prefixAt),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    // ── GetDataTableAsync ────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns the results as a <see cref="DataTable"/>.
    /// Returns an empty <see cref="DataTable"/> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static Task<DataTable> GetDataTableAsync(
        string            sql,
        SqlParameter[]?   parameters       = null,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    /// <inheritdoc cref="GetDataTableAsync(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<DataTable> GetDataTableAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt         = true,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCoreAsync(sql, cmd => AddParameters(cmd, parameters, prefixAt),
            cs, commandType, timeout, doThrow, cancellationToken);
    }

    // ── GetListAsync<T> ──────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and maps each row to an instance of <typeparamref name="T"/>.
    /// Returns an empty list on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<List<T>> GetListAsync<T>(
        string            sql,
        SqlParameter[]?   parameters       = null,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var dt = await GetDataTableAsync(sql, parameters, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return dt.ToList<T>();
    }

    /// <inheritdoc cref="GetListAsync{T}(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<List<T>> GetListAsync<T>(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt         = true,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        bool?             throwOnError     = null,
        CancellationToken cancellationToken = default)
    {
        var dt = await GetDataTableAsync(sql, parameters, prefixAt, connectionString,
            commandType, commandTimeout, throwOnError, cancellationToken).ConfigureAwait(false);
        return dt.ToList<T>();
    }

    // ── GetDataReaderAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns a <see cref="SqlDataReader"/>.
    /// <para>
    /// <b>Always throws on error</b> — there is no safe default value for a missing reader.
    /// The underlying connection is automatically closed when the reader is disposed
    /// (<see cref="CommandBehavior.CloseConnection"/>). Always wrap in a <c>using</c> block.
    /// </para>
    /// </summary>
    [DebuggerStepThrough]
    public static Task<SqlDataReader> GetDataReaderAsync(
        string            sql,
        SqlParameter[]?   parameters       = null,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCoreAsync(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, cancellationToken);
    }

    /// <inheritdoc cref="GetDataReaderAsync(string,SqlParameter[],string,CommandType,int?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static Task<SqlDataReader> GetDataReaderAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt         = true,
        string?           connectionString = null,
        CommandType       commandType      = CommandType.Text,
        int?              commandTimeout   = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCoreAsync(sql, cmd => AddParameters(cmd, parameters, prefixAt),
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
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <inheritdoc cref="ExecuteScalar(string,SqlParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static object? ExecuteScalar(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool            prefixAt         = true,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteScalarCore(sql, cmd => AddParameters(cmd, parameters, prefixAt),
            cs, commandType, timeout, doThrow);
    }

    /// <summary>
    /// Executes a SQL query and returns the first column of the first row cast to <typeparamref name="T"/>.
    /// Returns <c>default</c> when the result is SQL <c>NULL</c> or the conversion fails.
    /// </summary>
    [DebuggerStepThrough]
    public static T? ExecuteScalar<T>(
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
        => ConvertScalar<T>(ExecuteScalar(sql, parameters, connectionString, commandType, commandTimeout, throwOnError));

    /// <inheritdoc cref="ExecuteScalar{T}(string,SqlParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static T? ExecuteScalar<T>(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool            prefixAt         = true,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
        => ConvertScalar<T>(ExecuteScalar(sql, parameters, prefixAt, connectionString, commandType, commandTimeout, throwOnError));

    // ── ExecuteNonQuery ──────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL command and returns the number of rows affected.
    /// Returns <c>-1</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static int ExecuteNonQuery(
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <inheritdoc cref="ExecuteNonQuery(string,SqlParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static int ExecuteNonQuery(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool            prefixAt         = true,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return ExecuteNonQueryCore(sql, cmd => AddParameters(cmd, parameters, prefixAt),
            cs, commandType, timeout, doThrow);
    }

    // ── GetDataTable ─────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and returns the results as a <see cref="DataTable"/>.
    /// Returns an empty <see cref="DataTable"/> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static DataTable GetDataTable(
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCore(sql, cmd => AddParameters(cmd, parameters),
            cs, commandType, timeout, doThrow);
    }

    /// <inheritdoc cref="GetDataTable(string,SqlParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static DataTable GetDataTable(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool            prefixAt         = true,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var doThrow = throwOnError   ?? _options.ThrowOnError;
        return GetDataTableCore(sql, cmd => AddParameters(cmd, parameters, prefixAt),
            cs, commandType, timeout, doThrow);
    }

    // ── GetList<T> ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and maps each row to an instance of <typeparamref name="T"/>.
    /// Returns an empty list on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static List<T> GetList<T>(
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
        => GetDataTable(sql, parameters, connectionString, commandType, commandTimeout, throwOnError).ToList<T>();

    /// <inheritdoc cref="GetList{T}(string,SqlParameter[],string,CommandType,int?,bool?)"/>
    [DebuggerStepThrough]
    public static List<T> GetList<T>(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool            prefixAt         = true,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
        => GetDataTable(sql, parameters, prefixAt, connectionString, commandType, commandTimeout, throwOnError).ToList<T>();

    // ── GetDataReader ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and returns a <see cref="SqlDataReader"/>.
    /// <para>
    /// <b>Always throws on error</b> — there is no safe default value for a missing reader.
    /// The underlying connection is automatically closed when the reader is disposed
    /// (<see cref="CommandBehavior.CloseConnection"/>). Always wrap in a <c>using</c> block.
    /// </para>
    /// </summary>
    [DebuggerStepThrough]
    public static SqlDataReader GetDataReader(
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCore(sql, cmd => AddParameters(cmd, parameters), cs, commandType, timeout);
    }

    /// <inheritdoc cref="GetDataReader(string,SqlParameter[],string,CommandType,int?)"/>
    [DebuggerStepThrough]
    public static SqlDataReader GetDataReader(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool            prefixAt         = true,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        return GetDataReaderCore(sql, cmd => AddParameters(cmd, parameters, prefixAt), cs, commandType, timeout);
    }

    #endregion

    #region Parameter Helpers

    /// <summary>
    /// Builds a <see cref="SqlParameter"/> array from an object's public instance properties.
    /// <para>
    /// <see cref="DateTime"/> properties are only included when the value exceeds SQL Server's
    /// minimum date (1753-01-01). <c>null</c> properties are omitted entirely.
    /// </para>
    /// </summary>
    /// <param name="obj">Source object whose public declared instance properties are mapped.</param>
    /// <param name="prefixAt">When <c>true</c> (default), parameter names are prefixed with <c>@</c>.</param>
    [DebuggerStepThrough]
    public static SqlParameter[] GetSqlParameters(object obj, bool prefixAt = true)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var list  = new List<SqlParameter>();
        var props = obj.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var prop in props)
        {
            if (prop.Name.Equals("Item", StringComparison.OrdinalIgnoreCase)) continue;

            var value = prop.GetValue(obj, null);
            var name  = prefixAt ? $"@{prop.Name}" : prop.Name;

            if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
            {
                var dt = value as DateTime? ?? (value is DateTime d ? d : (DateTime?)null);
                if (dt.HasValue && dt.Value > SqlMinDate)
                    list.Add(new SqlParameter(name, dt.Value));
            }
            else if (value is not null)
            {
                list.Add(new SqlParameter(name, value));
            }
        }

        return [.. list];
    }

    /// <summary>
    /// Builds a parameter dictionary from an object's public instance properties.
    /// <para>
    /// <see cref="DateTime"/> properties are only included when the value exceeds SQL Server's
    /// minimum date (1753-01-01). <c>null</c> properties are omitted entirely.
    /// </para>
    /// </summary>
    /// <param name="obj">Source object whose public declared instance properties are mapped.</param>
    [DebuggerStepThrough]
    public static Dictionary<string, object?> GetParametersFromObject(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var parms = new Dictionary<string, object?>();
        var props = obj.GetType().GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var prop in props)
        {
            if (prop.Name.Equals("Item", StringComparison.OrdinalIgnoreCase)) continue;

            var value = prop.GetValue(obj, null);

            if (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?))
            {
                var dt = value as DateTime? ?? (value is DateTime d ? d : (DateTime?)null);
                if (dt.HasValue && dt.Value > SqlMinDate)
                    parms[prop.Name] = dt.Value;
            }
            else if (value is not null)
            {
                parms[prop.Name] = value;
            }
        }

        return parms;
    }

    #endregion

    #region Private Core Implementations

    private static async Task<object?> ExecuteScalarCoreAsync(
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        bool               throwOnError,
        CancellationToken  cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection)
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
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        bool               throwOnError,
        CancellationToken  cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection)
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
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        bool               throwOnError,
        CancellationToken  cancellationToken)
    {
        var result = new DataTable();
        try
        {
            await using var connection = new SqlConnection(cs);
            await using var command = new SqlCommand(sql, connection)
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

    private static async Task<SqlDataReader> GetDataReaderCoreAsync(
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        CancellationToken  cancellationToken)
    {
        var connection = new SqlConnection(cs);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new SqlCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing reader: {sql}");
            return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken)
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
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        bool               throwOnError)
    {
        try
        {
            using var connection = new SqlConnection(cs);
            connection.Open();
            using var command = new SqlCommand(sql, connection)
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
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        bool               throwOnError)
    {
        try
        {
            using var connection = new SqlConnection(cs);
            connection.Open();
            using var command = new SqlCommand(sql, connection)
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
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout,
        bool               throwOnError)
    {
        var result = new DataTable();
        try
        {
            using var connection = new SqlConnection(cs);
            using var command = new SqlCommand(sql, connection)
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

    private static SqlDataReader GetDataReaderCore(
        string             sql,
        Action<SqlCommand> addParams,
        string             cs,
        CommandType        commandType,
        int                timeout)
    {
        var connection = new SqlConnection(cs);
        try
        {
            connection.Open();
            var command = new SqlCommand(sql, connection)
            {
                CommandType    = commandType,
                CommandTimeout = timeout
            };
            addParams(command);
            Log(LogLevel.Debug, null, $"Executing reader: {sql}");
            return command.ExecuteReader(CommandBehavior.CloseConnection);
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