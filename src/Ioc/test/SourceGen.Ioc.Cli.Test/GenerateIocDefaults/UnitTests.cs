namespace SourceGen.Ioc.Cli.Test.GenerateIocDefaults;

[Category(Constants.GenerateIocDefaults)]
[Category(Constants.UnitCategory)]
public class UnitTests
{
    #region CreateClassWithBaseTypeMatchRegex Tests

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_MatchesClassWithInterface()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public class CommandHandler : IHandler { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsTrue();
        var (className, baseType) = ExtractClassAndBaseType(match);
        await Assert.That(className).IsEqualTo("CommandHandler");
        await Assert.That(baseType).IsEqualTo("IHandler");
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_MatchesClassWithBaseClass()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Service", @"BaseService");
        var content = "public class UserService : BaseService { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsTrue();
        var (className, baseType) = ExtractClassAndBaseType(match);
        await Assert.That(className).IsEqualTo("UserService");
        await Assert.That(baseType).IsEqualTo("BaseService");
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_MatchesClassWithMultipleInterfaces()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public class CommandHandler : IDisposable, IHandler { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsTrue();
        var (className, baseType) = ExtractClassAndBaseType(match);
        await Assert.That(className).IsEqualTo("CommandHandler");
        await Assert.That(baseType).IsEqualTo("IHandler");
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_DoesNotMatchClassWithoutBaseType()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public class CommandHandler { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsFalse();
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_MatchesInternalClass()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "internal class CommandHandler : IHandler { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsTrue();
        var (className, baseType) = ExtractClassAndBaseType(match);
        await Assert.That(className).IsEqualTo("CommandHandler");
        await Assert.That(baseType).IsEqualTo("IHandler");
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_DoesNotMatchStaticClass()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public static class CommandHandler : IHandler { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsFalse();
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_MatchesPartialClass()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public partial class CommandHandler : IHandler { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsTrue();
        var (className, baseType) = ExtractClassAndBaseType(match);
        await Assert.That(className).IsEqualTo("CommandHandler");
    }

    [Test]
    public async Task CreateClassWithBaseTypeMatchRegex_MatchesGenericBaseType()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler<.*>");
        var content = "public class CommandHandler : IHandler<Command> { }";

        var match = regex.Match(content);

        await Assert.That(match.Success).IsTrue();
        var (className, baseType) = ExtractClassAndBaseType(match);
        await Assert.That(className).IsEqualTo("CommandHandler");
        await Assert.That(baseType).IsEqualTo("IHandler<Command>");
    }

    #endregion

    #region MatchFileContentForDefaults Tests

    [Test]
    public async Task MatchFileContentForDefaults_SingleMatch_ReturnsClassGroupedByBaseType()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = """
            namespace TestNamespace;
            public class CommandHandler : IHandler { }
            """;
        Dictionary<string, List<string>> collected = [];

        var count = GenerateCommands.MatchFileContentForDefaults(
            regex, content, maxApply: 0, count: 0, collected);

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(collected).ContainsKey("IHandler");
        await Assert.That(collected["IHandler"]).Contains("TestNamespace.CommandHandler");
    }

    [Test]
    public async Task MatchFileContentForDefaults_MultipleClassesSameBaseType_GroupsTogether()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = """
            namespace TestNamespace;
            public class CommandHandler : IHandler { }
            public class QueryHandler : IHandler { }
            public class EventHandler : IHandler { }
            """;
        Dictionary<string, List<string>> collected = [];

        var count = GenerateCommands.MatchFileContentForDefaults(
            regex, content, maxApply: 0, count: 0, collected);

        await Assert.That(count).IsEqualTo(3);
        await Assert.That(collected["IHandler"].Count).IsEqualTo(3);
        await Assert.That(collected["IHandler"]).Contains("TestNamespace.CommandHandler");
        await Assert.That(collected["IHandler"]).Contains("TestNamespace.QueryHandler");
        await Assert.That(collected["IHandler"]).Contains("TestNamespace.EventHandler");
    }

    [Test]
    public async Task MatchFileContentForDefaults_NoMatches_ReturnsEmpty()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public class MyService { }";
        Dictionary<string, List<string>> collected = [];

        var count = GenerateCommands.MatchFileContentForDefaults(
            regex, content, maxApply: 0, count: 0, collected);

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(collected).IsEmpty();
    }

    [Test]
    public async Task MatchFileContentForDefaults_MaxApply_LimitsResults()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = """
            namespace TestNamespace;
            public class CommandHandler : IHandler { }
            public class QueryHandler : IHandler { }
            public class EventHandler : IHandler { }
            """;
        Dictionary<string, List<string>> collected = [];

        var count = GenerateCommands.MatchFileContentForDefaults(
            regex, content, maxApply: 2, count: 0, collected);

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(collected["IHandler"].Count).IsEqualTo(2);
    }

    [Test]
    public async Task MatchFileContentForDefaults_MaxApplyWithExistingCount_RespectsTotalLimit()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = """
            namespace TestNamespace;
            public class CommandHandler : IHandler { }
            public class QueryHandler : IHandler { }
            public class EventHandler : IHandler { }
            """;
        Dictionary<string, List<string>> collected = [];

        var count = GenerateCommands.MatchFileContentForDefaults(
            regex, content, maxApply: 2, count: 1, collected);

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(collected["IHandler"].Count).IsEqualTo(1);
    }

    [Test]
    public async Task MatchFileContentForDefaults_WithoutNamespace_UsesClassNameOnly()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public class CommandHandler : IHandler { }";
        Dictionary<string, List<string>> collected = [];

        var count = GenerateCommands.MatchFileContentForDefaults(
            regex, content, maxApply: 0, count: 0, collected);

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(collected["IHandler"]).Contains("CommandHandler");
    }

    #endregion

    #region ExtractClassAndBaseType Tests

    [Test]
    public async Task ExtractClassAndBaseType_ValidMatch_ReturnsClassAndBaseType()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler");
        var content = "public class CommandHandler : IHandler { }";

        var match = regex.Match(content);
        var (className, baseType) = ExtractClassAndBaseType(match);

        await Assert.That(className).IsEqualTo("CommandHandler");
        await Assert.That(baseType).IsEqualTo("IHandler");
    }

    [Test]
    public async Task ExtractClassAndBaseType_GenericInterface_ReturnsFullGenericType()
    {
        var regex = CreateClassWithBaseTypeMatchRegex(@".*Handler", @"IHandler<.*>");
        var content = "public class CommandHandler : IHandler<Command> { }";

        var match = regex.Match(content);
        var (className, baseType) = ExtractClassAndBaseType(match);

        await Assert.That(className).IsEqualTo("CommandHandler");
        await Assert.That(baseType).IsEqualTo("IHandler<Command>");
    }

    #endregion
}
