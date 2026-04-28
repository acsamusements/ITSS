using Microsoft.Win32;

using System.Runtime.Versioning;

namespace ITSS.Helpers;

/// <summary>
/// Helper for reading and writing Windows Registry values.
/// </summary>
[SupportedOSPlatform("windows")]
public static class RegistryHelper
{
    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a registry value cast to <typeparamref name="T"/>.
    /// Returns <paramref name="defaultValue"/> if the key or value does not exist.
    /// </summary>
    /// <param name="hive">Root hive (e.g. <see cref="RegistryHive.LocalMachine"/>).</param>
    /// <param name="keyPath">Sub-key path (e.g. <c>SOFTWARE\MyApp</c>).</param>
    /// <param name="valueName">Name of the value to read.</param>
    /// <param name="defaultValue">Value returned when the key or value is absent.</param>
    public static T? GetValue<T>(RegistryHive hive, string keyPath, string valueName, T? defaultValue = default)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        ArgumentNullException.ThrowIfNull(valueName);

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.OpenSubKey(keyPath);
        if (key is null) return defaultValue;

        var raw = key.GetValue(valueName);
        if (raw is null) return defaultValue;
        if (raw is T typed) return typed;

        try
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(raw, targetType);
        }
        catch { return defaultValue; }
    }

    /// <summary>
    /// Gets a registry string value.
    /// Returns <c>null</c> if the key or value does not exist.
    /// </summary>
    public static string? GetString(RegistryHive hive, string keyPath, string valueName)
        => GetValue<string>(hive, keyPath, valueName);

    /// <summary>
    /// Gets a registry DWORD value.
    /// Returns <c>0</c> if the key or value does not exist.
    /// </summary>
    public static int GetInt(RegistryHive hive, string keyPath, string valueName)
        => GetValue<int>(hive, keyPath, valueName);

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a registry value, creating the key path if it does not exist.
    /// </summary>
    /// <param name="hive">Root hive.</param>
    /// <param name="keyPath">Sub-key path.</param>
    /// <param name="valueName">Name of the value to write.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="valueKind">Registry value kind. Defaults to <see cref="RegistryValueKind.String"/> for strings, <see cref="RegistryValueKind.DWord"/> for integers.</param>
    public static void SetValue(RegistryHive hive, string keyPath, string valueName, object value,
        RegistryValueKind? valueKind = null)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        ArgumentNullException.ThrowIfNull(valueName);
        ArgumentNullException.ThrowIfNull(value);

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.CreateSubKey(keyPath, writable: true);

        var kind = valueKind ?? value switch
        {
            int  or long  => RegistryValueKind.DWord,
            byte[]        => RegistryValueKind.Binary,
            string[]      => RegistryValueKind.MultiString,
            _             => RegistryValueKind.String
        };

        key.SetValue(valueName, value, kind);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a registry value. Returns <c>true</c> if deleted, <c>false</c> if it did not exist.
    /// </summary>
    public static bool DeleteValue(RegistryHive hive, string keyPath, string valueName)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        ArgumentNullException.ThrowIfNull(valueName);

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.OpenSubKey(keyPath, writable: true);
        if (key is null) return false;

        try
        {
            key.DeleteValue(valueName, throwOnMissingValue: true);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Deletes a registry sub-key and all its values recursively.
    /// Returns <c>true</c> if deleted, <c>false</c> if the key did not exist.
    /// </summary>
    public static bool DeleteKey(RegistryHive hive, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(keyPath);

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        try
        {
            root.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: true);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    // ── Existence checks ──────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the registry key path exists.</summary>
    public static bool KeyExists(RegistryHive hive, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.OpenSubKey(keyPath);
        return key is not null;
    }

    /// <summary>Returns <c>true</c> if the named value exists within the key.</summary>
    public static bool ValueExists(RegistryHive hive, string keyPath, string valueName)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        ArgumentNullException.ThrowIfNull(valueName);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.OpenSubKey(keyPath);
        return key?.GetValue(valueName) is not null;
    }

    // ── Enumeration ───────────────────────────────────────────────────────────

    /// <summary>Returns all value names under a key, or an empty array if the key does not exist.</summary>
    public static string[] GetValueNames(RegistryHive hive, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.OpenSubKey(keyPath);
        return key?.GetValueNames() ?? [];
    }

    /// <summary>Returns all sub-key names under a key, or an empty array if the key does not exist.</summary>
    public static string[] GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(keyPath);
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var key  = root.OpenSubKey(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }
}
