using System.Diagnostics;
using System.Text;

namespace ITSS.Helpers;

/// <summary>
/// Result of a process execution.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Text written to stdout.</param>
/// <param name="StandardError">Text written to stderr.</param>
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Returns <c>true</c> when <see cref="ExitCode"/> is 0.</summary>
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Helper for launching external processes and capturing their output.
/// </summary>
public static class ProcessHelper
{
    // ── Sync ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs an executable synchronously and returns its exit code, stdout, and stderr.
    /// </summary>
    /// <param name="exePath">Path to the executable.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="workingDirectory">Working directory. Defaults to the current directory.</param>
    /// <param name="timeoutMs">Milliseconds to wait before killing the process. Default: 30 000.</param>
    /// <param name="throwOnError">When <c>true</c>, throws <see cref="InvalidOperationException"/> on non-zero exit code.</param>
    public static ProcessResult Run(
        string exePath,
        string arguments        = "",
        string? workingDirectory = null,
        int timeoutMs           = 30_000,
        bool throwOnError       = false)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        using var process = Build(exePath, arguments, workingDirectory);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool finished = process.WaitForExit(timeoutMs);
        if (!finished)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{exePath}' did not complete within {timeoutMs}ms.");
        }

        var result = new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());

        if (throwOnError && !result.Success)
            throw new InvalidOperationException(
                $"Process '{exePath}' exited with code {result.ExitCode}.\n{result.StandardError}");

        return result;
    }

    // ── Async ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs an executable asynchronously and returns its exit code, stdout, and stderr.
    /// </summary>
    /// <param name="exePath">Path to the executable.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="workingDirectory">Working directory. Defaults to the current directory.</param>
    /// <param name="throwOnError">When <c>true</c>, throws <see cref="InvalidOperationException"/> on non-zero exit code.</param>
    /// <param name="cancellationToken">Token that kills the process on cancellation.</param>
    public static async Task<ProcessResult> RunAsync(
        string exePath,
        string arguments         = "",
        string? workingDirectory = null,
        bool throwOnError        = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        using var process = Build(exePath, arguments, workingDirectory);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var result = new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());

        if (throwOnError && !result.Success)
            throw new InvalidOperationException(
                $"Process '{exePath}' exited with code {result.ExitCode}.\n{result.StandardError}");

        return result;
    }

    // ── Shell helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a command through the platform shell (<c>cmd.exe /C</c> on Windows, <c>sh -c</c> on Unix).
    /// </summary>
    public static ProcessResult RunShell(string command,
        string? workingDirectory = null,
        int timeoutMs            = 30_000,
        bool throwOnError        = false)
    {
        var (shell, prefix) = GetShell();
        return Run(shell, $"{prefix}\"{command}\"", workingDirectory, timeoutMs, throwOnError);
    }

    /// <summary>
    /// Runs a command through the platform shell asynchronously.
    /// </summary>
    public static Task<ProcessResult> RunShellAsync(string command,
        string? workingDirectory    = null,
        bool throwOnError           = false,
        CancellationToken cancellationToken = default)
    {
        var (shell, prefix) = GetShell();
        return RunAsync(shell, $"{prefix}\"{command}\"", workingDirectory, throwOnError, cancellationToken);
    }

    // ── Open with default app ─────────────────────────────────────────────────

    /// <summary>
    /// Opens a file or URL with the system default application (Explorer, browser, etc.).
    /// </summary>
    public static void OpenWithDefault(string fileOrUrl)
    {
        ArgumentNullException.ThrowIfNull(fileOrUrl);
        Process.Start(new ProcessStartInfo(fileOrUrl) { UseShellExecute = true });
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private static Process Build(string exePath, string arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(exePath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = workingDirectory ?? Directory.GetCurrentDirectory()
        };
        return new Process { StartInfo = info, EnableRaisingEvents = true };
    }

    private static (string shell, string prefix) GetShell() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", "/C ")
            : ("/bin/sh", "-c ");
}
