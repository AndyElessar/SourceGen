using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SourceGen.Ioc.Cli;

#pragma warning disable CA1822
public sealed class AddAttributeCommands(
    ILogger<AddAttributeCommands> logger,
    GlobalOptions globalOptions,
    IFileSystem fileSystem,
    IEnvironmentProvider environmentProvider)
{
    private readonly ILogger<AddAttributeCommands> logger = logger;
    private readonly GlobalOptions globalOptions = globalOptions;
    private readonly IFileSystem fileSystem = fileSystem;
    private readonly IEnvironmentProvider environmentProvider = environmentProvider;

    private const string baseClassRegex_1 = @"(public|internal)\s+(?!static\s+).*class\s+(";
    private const string baseClassRegex_2 = @")(?=\s|:|$)";

    /// <summary>
    /// Add attribute.
    /// </summary>
    /// <param name="target">-t, Target directory or file, default is current directory.</param>
    /// <param name="filePattern">-f, File pattern to filter files.</param>
    /// <param name="searchSubDirectories">-s, Whether to search sub directories.</param>
    /// <param name="classNameRegex">-cn, Regex pattern to match class names.
    ///                                   Full regex will be: "(public|internal)\s+(?!static\s+).*class\s+(classNameRegex)(?=\s|:|$)"</param>
    /// <param name="fullRegex">Full regex pattern to match file content.</param>
    /// <param name="attributeName">Name of the attribute to add, default is IocRegister</param>
    /// <param name="maxApply">-m, How many matches should apply, 0 means unlimited.</param>
    /// <returns></returns>
    [Command("")]
    public async Task AddAttribute(
        string? target = null,
        string filePattern = "*.cs",
        bool searchSubDirectories = false,
        string? classNameRegex = null,
        string? fullRegex = null,
        string attributeName = "IocRegister",
        int maxApply = 0,
        CancellationToken ct = default)
    {
        if(string.IsNullOrWhiteSpace(filePattern))
        {
            logger.ZLogError($"-f|--file-pattern can not be empty!");
            return;
        }

        if(string.IsNullOrWhiteSpace(attributeName))
        {
            logger.ZLogError($"--attribute-name can not be empty!");
            return;
        }
        string attributeLine = $"[{attributeName}]{environmentProvider.NewLine}";

        if(string.IsNullOrWhiteSpace(classNameRegex) && string.IsNullOrWhiteSpace(fullRegex))
        {
            logger.ZLogError($"-cn|--class-name-regex and --full-regex can not all be empty! You must specify at least one.");
            return;
        }
        Regex regex;
        if(!string.IsNullOrWhiteSpace(fullRegex))
        {
            regex = CreateFullMatchRegex(fullRegex);
        }
        else if(!string.IsNullOrWhiteSpace(classNameRegex))
        {
            regex = CreateClassMatchRegex(classNameRegex);
        }
        else
        {
            throw new UnreachableException();
        }

        int fileCount = 0;
        int appliedCount = 0;
        bool targetIsEmpty = string.IsNullOrWhiteSpace(target);
        if(targetIsEmpty || fileSystem.Directory.Exists(target))
        {
            var files = fileSystem.Directory.EnumerateFiles(
                targetIsEmpty ? environmentProvider.CurrentDirectory : target!,
                filePattern,
                searchSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            foreach(var file in files)
            {
                var result = await ProcessFile(file, regex, maxApply, fileCount, appliedCount, attributeLine, ct);
                fileCount = result.FileCount;
                appliedCount = result.AppliedCount;
                if(maxApply > 0 && appliedCount >= maxApply)
                {
                    logger.ZLogInformation($"Reached the --maxApply:{maxApply} limit. Stopping further processing.");
                    break;
                }
            }
        }
        else if(fileSystem.File.Exists(target))
        {
            var result = await ProcessFile(target, regex, maxApply, fileCount, appliedCount, attributeLine, ct);
            fileCount = result.FileCount;
            appliedCount = result.AppliedCount;
        }
        else
        {
            logger.ZLogError($"Target path does not exist: {target}");
            return;
        }

        logger.ZLogInformation($"Total applied count: {appliedCount}");
        logger.ZLogInformation($"Total processed files: {fileCount}");
    }

    private async Task<(int FileCount, int AppliedCount)> ProcessFile(
        string file, Regex regex, int maxApply, int fileCount, int appliedCount, string attributeLine, CancellationToken ct)
    {
        logger.ZLogTrace($"Processing file: {file}");

        if(!fileSystem.File.Exists(file))
        {
            logger.ZLogWarning($"File not found: {file}");
            return (fileCount, appliedCount);
        }

        var content = await fileSystem.File.ReadAllTextAsync(file, ct);

        var result = MatchFileContent(regex, content, maxApply, appliedCount, attributeLine, globalOptions, logger);

        if(!globalOptions.DryRun)
        {
            await fileSystem.File.WriteAllTextAsync(file, result.Result, ct);
        }

        fileCount++;
        appliedCount = result.AppliedCount;
        return (fileCount, appliedCount);
    }

    public static Regex CreateFullMatchRegex(string fullRegex) =>
        new Regex(
            fullRegex,
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(1000));

    public static Regex CreateClassMatchRegex(string classNameRegex) =>
        new Regex(
            string.Concat(baseClassRegex_1, classNameRegex, baseClassRegex_2),
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(1000));

    public static (int AppliedCount, string Result) MatchFileContent(
        Regex regex, string fileContent, int maxApply, int appliedCount, string attribute,
        GlobalOptions globalOptions, ILogger? logger)
    {
        int remainingCount = maxApply > 0 ? maxApply - appliedCount : int.MaxValue;

        string result = regex.Replace(fileContent, match =>
        {
            logger?.ZLogDebug($"Matched class declaration: {match.Value}");
            appliedCount++;
            if(globalOptions.DryRun)
            {
                logger?.ZLogInformation($"[Dry Run] Result: {attribute}{match.Value}");
                return match.Value;
            }
            else
            {
                return attribute + match.Value;
            }
        }, remainingCount);

        return (appliedCount, result);
    }
}