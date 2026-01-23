namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for decorator container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.Decorator)]
public class DecoratorContainerTests
{
    [Test]
    public async Task Container_WithDecorators_GeneratesDecoratedResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { void Handle(); }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Decorators = [typeof(LoggingDecorator), typeof(CachingDecorator)])]
            public class Handler : IHandler
            {
                public void Handle() { }
            }

            public class LoggingDecorator(IHandler inner) : IHandler
            {
                public void Handle() => inner.Handle();
            }

            public class CachingDecorator(IHandler inner) : IHandler
            {
                public void Handle() => inner.Handle();
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
