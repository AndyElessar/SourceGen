using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Sepcifies default settings for types marked with <see cref="IoCRegisterAttribute"/> and implement/inherit <see cref="TargetServiceType"/>.
/// </summary>
/// <param name="targetServiceType">Find types marked with <see cref="IoCRegisterAttribute"/> and implement/inherit <see cref="TargetServiceType"/>.
/// Apply settings from this attribute as defaults.</param>
/// <param name="lifetime">The lifetime with which the service should be registered in the dependency injection container.
/// Determines the scope of the service instance.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IoCRegisterDefaultsAttribute(Type targetServiceType, ServiceLifetime lifetime) : Attribute
{
    /// <summary>
    /// Gets the target service type. Types marked with <see cref="IoCRegisterAttribute"/> that implement/inherit this type
    /// will have settings from this attribute applied as defaults.
    /// </summary>
    public Type TargetServiceType { get; } = targetServiceType;

    /// <inheritdoc cref="IoCRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <inheritdoc cref="IoCRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.ExcludeFromDefault"/>
    public bool ExcludeFromDefault { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];
}

#if NET7_0_OR_GREATER

/// <summary>
/// Sepcifies default settings for types marked with <see cref="IoCRegisterAttribute{}"/> and implement/inherit <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">Find types marked with <see cref="IoCRegisterAttribute{}"/> and implement/inherit <typeparamref name="T"/>.
/// Apply settings from this attribute as defaults.</typeparam>
/// <param name="lifetime">The lifetime with which the service should be registered in the dependency injection container.
/// Determines the scope of the service instance.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IoCRegisterDefaultsAttribute<T>(ServiceLifetime lifetime) : Attribute
{
    /// <inheritdoc cref="IoCRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <inheritdoc cref="IoCRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.ExcludeFromDefault"/>
    public bool ExcludeFromDefault { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];
}

#endif