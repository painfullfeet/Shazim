using System;
using Shazim.Core.Logging;

namespace Shazim.Core.Services;

/// <summary>
/// No-op logger used in tests or when Serilog is not configured.
/// </summary>
public class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();

    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
    public void LogError(Exception? exception, string message, params object[] args) { }
    public void LogDebug(string message, params object[] args) { }
}