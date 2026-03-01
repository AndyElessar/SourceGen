using System.IO.Abstractions.TestingHelpers;

namespace SourceGen.Ioc.Cli.Test.GenerateIocFor;

[Category(Constants.GenerateIocFor)]
[Category(Constants.SnapshotCategory)]
public class SnapshotTests
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

    [Test]
    public async Task GenerateIocRegisterFor_SingleClass_TypeofSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));

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
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_SingleClass_GenericSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handler.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));

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
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_MultipleClasses_TypeofSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class CreateCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }

            public class UpdateCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }

            public class DeleteCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handlers.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_MultipleClasses_GenericSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class CreateCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }

            public class UpdateCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }

            public class DeleteCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handlers.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: true,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_MixedClassTypes_ExcludesStatic(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Services.cs"), new MockFileData("""
            namespace MyApp.Services;

            public class UserService : IUserService
            {
                public void DoWork() { }
            }

            public static class StaticHelperService
            {
                public static void Help() { }
            }

            public sealed class OrderService : IOrderService
            {
                public void Process() { }
            }

            internal class InternalService : IInternalService
            {
                public void Internal() { }
            }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Services.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Service",
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_MultipleFiles_TypeofSyntax(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddDirectory(TestPaths.Root);
        fileSystem.AddFile(TestPaths.Combine("CommandHandler.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));
        fileSystem.AddFile(TestPaths.Combine("QueryHandler.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class QueryHandler : IQueryHandler
            {
                public void Handle() { }
            }
            """));
        fileSystem.AddFile(TestPaths.Combine("EventHandler.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class EventHandler : IEventHandler
            {
                public void Handle() { }
            }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Root,
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_WithMaxApply_LimitsOutput(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Handlers.cs"), new MockFileData("""
            namespace MyApp.Handlers;

            public class CreateHandler { }
            public class UpdateHandler { }
            public class DeleteHandler { }
            public class ReadHandler { }
            public class ListHandler { }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Handlers.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            maxApply: 3,
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterFor_NoMatches_GeneratesEmptyFile(CancellationToken ct)
    {
        // Arrange
        fileSystem.AddFile(TestPaths.Combine("Models.cs"), new MockFileData("""
            namespace MyApp.Models;

            public class User { }
            public class Order { }
            """));

        // Act
        await sut.GenerateIocRegisterFor(
            outputPath: TestPaths.Combine("Generated.cs"),
            target: TestPaths.Combine("Models.cs"),
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            isGenericAttribute: false,
            ct: ct);

        // Assert
        var content = await fileSystem.File.ReadAllTextAsync(TestPaths.Combine("Generated.cs"), ct);
        await Verify(content);
    }
}
