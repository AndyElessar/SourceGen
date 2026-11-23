#pragma warning disable SGIOCEXP001 // The test reads the types that are only for generated code

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SourceGen.Ioc.Test.Library;

/// <summary>
/// Checks the declarations of the attributes against the table in Annotations.spec, "Attribute declarations", and the
/// types only for generated code against SPEC.spec, "Types only for generated code".
/// </summary>
public class AttributeDeclarationTests
{
    public static IEnumerable<Func<(Type Type, AttributeTargets Targets, bool AllowMultiple)>> Attributes()
    {
        yield return () => (typeof(IocRegisterAttribute), AttributeTargets.Class, true);
        yield return () => (typeof(IocRegisterForAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocRegisterBatchAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocFactoryAttribute), AttributeTargets.Method, true);
        yield return () => (typeof(IocFactoryForAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocInstanceAttribute), AttributeTargets.Field | AttributeTargets.Property, true);
        yield return () => (typeof(IocInstanceForAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocInjectAttribute), AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, false);
        yield return () => (typeof(IocInjectMemberAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocDiscoverAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocImportModuleAttribute), AttributeTargets.Assembly | AttributeTargets.Class, true);
        yield return () => (typeof(IocModuleAttribute), AttributeTargets.Class, false);
        yield return () => (typeof(IocContainerAttribute), AttributeTargets.Class, false);
        yield return () => (typeof(IocServiceCollectionExtensionsAttribute), AttributeTargets.Assembly, false);
    }

    [Test]
    [MethodDataSource(nameof(Attributes))]
    public async Task Attribute_Declaration_MatchesSpec(Type type, AttributeTargets targets, bool allowMultiple)
    {
        AttributeUsageAttribute usage = type.GetCustomAttribute<AttributeUsageAttribute>()!;

        await Assert.That(type.IsSealed).IsTrue();
        await Assert.That(usage.ValidOn).IsEqualTo(targets);
        await Assert.That(usage.AllowMultiple).IsEqualTo(allowMultiple);
        await Assert.That(usage.Inherited).IsFalse();
        await Assert.That(type.GetCustomAttributes<ConditionalAttribute>().Select(static c => c.ConditionString)).IsEquivalentTo(["SOURCEGEN"]);
    }

    [Test]
    public async Task IocModuleInfoAttribute_Declaration_HasNoConditional()
    {
        AttributeUsageAttribute usage = typeof(IocModuleInfoAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        await Assert.That(typeof(IocModuleInfoAttribute).GetCustomAttributes<ConditionalAttribute>()).IsEmpty();
        await Assert.That(usage.ValidOn).IsEqualTo(AttributeTargets.Class);
        await Assert.That(usage.AllowMultiple).IsFalse();
        await Assert.That(usage.Inherited).IsFalse();
    }

    public static IEnumerable<Func<Type>> GeneratedCodeOnlyTypes()
    {
        yield return () => typeof(IocModuleInfoAttribute);
        yield return () => typeof(IocHolder<>);
        yield return () => typeof(IocHolder);
        yield return () => typeof(IocTask<>);
        yield return () => typeof(IocRegistrationMarker);
    }

    [Test]
    [MethodDataSource(nameof(GeneratedCodeOnlyTypes))]
    public async Task GeneratedCodeOnlyType_Declaration_IsHiddenAndExperimental(Type type)
    {
        ExperimentalAttribute? experimental = type.GetCustomAttribute<ExperimentalAttribute>();

        await Assert.That(type.GetCustomAttribute<EditorBrowsableAttribute>()?.State).IsEqualTo(EditorBrowsableState.Never);
        await Assert.That(experimental?.DiagnosticId).IsEqualTo("SGIOCEXP001");
        await Assert.That(experimental?.Message).Contains("SourceGen.Ioc generates");
    }

    [Test]
    public async Task Attributes_Defaults_MatchSpec()
    {
        IocContainerAttribute container = new();
        IocRegisterAttribute register = new(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped);
        IocInjectAttribute inject = new();

        await Assert.That(container.IntegrateServiceProvider).IsTrue();
        await Assert.That(container.ExplicitOnly).IsFalse();
        await Assert.That(container.IncludeTags).IsEmpty();
        await Assert.That(container.ThreadSafeStrategy).IsEqualTo(ThreadSafeStrategy.Lock);
        await Assert.That(container.EagerResolveOptions).IsEqualTo(EagerResolveOptions.Singleton);
        await Assert.That(register.KeyType).IsEqualTo(KeyType.Value);
        await Assert.That(register.Key).IsNull();
        await Assert.That(register.ServiceTypes).IsEmpty();
        await Assert.That(register.Decorators).IsEmpty();
        await Assert.That(register.Tags).IsEmpty();
        await Assert.That(inject.LookupMode).IsEqualTo(Microsoft.Extensions.DependencyInjection.ServiceKeyLookupMode.ExplicitKey);
    }

    [Test]
    public async Task Enums_Values_MatchSpec()
    {
        await Assert.That(Values<KeyType>()).IsEquivalentTo(["Value=0", "Csharp=1"]);
        await Assert.That(Values<EagerResolveOptions>()).IsEquivalentTo(["None=0", "Singleton=1", "Scoped=2", "SingletonAndScoped=3"]);
        await Assert.That(Values<ThreadSafeStrategy>()).IsEquivalentTo(["None=0", "Lock=1", "CompareExchange=2"]);
        await Assert.That(typeof(EagerResolveOptions).GetCustomAttribute<FlagsAttribute>()).IsNotNull();
    }

    private static string[] Values<T>()
        where T : struct, Enum
        => [.. Enum.GetValues<T>().Select(static v => $"{v}={Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)}")];
}