using System.IO.Abstractions.TestingHelpers;

namespace SourceGen.Ioc.Cli.Test.GenerateIocDefaults;

[Category(Constants.GenerateIocDefaults)]
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
        environmentProvider = new FakeEnvironmentProvider { CurrentDirectory = @"C:\TestDir" };
        logger = new FakeLogger<GenerateCommands>();
        globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");
        sut = new GenerateCommands(logger, globalOptions, fileSystem, environmentProvider);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_SingleClass_TypeofSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"ICommandHandler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_SingleClass_GenericSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : ICommandHandler
            {
                public void Handle() { }
            }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"ICommandHandler",
            isGenericAttribute: true,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_MultipleClasses_SameBaseType_TypeofSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handlers.cs", new MockFileData("""
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

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handlers.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"ICommandHandler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_MultipleClasses_DifferentBaseTypes_TypeofSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handlers.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CreateCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }

            public class GetQueryHandler : IQueryHandler
            {
                public void Handle() { }
            }

            public class UpdateCommandHandler : ICommandHandler
            {
                public void Handle() { }
            }

            public class ListQueryHandler : IQueryHandler
            {
                public void Handle() { }
            }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handlers.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"I.*Handler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_MixedClassTypes_ExcludesStatic(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Services.cs", new MockFileData("""
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

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Services.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Service",
            baseTypeRegex: @"I.*Service",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_MultipleFiles_TypeofSyntax(CancellationToken ct)
    {
        fileSystem.AddDirectory(@"C:\TestDir");
        fileSystem.AddFile(@"C:\TestDir\CommandHandler.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : IHandler
            {
                public void Handle() { }
            }
            """));
        fileSystem.AddFile(@"C:\TestDir\QueryHandler.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class QueryHandler : IHandler
            {
                public void Handle() { }
            }
            """));
        fileSystem.AddFile(@"C:\TestDir\EventHandler.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class EventHandler : IHandler
            {
                public void Handle() { }
            }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_WithMaxApply_LimitsOutput(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handlers.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CreateHandler : IHandler { }
            public class UpdateHandler : IHandler { }
            public class DeleteHandler : IHandler { }
            public class ReadHandler : IHandler { }
            public class ListHandler : IHandler { }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handlers.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            maxApply: 3,
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_NoMatches_GeneratesEmptyFile(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Models.cs", new MockFileData("""
            namespace MyApp.Models;

            public class User { }
            public class Order { }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Models.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_GenericInterface_TypeofSyntax(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handlers.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CreateUserHandler : IHandler<CreateUserCommand>
            {
                public void Handle() { }
            }

            public class UpdateUserHandler : IHandler<UpdateUserCommand>
            {
                public void Handle() { }
            }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handlers.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler<.*>",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }

    [Test]
    public async Task GenerateIocRegisterDefaults_MultipleInterfaces_MatchesLastOne(CancellationToken ct)
    {
        fileSystem.AddFile(@"C:\TestDir\Handler.cs", new MockFileData("""
            namespace MyApp.Handlers;

            public class CommandHandler : IDisposable, IHandler
            {
                public void Handle() { }
                public void Dispose() { }
            }
            """));

        await sut.GenerateIocRegisterDefaults(
            outputPath: @"C:\TestDir\Generated.cs",
            target: @"C:\TestDir\Handler.cs",
            filePattern: "*.cs",
            searchSubDirectories: false,
            classNameRegex: @".*Handler",
            baseTypeRegex: @"IHandler",
            isGenericAttribute: false,
            ct: ct);

        var content = await fileSystem.File.ReadAllTextAsync(@"C:\TestDir\Generated.cs", ct);
        await Verify(content);
    }
}
