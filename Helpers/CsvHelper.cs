using CsvHelper;
using CsvHelper.Configuration;

using System.Data;
using System.Globalization;
using System.Text;

namespace ITSS.Helpers;

/// <summary>
/// Helper for reading and writing CSV data.
/// Supports <see cref="DataTable"/>, strongly-typed lists, and raw strings/files.
/// </summary>
public static class CsvHelper
{
    // ── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes a list of <typeparamref name="T"/> to a CSV string.
    /// </summary>
    public static string ToCsvString<T>(IEnumerable<T> records, CsvConfiguration? config = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, config ?? DefaultConfig());
        csv.WriteRecords(records);
        return writer.ToString();
    }

    /// <summary>
    /// Writes a list of <typeparamref name="T"/> to a CSV file, creating or overwriting it.
    /// </summary>
    public static void WriteToFile<T>(string filePath, IEnumerable<T> records, CsvConfiguration? config = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(records);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        using var writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        using var csv = new CsvWriter(writer, config ?? DefaultConfig());
        csv.WriteRecords(records);
    }

    /// <summary>
    /// Writes a list of <typeparamref name="T"/> to a CSV file asynchronously.
    /// </summary>
    public static async Task WriteToFileAsync<T>(string filePath, IEnumerable<T> records,
        CsvConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(records);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        await using var writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        await using var csv = new CsvWriter(writer, config ?? DefaultConfig());
        await csv.WriteRecordsAsync(records, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a <see cref="DataTable"/> to a CSV string including a header row.
    /// </summary>
    public static string DataTableToCsvString(DataTable table, bool includeHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(table);
        var sb = new StringBuilder();

        if (includeHeaders)
        {
            var headers = table.Columns.Cast<DataColumn>().Select(c => EscapeCsvField(c.ColumnName));
            sb.AppendLine(string.Join(",", headers));
        }

        foreach (DataRow row in table.Rows)
        {
            var fields = row.ItemArray.Select(f => EscapeCsvField(f?.ToString() ?? string.Empty));
            sb.AppendLine(string.Join(",", fields));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes a <see cref="DataTable"/> to a CSV file.
    /// </summary>
    public static void DataTableToFile(string filePath, DataTable table, bool includeHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(table);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        File.WriteAllText(filePath, DataTableToCsvString(table, includeHeaders), Encoding.UTF8);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a CSV string into a list of <typeparamref name="T"/>.
    /// </summary>
    public static List<T> FromCsvString<T>(string csv, CsvConfiguration? config = null)
    {
        ArgumentNullException.ThrowIfNull(csv);
        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, config ?? DefaultConfig());
        return csvReader.GetRecords<T>().ToList();
    }

    /// <summary>
    /// Reads a CSV file into a list of <typeparamref name="T"/>.
    /// </summary>
    public static List<T> ReadFromFile<T>(string filePath, CsvConfiguration? config = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, config ?? DefaultConfig());
        return csv.GetRecords<T>().ToList();
    }

    /// <summary>
    /// Reads a CSV file into a list of <typeparamref name="T"/> asynchronously.
    /// </summary>
    public static async Task<List<T>> ReadFromFileAsync<T>(string filePath,
        CsvConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, config ?? DefaultConfig());
        var records = new List<T>();
        await foreach (var record in csv.GetRecordsAsync<T>(cancellationToken).ConfigureAwait(false))
            records.Add(record);
        return records;
    }

    /// <summary>
    /// Reads a CSV file into a <see cref="DataTable"/>.
    /// </summary>
    public static DataTable FileToDataTable(string filePath, CsvConfiguration? config = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        using var csv = new CsvReader(reader, config ?? DefaultConfig());
        using var dr = new CsvDataReader(csv);
        var dt = new DataTable();
        dt.Load(dr);
        return dt;
    }

    /// <summary>
    /// Reads a CSV string into a <see cref="DataTable"/>.
    /// </summary>
    public static DataTable StringToDataTable(string csvContent, CsvConfiguration? config = null)
    {
        ArgumentNullException.ThrowIfNull(csvContent);
        using var reader = new StringReader(csvContent);
        using var csv = new CsvReader(reader, config ?? DefaultConfig());
        using var dr = new CsvDataReader(csv);
        var dt = new DataTable();
        dt.Load(dr);
        return dt;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CsvConfiguration DefaultConfig() =>
        new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
