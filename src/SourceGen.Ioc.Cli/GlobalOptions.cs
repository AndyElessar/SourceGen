namespace SourceGen.Ioc.Cli;
/// <summary>
/// Global Options for CLI.
/// </summary>
/// <param name="DryRun">Dry run.</param>
/// <param name="Verbose">Detailed logging message.</param>
/// <param name="LoggingFile">Logging file path.</param>
public sealed record GlobalOptions(bool DryRun, bool Verbose, string LoggingFile);