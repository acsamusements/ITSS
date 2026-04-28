using ClosedXML.Excel;

using System.Data;

namespace ITSS.Helpers;

/// <summary>
/// Helper for reading and writing Excel (.xlsx) files using ClosedXML.
/// Cross-platform — no Office installation required.
/// </summary>
public static class ExcelHelper
{
    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exports a <see cref="DataTable"/> to an .xlsx file.
    /// The sheet is named after <see cref="DataTable.TableName"/> when set, otherwise <c>Sheet1</c>.
    /// </summary>
    /// <param name="filePath">Destination .xlsx path. Directory is created if absent.</param>
    /// <param name="table">Source data.</param>
    /// <param name="includeHeaders">When <c>true</c> (default), writes column names in the first row.</param>
    /// <param name="autoFit">When <c>true</c> (default), auto-fits column widths.</param>
    public static void DataTableToFile(string filePath, DataTable table,
        bool includeHeaders = true, bool autoFit = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(table);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        using var workbook  = new XLWorkbook();
        var sheetName = string.IsNullOrWhiteSpace(table.TableName) ? "Sheet1" : table.TableName;
        var sheet     = workbook.Worksheets.Add(sheetName);

        WriteDataTable(sheet, table, includeHeaders, autoFit);
        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Exports multiple <see cref="DataTable"/> objects to separate sheets in one .xlsx file.
    /// </summary>
    public static void DataSetToFile(string filePath, DataSet dataSet,
        bool includeHeaders = true, bool autoFit = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(dataSet);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");

        using var workbook = new XLWorkbook();
        int index = 1;
        foreach (DataTable table in dataSet.Tables)
        {
            var sheetName = string.IsNullOrWhiteSpace(table.TableName) ? $"Sheet{index}" : table.TableName;
            var sheet     = workbook.Worksheets.Add(sheetName);
            WriteDataTable(sheet, table, includeHeaders, autoFit);
            index++;
        }
        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Exports a list of <typeparamref name="T"/> to an .xlsx file.
    /// </summary>
    public static void ListToFile<T>(string filePath, IEnumerable<T> records,
        string sheetName = "Sheet1", bool autoFit = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(records);

        var table = ToDataTable(records, sheetName);
        DataTableToFile(filePath, table, includeHeaders: true, autoFit);
    }

    /// <summary>
    /// Returns a workbook as a byte array (useful for HTTP responses or in-memory handling).
    /// </summary>
    public static byte[] DataTableToBytes(DataTable table, bool includeHeaders = true, bool autoFit = true)
    {
        ArgumentNullException.ThrowIfNull(table);
        using var workbook  = new XLWorkbook();
        var sheetName = string.IsNullOrWhiteSpace(table.TableName) ? "Sheet1" : table.TableName;
        var sheet     = workbook.Worksheets.Add(sheetName);
        WriteDataTable(sheet, table, includeHeaders, autoFit);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the first worksheet of an .xlsx file into a <see cref="DataTable"/>.
    /// </summary>
    /// <param name="filePath">Path to the .xlsx file.</param>
    /// <param name="hasHeaders">When <c>true</c> (default), treats the first row as column headers.</param>
    public static DataTable FileToDataTable(string filePath, bool hasHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();
        return SheetToDataTable(sheet, hasHeaders);
    }

    /// <summary>
    /// Reads a named worksheet from an .xlsx file into a <see cref="DataTable"/>.
    /// </summary>
    public static DataTable FileToDataTable(string filePath, string sheetName, bool hasHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sheetName);
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheet(sheetName);
        return SheetToDataTable(sheet, hasHeaders);
    }

    /// <summary>
    /// Reads all worksheets from an .xlsx file into a <see cref="DataSet"/>.
    /// Each sheet becomes a <see cref="DataTable"/> named after the sheet.
    /// </summary>
    public static DataSet FileToDataSet(string filePath, bool hasHeaders = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var dataSet = new DataSet();
        using var workbook = new XLWorkbook(filePath);
        foreach (var sheet in workbook.Worksheets)
        {
            var table = SheetToDataTable(sheet, hasHeaders);
            table.TableName = sheet.Name;
            dataSet.Tables.Add(table);
        }
        return dataSet;
    }

    /// <summary>
    /// Reads the first worksheet of an .xlsx file into a list of <typeparamref name="T"/>.
    /// </summary>
    public static List<T> FileToList<T>(string filePath, bool hasHeaders = true)
        => FileToDataTable(filePath, hasHeaders).ToList<T>();

    /// <summary>
    /// Returns the names of all worksheets in an .xlsx file.
    /// </summary>
    public static IReadOnlyList<string> GetSheetNames(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var workbook = new XLWorkbook(filePath);
        return workbook.Worksheets.Select(w => w.Name).ToList();
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private static void WriteDataTable(IXLWorksheet sheet, DataTable table,
        bool includeHeaders, bool autoFit)
    {
        int startRow = 1;

        if (includeHeaders)
        {
            for (int col = 0; col < table.Columns.Count; col++)
            {
                var cell = sheet.Cell(1, col + 1);
                cell.Value = table.Columns[col].ColumnName;
                cell.Style.Font.Bold = true;
            }
            startRow = 2;
        }

        for (int row = 0; row < table.Rows.Count; row++)
        {
            for (int col = 0; col < table.Columns.Count; col++)
            {
                var value = table.Rows[row][col];
                var cell  = sheet.Cell(row + startRow, col + 1);
                if (value == DBNull.Value || value is null)
                    cell.Value = Blank.Value;
                else
                    cell.Value = XLCellValue.FromObject(value);
            }
        }

        if (autoFit && table.Columns.Count > 0)
            sheet.ColumnsUsed().AdjustToContents();
    }

    private static DataTable SheetToDataTable(IXLWorksheet sheet, bool hasHeaders)
    {
        var table = new DataTable(sheet.Name);
        var rows  = sheet.RowsUsed().ToList();
        if (rows.Count == 0) return table;

        int startRow = 0;

        if (hasHeaders)
        {
            foreach (var cell in rows[0].CellsUsed())
                table.Columns.Add(cell.GetString());
            startRow = 1;
        }
        else
        {
            int colCount = rows[0].CellsUsed().Count();
            for (int i = 0; i < colCount; i++)
                table.Columns.Add($"Column{i + 1}");
        }

        for (int r = startRow; r < rows.Count; r++)
        {
            var dataRow = table.NewRow();
            var cells   = rows[r].Cells(1, table.Columns.Count).ToList();
            for (int c = 0; c < table.Columns.Count; c++)
                dataRow[c] = c < cells.Count ? cells[c].GetString() : string.Empty;
            table.Rows.Add(dataRow);
        }

        return table;
    }

    private static DataTable ToDataTable<T>(IEnumerable<T> records, string tableName)
    {
        var table = new DataTable(tableName);
        var props = typeof(T).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var prop in props)
            table.Columns.Add(prop.Name, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);

        foreach (var record in records)
        {
            var row = table.NewRow();
            foreach (var prop in props)
                row[prop.Name] = prop.GetValue(record) ?? DBNull.Value;
            table.Rows.Add(row);
        }

        return table;
    }
}
