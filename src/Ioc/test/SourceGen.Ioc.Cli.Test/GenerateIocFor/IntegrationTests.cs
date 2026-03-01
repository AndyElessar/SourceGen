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
        environmentProvider = new FakeEnvironmentProvider { CurrentDirectory = TestPaths.Root };
        logger = new FakeLogger<GenerateCommands>();
        globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");
        sut = new GenerateCommands(logger, globalOptions, fileSystem, environmentProvider);
    }

    #region GenerateIocRegisterFor - Directory Processing Tests

    [Test]
    public async Task GenerateIocRegisterFor_DirectoryWithMatchingFiles_GeneratesOutputFile(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler2.cs"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(fileSystem.File.Exists(TestPaths.Combine("Generated.cs"))).IsTrue();
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("[assembly: IocRegisterFor(typeof(CommandHandler))]");
        await Assert.That(content).Contains("[assembly: IocRegisterFor(typeof(QueryHandler))]");
    }

    [Test]
    public async Task GenerateIocRegisterFor_SubDirectories_ProcessesRecursively(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddDirectory(TestPaths.Combine("SubDir"));
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("SubDir", "Handler2.cs"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: true,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).Contains("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_SubDirectoriesDisabled_ProcessesOnlyTopLevel(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddDirectory(TestPaths.Combine("SubDir"));
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("SubDir", "Handler2.cs"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).DoesNotContain("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_NullTarget_UsesCurrentDirectory(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: null,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
    }

    #endregion

    #region GenerateIocRegisterFor - Single File Processing Tests

    [Test]
    public async Task GenerateIocRegisterFor_SingleFile_ProcessesCorrectly(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_NonExistentTarget_LogsError(CancellationToken ct)
    {
        // Arrange & Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("..", "NonExistent", "File.cs"),
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
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 2,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        var matchCount = CountIocRegisterForOccurrences(content);
        await Assert.That(matchCount).IsEqualTo(2);
    }

    [Test]
    public async Task GenerateIocRegisterFor_MaxApplyAcrossFiles_StopsAfterTotalLimit(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler2.cs"), new MockFileData("public class QueryHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler3.cs"), new MockFileData("public class EventHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 2,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        var matchCount = CountIocRegisterForOccurrences(content);
        await Assert.That(matchCount).IsEqualTo(2);
    }

    #endregion

    #region GenerateIocRegisterFor - FilePattern Tests

    [Test]
    public async Task GenerateIocRegisterFor_FilePattern_FiltersCorrectly(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler.txt"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).DoesNotContain("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterFor_EmptyFilePattern_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterFor - ClassNameRegex Null Tests

    [Test]
    public async Task GenerateIocRegisterFor_ClassNameRegexNull_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: null,
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
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: true,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("[assembly: IocRegisterFor<");
        await Assert.That(content).DoesNotContain("typeof(");
    }

    [Test]
    public async Task GenerateIocRegisterFor_NotGenericAttribute_GeneratesTypeofSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
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
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await dryRunSut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(fileSystem.File.Exists(TestPaths.Combine("Generated.cs"))).IsFalse();
        await Assert.That(logger.HasLoggedLevel(LogLevel.Information)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterFor - Output Format Tests

    [Test]
    public async Task GenerateIocRegisterFor_OutputFormat_ContainsRequiredElements(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).StartsWith("// <auto-generated />");
        await Assert.That(content).Contains("using SourceGen.Ioc;");
    }

    #endregion

    #region GenerateIocRegisterFor - Validation Tests

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task GenerateIocRegisterFor_EmptyOrWhitespaceOutputPath_LogsError(string outputPath, CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: outputPath,
            target: TestPaths.Combine("Handler.cs"),
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
