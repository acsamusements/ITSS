using System.Reflection;

namespace ITSS;

/// <summary>
/// Extension methods for common reflection operations:
/// property copy, get/set by name, type inspection, and deep clone.
/// </summary>
public static class ReflectionExtensions
{
    // ── Property Copy ─────────────────────────────────────────────────────────

    /// <summary>
    /// Copies all matching public instance properties from <paramref name="source"/> to <paramref name="target"/>.
    /// Properties are matched by name (case-insensitive) and must be readable on source and writable on target.
    /// </summary>
    public static void CopyPropertiesTo<TSource, TTarget>(this TSource source, TTarget target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var sourceProps = typeof(TSource).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                         .Where(p => p.CanRead)
                                         .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var targetProp in typeof(TTarget).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                   .Where(p => p.CanWrite))
        {
            if (!sourceProps.TryGetValue(targetProp.Name, out var sourceProp)) continue;

            var value      = sourceProp.GetValue(source);
            var targetType = Nullable.GetUnderlyingType(targetProp.PropertyType) ?? targetProp.PropertyType;
            var sourceType = Nullable.GetUnderlyingType(sourceProp.PropertyType) ?? sourceProp.PropertyType;

            if (value is null)
            {
                if (!targetProp.PropertyType.IsValueType || Nullable.GetUnderlyingType(targetProp.PropertyType) is not null)
                    targetProp.SetValue(target, null);
                continue;
            }

            try
            {
                targetProp.SetValue(target,
                    targetType.IsAssignableFrom(sourceType) ? value : Convert.ChangeType(value, targetType));
            }
            catch { /* skip incompatible types */ }
        }
    }

    /// <summary>
    /// Creates a new instance of <typeparamref name="TTarget"/> and copies all matching properties
    /// from <paramref name="source"/> into it.
    /// </summary>
    public static TTarget MapTo<TTarget>(this object source) where TTarget : new()
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = new TTarget();
        source.CopyPropertiesTo(target);
        return target;
    }

    // ── Get / Set by Name ─────────────────────────────────────────────────────

    /// <summary>
    /// Gets the value of a public instance property by name (case-insensitive).
    /// Returns <c>null</c> if the property does not exist.
    /// </summary>
    public static object? GetPropertyValue(this object obj, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(propertyName);
        return obj.GetType()
                  .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                  ?.GetValue(obj);
    }

    /// <summary>
    /// Gets the value of a public instance property by name cast to <typeparamref name="T"/>.
    /// Returns <c>default</c> if the property does not exist or the cast fails.
    /// </summary>
    public static T? GetPropertyValue<T>(this object obj, string propertyName)
    {
        var raw = obj.GetPropertyValue(propertyName);
        if (raw is null) return default;
        if (raw is T typed) return typed;
        try { return (T)Convert.ChangeType(raw, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)); }
        catch { return default; }
    }

    /// <summary>
    /// Sets the value of a public instance property by name (case-insensitive).
    /// Returns <c>true</c> if the property was found and set, <c>false</c> otherwise.
    /// </summary>
    public static bool SetPropertyValue(this object obj, string propertyName, object? value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(propertyName);
        var prop = obj.GetType()
                      .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null || !prop.CanWrite) return false;
        try
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            prop.SetValue(obj, value is null ? null : Convert.ChangeType(value, targetType));
            return true;
        }
        catch { return false; }
    }

    // ── Type Inspection ───────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the type has a public instance property with the given name (case-insensitive).</summary>
    public static bool HasProperty(this Type type, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(propertyName);
        return type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null;
    }

    /// <summary>Returns <c>true</c> if the type has a public instance property with the given name (case-insensitive).</summary>
    public static bool HasProperty(this object obj, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GetType().HasProperty(propertyName);
    }

    /// <summary>Returns <c>true</c> if <paramref name="type"/> implements <typeparamref name="TInterface"/>.</summary>
    public static bool Implements<TInterface>(this Type type)
        => typeof(TInterface).IsAssignableFrom(type);

    /// <summary>Returns <c>true</c> if <paramref name="type"/> is a nullable value type.</summary>
    public static bool IsNullable(this Type type)
        => Nullable.GetUnderlyingType(type) is not null;

    /// <summary>Returns <c>true</c> if <paramref name="type"/> is a numeric type (integral or floating-point).</summary>
    public static bool IsNumeric(this Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(byte)   || t == typeof(sbyte)  || t == typeof(short)  ||
               t == typeof(ushort) || t == typeof(int)    || t == typeof(uint)   ||
               t == typeof(long)   || t == typeof(ulong)  || t == typeof(float)  ||
               t == typeof(double) || t == typeof(decimal);
    }

    /// <summary>
    /// Returns all public instance properties and their current values as a dictionary.
    /// </summary>
    public static Dictionary<string, object?> ToDictionary(this object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GetType()
                  .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                  .Where(p => p.CanRead)
                  .ToDictionary(p => p.Name, p => p.GetValue(obj));
    }

    // ── Attribute helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the custom attribute of type <typeparamref name="TAttribute"/> on the given member, or <c>null</c>.
    /// </summary>
    public static TAttribute? GetAttribute<TAttribute>(this MemberInfo member) where TAttribute : Attribute
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.GetCustomAttribute<TAttribute>();
    }

    /// <summary>Returns <c>true</c> if the member has an attribute of type <typeparamref name="TAttribute"/>.</summary>
    public static bool HasAttribute<TAttribute>(this MemberInfo member) where TAttribute : Attribute
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.IsDefined(typeof(TAttribute), inherit: true);
    }
}
