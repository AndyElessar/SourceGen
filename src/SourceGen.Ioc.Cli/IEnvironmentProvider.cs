namespace SourceGen.Ioc.Cli;

/// <summary>
/// Abstraction for <see cref="Environment"/> static methods to enable testing.
/// </summary>
public interface IEnvironmentProvider
{
    /// <inheritdoc cref="Environment.CurrentDirectory" />
    string CurrentDirectory { get; }

    /// <inheritdoc cref="Environment.GetEnvironmentVariables"/>
    string? GetEnvironmentVariable(string variable);

    /// <inheritdoc cref="Environment.NewLine" />
    string NewLine { get; }
}

/// <summary>
/// Default implementation of <see cref="IEnvironmentProvider"/> using <see cref="Environment"/>.
/// </summary>
internal sealed class EnvironmentProvider : IEnvironmentProvider
{
    /// <inheritdoc />
    public string CurrentDirectory => Environment.CurrentDirectory;

    /// <inheritdoc />
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);

    /// <inheritdoc />
    public string NewLine => Environment.NewLine;
}
