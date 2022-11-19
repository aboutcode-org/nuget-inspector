using System.Diagnostics;
using NuGet.Common;

namespace NugetInspector;

/// <summary>
/// A Nuget.Common.ILogger implementation for use in Nuget calls.
/// </summary>
public class NugetLogger : ILogger
{
    public void LogDebug(string data)
    {
        Trace.WriteLine($"DEBUG: {data}");
    }

    public void LogVerbose(string data)
    {
        Trace.WriteLine($"VERBOSE: {data}");
    }

    public void LogInformation(string data)
    {
        Trace.WriteLine($"INFORMATION: {data}");
    }

    public void LogMinimal(string data)
    {
        Trace.WriteLine($"MINIMAL: {data}");
    }

    public void LogWarning(string data)
    {
        Trace.WriteLine($"WARNING: {data}");
    }

    public void LogError(string data)
    {
        Trace.WriteLine($"ERROR: {data}");
    }

    public void LogInformationSummary(string data)
    {
        Trace.WriteLine($"INFORMATION SUMMARY: {data}");
    }

    public void Log(LogLevel level, string data)
    {
        Trace.WriteLine($"{level.ToString()}: {data}");
    }

    public Task LogAsync(LogLevel level, string data)
    {
        return Task.Run(() => Trace.WriteLine($"{level.ToString()}: {data}"));
    }

    public void Log(ILogMessage message)
    {
        Trace.WriteLine($"{message.Level.ToString()}: {message.Message}");
    }

    public Task LogAsync(ILogMessage message)
    {
        return Task.Run(() => Trace.WriteLine($"{message.Level.ToString()}: {message.Message}"));
    }

    public void LogSummary(string data)
    {
        Trace.WriteLine($"SUMMARY: {data}");
    }

    public void LogErrorSummary(string data)
    {
        Trace.WriteLine($"ERROR SUMMARY: {data}");
    }
}