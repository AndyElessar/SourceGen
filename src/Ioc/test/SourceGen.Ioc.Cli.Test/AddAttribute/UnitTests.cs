namespace SourceGen.Ioc.Cli.Test.AddAttribute;

[Category(Constants.AddAttribute)]
[Category(Constants.UnitCategory)]
public class UnitTests
{
    #region CreateClassMatchRegex Tests

    [Test]
    public async Task CreateClassMatchRegex_SimpleClassName_MatchesPublicClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("public class MyClass");
    }

    [Test]
    public async Task CreateClassMatchRegex_SimpleClassName_MatchesInternalClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "internal class MyClass { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("internal class MyClass");
    }

    [Test]
    public async Task CreateClassMatchRegex_SimpleClassName_MatchesPartialClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public partial class MyClass { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("public partial class MyClass");
    }

    [Test]
    public async Task CreateClassMatchRegex_SimpleClassName_MatchesSealedClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public sealed class MyClass { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("public sealed class MyClass");
    }

    [Test]
    public async Task CreateClassMatchRegex_SimpleClassName_DoesNotMatchStaticClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public static class MyClass { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsFalse();
    }

    [Test]
    public async Task CreateClassMatchRegex_SimpleClassName_MatchesClassWithBaseType()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass : BaseClass { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("public class MyClass");
    }

    [Test]
    public async Task CreateClassMatchRegex_RegexPattern_MatchesMultipleClasses()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;

        // Act
        var matches = regex.Matches(content);

        // Assert
        await Assert.That(matches.Count).IsEqualTo(3);
    }

    [Test]
    public async Task CreateClassMatchRegex_RegexPattern_MatchesSpecificPattern()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@"(Command|Query)Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;

        // Act
        var matches = regex.Matches(content);

        // Assert
        await Assert.That(matches.Count).IsEqualTo(2);
    }

    #endregion

    #region CreateFullMatchRegex Tests

    [Test]
    public async Task CreateFullMatchRegex_CustomPattern_MatchesCorrectly()
    {
        // Arrange
        var regex = CreateFullMatchRegex(@"class\s+\w+Service");
        var content = "public class MyService { }";

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Value).IsEqualTo("class MyService");
    }

    [Test]
    public async Task CreateFullMatchRegex_MultilinePattern_MatchesAcrossLines()
    {
        // Arrange
        var regex = CreateFullMatchRegex(@"^\s*public class");
        var content = """
            namespace Test;
            
            public class MyClass { }
            """;

        // Act
        var match = regex.Match(content);

        // Assert
        await Assert.That(match.Success).IsTrue();
    }

    #endregion

    #region MatchFileContent Tests

    [Test]
    public async Task MatchFileContent_SingleMatch_AddsAttribute()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass { }";
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 0, appliedCount: 0, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(1);
        await Assert.That(result).IsEqualTo("[IocRegister]\npublic class MyClass { }");
    }

    [Test]
    public async Task MatchFileContent_MultipleMatches_AddsAttributeToAll()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            """;
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 0, appliedCount: 0, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(2);
        await Assert.That(result).Contains("[IocRegister]\npublic class CommandHandler");
        await Assert.That(result).Contains("[IocRegister]\npublic class QueryHandler");
    }

    [Test]
    public async Task MatchFileContent_MaxApplyLimit_RespectsLimit()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 2, appliedCount: 0, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(2);
        await Assert.That(result).Contains("[IocRegister]\npublic class CommandHandler");
        await Assert.That(result).Contains("[IocRegister]\npublic class QueryHandler");
        await Assert.That(result).DoesNotContain("[IocRegister]\npublic class EventHandler");
    }

    [Test]
    public async Task MatchFileContent_WithExistingAppliedCount_RespectsRemainingLimit()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 3, appliedCount: 2, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(3);
        await Assert.That(result).Contains("[IocRegister]\npublic class CommandHandler");
        await Assert.That(result).DoesNotContain("[IocRegister]\npublic class QueryHandler");
        await Assert.That(result).DoesNotContain("[IocRegister]\npublic class EventHandler");
    }

    [Test]
    public async Task MatchFileContent_DryRun_DoesNotModifyContent()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass { }";
        var globalOptions = new GlobalOptions(DryRun: true, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 0, appliedCount: 0, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(1);
        await Assert.That(result).IsEqualTo(content); // Content unchanged in dry run
    }

    [Test]
    public async Task MatchFileContent_NoMatch_ReturnsOriginalContent()
    {
        // Arrange
        var regex = CreateClassMatchRegex("NonExistentClass");
        var content = "public class MyClass { }";
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 0, appliedCount: 0, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(0);
        await Assert.That(result).IsEqualTo(content);
    }

    [Test]
    public async Task MatchFileContent_CustomAttribute_UsesProvidedAttribute()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass { }";
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 0, appliedCount: 0, "[CustomAttribute]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(1);
        await Assert.That(result).IsEqualTo("[CustomAttribute]\npublic class MyClass { }");
    }

    [Test]
    public async Task MatchFileContent_ComplexClassDeclaration_PreservesFormatting()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = """
            namespace Test;

            /// <summary>
            /// My class description.
            /// </summary>
            public sealed partial class MyClass : IDisposable
            {
                public void Dispose() { }
            }
            """;
        var globalOptions = new GlobalOptions(DryRun: false, Verbose: false, LoggingFile: "");

        // Act
        var (appliedCount, result) = AddAttributeCommands.MatchFileContent(
            regex, content, maxApply: 0, appliedCount: 0, "[IocRegister]\n", globalOptions, null);

        // Assert
        await Assert.That(appliedCount).IsEqualTo(1);
        await Assert.That(result).Contains("[IocRegister]\npublic sealed partial class MyClass");
    }

    #endregion
}
