using Microsoft.Extensions.Logging;

namespace SourceGen.Ioc.Cli.Test;

internal sealed class FakeEnvironmentProvider : IEnvironmentProvider
{
    public required string CurrentDirectory { get; set; }
    public Dictionary<string, string?> EnvironmentVariables { get; } = [];

    public string? GetEnvironmentVariable(string variable) =>
        EnvironmentVariables.TryGetValue(variable, out var value) ? value : null;

    public string NewLine => "\n";
}

internal sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _logs = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Logs => _logs;

    public bool HasLoggedLevel(LogLevel level) => _logs.Any(l => l.Level == level);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _logs.Add((logLevel, formatter(state, exception)));
    }
}

internal static class TestPaths
{
    internal static readonly string Root = OperatingSystem.IsWindows() ? @"C:\TestDir" : "/TestDir";
    internal static string Combine(params string[] segments) => Path.Combine([Root, .. segments]);
}