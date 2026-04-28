namespace ITSS
{
    public static class ConversionExtensions
    {
        public static double ToDouble(this object? value)
        {
            if (value is null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString()))
                return 0;
            return Convert.ToDouble(value);
        }

        public static decimal ToDecimal(this object? value)
        {
            if (value is null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString()))
                return 0;
            return Convert.ToDecimal(value);
        }

        public static int ToInt32(this object? value)
        {
            if (value is null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString()))
                return 0;
            return Convert.ToInt32(value);
        }

        public static long ToInt64(this object? value)
        {
            if (value is null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString()))
                return 0;
            return Convert.ToInt64(value);
        }

        public static short ToInt16(this object? value)
        {
            if (value is null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString()))
                return 0;
            return Convert.ToInt16(value);
        }

        public static bool ToBoolean(this object? value)
        {
            if (value is null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString()))
                return false;

            if (value is string str)
            {
                if (str.Equals("false", StringComparison.OrdinalIgnoreCase) || str == "0" || str.Equals("no", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (str.Equals("true", StringComparison.OrdinalIgnoreCase) || str == "1" || str.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return Convert.ToBoolean(value);
        }

        public static DateTime ToDateTime(this object? text)
        {
            if (DateTime.TryParse(text?.ToString(), out DateTime result))
                return result;
            try { return Convert.ToDateTime(text); }
            catch { return default; }
        }
    }
}
