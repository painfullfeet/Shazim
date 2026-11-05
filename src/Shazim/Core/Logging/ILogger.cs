using System;

namespace Shazim.Core.Logging;

/// <summary>
/// Minimal logging interface. Serilog provides the implementation.
/// </summary>
public interface ILogger<T>
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(Exception? exception, string message, params object[] args);
    void LogDebug(string message, params object[] args);
}