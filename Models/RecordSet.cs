using System.Data;
using Microsoft.Data.SqlClient;

namespace ITSS.Models;

public class RecordSet : IDisposable
{
    private DataTable _dataTable;
    private int _currentPosition = -1;
    private bool _disposed = false;
    private SqlConnection? _connection;
    private SqlDataAdapter? _adapter;
    private SqlCommandBuilder? _commandBuilder;
    private string? _tableName;
    private SqlConnection? _sharedConnection;
    private string? _defaultConnectionString;
    private bool _ownsConnection = false;

    public RecordSet()
    {
        _dataTable = new DataTable();
    }

    public RecordSet(DataTable dataTable)
    {
        _dataTable = dataTable ?? throw new ArgumentNullException(nameof(dataTable));
        if (_dataTable.Rows.Count > 0)
            _currentPosition = 0;
    }

    // ── Connection configuration ─────────────────────────────────────────────

    public void SetDefaultConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        _defaultConnectionString = connectionString;
    }

    public void SetSharedConnection(SqlConnection connection)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    // ── Cursor properties ────────────────────────────────────────────────────

    public bool BOF => _currentPosition < 0 || _dataTable.Rows.Count == 0;

    public bool EOF => _currentPosition >= _dataTable.Rows.Count || _dataTable.Rows.Count == 0;

    public int RecordCount => _dataTable.Rows.Count;

    public int AbsolutePosition
    {
        get => _currentPosition;
        set
        {
            if (value < 0 || value >= _dataTable.Rows.Count)
                throw new IndexOutOfRangeException("Position out of range");
            _currentPosition = value;
        }
    }

    public object this[string fieldName]
    {
        get
        {
            if (EOF || BOF)
                throw new InvalidOperationException("No current record");
            return _dataTable.Rows[_currentPosition][fieldName];
        }
        set
        {
            if (EOF || BOF)
                throw new InvalidOperationException("No current record");
            _dataTable.Rows[_currentPosition][fieldName] = value;
        }
    }

    public object this[int index]
    {
        get
        {
            if (EOF || BOF)
                throw new InvalidOperationException("No current record");
            return _dataTable.Rows[_currentPosition][index];
        }
        set
        {
            if (EOF || BOF)
                throw new InvalidOperationException("No current record");
            _dataTable.Rows[_currentPosition][index] = value;
        }
    }

    public DataRow? CurrentRow => (EOF || BOF) ? null : _dataTable.Rows[_currentPosition];

    public DataTable Table => _dataTable;

    // ── Open overloads ───────────────────────────────────────────────────────

    public void Open(string sql, string? tableName = null)
    {
        if (_sharedConnection != null)
        {
            Open(_sharedConnection, sql, tableName);
        }
        else if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            Open(_defaultConnectionString, sql, tableName);
        }
        else
        {
            throw new InvalidOperationException(
                "No connection available. Call SetDefaultConnection or SetSharedConnection first, " +
                "or use an Open overload that accepts a connection.");
        }
    }

    public void Open(string connectionString, string sql, string? tableName = null)
    {
        _connection = new SqlConnection(connectionString);
        _ownsConnection = true;
        _tableName = tableName ?? "Table";
        _adapter = new SqlDataAdapter(sql, _connection);
        _commandBuilder = new SqlCommandBuilder(_adapter);
        _dataTable = new DataTable(_tableName);
        _adapter.Fill(_dataTable);

        _currentPosition = _dataTable.Rows.Count > 0 ? 0 : -1;
    }

    public void Open(SqlConnection connection, string sql, string? tableName = null)
    {
        _connection = connection;
        _ownsConnection = false;
        _tableName = tableName ?? "Table";
        _adapter = new SqlDataAdapter(sql, connection);
        _commandBuilder = new SqlCommandBuilder(_adapter);
        _dataTable = new DataTable(_tableName);
        _adapter.Fill(_dataTable);

        _currentPosition = _dataTable.Rows.Count > 0 ? 0 : -1;
    }

    public void Open(string sql, Dictionary<string, object> parameters, string? tableName = null)
    {
        if (_sharedConnection != null)
        {
            OpenWithParameters(_sharedConnection, sql, parameters, tableName);
        }
        else if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            _connection = new SqlConnection(_defaultConnectionString);
            _ownsConnection = true;
            OpenWithParameters(_connection, sql, parameters, tableName);
        }
        else
        {
            throw new InvalidOperationException("No connection available.");
        }
    }

    private void OpenWithParameters(SqlConnection connection, string sql, Dictionary<string, object> parameters, string? tableName = null)
    {
        _tableName = tableName ?? "Table";
        _adapter = new SqlDataAdapter();
        _adapter.SelectCommand = new SqlCommand(sql, connection);

        foreach (var param in parameters)
            _adapter.SelectCommand.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);

        _commandBuilder = new SqlCommandBuilder(_adapter);
        _dataTable = new DataTable(_tableName);
        _adapter.Fill(_dataTable);

        _currentPosition = _dataTable.Rows.Count > 0 ? 0 : -1;
    }

    // ── Execute helpers ──────────────────────────────────────────────────────

    public int Execute(string sql)
    {
        if (_sharedConnection != null)
            return Execute(_sharedConnection, sql);

        if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            using var connection = new SqlConnection(_defaultConnectionString);
            connection.Open();
            return Execute(connection, sql);
        }

        throw new InvalidOperationException("No connection available.");
    }

    public static int Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        bool needToClose = connection.State != ConnectionState.Open;
        if (needToClose) connection.Open();
        try   { return command.ExecuteNonQuery(); }
        finally { if (needToClose) connection.Close(); }
    }

    public int Execute(string sql, Dictionary<string, object> parameters)
    {
        if (_sharedConnection != null)
            return ExecuteWithParameters(_sharedConnection, sql, parameters);

        if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            using var connection = new SqlConnection(_defaultConnectionString);
            connection.Open();
            return ExecuteWithParameters(connection, sql, parameters);
        }

        throw new InvalidOperationException("No connection available.");
    }

    public static int Execute(SqlConnection connection, string sql, Dictionary<string, object> parameters)
        => ExecuteWithParameters(connection, sql, parameters);

    private static int ExecuteWithParameters(SqlConnection connection, string sql, Dictionary<string, object> parameters)
    {
        using var command = new SqlCommand(sql, connection);
        bool needToClose = connection.State != ConnectionState.Open;

        foreach (var param in parameters)
            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);

        if (needToClose) connection.Open();
        try   { return command.ExecuteNonQuery(); }
        finally { if (needToClose) connection.Close(); }
    }

    public T? ExecuteScalar<T>(string sql)
    {
        if (_sharedConnection != null)
            return ExecuteScalar<T>(_sharedConnection, sql);

        if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            using var connection = new SqlConnection(_defaultConnectionString);
            connection.Open();
            return ExecuteScalar<T>(connection, sql);
        }

        throw new InvalidOperationException("No connection available.");
    }

    public static T? ExecuteScalar<T>(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        bool needToClose = connection.State != ConnectionState.Open;
        if (needToClose) connection.Open();
        try
        {
            var result = command.ExecuteScalar();
            if (result == null || result == DBNull.Value) return default;
            return (T)Convert.ChangeType(result, typeof(T));
        }
        finally { if (needToClose) connection.Close(); }
    }

    public T? ExecuteScalar<T>(string sql, Dictionary<string, object> parameters)
    {
        if (_sharedConnection != null)
            return ExecuteScalarWithParameters<T>(_sharedConnection, sql, parameters);

        if (!string.IsNullOrEmpty(_defaultConnectionString))
        {
            using var connection = new SqlConnection(_defaultConnectionString);
            connection.Open();
            return ExecuteScalarWithParameters<T>(connection, sql, parameters);
        }

        throw new InvalidOperationException("No connection available.");
    }

    private static T? ExecuteScalarWithParameters<T>(SqlConnection connection, string sql, Dictionary<string, object> parameters)
    {
        using var command = new SqlCommand(sql, connection);
        bool needToClose = connection.State != ConnectionState.Open;

        foreach (var param in parameters)
            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);

        if (needToClose) connection.Open();
        try
        {
            var result = command.ExecuteScalar();
            if (result == null || result == DBNull.Value) return default;
            return (T)Convert.ChangeType(result, typeof(T));
        }
        finally { if (needToClose) connection.Close(); }
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    public void MoveNext()
    {
        if (_currentPosition < _dataTable.Rows.Count)
            _currentPosition++;
    }

    public void MovePrevious()
    {
        if (_currentPosition >= 0)
            _currentPosition--;
    }

    public void MoveFirst()
    {
        if (_dataTable.Rows.Count > 0)
            _currentPosition = 0;
    }

    public void MoveLast()
    {
        if (_dataTable.Rows.Count > 0)
            _currentPosition = _dataTable.Rows.Count - 1;
    }

    public void Move(int position)
    {
        if (position < 0 || position >= _dataTable.Rows.Count)
            throw new IndexOutOfRangeException("Position out of range");
        _currentPosition = position;
    }

    // ── Record editing ───────────────────────────────────────────────────────

    public void AddNew()
    {
        DataRow newRow = _dataTable.NewRow();
        _dataTable.Rows.Add(newRow);
        _currentPosition = _dataTable.Rows.Count - 1;
    }

    public void Edit()
    {
        if (EOF || BOF)
            throw new InvalidOperationException("No current record");

        // BeginEdit is only needed (and valid) on unchanged rows
        if (CurrentRow!.RowState == DataRowState.Unchanged)
            CurrentRow.BeginEdit();
    }

    public void Update()
    {
        if (EOF || BOF)
            throw new InvalidOperationException("No current record");

        if (CurrentRow!.RowState == DataRowState.Modified || CurrentRow.RowState == DataRowState.Added)
            CurrentRow.EndEdit();
    }

    public void CancelUpdate()
    {
        if (EOF || BOF)
            throw new InvalidOperationException("No current record");

        if (CurrentRow!.RowState == DataRowState.Modified)
            CurrentRow.CancelEdit();
    }

    public void Delete()
    {
        if (EOF || BOF)
            throw new InvalidOperationException("No current record");

        // Row is marked Deleted in the DataTable so UpdateBatch can push the DELETE to the DB.
        // The row remains physically in the collection until AcceptChanges; callers must
        // call MoveNext before accessing the next record (mirrors VB6 ADO behavior).
        _dataTable.Rows[_currentPosition].Delete();
    }

    // ── Batch persistence ────────────────────────────────────────────────────

    public void UpdateBatch()
    {
        if (_adapter == null)
            throw new InvalidOperationException("RecordSet was not opened with a connection");

        _adapter.Update(_dataTable);
        _dataTable.AcceptChanges();
    }

    public void Requery()
    {
        if (_adapter == null)
            throw new InvalidOperationException("RecordSet was not opened with a connection");

        _dataTable.Clear();
        _adapter.Fill(_dataTable);
        _currentPosition = _dataTable.Rows.Count > 0 ? 0 : -1;
    }

    public void Close()
    {
        _currentPosition = -1;
        _dataTable?.Clear();
    }

    // ── Field accessors ──────────────────────────────────────────────────────

    public object GetFieldValue(string fieldName) => this[fieldName];

    public void SetFieldValue(string fieldName, object value) => this[fieldName] = value;

    // ── Search ───────────────────────────────────────────────────────────────

    public bool Find(string filterExpression)
    {
        DataRow[] foundRows = _dataTable.Select(filterExpression);
        if (foundRows.Length > 0)
        {
            _currentPosition = _dataTable.Rows.IndexOf(foundRows[0]);
            return true;
        }
        return false;
    }

    // ── Clone / Copy ─────────────────────────────────────────────────────────

    /// <summary>Returns a new RecordSet with the same schema AND data (mirrors VB6 rs.Clone).</summary>
    public RecordSet Clone() => new RecordSet(_dataTable.Copy());

    /// <summary>Returns a new RecordSet with the same schema but no rows.</summary>
    public RecordSet CloneSchema() => new RecordSet(_dataTable.Clone());

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _commandBuilder?.Dispose();
                _adapter?.Dispose();
                // Only dispose the connection if we created it; never dispose a shared/injected connection
                if (_ownsConnection)
                    _connection?.Dispose();
                _dataTable?.Dispose();
            }
            _disposed = true;
        }
    }
}
