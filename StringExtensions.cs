using System.Globalization;
using System.Text.Json;
using System.Xml.Serialization;

namespace ITSS
{
    public static class StringExtensions
    {

        public static char ToChar(this object text) => Convert.ToChar(text);

        //Truncates string to desired length
        public static string Truncate(this string? tmp, int length)
        {
            if (string.IsNullOrEmpty(tmp)) return string.Empty;
            return tmp.Length <= length ? tmp : tmp[..length];
        }
        //Truncates object to string to desired length
        public static string Truncate(this object? tmpObject, int length)
        {
            string tmp = tmpObject?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(tmp)) return string.Empty;
            return tmp.Length <= length ? tmp : tmp[..length];
        }

        // Returns the leftmost N characters
        public static string Left(this string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= length ? value : value[..length];
        }

        // Returns the rightmost N characters
        public static string Right(this string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= length ? value : value[^length..];
        }

        // Checks if object exist in list
        public static bool Contains<T>(this T tmp, IEnumerable<T> compareTo)
            => compareTo.Contains(tmp);

        // Case-insensitive: checks if this string contains any of the given strings
        public static bool Contains(this string tmp, IEnumerable<string> compareTo)
            => compareTo.Any(s => tmp.Contains(s, StringComparison.InvariantCultureIgnoreCase));

        public static string Pad(this string tmp, int sp, bool flag)
        {
            string tmpStr = tmp.Length > sp ? tmp[^(sp - 1)..] : tmp;
            string spaces = new(' ', sp - tmpStr.Length);
            return flag ? tmpStr + spaces : spaces + tmpStr;
        }

        public static string GetLetters(this string s)
            => new string(s.Where(char.IsLetter).ToArray());

        public static bool IsNullOrEmpty(this string? value)
            => string.IsNullOrEmpty(value);

        public static bool IsNullOrWhiteSpace(this string? value)
            => string.IsNullOrWhiteSpace(value);

        public static string RemoveWhitespace(this string value)
            => new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

        public static string ToTitleCase(this string value)
            => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLower());

        public static string Repeat(this string value, int count)
            => string.Concat(Enumerable.Repeat(value, count));

        public static string SerializeObject<T>(this T obj, JsonSerializerOptions? options = null)
            => JsonSerializer.Serialize(obj, options);

        public static T? DeserializeObject<T>(this string json, JsonSerializerOptions? options = null)
            => JsonSerializer.Deserialize<T>(json, options);

        public static T? DeserializeXml<T>(this string input)
        {
            var serializer = new XmlSerializer(typeof(T));
            var result = serializer.Deserialize(new StringReader(input));
            return result is null ? default : (T)result;
        }

        public static string SerializeXml<T>(this T obj)
        {
            if (obj is null) return string.Empty;
            XmlSerializer xmlSerializer = new(obj.GetType());
            using StringWriter textWriter = new();
            xmlSerializer.Serialize(textWriter, obj);
            return textWriter.ToString();
        }
    }
}
