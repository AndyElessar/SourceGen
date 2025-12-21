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
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
[Conditional("NEVER")]
public sealed class IoCRegisterDefaultSettingsAttribute(Type targetServiceType, ServiceLifetime lifetime) : Attribute
{
    /// <summary>
    /// Find types marked with <see cref="IoCRegisterAttribute"/> and implement/inherit <see cref="TargetServiceType"/>.
    /// Apply settings from this attribute as defaults.
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
}
