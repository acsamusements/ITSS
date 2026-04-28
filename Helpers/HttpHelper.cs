using Microsoft.Extensions.Logging;

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ITSS.Helpers;

/// <summary>
/// Configuration options for <see cref="HttpHelper"/>.
/// Pass an action to <see cref="HttpHelper.Initialize"/> to set application-wide defaults.
/// </summary>
public sealed class HttpHelperOptions
{
    /// <summary>Base URL prepended to all relative request URIs.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Default request timeout. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional logger. When <c>null</c>, all logging is silently skipped.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>Default headers added to every request (e.g. Authorization, API keys).</summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, all exceptions are re-thrown after logging.
    /// When <c>false</c> (default), exceptions return <c>default/null</c>.
    /// </summary>
    public bool ThrowOnError { get; set; } = false;

    /// <summary>
    /// Optional custom <see cref="JsonSerializerOptions"/> for serialization/deserialization.
    /// Defaults to camelCase with case-insensitive property matching.
    /// </summary>
    public JsonSerializerOptions? JsonOptions { get; set; }
}

/// <summary>
/// A reusable static HTTP helper wrapping <see cref="HttpClient"/>.
/// <para>
/// Call <see cref="Initialize"/> once at application startup to configure defaults.
/// All methods accept per-call overrides for headers and cancellation.
/// </para>
/// </summary>
public static class HttpHelper
{
    #region Initialization

    private static HttpHelperOptions _options = new();
    private static HttpClient _client = BuildClient(_options);

    /// <summary>
    /// Configures <see cref="HttpHelper"/> with application-wide defaults.
    /// Safe to call multiple times; the last call wins and rebuilds the internal <see cref="HttpClient"/>.
    /// </summary>
    public static void Initialize(Action<HttpHelperOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var opts = new HttpHelperOptions();
        configure(opts);
        _options = opts;
        _client  = BuildClient(opts);
    }

    private static HttpClient BuildClient(HttpHelperOptions opts)
    {
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        var client  = new HttpClient(handler) { Timeout = opts.Timeout };
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + '/');
        foreach (var (key, value) in opts.DefaultHeaders)
            client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        return client;
    }

    #endregion

    #region GET

    /// <summary>
    /// Sends a GET request and returns the response body as a string.
    /// Returns <c>null</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    public static async Task<string?> GetStringAsync(
        string url,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var request  = BuildRequest(HttpMethod.Get, url, null, headers);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"GET {url} failed");
            return null;
        }
    }

    /// <summary>
    /// Sends a GET request and deserializes the JSON response to <typeparamref name="T"/>.
    /// Returns <c>default</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    public static async Task<T?> GetAsync<T>(
        string url,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var request  = BuildRequest(HttpMethod.Get, url, null, headers);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"GET<{typeof(T).Name}> {url} failed");
            return default;
        }
    }

    #endregion

    #region POST

    /// <summary>
    /// Sends a POST request with a JSON body and deserializes the JSON response to <typeparamref name="TResponse"/>.
    /// Returns <c>default</c> on error when <paramref name="throwOnError"/> is <c>false</c>.
    /// </summary>
    public static async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var content  = JsonContent.Create(body, options: JsonOptions());
            using var request  = BuildRequest(HttpMethod.Post, url, content, headers);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"POST<{typeof(TRequest).Name}> {url} failed");
            return default;
        }
    }

    /// <summary>
    /// Sends a POST request with a JSON body and returns the raw response string.
    /// </summary>
    public static async Task<string?> PostAsync<TRequest>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var content  = JsonContent.Create(body, options: JsonOptions());
            using var request  = BuildRequest(HttpMethod.Post, url, content, headers);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"POST {url} failed");
            return null;
        }
    }

    /// <summary>
    /// Sends a POST request with a raw string body (e.g. form data or plain text).
    /// </summary>
    public static async Task<string?> PostRawAsync(
        string url,
        string body,
        string mediaType                    = "application/json",
        Dictionary<string, string>? headers = null,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content  = new StringContent(body, Encoding.UTF8, mediaType);
            using var request  = BuildRequest(HttpMethod.Post, url, content, headers);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"POST (raw) {url} failed");
            return null;
        }
    }

    #endregion

    #region PUT / PATCH / DELETE

    /// <summary>
    /// Sends a PUT request with a JSON body and deserializes the JSON response.
    /// </summary>
    public static async Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var content  = JsonContent.Create(body, options: JsonOptions());
            using var request  = BuildRequest(HttpMethod.Put, url, content, headers);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"PUT<{typeof(TRequest).Name}> {url} failed");
            return default;
        }
    }

    /// <summary>
    /// Sends a PATCH request with a JSON body and deserializes the JSON response.
    /// </summary>
    public static async Task<TResponse?> PatchAsync<TRequest, TResponse>(
        string url,
        TRequest body,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var content  = JsonContent.Create(body, options: JsonOptions());
            using var request  = BuildRequest(HttpMethod.Patch, url, content, headers);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<TResponse>(stream, JsonOptions(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"PATCH<{typeof(TRequest).Name}> {url} failed");
            return default;
        }
    }

    /// <summary>
    /// Sends a DELETE request. Returns <c>true</c> on success, <c>false</c> on error.
    /// </summary>
    public static async Task<bool> DeleteAsync(
        string url,
        Dictionary<string, string>? headers  = null,
        bool?             throwOnError        = null,
        CancellationToken cancellationToken   = default)
    {
        try
        {
            using var request  = BuildRequest(HttpMethod.Delete, url, null, headers);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"DELETE {url} failed");
            return false;
        }
    }

    #endregion

    #region Download

    /// <summary>
    /// Downloads a file from <paramref name="url"/> and saves it to <paramref name="destinationPath"/>.
    /// </summary>
    public static async Task<bool> DownloadFileAsync(
        string url,
        string destinationPath,
        bool?             throwOnError      = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            await using var fs     = File.Create(destinationPath);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!(throwOnError ?? _options.ThrowOnError))
        {
            Log(LogLevel.Error, ex, $"Download {url} → {destinationPath} failed");
            return false;
        }
    }

    #endregion

    #region Private Helpers

    private static HttpRequestMessage BuildRequest(
        HttpMethod method, string url, HttpContent? content,
        Dictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (headers is null) return request;
        foreach (var (key, value) in headers)
            request.Headers.TryAddWithoutValidation(key, value);
        return request;
    }

    private static JsonSerializerOptions JsonOptions() =>
        _options.JsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive  = true,
            PropertyNamingPolicy         = JsonNamingPolicy.CamelCase
        };

    private static void Log(LogLevel level, Exception? ex, string message)
    {
        if (_options.Logger is null) return;
        if (ex is not null)
            _options.Logger.Log(level, ex, "{Message}", message);
        else
            _options.Logger.Log(level, "{Message}", message);
    }

    #endregion
}
