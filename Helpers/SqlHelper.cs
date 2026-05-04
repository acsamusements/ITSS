using Dapper;
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
/// <para>
/// Typed query methods (<see cref="GetList{T}"/>, <see cref="GetListAsync{T}"/>,
/// <see cref="ExecuteScalar{T}"/>) use Dapper for fast IL-based object mapping,
/// bypassing DataTable allocation. <see cref="GetDataTable"/> retains ADO.NET for
/// callers that require a <see cref="DataTable"/> result.
/// </para>
/// <para>Required NuGet: <c>Dapper</c>, <c>Microsoft.Data.SqlClient</c></para>
/// </summary>
public static class SqlHelper
{
    #region Initialization

    private static SqlHelperOptions _options = new();

    /// <summary>
    /// Configures <see cref="SqlHelper"/> with application-wide defaults.
    /// Safe to call multiple times; the last call wins.
    /// </summary>
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

    // Resolves the three common per-call overrides in one go.
    private static (string cs, int timeout, bool doThrow) Resolve(
        string? connectionString, int? commandTimeout, bool? throwOnError) =>
        (ResolveConnectionString(connectionString),
         commandTimeout ?? _options.DefaultCommandTimeout,
         throwOnError   ?? _options.ThrowOnError);

    // Packages all execution options into a Dapper CommandDefinition (the canonical
    // way to forward CancellationToken without needing a separate parameter per call).
    private static CommandDefinition Cmd(
        string sql, object? parameters, CommandType commandType, int timeout,
        CancellationToken cancellationToken = default) =>
        new(sql, parameters, commandType: commandType, commandTimeout: timeout,
            cancellationToken: cancellationToken);

    // Converts SqlParameter[] → Dapper DynamicParameters for Dapper execution paths.
    // Direction and Size are preserved; SqlDbType is inferred by Dapper from the value,
    // which is correct for the vast majority of cases.
    private static DynamicParameters ToParams(SqlParameter[]? parameters)
    {
        var dp = new DynamicParameters();
        if (parameters is null || parameters.Length == 0) return dp;
        foreach (var p in parameters)
            dp.Add(p.ParameterName, p.Value is DBNull ? null : p.Value,
                   direction: p.Direction, size: p.Size > 0 ? p.Size : null);
        return dp;
    }

    private static DynamicParameters ToParams(Dictionary<string, object?>? parameters, bool prefixAt)
    {
        var dp = new DynamicParameters();
        if (parameters is null || parameters.Count == 0) return dp;
        foreach (var (key, value) in parameters)
            dp.Add(prefixAt && !key.StartsWith('@') ? "@" + key : key, value);
        return dp;
    }

    // Clones SqlParameter[] onto a SqlCommand with full type fidelity (SqlDbType, Size,
    // Direction, Precision, Scale). Used only by the DataTable and DataReader paths
    // where raw ADO.NET is required.
    private static void Parameterize(SqlCommand cmd, SqlParameter[]? parameters)
    {
        if (parameters is null || parameters.Length == 0) return;
        foreach (var p in parameters)
            cmd.Parameters.Add(new SqlParameter(p.ParameterName, p.Value ?? DBNull.Value)
            {
                SqlDbType = p.SqlDbType,
                Size      = p.Size,
                Direction = p.Direction,
                Precision = p.Precision,
                Scale     = p.Scale
            });
    }

    private static void Parameterize(SqlCommand cmd, Dictionary<string, object?>? parameters, bool prefixAt)
    {
        if (parameters is null || parameters.Count == 0) return;
        foreach (var (key, value) in parameters)
        {
            var name = prefixAt && !key.StartsWith('@') ? "@" + key : key;
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

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

    private static readonly DateTime SqlMinDate = new(1753, 1, 1);

    #endregion

    #region Async Methods

    // ── ExecuteScalarAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first column of the first row,
    /// or <c>null</c> when the result is SQL <c>NULL</c> or an error occurs (see <paramref name="throwOnError"/>).
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<object?> ExecuteScalarAsync(
        string            sql,
        SqlParameter[]?   parameters        = null,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return await conn.ExecuteScalarAsync(Cmd(sql, ToParams(parameters), commandType, timeout, cancellationToken))
                             .ConfigureAwait(false);
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return null; }
    }

    /// <inheritdoc cref="ExecuteScalarAsync(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<object?> ExecuteScalarAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt          = true,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return await conn.ExecuteScalarAsync(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout, cancellationToken))
                             .ConfigureAwait(false);
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return null; }
    }

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first column of the first row
    /// cast to <typeparamref name="T"/>. Returns <c>default</c> when the result is SQL <c>NULL</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<T?> ExecuteScalarAsync<T>(
        string            sql,
        SqlParameter[]?   parameters        = null,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return await conn.ExecuteScalarAsync<T>(Cmd(sql, ToParams(parameters), commandType, timeout, cancellationToken))
                             .ConfigureAwait(false);
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return default; }
    }

    /// <inheritdoc cref="ExecuteScalarAsync{T}(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<T?> ExecuteScalarAsync<T>(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt          = true,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return await conn.ExecuteScalarAsync<T>(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout, cancellationToken))
                             .ConfigureAwait(false);
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return default; }
    }

    // ── ExecuteNonQueryAsync ─────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL command asynchronously and returns the number of rows affected.
    /// Returns <c>-1</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<int> ExecuteNonQueryAsync(
        string            sql,
        SqlParameter[]?   parameters        = null,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing non-query: {sql}");
            return await conn.ExecuteAsync(Cmd(sql, ToParams(parameters), commandType, timeout, cancellationToken))
                             .ConfigureAwait(false);
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return -1; }
    }

    /// <inheritdoc cref="ExecuteNonQueryAsync(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<int> ExecuteNonQueryAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt          = true,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing non-query: {sql}");
            return await conn.ExecuteAsync(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout, cancellationToken))
                             .ConfigureAwait(false);
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return -1; }
    }

    // ── GetDataTableAsync ────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns the results as a <see cref="DataTable"/>.
    /// Returns an empty <see cref="DataTable"/> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<DataTable> GetDataTableAsync(
        string            sql,
        SqlParameter[]?   parameters        = null,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        var result = new DataTable();
        try
        {
            await using var conn = new SqlConnection(cs);
            await using var cmd  = new SqlCommand(sql, conn) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(cmd, parameters);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            result.Load(reader);
            Log(LogLevel.Debug, null, $"Loaded {result.Rows.Count} row(s).");
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); }
        return result;
    }

    /// <inheritdoc cref="GetDataTableAsync(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<DataTable> GetDataTableAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt          = true,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        var result = new DataTable();
        try
        {
            await using var conn = new SqlConnection(cs);
            await using var cmd  = new SqlCommand(sql, conn) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(cmd, parameters, prefixAt);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            result.Load(reader);
            Log(LogLevel.Debug, null, $"Loaded {result.Rows.Count} row(s).");
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); }
        return result;
    }

    // ── GetListAsync<T> ──────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and maps each row to <typeparamref name="T"/> using
    /// Dapper's IL-based mapper — faster than DataTable allocation followed by reflection mapping.
    /// Returns an empty list on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<List<T>> GetListAsync<T>(
        string            sql,
        SqlParameter[]?   parameters        = null,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            var rows = await conn.QueryAsync<T>(Cmd(sql, ToParams(parameters), commandType, timeout, cancellationToken))
                                 .ConfigureAwait(false);
            return rows.AsList();
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return []; }
    }

    /// <inheritdoc cref="GetListAsync{T}(string,SqlParameter[],string,CommandType,int?,bool?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<List<T>> GetListAsync<T>(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt          = true,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            await using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            var rows = await conn.QueryAsync<T>(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout, cancellationToken))
                                 .ConfigureAwait(false);
            return rows.AsList();
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return []; }
    }

    // ── GetDataReaderAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query asynchronously and returns a <see cref="SqlDataReader"/>.
    /// <para>
    /// <b>Always throws on error</b> — there is no safe default for a missing reader.
    /// The underlying connection closes automatically when the reader is disposed
    /// (<see cref="CommandBehavior.CloseConnection"/>). Always wrap in a <c>using</c> block.
    /// </para>
    /// </summary>
    [DebuggerStepThrough]
    public static async Task<SqlDataReader> GetDataReaderAsync(
        string            sql,
        SqlParameter[]?   parameters        = null,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var connection = new SqlConnection(cs);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new SqlCommand(sql, connection) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(command, parameters);
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

    /// <inheritdoc cref="GetDataReaderAsync(string,SqlParameter[],string,CommandType,int?,CancellationToken)"/>
    [DebuggerStepThrough]
    public static async Task<SqlDataReader> GetDataReaderAsync(
        string                       sql,
        Dictionary<string, object?>? parameters,
        bool              prefixAt          = true,
        string?           connectionString  = null,
        CommandType       commandType       = CommandType.Text,
        int?              commandTimeout    = null,
        CancellationToken cancellationToken = default)
    {
        var cs      = ResolveConnectionString(connectionString);
        var timeout = commandTimeout ?? _options.DefaultCommandTimeout;
        var connection = new SqlConnection(cs);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new SqlCommand(sql, connection) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(command, parameters, prefixAt);
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
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return conn.ExecuteScalar(Cmd(sql, ToParams(parameters), commandType, timeout));
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return null; }
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
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return conn.ExecuteScalar(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout));
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return null; }
    }

    /// <summary>
    /// Executes a SQL query and returns the first column of the first row cast to <typeparamref name="T"/>.
    /// Returns <c>default</c> when the result is SQL <c>NULL</c>.
    /// </summary>
    [DebuggerStepThrough]
    public static T? ExecuteScalar<T>(
        string          sql,
        SqlParameter[]? parameters       = null,
        string?         connectionString = null,
        CommandType     commandType      = CommandType.Text,
        int?            commandTimeout   = null,
        bool?           throwOnError     = null)
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return conn.ExecuteScalar<T>(Cmd(sql, ToParams(parameters), commandType, timeout));
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return default; }
    }

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
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing scalar: {sql}");
            return conn.ExecuteScalar<T>(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout));
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return default; }
    }

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
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing non-query: {sql}");
            return conn.Execute(Cmd(sql, ToParams(parameters), commandType, timeout));
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return -1; }
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
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing non-query: {sql}");
            return conn.Execute(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout));
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return -1; }
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
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        var result = new DataTable();
        try
        {
            using var conn   = new SqlConnection(cs);
            using var cmd    = new SqlCommand(sql, conn) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(cmd, parameters);
            conn.Open();
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            using var reader = cmd.ExecuteReader();
            result.Load(reader);
            Log(LogLevel.Debug, null, $"Loaded {result.Rows.Count} row(s).");
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); }
        return result;
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
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        var result = new DataTable();
        try
        {
            using var conn   = new SqlConnection(cs);
            using var cmd    = new SqlCommand(sql, conn) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(cmd, parameters, prefixAt);
            conn.Open();
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            using var reader = cmd.ExecuteReader();
            result.Load(reader);
            Log(LogLevel.Debug, null, $"Loaded {result.Rows.Count} row(s).");
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); }
        return result;
    }

    // ── GetList<T> ───────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and maps each row to <typeparamref name="T"/> using Dapper's
    /// IL-based mapper — faster than DataTable allocation followed by reflection mapping.
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
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            return conn.Query<T>(Cmd(sql, ToParams(parameters), commandType, timeout)).AsList();
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return []; }
    }

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
    {
        var (cs, timeout, doThrow) = Resolve(connectionString, commandTimeout, throwOnError);
        try
        {
            using var conn = new SqlConnection(cs);
            Log(LogLevel.Debug, null, $"Executing query: {sql}");
            return conn.Query<T>(Cmd(sql, ToParams(parameters, prefixAt), commandType, timeout)).AsList();
        }
        catch (Exception ex) when (!doThrow) { Log(LogLevel.Error, ex, $"Failed: {sql}"); return []; }
    }

    // ── GetDataReader ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a SQL query and returns a <see cref="SqlDataReader"/>.
    /// <para>
    /// <b>Always throws on error</b> — there is no safe default for a missing reader.
    /// The underlying connection closes automatically when the reader is disposed
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
        var cs         = ResolveConnectionString(connectionString);
        var timeout    = commandTimeout ?? _options.DefaultCommandTimeout;
        var connection = new SqlConnection(cs);
        try
        {
            connection.Open();
            var command = new SqlCommand(sql, connection) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(command, parameters);
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
        var cs         = ResolveConnectionString(connectionString);
        var timeout    = commandTimeout ?? _options.DefaultCommandTimeout;
        var connection = new SqlConnection(cs);
        try
        {
            connection.Open();
            var command = new SqlCommand(sql, connection) { CommandType = commandType, CommandTimeout = timeout };
            Parameterize(command, parameters, prefixAt);
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
}
