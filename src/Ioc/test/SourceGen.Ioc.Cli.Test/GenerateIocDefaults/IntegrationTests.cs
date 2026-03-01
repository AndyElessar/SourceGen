using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging;

namespace SourceGen.Ioc.Cli.Test.GenerateIocDefaults;

[Category(Constants.GenerateIocDefaults)]
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

    #region GenerateIocRegisterDefaults - Directory Processing Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_DirectoryWithMatchingFiles_GeneratesOutputFile(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler : IHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler2.cs"), new MockFileData("public class QueryHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        await Assert.That(fileSystem.File.Exists(TestPaths.Combine("Generated.cs"))).IsTrue();
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("IocRegisterDefaults");
        await Assert.That(content).Contains("IHandler");
        await Assert.That(content).Contains("typeof(CommandHandler)");
        await Assert.That(content).Contains("typeof(QueryHandler)");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_SubDirectories_ProcessesRecursively(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddDirectory(TestPaths.Combine("SubDir"));
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler : IHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("SubDir", "Handler2.cs"), new MockFileData("public class QueryHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: true,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).Contains("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_SubDirectoriesDisabled_ProcessesOnlyTopLevel(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddDirectory(TestPaths.Combine("SubDir"));
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler : IHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("SubDir", "Handler2.cs"), new MockFileData("public class QueryHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).DoesNotContain("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_NullTarget_UsesCurrentDirectory(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: null,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
    }

    #endregion

    #region GenerateIocRegisterDefaults - Single File Processing Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_SingleFile_ProcessesCorrectly(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_NonExistentTarget_LogsError(CancellationToken ct)
    {
        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("..", "NonExistent", "File.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterDefaults - Multiple Base Types Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_MultipleBaseTypes_GroupsByBaseType(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            public class CommandHandler : ICommandHandler { }
            public class QueryHandler : IQueryHandler { }
            public class EventHandler : ICommandHandler { }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handlers.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"I.*Handler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("ICommandHandler");
        await Assert.That(content).Contains("IQueryHandler");
    }

    #endregion

    #region GenerateIocRegisterDefaults - MaxApply Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_MaxApply_StopsAfterLimit(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            public class CommandHandler : IHandler { }
            public class QueryHandler : IHandler { }
            public class EventHandler : IHandler { }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            maxApply: 2,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        var typeofCount = CountTypeofOccurrences(content);
        await Assert.That(typeofCount).IsEqualTo(2);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_MaxApplyAcrossFiles_StopsAfterTotalLimit(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler1.cs"), new MockFileData("public class CommandHandler : IHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler2.cs"), new MockFileData("public class QueryHandler : IHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler3.cs"), new MockFileData("public class EventHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            maxApply: 2,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        var typeofCount = CountTypeofOccurrences(content);
        await Assert.That(typeofCount).IsEqualTo(2);
    }

    #endregion

    #region GenerateIocRegisterDefaults - FilePattern Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_FilePattern_FiltersCorrectly(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));
        fileSystem.AddFile(TestPaths.Combine("Handler.txt"), new MockFileData("public class QueryHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("CommandHandler");
        await Assert.That(content).DoesNotContain("QueryHandler");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_EmptyFilePattern_LogsError(CancellationToken ct)
    {
        fileSystem.AddDirectory(TestPaths.Root);

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterDefaults - Generic Attribute Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_IsGenericAttribute_GeneratesGenericSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            isGenericAttribute: true,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("[assembly: IocRegisterDefaults<IHandler>(ServiceLifetime.Transient");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_NotGenericAttribute_GeneratesTypeofSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("[assembly: IocRegisterDefaults(typeof(IHandler), ServiceLifetime.Transient");
    }

    #endregion

    #region GenerateIocRegisterDefaults - Lifetime Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_SingletonLifetime_GeneratesSingletonInOutput(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            lifetime: "singleton",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("ServiceLifetime.Singleton");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_ScopedLifetime_GeneratesScopedInOutput(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            lifetime: "SCOPED",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).Contains("ServiceLifetime.Scoped");
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_InvalidLifetime_LogsErrorAndDoesNotCreateFile(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            lifetime: "Invalid",
            ct: ct);

        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
        await Assert.That(fileSystem.File.Exists(TestPaths.Combine("Generated.cs"))).IsFalse();
    }

    #endregion

    #region GenerateIocRegisterDefaults - DryRun Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_DryRun_DoesNotCreateFile(CancellationToken ct)
    {
        var dryRunOptions = new GlobalOptions(DryRun: true, Verbose: false, LoggingFile: "");
        var dryRunSut = new GenerateCommands(logger, dryRunOptions, fileSystem, environmentProvider);
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await dryRunSut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        await Assert.That(fileSystem.File.Exists(TestPaths.Combine("Generated.cs"))).IsFalse();
        await Assert.That(logger.HasLoggedLevel(LogLevel.Information)).IsTrue();
    }

    #endregion

    #region GenerateIocRegisterDefaults - Output Format Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_OutputFormat_ContainsRequiredElements(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("""
            namespace MyApp;
            public class CommandHandler : IHandler { }
            public class QueryHandler : IHandler { }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Assert.That(content).StartsWith("// <auto-generated />");
        await Assert.That(content).Contains("using SourceGen.Ioc;");
        await Assert.That(content).Contains("using Microsoft.Extensions.DependencyInjection;");
        await Assert.That(content).Contains("ServiceLifetime.Transient");
        await Assert.That(content).Contains("ImplementationTypes = [");
        await Assert.That(content).Contains("typeof(MyApp.CommandHandler)");
        await Assert.That(content).Contains("typeof(MyApp.QueryHandler)");
    }

    #endregion

    #region GenerateIocRegisterDefaults - Validation Tests

    [Test]
    public async Task GenerateIocRegisterDefaults_EmptyOutputPath_LogsError(CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: "",
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            ct: ct);

        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    [Test]
    [Arguments(null, "IHandler")]
    [Arguments(".*Handler", null)]
    [Arguments(null, null)]
    public async Task GenerateIocRegisterDefaults_MissingRegex_LogsError(string? classNameRegex, string? baseTypeRegex, CancellationToken ct)
    {
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("public class CommandHandler : IHandler { }"));

        await sut.GenerateIocRegisterDefaults(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handler.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: classNameRegex,
            baseTypeRegex: baseTypeRegex,
            ct: ct);

        await Assert.That(logger.HasLoggedLevel(LogLevel.Error)).IsTrue();
    }

    #endregion

    #region Helper Methods

    private static int CountTypeofOccurrences(string content)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf("typeof(", index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index++;
        }

        // Subtract 1 for the typeof in IocRegisterDefaults(typeof(BaseType), ...)
        return count > 0 ? count - 1 : 0;
    }

    #endregion
}
