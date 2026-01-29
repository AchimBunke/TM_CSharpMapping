using System.Runtime.CompilerServices;

namespace TM_GenericMapping.Common;


[Flags]
public enum LogTarget
{
    None = 0,
    Console = 1,
    Debug = 2
}

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}

public static class Logger
{
    public static LogTarget Target { get; set; } = LogTarget.Console;
    public static bool Enabled { get; set; } = true;
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;
    public static bool UseColors { get; set; } = true;
    public static bool ShowTimestamp { get; set; } = false;
    public static bool ShowCaller { get; set; } = false;

    private static readonly Dictionary<LogLevel, ConsoleColor> LevelColors = new()
    {
        { LogLevel.Trace, ConsoleColor.Gray },
        { LogLevel.Debug, ConsoleColor.Cyan },
        { LogLevel.Info, ConsoleColor.White },
        { LogLevel.Warning, ConsoleColor.Yellow },
        { LogLevel.Error, ConsoleColor.Red }
    };

    public static void Trace(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => Log(LogLevel.Trace, message, memberName, filePath, lineNumber);

    public static void Debug(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => Log(LogLevel.Debug, message, memberName, filePath, lineNumber);

    public static void Info(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => Log(LogLevel.Info, message, memberName, filePath, lineNumber);

    public static void Warn(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => Log(LogLevel.Warning, message, memberName, filePath, lineNumber);

    public static void Error(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
        => Log(LogLevel.Error, message, memberName, filePath, lineNumber);

    public static void Trace(string format, params object[] args) => Trace(string.Format(format, args));
    public static void Debug(string format, params object[] args) => Debug(string.Format(format, args));
    public static void Info(string format, params object[] args) => Info(string.Format(format, args));
    public static void Warn(string format, params object[] args) => Warn(string.Format(format, args));
    public static void Error(string format, params object[] args) => Error(string.Format(format, args));

    private static void Log(LogLevel level, string message, string memberName, string filePath, int lineNumber)
    {
        if (!Enabled || level < MinLevel) return;

        string prefix = "";

        if (ShowTimestamp)
            prefix += $"[{DateTime.Now:HH:mm:ss}] ";

        prefix += $"[{level}] ";

        if (ShowCaller)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            prefix += $"[{fileName}.{memberName}] ";
        }

        string fullMessage = prefix + message;

        if (Target.HasFlag(LogTarget.Console))
        {
            if (UseColors)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = LevelColors[level];
                Console.WriteLine(fullMessage);
                Console.ForegroundColor = oldColor;
            }
            else
            {
                Console.WriteLine(fullMessage);
            }
        }

        if (Target.HasFlag(LogTarget.Debug))
        {
            System.Diagnostics.Debug.WriteLine(fullMessage);
        }
    }
}

