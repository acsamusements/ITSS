using System.Text;
using System.Text.Json;
using System.Xml.Serialization;

namespace ITSS.Helpers;

/// <summary>
/// Helper for common file I/O operations including retry-on-lock, JSON/XML convenience methods,
/// safe delete/move, and streaming reads.
/// </summary>
public static class FileHelper
{
    private const int DefaultRetryCount  = 3;
    private const int DefaultRetryDelayMs = 200;

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads all text from a file, retrying on <see cref="IOException"/> (e.g. file lock).
    /// </summary>
    public static string ReadAllText(string filePath, Encoding? encoding = null,
        int retries = DefaultRetryCount, int retryDelayMs = DefaultRetryDelayMs)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Retry(() => File.ReadAllText(filePath, encoding ?? Encoding.UTF8), retries, retryDelayMs);
    }

    /// <summary>
    /// Reads all text from a file asynchronously, retrying on <see cref="IOException"/>.
    /// </summary>
    public static Task<string> ReadAllTextAsync(string filePath, Encoding? encoding = null,
        int retries = DefaultRetryCount, int retryDelayMs = DefaultRetryDelayMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return RetryAsync(() => File.ReadAllTextAsync(filePath, encoding ?? Encoding.UTF8, cancellationToken),
            retries, retryDelayMs, cancellationToken);
    }

    /// <summary>
    /// Reads all bytes from a file, retrying on <see cref="IOException"/>.
    /// </summary>
    public static byte[] ReadAllBytes(string filePath,
        int retries = DefaultRetryCount, int retryDelayMs = DefaultRetryDelayMs)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return Retry(() => File.ReadAllBytes(filePath), retries, retryDelayMs);
    }

    /// <summary>
    /// Streams lines from a file without loading the entire file into memory.
    /// </summary>
    public static IEnumerable<string> ReadLines(string filePath, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return File.ReadLines(filePath, encoding ?? Encoding.UTF8);
    }

    /// <summary>
    /// Streams lines from a file asynchronously without loading the entire file into memory.
    /// </summary>
    public static IAsyncEnumerable<string> ReadLinesAsync(string filePath, Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        return File.ReadLinesAsync(filePath, encoding ?? Encoding.UTF8, cancellationToken);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes text to a file, creating the directory if needed, retrying on <see cref="IOException"/>.
    /// </summary>
    public static void WriteAllText(string filePath, string content, Encoding? encoding = null,
        int retries = DefaultRetryCount, int retryDelayMs = DefaultRetryDelayMs)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        EnsureDirectory(filePath);
        Retry(() => File.WriteAllText(filePath, content, encoding ?? Encoding.UTF8), retries, retryDelayMs);
    }

    /// <summary>
    /// Writes text to a file asynchronously, creating the directory if needed.
    /// </summary>
    public static Task WriteAllTextAsync(string filePath, string content, Encoding? encoding = null,
        int retries = DefaultRetryCount, int retryDelayMs = DefaultRetryDelayMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        EnsureDirectory(filePath);
        return RetryAsync(() => File.WriteAllTextAsync(filePath, content, encoding ?? Encoding.UTF8, cancellationToken),
            retries, retryDelayMs, cancellationToken);
    }

    /// <summary>
    /// Writes bytes to a file, creating the directory if needed.
    /// </summary>
    public static void WriteAllBytes(string filePath, byte[] bytes,
        int retries = DefaultRetryCount, int retryDelayMs = DefaultRetryDelayMs)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        EnsureDirectory(filePath);
        Retry(() => File.WriteAllBytes(filePath, bytes), retries, retryDelayMs);
    }

    // ── JSON ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserializes a JSON file into <typeparamref name="T"/>.
    /// Returns <c>default</c> if the file does not exist or deserialization fails.
    /// </summary>
    public static T? ReadJson<T>(string filePath, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) return default;
        var json = ReadAllText(filePath);
        return JsonSerializer.Deserialize<T>(json, options);
    }

    /// <summary>
    /// Deserializes a JSON file into <typeparamref name="T"/> asynchronously.
    /// </summary>
    public static async Task<T?> ReadJsonAsync<T>(string filePath, JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) return default;
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes <paramref name="obj"/> to a JSON file.
    /// </summary>
    public static void WriteJson<T>(string filePath, T obj, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        EnsureDirectory(filePath);
        var json = JsonSerializer.Serialize(obj, options ?? PrettyJson());
        WriteAllText(filePath, json);
    }

    /// <summary>
    /// Serializes <paramref name="obj"/> to a JSON file asynchronously.
    /// </summary>
    public static async Task WriteJsonAsync<T>(string filePath, T obj, JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        EnsureDirectory(filePath);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, obj, options ?? PrettyJson(), cancellationToken).ConfigureAwait(false);
    }

    // ── XML ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserializes an XML file into <typeparamref name="T"/>.
    /// Returns <c>default</c> if the file does not exist.
    /// </summary>
    public static T? ReadXml<T>(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) return default;
        var serializer = new XmlSerializer(typeof(T));
        using var reader = new StreamReader(filePath, Encoding.UTF8);
        return (T?)serializer.Deserialize(reader);
    }

    /// <summary>
    /// Serializes <paramref name="obj"/> to an XML file.
    /// </summary>
    public static void WriteXml<T>(string filePath, T obj)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(obj);
        EnsureDirectory(filePath);
        var serializer = new XmlSerializer(typeof(T));
        using var writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        serializer.Serialize(writer, obj);
    }

    // ── File Operations ───────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a file if it exists, suppressing errors silently.
    /// </summary>
    public static bool SafeDelete(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            File.Delete(filePath);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Moves a file, overwriting the destination if it exists.
    /// Creates the destination directory if needed.
    /// </summary>
    public static void SafeMove(string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        EnsureDirectory(destination);
        File.Move(source, destination, overwrite: true);
    }

    /// <summary>
    /// Copies a file, overwriting the destination if it exists.
    /// Creates the destination directory if needed.
    /// </summary>
    public static void SafeCopy(string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        EnsureDirectory(destination);
        File.Copy(source, destination, overwrite: true);
    }

    /// <summary>
    /// Creates all directories in the path of <paramref name="filePath"/> if they do not exist.
    /// </summary>
    public static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Returns a unique temp file path with an optional extension. Does not create the file.
    /// </summary>
    public static string GetTempFilePath(string extension = ".tmp")
        => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");

    /// <summary>
    /// Returns the size of a file in bytes, or <c>-1</c> if the file does not exist.
    /// </summary>
    public static long GetFileSize(string filePath)
        => File.Exists(filePath) ? new FileInfo(filePath).Length : -1;

    // ── Private Retry Helpers ─────────────────────────────────────────────────

    private static T Retry<T>(Func<T> action, int retries, int delayMs)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try { return action(); }
            catch (IOException) when (attempt < retries)
            {
                Thread.Sleep(delayMs);
            }
        }
        return action(); // final attempt — let exception propagate
    }

    private static void Retry(Action action, int retries, int delayMs)
        => Retry<object?>(() => { action(); return null; }, retries, delayMs);

    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int retries, int delayMs,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try { return await action().ConfigureAwait(false); }
            catch (IOException) when (attempt < retries)
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        return await action().ConfigureAwait(false);
    }

    private static async Task RetryAsync(Func<Task> action, int retries, int delayMs,
        CancellationToken cancellationToken)
        => await RetryAsync<object?>(async () => { await action().ConfigureAwait(false); return null; },
            retries, delayMs, cancellationToken).ConfigureAwait(false);

    private static JsonSerializerOptions PrettyJson() => new() { WriteIndented = true };
}
