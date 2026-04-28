using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ITSS
{
    public static class DataExtensions
    {
        public static T To<T>(this DataRow row)
        {
            Type type = typeof(T);
            T item = (T)Activator.CreateInstance(type)!;

            foreach (DataColumn column in row.Table.Columns)
            {
                if (row[column] != DBNull.Value)
                {
                    PropertyInfo? info = type.GetProperty(column.ColumnName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (info != null && info.CanWrite)
                    {
                        object? value = row[column];

                        // Skip special timestamp columns
                        if (column.ColumnName.Equals("upsize_ts", StringComparison.OrdinalIgnoreCase) || column.ColumnName.Equals("SSMA_TimeStamp", StringComparison.OrdinalIgnoreCase))
                            continue;
                        
                        try
                        {
                            // Handle nullable types
                            Type targetType = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;

                            if (value is string strValue)
                            {
                                value = strValue.Trim();
                            }

                            // Convert.ChangeType cannot handle some conversions (e.g., Guid, enums)
                            if (targetType.IsEnum)
                            {
                                value = Enum.Parse(targetType, value.ToString()!);
                            }
                            else if (targetType == typeof(Guid))
                            {
                                value = Guid.Parse(value.ToString()!);
                            }
                            else if (targetType == typeof(bool) && value is string s)
                            {
                                value = s.Equals("1") || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (targetType == typeof(DateTime) && value is string dateStr)
                            {
                                if (DateTime.TryParse(dateStr, out DateTime parsed))
                                    value = parsed;
                            }
                            else if (value != null && value.GetType() != targetType)
                            {
                                value = Convert.ChangeType(value, targetType);
                            }

                            info.SetValue(item, value);
                        }
                        catch
                        {
                            // Optionally log or handle conversion errors here
                            // logger?.Warning($"Failed to set property {info.Name} from column {column.ColumnName}");
                        }
                    }
                }
            }
            return item;
        }
        public static List<T> ToList<T>(this DataTable table)
        {
            var data = new List<T>();
            if (table is null || table.Rows.Count == 0) return data;

            var type = typeof(T);
            var isScalar = type.IsPrimitive
                        || type == typeof(string)
                        || type == typeof(decimal)
                        || type == typeof(Guid)
                        || type == typeof(DateTime);

            foreach (DataRow row in table.Rows)
            {
                // ── Scalar projection (single-column result sets) ─────────────
                if (isScalar)
                {
                    var raw = row[0] == DBNull.Value ? null : row[0];
                    if (raw is null) { data.Add(default!); continue; }
                    if (raw is T direct) { data.Add(direct); continue; }
                    try { data.Add((T)Convert.ChangeType(raw, type)); } catch { /* skip unresolvable */ }
                    continue;
                }

                // ── Object mapping ────────────────────────────────────────────
                T item = type.IsValueType
                    ? default!
                    : (T)RuntimeHelpers.GetUninitializedObject(type);

                foreach (DataColumn column in table.Columns)
                {
                    if (row[column] == DBNull.Value) continue;

                    if (column.ColumnName.Equals("upsize_ts", StringComparison.OrdinalIgnoreCase) ||
                        column.ColumnName.Equals("SSMA_TimeStamp", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var info = type.GetProperty(column.ColumnName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (info is null || !info.CanWrite) continue;

                    var columnValue = row[column];
                    var targetType = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;

                    try
                    {
                        if (columnValue is string str) columnValue = str.Trim();

                        object? converted = targetType switch
                        {
                            _ when targetType == typeof(DateTime)
                                => columnValue is string ds
                                    ? DateTime.TryParse(ds, out var dt) ? dt : null
                                    : Convert.ToDateTime(columnValue),
                            _ when targetType == typeof(bool)
                                => columnValue is string bs
                                    ? bs == "1" || bs.Equals("true", StringComparison.OrdinalIgnoreCase)
                                    : Convert.ToBoolean(columnValue),
                            _ when targetType == typeof(Guid) => Guid.Parse(columnValue.ToString()!),
                            _ when targetType.IsEnum => Enum.Parse(targetType, columnValue.ToString()!),
                            _ when targetType == typeof(decimal) => Convert.ToDecimal(columnValue),
                            _ when targetType == typeof(double) => Convert.ToDouble(columnValue),
                            _ when targetType == typeof(float) => Convert.ToSingle(columnValue),
                            _ when targetType == typeof(int) => Convert.ToInt32(columnValue),
                            _ when targetType == typeof(long) => Convert.ToInt64(columnValue),
                            _ when targetType == typeof(short) => Convert.ToInt16(columnValue),
                            _ when targetType == typeof(byte) => Convert.ToByte(columnValue),
                            _ when targetType == typeof(string) => Convert.ToString(columnValue),
                            _ => columnValue.GetType() != targetType
                                    ? Convert.ChangeType(columnValue, targetType)
                                    : columnValue
                        };

                        info.SetValue(item, converted);
                    }
                    catch
                    {
                        // Partial mapping is acceptable — skip columns that cannot be converted
                    }
                }

                data.Add(item);
            }

            return data;
        }
        public static bool CompareColumns(this DataTable table, string[] columns)
        {
            if (table is null || columns is null || columns.Length == 0) return false;
            var existing = table.Columns
                .Cast<DataColumn>()
                .Select(c => c.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return columns.All(existing.Contains);
        }
    }
}
