using System.Data;
using System.Text;

namespace ITSS.Helpers;

/// <summary>
/// Helper for formatted, colorized console output.
/// Includes table rendering, progress bars, prompts, and spinners.
/// All methods are no-ops when <see cref="Environment.UserInteractive"/> is <c>false</c>.
/// </summary>
public static class ConsoleHelper
{
    // ── Colorized Write ───────────────────────────────────────────────────────

    /// <summary>Writes a message to stdout in the specified color, then resets.</summary>
    public static void WriteColored(string message, ConsoleColor color, bool newLine = true)
    {
        Console.ForegroundColor = color;
        if (newLine) Console.WriteLine(message);
        else         Console.Write(message);
        Console.ResetColor();
    }

    /// <summary>Writes a success message in green.</summary>
    public static void WriteSuccess(string message) => WriteColored($"[OK]  {message}", ConsoleColor.Green);

    /// <summary>Writes an error message in red.</summary>
    public static void WriteError(string message) => WriteColored($"[ERR] {message}", ConsoleColor.Red);

    /// <summary>Writes a warning message in yellow.</summary>
    public static void WriteWarning(string message) => WriteColored($"[WRN] {message}", ConsoleColor.Yellow);

    /// <summary>Writes an informational message in cyan.</summary>
    public static void WriteInfo(string message) => WriteColored($"[INF] {message}", ConsoleColor.Cyan);

    /// <summary>Writes a section header underlined with dashes.</summary>
    public static void WriteHeader(string title, ConsoleColor color = ConsoleColor.White)
    {
        WriteColored(title, color);
        WriteColored(new string('-', title.Length), color);
    }

    // ── DataTable rendering ───────────────────────────────────────────────────

    /// <summary>
    /// Writes a <see cref="DataTable"/> to the console as a formatted ASCII table.
    /// </summary>
    public static void WriteTable(DataTable table, ConsoleColor headerColor = ConsoleColor.Cyan)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Columns.Count == 0) return;

        // Calculate column widths
        var widths = table.Columns.Cast<DataColumn>()
            .Select((col, i) => Math.Max(
                col.ColumnName.Length,
                table.Rows.Count > 0
                    ? table.Rows.Cast<DataRow>().Max(r => r[i]?.ToString()?.Length ?? 0)
                    : 0))
            .ToArray();

        var separator = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";

        Console.WriteLine(separator);

        // Header
        Console.ForegroundColor = headerColor;
        var headerRow = "|" + string.Join("|", table.Columns.Cast<DataColumn>()
            .Select((col, i) => $" {col.ColumnName.PadRight(widths[i])} ")) + "|";
        Console.WriteLine(headerRow);
        Console.ResetColor();
        Console.WriteLine(separator);

        // Rows
        foreach (DataRow row in table.Rows)
        {
            var dataRow = "|" + string.Join("|", Enumerable.Range(0, table.Columns.Count)
                .Select(i => $" {(row[i]?.ToString() ?? string.Empty).PadRight(widths[i])} ")) + "|";
            Console.WriteLine(dataRow);
        }

        Console.WriteLine(separator);
        Console.WriteLine($"  {table.Rows.Count} row(s)");
    }

    // ── Progress Bar ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renders an inline progress bar on the current console line.
    /// Call repeatedly in a loop; the bar rewrites itself in place.
    /// </summary>
    /// <param name="current">Current progress value.</param>
    /// <param name="total">Total / maximum value.</param>
    /// <param name="width">Bar width in characters. Default: 40.</param>
    /// <param name="label">Optional label shown after the percentage.</param>
    public static void WriteProgress(int current, int total, int width = 40, string? label = null)
    {
        if (total <= 0) return;
        double pct   = Math.Clamp((double)current / total, 0, 1);
        int    filled = (int)(pct * width);
        var    bar    = new string('#', filled) + new string('-', width - filled);
        var    text   = $"\r[{bar}] {pct:P0}";
        if (!string.IsNullOrEmpty(label)) text += $"  {label}";
        Console.Write(text);
        if (current >= total) Console.WriteLine();
    }

    // ── Spinner ───────────────────────────────────────────────────────────────

    private static readonly char[] SpinnerFrames = ['|', '/', '-', '\\'];

    /// <summary>
    /// Runs <paramref name="action"/> on a background thread while displaying a console spinner.
    /// Blocks until the action completes.
    /// </summary>
    public static void WithSpinner(Action action, string message = "Working")
    {
        ArgumentNullException.ThrowIfNull(action);
        bool running = true;
        var thread   = new Thread(() =>
        {
            int i = 0;
            while (running)
            {
                Console.Write($"\r{SpinnerFrames[i++ % SpinnerFrames.Length]} {message}...");
                Thread.Sleep(100);
            }
        }) { IsBackground = true };

        thread.Start();
        try   { action(); }
        finally
        {
            running = false;
            thread.Join();
            Console.Write("\r" + new string(' ', message.Length + 10) + "\r");
        }
    }

    /// <summary>
    /// Runs <paramref name="asyncAction"/> while displaying a console spinner.
    /// </summary>
    public static async Task WithSpinnerAsync(Func<Task> asyncAction, string message = "Working",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asyncAction);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var spinTask = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                Console.Write($"\r{SpinnerFrames[i++ % SpinnerFrames.Length]} {message}...");
                try { await Task.Delay(100, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, cts.Token);

        try   { await asyncAction().ConfigureAwait(false); }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try { await spinTask.ConfigureAwait(false); } catch { /* cancelled */ }
            Console.Write("\r" + new string(' ', message.Length + 10) + "\r");
        }
    }

    // ── Prompts ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Prompts the user for input and returns the entered string.
    /// Returns <paramref name="defaultValue"/> if the user presses Enter without typing.
    /// </summary>
    public static string Prompt(string question, string defaultValue = "")
    {
        Console.Write(string.IsNullOrEmpty(defaultValue)
            ? $"{question}: "
            : $"{question} [{defaultValue}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
    }

    /// <summary>
    /// Prompts the user for a yes/no answer. Returns <c>true</c> for yes.
    /// </summary>
    public static bool PromptYesNo(string question, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n]" : "[y/N]";
        Console.Write($"{question} {hint}: ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        return input switch
        {
            "y" or "yes" => true,
            "n" or "no"  => false,
            _            => defaultYes
        };
    }

    /// <summary>
    /// Prompts the user for a password, masking input with <c>*</c>.
    /// </summary>
    public static string PromptPassword(string question = "Password")
    {
        Console.Write($"{question}: ");
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) { sb.Remove(sb.Length - 1, 1); Console.Write("\b \b"); }
            }
            else
            {
                sb.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Pauses execution until the user presses any key.
    /// </summary>
    public static void PauseForKey(string message = "Press any key to continue...")
    {
        Console.Write(message);
        Console.ReadKey(intercept: true);
        Console.WriteLine();
    }

    // ── Dividers ──────────────────────────────────────────────────────────────

    /// <summary>Writes a horizontal rule across the console width.</summary>
    public static void WriteRule(char character = '─', ConsoleColor color = ConsoleColor.DarkGray)
    {
        int width = Console.IsOutputRedirected ? 80 : Console.WindowWidth;
        WriteColored(new string(character, width), color);
    }
}
