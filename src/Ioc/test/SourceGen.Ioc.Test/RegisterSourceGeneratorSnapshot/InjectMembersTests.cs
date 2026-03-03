namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for the <c>InjectMembers</c> property on registration attributes,
/// which allows specifying property/field/method injection without placing <c>[IocInject]</c>
/// directly on the member.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.InjectMembers)]
public class InjectMembersTests
{
    [Test]
    public async Task InjectMembers_PropertyInjection_NoKey_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService.Dep)])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_FieldInjection_NoKey_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService._dep)])]
            public class MyService
            {
                public IDependency? _dep;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_WithStringKey_GeneratesKeyedInjection()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "myKey")]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [new object[] { nameof(MyService.Dep), "myKey" }])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_And_IocInject_Mixed_IocInjectTakesPriority()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "keyed")]
            public class KeyedDependency : IDependency { }

            // InjectMembers specifies Dep with no key,
            // but [IocInject] on Dep specifies "keyed" key — [IocInject] wins.
            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService.Dep)])]
            public class MyService
            {
                [IocInject(Key = "keyed")]
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_MultipleMembers_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDep1 { }
            public interface IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService.Dep1), nameof(MyService.Dep2)])]
            public class MyService
            {
                public IDep1? Dep1 { get; set; }
                public IDep2? Dep2 { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_WithCsharpKeyType_GeneratesKeyedInjection()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            public static class Keys
            {
                public static string PrimaryKey => "primary";
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = nameof(Keys.PrimaryKey), KeyType = KeyType.Csharp)]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [new object[] { nameof(MyService.Dep), nameof(Keys.PrimaryKey), KeyType.Csharp }])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
