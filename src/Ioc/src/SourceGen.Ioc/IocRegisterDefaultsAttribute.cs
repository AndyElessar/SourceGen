using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Sepcifies default settings for types marked with <see cref="IocRegisterAttribute"/> and implement/inherit <see cref="TargetServiceType"/>.
/// </summary>
/// <param name="targetServiceType">Find types marked with <see cref="IocRegisterAttribute"/> and implement/inherit <see cref="TargetServiceType"/>.
/// Apply settings from this attribute as defaults.</param>
/// <param name="lifetime">The lifetime with which the service should be registered in the dependency injection container.
/// Determines the scope of the service instance.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterDefaultsAttribute(Type targetServiceType, ServiceLifetime lifetime) : Attribute
{
    /// <summary>
    /// Gets the target service type. Types marked with <see cref="IocRegisterAttribute"/> that implement/inherit this type
    /// will have settings from this attribute applied as defaults.
    /// </summary>
    public Type TargetServiceType { get; } = targetServiceType;

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <summary>
    /// Gets the generic factory type mapping for the factory method.
    /// The first type is the service type template with placeholders,
    /// subsequent types are placeholder types mapping to factory method type parameters.
    /// </summary>
    public Type[]? GenericFactoryTypeMapping { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <summary>
    /// Gets implementation types to consider when applying the defaults.
    /// </summary>
    public Type[] ImplementationTypes { get; init; } = [];
}

#if NET7_0_OR_GREATER

/// <summary>
/// Sepcifies default settings for types marked with <see cref="IocRegisterAttribute{T}"/> and implement/inherit <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">Find types marked with <see cref="IocRegisterAttribute{T}"/> and implement/inherit <typeparamref name="T"/>.
/// Apply settings from this attribute as defaults.</typeparam>
/// <param name="lifetime">The lifetime with which the service should be registered in the dependency injection container.
/// Determines the scope of the service instance.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterDefaultsAttribute<T>(ServiceLifetime lifetime) : Attribute
{
    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; } = lifetime;

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <summary>
    /// Gets the generic factory type mapping for the factory method.
    /// The first type is the service type template with placeholders,
    /// subsequent types are placeholder types mapping to factory method type parameters.
    /// </summary>
    public Type[]? GenericFactoryTypeMapping { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <inheritdoc cref="IocRegisterDefaultsAttribute.ImplementationTypes"/>
    public Type[] ImplementationTypes { get; init; } = [];
}

#endif
