using System.Text;
using System.Text.RegularExpressions;
using Shazim.Core.Logging;

namespace Shazim.Core.Services;

/// <summary>
/// Small file logger with structured-style templates ("{Name}") and size-based rotation.
/// Log file: %LOCALAPPDATA%\Shazim\logs\shazim.log
/// </summary>
public class FileLogger<T> : ILogger<T>
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly Regex Placeholder = new(@"\{([^{}:]+)(?::([^{}]+))?\}", RegexOptions.Compiled);
    private static readonly object Lock = new();

    public static string LogDirectory { get; set; } = Core.AppPaths.LogDirectory;

    private static string LogPath => Path.Combine(LogDirectory, "shazim.log");

    public void LogInformation(string message, params object[] args) => Write("INFO", null, message, args);
    public void LogWarning(string message, params object[] args) => Write("WARN", null, message, args);
    public void LogError(Exception? exception, string message, params object[] args) => Write("ERROR", exception, message, args);

    public void LogDebug(string message, params object[] args)
    {
#if DEBUG
        Write("DEBUG", null, message, args);
#endif
    }

    /// <summary>Replaces named or indexed placeholders with the arguments, in order.</summary>
    public static string Format(string template, object?[]? args)
    {
        if (args == null || args.Length == 0) return template;
        int index = 0;
        return Placeholder.Replace(template, m =>
        {
            if (index >= args.Length) return m.Value;
            var value = args[index++];
            var format = m.Groups[2].Success ? m.Groups[2].Value : null;
            return value is IFormattable f && format != null
                ? f.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
                : value?.ToString() ?? "null";
        });
    }

    private static void Write(string level, Exception? ex, string message, object[]? args)
    {
        try
        {
            var line = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] [")
                .Append(level).Append("] [").Append(typeof(T).Name).Append("] ")
                .Append(Format(message, args));
            if (ex != null) line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine().Append(ex.StackTrace);
            line.AppendLine();

            lock (Lock)
            {
                Directory.CreateDirectory(LogDirectory);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = LogPath + ".1";
                    File.Delete(old);
                    File.Move(LogPath, old);
                }
                File.AppendAllText(LogPath, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
