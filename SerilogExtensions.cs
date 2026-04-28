using Serilog;

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ITSS
{
    public static class SerilogExtensions
    {
        // Uses CallerMemberName/CallerFilePath instead of StackTrace — resolved at compile time,
        // no runtime reflection overhead, and not affected by JIT inlining.
        [DebuggerStepThrough]
        private static string BuildLogMessage(object message, string memberName, string filePath)
        {
            string className = Path.GetFileNameWithoutExtension(filePath);
            return $"{className}.{memberName}\t{message.SerializeObject()}";
        }

        [DebuggerStepThrough]
        public static void ExtendedDebug(this ILogger? log, object message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "")
        {
            string logMessage = BuildLogMessage(message, memberName, filePath);
            log?.Debug(logMessage);
            if (Environment.UserInteractive)
                Console.WriteLine($"[DEBUG] {logMessage}");
        }

        [DebuggerStepThrough]
        public static void ExtendedError(this ILogger? log, object message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "")
        {
            string logMessage = BuildLogMessage(message, memberName, filePath);
            log?.Error(logMessage);
            if (Environment.UserInteractive)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {logMessage}");
                Console.ResetColor();
            }
        }

        [DebuggerStepThrough]
        public static void ExtendedWarning(this ILogger? log, object message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "")
        {
            string logMessage = BuildLogMessage(message, memberName, filePath);
            log?.Warning(logMessage);
            if (Environment.UserInteractive)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARNING] {logMessage}");
                Console.ResetColor();
            }
        }

        [DebuggerStepThrough]
        public static void ExtendedInformation(this ILogger? log, object message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "")
        {
            string logMessage = BuildLogMessage(message, memberName, filePath);
            log?.Information(logMessage);
            if (Environment.UserInteractive)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[INFO] {logMessage}");
                Console.ResetColor();
            }
        }

        [DebuggerStepThrough]
        public static void ExtendedFatal(this ILogger? log, object message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "")
        {
            string logMessage = BuildLogMessage(message, memberName, filePath);
            log?.Fatal(logMessage);
            if (Environment.UserInteractive)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"[FATAL] {logMessage}");
                Console.ResetColor();
            }
        }

        [DebuggerStepThrough]
        public static void ExtendedVerbose(this ILogger? log, object message, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            string logMessage = BuildLogMessage(message, memberName, filePath);
            log?.Verbose(logMessage);
            if (Environment.UserInteractive)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"[VERBOSE] {logMessage}");
                Console.ResetColor();
            }
        }
    }
}
