using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies <paramref name="targetType"/> should be registered in the dependency injection container.
/// </summary>
/// <param name="targetType">Specifies which type should be registered in the dependency injection container.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterForAttribute(Type targetType) : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterForAttribute"/> class.
    /// </summary>
    /// <param name="targetType">Specifies which type should be registered in the dependency injection container.</param>
    /// <param name="lifetime">Specifies the service lifetime for the registration.</param>
    public IocRegisterForAttribute(Type targetType, ServiceLifetime lifetime)
        : this(targetType)
    {
        this.Lifetime = lifetime;
    }

    /// <summary>
    /// Gets the type that should be registered in the dependency injection container.
    /// </summary>
    public Type TargetType { get; } = targetType;

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.KeyType"/>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <inheritdoc cref="IocRegisterAttribute.Key"/>
    public object? Key { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.TagOnly"/>
    public bool TagOnly { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Instance"/>
    public string? Instance { get; init; }
}

#if NET7_0_OR_GREATER

/// <summary>
/// Specifies <typeparamref name="T"/> should be registered in the dependency injection container.
/// </summary>
/// <typeparam name="T">Specifies which type should be registered in the dependency injection container.</typeparam>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterForAttribute<T> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterForAttribute{T}"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    public IocRegisterForAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterForAttribute{T}"/> class.
    /// </summary>
    /// <param name="lifetime">Specifies the service lifetime for the registration.</param>
    public IocRegisterForAttribute(ServiceLifetime lifetime)
    {
        this.Lifetime = lifetime;
    }

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.ServiceTypes"/>
    public Type[] ServiceTypes { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.KeyType"/>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <inheritdoc cref="IocRegisterAttribute.Key"/>
    public object? Key { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.TagOnly"/>
    public bool TagOnly { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <inheritdoc cref="IocRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Instance"/>
    public string? Instance { get; init; }
}

#endif