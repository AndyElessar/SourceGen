using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies <paramref name="targetType"/> should be registered in the dependency injection container.
/// </summary>
/// <param name="targetType">Specifies which type should be registered in the dependency injection container.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("NEVER")]
public sealed class IoCRegisterForAttribute(Type targetType) : Attribute
{
    /// <summary>
    /// Specifies which type should be registered in the dependency injection container.
    /// </summary>
    public Type TargetType { get; } = targetType;

    /// <inheritdoc cref="IoCRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.KeyType"/>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <inheritdoc cref="IoCRegisterAttribute.Key"/>
    public object? Key { get; init; }
}
