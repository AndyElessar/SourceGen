using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging;

namespace SourceGen.Ioc.Cli.Test.GenerateIocFor;

[Category(Constants.GenerateIocFor)]
[Category(Constants.IntegrationCategory)]
public class IntegrationTests
{
    private MockFileSystem fileSystem = null!;
    private FakeEnvironmentProvider environmentProvider = null!;
    private FakeLogger<GenerateCommands> logger = null!;
    private GlobalOptions globalOptions = null!;
    private GenerateCommands sut = null!;

    [Before(HookType.Test)]
    public void Setup()
    {
        fileSystem = new MockFileSystem();
        environmentProvider = new FakeEnvironmentProvider { CurrentDirectory = @"C:\TestDir" };
        logger = new FakeLogger<GenerateCommands>();
        globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");
        sut = new GenerateCommands(logger, globalOptions, fileSystem, environmentProvider);
    }

    #region GenerateIocRegisterFor - Directory Processing Tests

    [Test]
    public async Task GenerateIocRegisterFor_DirectoryWithMatchingFiles_GeneratesOutputFile(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddFile(@"C:\TestDir\Handler1.cs", new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(@"C:\TestDir\Handler2.cs", new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(fileSystem.File.Exists(@"C:\TestDir\Generated.cs")).IsTrue();
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("[assembly: IocRegisterFor(typeof(CommandHandler))]");
        await Assert.That(content).Contains("[assembly: IocRegisterFor(typeof(QueryHandler))]");
    }

    [Test]
    public async Task GenerateIocRegisterFor_SubDirectories_ProcessesRecursively(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddDirectory(@"C:\TestDir\SubDir");
        fileSystem.AddFile(@"C:\TestDir\Handler1.cs", new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(@"C:\TestDir\SubDir\Handler2.cs", new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: true,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).Contains("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_SubDirectoriesDisabled_ProcessesOnlyTopLevel(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddDirectory(@"C:\TestDir\SubDir");
        fileSystem.AddFile(@"C:\TestDir\Handler1.cs", new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(@"C:\TestDir\SubDir\Handler2.cs", new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).DoesNotContain("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_NullTarget_UsesCurrentDirectory(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: null,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("CommandHandler");
    }

    #endregion

    #region GenerateIocRegisterFor - Single File Processing Tests

    [Test]
    public async Task GenerateIocRegisterFor_SingleFile_ProcessesCorrectly(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("CommandHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_NonExistentTarget_LogsError(CancellationToken ct)
    {
        // Arrange & Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\NonExistent\File.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterFor - MaxApply Tests

    [Test]
    public async Task GenerateIocRegisterFor_MaxApply_StopsAfterLimit(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddFile(@"C:\TestDir\Handlers.cs", new MockFileData("""
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 2,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        var matchCount = CountIocRegisterForOccurrences(content);
        await Assert.That(matchCount).IsEqualTo(2);
    }

    [Test]
    public async Task GenerateIocRegisterFor_MaxApplyAcrossFiles_StopsAfterTotalLimit(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddFile(@"C:\TestDir\Handler1.cs", new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(@"C:\TestDir\Handler2.cs", new MockFileData("public class QueryHandler { }"));
        fileSystem.AddFile(@"C:\TestDir\Handler3.cs", new MockFileData("public class EventHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 2,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        var matchCount = CountIocRegisterForOccurrences(content);
        await Assert.That(matchCount).IsEqualTo(2);
    }

    #endregion

    #region GenerateIocRegisterFor - FilePattern Tests

    [Test]
    public async Task GenerateIocRegisterFor_FilePattern_FiltersCorrectly(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(@"C:\TestDir\Handler.txt", new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).DoesNotContain("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_EmptyFilePattern_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(@"C:\TestDir");

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterFor - FullRegex Tests

    [Test]
    public async Task GenerateIocRegisterFor_FullRegex_UsesFullRegex(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Service.cs", new MockFileData("public class MyService { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Service.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: null,
            fullRegex: @"\w+Service",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("IocRegisterFor");
        await Assert.That(content).Contains("MyService");
    }

    [Test]
    public async Task GenerateIocRegisterFor_BothRegexNull_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: null,
            fullRegex: null,
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterFor - Generic Attribute Tests

    [Test]
    public async Task GenerateIocRegisterFor_IsGenericAttribute_GeneratesGenericSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: true,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("[assembly: IocRegisterFor<");
        await Assert.That(content).DoesNotContain("typeof(");
    }

    [Test]
    public async Task GenerateIocRegisterFor_NotGenericAttribute_GeneratesTypeofSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("[assembly: IocRegisterFor(typeof(");
        await Assert.That(content).DoesNotContain("IocRegisterFor<");
    }

    #endregion

    #region GenerateIocRegisterFor - DryRun Tests

    [Test]
    public async Task GenerateIocRegisterFor_DryRun_DoesNotCreateFile(CancellationToken ct)
    {
        // Arrange
        var dryRunOptions = new GlobalOptions(DryRun: true, Verbose: false, LoggingFile: "");
        var dryRunSut = new GenerateCommands(logger, dryRunOptions, fileSystem, environmentProvider);
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await dryRunSut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(fileSystem.File.Exists(@"C:\TestDir\Generated.cs")).IsFalse();
        await Assert.That(logger.HasLoggedLevel(LogLevel.Information)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterFor - Output Format Tests

    [Test]
    public async Task GenerateIocRegisterFor_OutputFormat_ContainsAutoGeneratedComment(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).StartsWith("// <auto-generated />");
    }

    [Test]
    public async Task GenerateIocRegisterFor_OutputFormat_ContainsUsingStatement(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Assert.That(content).Contains("using SourceGen.Ioc;");
    }

    #endregion

    #region GenerateIocRegisterFor - Validation Tests

    [Test]
    public async Task GenerateIocRegisterFor_EmptyOutputPath_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: "",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    [Test]
    public async Task GenerateIocRegisterFor_WhitespaceOutputPath_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: "   ",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region Helper Methods

    private static int CountIocRegisterForOccurrences(string content)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf("[assembly: IocRegisterFor", index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index++;
        }

        return count;
    }

    #endregion
}

#region Test Helpers

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

#endregion
