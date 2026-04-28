using System.Text.RegularExpressions;

namespace ITSS;

/// <summary>
/// Extension methods for common string validation scenarios.
/// </summary>
public static partial class ValidationExtensions
{
    // ── Email ─────────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string is a valid e-mail address.</summary>
    public static bool IsValidEmail(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return EmailRegex().IsMatch(value);
    }

    // ── URL ───────────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string is a valid absolute HTTP or HTTPS URL.</summary>
    public static bool IsValidUrl(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    // ── Phone ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the string looks like a US phone number.
    /// Accepts formats: <c>1234567890</c>, <c>(123) 456-7890</c>, <c>123-456-7890</c>, <c>+11234567890</c>.
    /// </summary>
    public static bool IsValidPhone(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return PhoneRegex().IsMatch(value.Trim());
    }

    // ── ZIP ───────────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string is a valid US ZIP code (5-digit or ZIP+4).</summary>
    public static bool IsValidZip(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return ZipRegex().IsMatch(value.Trim());
    }

    // ── Character class checks ────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string is non-empty and contains only letters.</summary>
    public static bool ContainsOnlyLetters(this string? value)
        => !string.IsNullOrEmpty(value) && value.All(char.IsLetter);

    /// <summary>Returns <c>true</c> if the string is non-empty and contains only digits.</summary>
    public static bool ContainsOnlyDigits(this string? value)
        => !string.IsNullOrEmpty(value) && value.All(char.IsDigit);

    /// <summary>Returns <c>true</c> if the string is non-empty and contains only letters or digits.</summary>
    public static bool IsAlphanumeric(this string? value)
        => !string.IsNullOrEmpty(value) && value.All(char.IsLetterOrDigit);

    // ── Length ────────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string length is between <paramref name="min"/> and <paramref name="max"/> (inclusive).</summary>
    public static bool IsLengthBetween(this string? value, int min, int max)
    {
        if (value is null) return false;
        return value.Length >= min && value.Length <= max;
    }

    // ── GUID ──────────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string is a valid <see cref="Guid"/>.</summary>
    public static bool IsValidGuid(this string? value)
        => !string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out _);

    // ── Numeric range ─────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string parses as a decimal within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static bool IsInRange(this string? value, decimal min, decimal max)
        => decimal.TryParse(value, out var d) && d >= min && d <= max;

    // ── Password strength ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the string meets minimum password strength:
    /// at least <paramref name="minLength"/> chars, one uppercase, one lowercase, one digit, one special character.
    /// </summary>
    public static bool IsStrongPassword(this string? value, int minLength = 8)
    {
        if (string.IsNullOrEmpty(value) || value.Length < minLength) return false;
        return value.Any(char.IsUpper)
            && value.Any(char.IsLower)
            && value.Any(char.IsDigit)
            && value.Any(c => !char.IsLetterOrDigit(c));
    }

    // ── IP address ────────────────────────────────────────────────────────────

    /// <summary>Returns <c>true</c> if the string is a valid IPv4 or IPv6 address.</summary>
    public static bool IsValidIpAddress(this string? value)
        => !string.IsNullOrWhiteSpace(value)
            && System.Net.IPAddress.TryParse(value, out _);

    // ── Source-generated regexes ──────────────────────────────────────────────

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(\+1\s?)?((\([0-9]{3}\))|[0-9]{3})[\s\-]?[0-9]{3}[\s\-]?[0-9]{4}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^\d{5}(-\d{4})?$")]
    private static partial Regex ZipRegex();
}
