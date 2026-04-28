using System.ComponentModel;
using System.Reflection;

namespace ITSS
{
    public static class EnumExtensions
    {
        // Returns the [Description] attribute value, or the enum name if none is set.
        // Usage: MyEnum.SomeValue.GetDescription()
        public static string GetDescription(this Enum value)
        {
            FieldInfo? field = value.GetType().GetField(value.ToString());
            DescriptionAttribute? attribute = field?.GetCustomAttribute<DescriptionAttribute>();
            return attribute?.Description ?? value.ToString();
        }

        // Parses a string to the given enum type, case-insensitive.
        // Returns defaultValue if parsing fails.
        // Usage: "Active".ToEnum<StatusEnum>()
        public static T ToEnum<T>(this string value, T defaultValue = default) where T : struct, Enum
            => Enum.TryParse(value, ignoreCase: true, out T result) ? result : defaultValue;

        // Returns all values of an enum type.
        // Usage: EnumExtensions.GetValues<StatusEnum>()
        public static IEnumerable<T> GetValues<T>() where T : struct, Enum
            => Enum.GetValues<T>();

        // Returns all (value, description) pairs for an enum — useful for populating dropdowns.
        public static IEnumerable<(T Value, string Description)> GetValuesWithDescriptions<T>() where T : struct, Enum
            => Enum.GetValues<T>().Select(v => (v, v.GetDescription()));

        public static bool IsDefined<T>(this T value) where T : struct, Enum
            => Enum.IsDefined(value);
    }
}
