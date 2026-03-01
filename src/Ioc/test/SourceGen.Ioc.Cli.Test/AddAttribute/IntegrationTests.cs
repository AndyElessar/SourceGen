using System.IO.Abstractions.TestingHelpers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SourceGen.Ioc.Cli.Test.AddAttribute;

[Category(Constants.AddAttribute)]
[Category(Constants.IntegrationCategory)]
public partial class IntegrationTests
{
    private MockFileSystem fileSystem = null!;
    private FakeEnvironmentProvider environmentProvider = null!;
    private FakeLogger<AddAttributeCommands> logger = null!;
    private GlobalOptions globalOptions = null!;
    private AddAttributeCommands sut = null!;

    [Before(HookType.Test)]
    public void Setup()
    {
        fileSystem = new MockFileSystem();
        environmentProvider = new FakeEnvironmentProvider { CurrentDirectory = TestPaths.Root };
        logger = new FakeLogger<AddAttributeCommands>();
        globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");
        sut = new AddAttributeCommands(logger, globalOptions, fileSystem, environmentProvider);
    }

    #region AddAttribute - Directory Processing Tests

    [Test]
    public async Task AddAttribute_DirectoryWithMatchingFiles_AddsAttributeToMatchingClasses(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler2.cs"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content1 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler1.cs"), ct);
        var content2 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler2.cs"), ct);
        await Assert.That(content1).IsEqualTo("[IocRegister]\npublic class CommandHandler { }");
        await Assert.That(content2).IsEqualTo("[IocRegister]\npublic class QueryHandler { }");
    }

    [Test]
    public async Task AddAttribute_SubDirectories_ProcessesRecursively(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddDirectory(TestPaths.Combine("SubDir"));
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("SubDir", "Handler2.cs"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: true,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content1 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler1.cs"), ct);
        var content2 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("SubDir", "Handler2.cs"), ct);
        await Assert.That(content1).IsEqualTo("[IocRegister]\npublic class CommandHandler { }");
        await Assert.That(content2).IsEqualTo("[IocRegister]\npublic class QueryHandler { }");
    }

    [Test]
    public async Task AddAttribute_SubDirectoriesDisabled_ProcessesOnlyTopLevel(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddDirectory(TestPaths.Combine("SubDir"));
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("SubDir", "Handler2.cs"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content1 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler1.cs"), ct);
        var content2 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("SubDir", "Handler2.cs"), ct);
        await Assert.That(content1).IsEqualTo("[IocRegister]\npublic class CommandHandler { }");
        await Assert.That(content2).IsEqualTo("public class QueryHandler { }"); // Unchanged
    }

    [Test]
    public async Task AddAttribute_NullTarget_UsesCurrentDirectory(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.AddAttribute(
            target: null,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler.cs"), ct);
        await Assert.That(content).IsEqualTo("[IocRegister]\npublic class CommandHandler { }");
    }

    #endregion

    #region AddAttribute - Single File Processing Tests

    [Test]
    public async Task AddAttribute_SingleFile_ProcessesCorrectly(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler.cs"), ct);
        await Assert.That(content).IsEqualTo("[IocRegister]\npublic class CommandHandler { }");
    }

    [Test]
    public async Task AddAttribute_NonExistentFile_LogsError(CancellationToken ct)
    {
        // Arrange & Act
        await sut.AddAttribute(
            target: TestPaths.Combine("..", "NonExistent", "File.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert - Logger should have been called with error
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region AddAttribute - MaxApply Tests

    [Test]
    public async Task AddAttribute_MaxApply_StopsAfterLimit(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 2,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handlers.cs"), ct);
        var matchCount = IocRegisterAttributeRegex.Count(content);
        await Assert.That(matchCount).IsEqualTo(2);
    }

    [Test]
    public async Task AddAttribute_MaxApplyAcrossFiles_StopsAfterTotalLimit(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler2.cs"), new MockFileData("public class QueryHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler3.cs"), new MockFileData("public class EventHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 2,
            ct: ct);

        // Assert - Only first 2 files should be modified (order depends on file system enumeration)
        var content1 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler1.cs"), ct);
        var content2 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler2.cs"), ct);
        var content3 = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler3.cs"), ct);

        var totalAttributes =
            (content1.Contains("[IocRegister]") ? 1 : 0) +
            (content2.Contains("[IocRegister]") ? 1 : 0) +
            (content3.Contains("[IocRegister]") ? 1 : 0);
        await Assert.That(totalAttributes).IsEqualTo(2);
    }

    #endregion

    #region AddAttribute - FilePattern Tests

    [Test]
    public async Task AddAttribute_FilePattern_FiltersCorrectly(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler.txt"), new MockFileData("public class QueryHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var csContent = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler.cs"), ct);
        var txtContent = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler.txt"), ct);
        await Assert.That(csContent).IsEqualTo("[IocRegister]\npublic class CommandHandler { }");
        await Assert.That(txtContent).IsEqualTo("public class QueryHandler { }"); // Unchanged
    }

    [Test]
    public async Task AddAttribute_EmptyFilePattern_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);

        // Act
        await sut.AddAttribute(
            target: TestPaths.Root,
            filePattern: "",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region AddAttribute - Custom Attribute Tests

    [Test]
    public async Task AddAttribute_CustomAttributeName_UsesCustomName(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            attributeName: "MyCustomAttribute",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler.cs"), ct);
        await Assert.That(content).IsEqualTo("[MyCustomAttribute]\npublic class CommandHandler { }");
    }

    [Test]
    public async Task AddAttribute_EmptyAttributeName_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            attributeName: "",
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region AddAttribute - FullRegex Tests

    [Test]
    public async Task AddAttribute_FullRegex_UsesFullRegexInsteadOfClassNameRegex(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Service.cs"), new MockFileData("public class MyService { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Service.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: null,
            fullRegex: @"public class \w+Service",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Service.cs"), ct);
        await Assert.That(content).IsEqualTo("[IocRegister]\npublic class MyService { }");
    }

    [Test]
    public async Task AddAttribute_BothRegexNull_LogsError(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: null,
            fullRegex: null,
            ct: ct);

        // Assert
        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region AddAttribute - DryRun Tests

    [Test]
    public async Task AddAttribute_DryRun_DoesNotModifyFiles(CancellationToken ct)
    {
        // Arrange
        var dryRunOptions = new GlobalOptions(DryRun: true, Verbose: false, LoggingFile: "");
        var dryRunSut = new AddAttributeCommands(logger, dryRunOptions, fileSystem, environmentProvider);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler { }"));

        // Act
        await dryRunSut.AddAttribute(
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handler.cs"), ct);
        await Assert.That(content).IsEqualTo("public class CommandHandler { }"); // Unchanged
    }

    #endregion

    #region AddAttribute - Complex Scenarios

    [Test]
    public async Task AddAttribute_MultipleClassesInFile_AddsAttributeToAll(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            namespace Test;

            public class CommandHandler { }

            internal class QueryHandler { }

            public sealed class EventHandler { }
            """));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Handlers.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handlers.cs"), ct);
        await Assert.That(content).Contains("[IocRegister]\npublic class CommandHandler");
        await Assert.That(content).Contains("[IocRegister]\ninternal class QueryHandler");
        await Assert.That(content).Contains("[IocRegister]\npublic sealed class EventHandler");
    }

    [Test]
    public async Task AddAttribute_StaticClassExcluded_DoesNotAddAttribute(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            public class CommandHandler { }
            public static class StaticHandler { }
            """));

        // Act
        await sut.AddAttribute(
            target: TestPaths.Combine("Handlers.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Handlers.cs"), ct);
        await Assert.That(content).Contains("[IocRegister]\npublic class CommandHandler");
        await Assert.That(content).DoesNotContain("[IocRegister]\npublic static class StaticHandler");
    }

    [GeneratedRegex(@"\[IocRegister\]", RegexOptions.CultureInvariant, 500)]
    private static partial Regex IocRegisterAttributeRegex { get; }

    #endregion
}
