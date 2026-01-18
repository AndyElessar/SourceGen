namespace SourceGen.Ioc.Cli.Test.GenerateIocFor;

[Category(Constants.GenerateIocFor)]
[Category(Constants.UnitCategory)]
public class UnitTests
{
    #region MatchFileContent Tests

    [Test]
    public async Task MatchFileContent_SingleMatch_ReturnsClassName()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result).Contains("public class MyClass");
    }

    [Test]
    public async Task MatchFileContent_MultipleMatches_ReturnsAllClassNames()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(3);
        await Assert.That(result.Count).IsEqualTo(3);
    }

    [Test]
    public async Task MatchFileContent_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var regex = CreateClassMatchRegex("NonExistent");
        var content = "public class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task MatchFileContent_MaxApply_LimitsResults()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 2, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(2);
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MatchFileContent_MaxApplyWithExistingCount_RespectsTotalLimit()
    {
        // Arrange
        var regex = CreateClassMatchRegex(@".*Handler");
        var content = """
            public class CommandHandler { }
            public class QueryHandler { }
            public class EventHandler { }
            """;

        // Act (already have 1, limit is 2)
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 2, count: 1, useClassNameExtraction: false);

        // Assert (should only add 1 more)
        await Assert.That(count).IsEqualTo(2);
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MatchFileContent_MatchesPublicClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).Contains("public class MyClass");
    }

    [Test]
    public async Task MatchFileContent_MatchesInternalClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "internal class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).Contains("internal class MyClass");
    }

    [Test]
    public async Task MatchFileContent_DoesNotMatchStaticClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public static class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task MatchFileContent_MatchesSealedClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public sealed class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).Contains("public sealed class MyClass");
    }

    [Test]
    public async Task MatchFileContent_MatchesPartialClass()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public partial class MyClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).Contains("public partial class MyClass");
    }

    [Test]
    public async Task MatchFileContent_MatchesClassWithBaseType()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass : BaseClass { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).Contains("public class MyClass");
    }

    [Test]
    public async Task MatchFileContent_MatchesClassWithInterfaces()
    {
        // Arrange
        var regex = CreateClassMatchRegex("MyClass");
        var content = "public class MyClass : IInterface1, IInterface2 { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).Contains("public class MyClass");
    }

    #endregion

    #region FullRegex Tests

    [Test]
    public async Task MatchFileContent_FullRegex_MatchesCustomPattern()
    {
        // Arrange
        var regex = CreateFullMatchRegex(@"\w+Service");
        var content = "public class MyService { }";

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo("MyService");
    }

    [Test]
    public async Task MatchFileContent_FullRegex_MatchesMultiplePatterns()
    {
        // Arrange
        var regex = CreateFullMatchRegex(@"\w+Service");
        var content = """
            public class MyService { }
            public class YourService { }
            """;

        // Act
        var (count, result) = GenerateCommands.MatchFileContent(
            regex, content, maxApply: 0, count: 0, useClassNameExtraction: false);

        // Assert
        await Assert.That(count).IsEqualTo(2);
        await Assert.That(result).Contains("MyService");
        await Assert.That(result).Contains("YourService");
    }

    #endregion
}
