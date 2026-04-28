using System.Security.Cryptography;
using System.Text;

namespace ITSS.Helpers;

/// <summary>
/// Helper for common cryptographic operations: hashing, AES encryption/decryption,
/// file hashing, and secure random token generation.
/// </summary>
public static class SecurityHelper
{
    // ── Hashing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the SHA-256 hash of <paramref name="input"/> as a lowercase hex string.
    /// </summary>
    public static string HashSha256(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the SHA-512 hash of <paramref name="input"/> as a lowercase hex string.
    /// </summary>
    public static string HashSha512(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the MD5 hash of <paramref name="input"/> as a lowercase hex string.
    /// <para><b>Note:</b> MD5 is not cryptographically secure. Use for checksums or non-security scenarios only.</para>
    /// </summary>
    public static string HashMd5(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Returns a HMAC-SHA256 of <paramref name="input"/> using <paramref name="key"/> as a lowercase hex string.
    /// </summary>
    public static string HmacSha256(string input, string key)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(key);
        var keyBytes   = Encoding.UTF8.GetBytes(key);
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hash       = HMACSHA256.HashData(keyBytes, inputBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── File Hashing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as a lowercase hex string.
    /// </summary>
    public static string ComputeFileHashSha256(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var stream = File.OpenRead(filePath);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the SHA-256 hash of a file asynchronously and returns it as a lowercase hex string.
    /// </summary>
    public static async Task<string> ComputeFileHashSha256Async(string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        await using var stream = File.OpenRead(filePath);
        var bytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── AES Encryption ────────────────────────────────────────────────────────

    /// <summary>
    /// Encrypts <paramref name="plainText"/> using AES-256-CBC.
    /// Returns a Base64 string containing the IV prepended to the cipher text.
    /// </summary>
    /// <param name="plainText">Text to encrypt.</param>
    /// <param name="key">Encryption key. Will be stretched to 32 bytes via SHA-256 if needed.</param>
    public static string EncryptAes(string plainText, string key)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentNullException.ThrowIfNull(key);

        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a Base64 AES-256-CBC cipher text produced by <see cref="EncryptAes"/>.
    /// Returns <c>null</c> if decryption fails.
    /// </summary>
    /// <param name="cipherText">Base64-encoded IV + cipher text.</param>
    /// <param name="key">Encryption key used during <see cref="EncryptAes"/>.</param>
    public static string? DecryptAes(string cipherText, string key)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        ArgumentNullException.ThrowIfNull(key);

        try
        {
            var fullBytes = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = DeriveKey(key);

            int ivLength   = aes.BlockSize / 8;
            var iv         = fullBytes[..ivLength];
            var cipher     = fullBytes[ivLength..];
            aes.IV         = iv;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch { return null; }
    }

    // ── Token / Password Generation ───────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically secure random token as a URL-safe Base64 string.
    /// </summary>
    /// <param name="byteLength">Number of random bytes before encoding. Default: 32 (256-bit).</param>
    public static string GenerateRandomToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// Generates a cryptographically secure random numeric PIN of <paramref name="length"/> digits.
    /// </summary>
    public static string GeneratePin(int length = 6)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(RandomNumberGenerator.GetInt32(0, 10));
        return sb.ToString();
    }

    /// <summary>
    /// Generates a secure random password containing upper, lower, digit, and special characters.
    /// </summary>
    public static string GeneratePassword(int length = 16)
    {
        const string upper   = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower   = "abcdefghijklmnopqrstuvwxyz";
        const string digits  = "0123456789";
        const string special = "!@#$%^&*()-_=+[]{}";
        const string all     = upper + lower + digits + special;

        if (length < 4) throw new ArgumentOutOfRangeException(nameof(length), "Minimum length is 4.");

        var password = new char[length];
        // Guarantee at least one of each category
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];

        for (int i = 4; i < length; i++)
            password[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        // Shuffle to avoid predictable positions
        RandomNumberGenerator.Shuffle(password.AsSpan());
        return new string(password);
    }

    // ── Constant-time comparison ──────────────────────────────────────────────

    /// <summary>
    /// Compares two strings in constant time to prevent timing attacks.
    /// </summary>
    public static bool SecureEquals(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>Derives a 32-byte AES key from a string key via SHA-256.</summary>
    private static byte[] DeriveKey(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}
