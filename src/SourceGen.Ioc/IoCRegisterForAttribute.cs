using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies <paramref name="targetType"/> should be registered in the dependency injection container.
/// </summary>
/// <param name="targetType">Specifies which type should be registered in the dependency injection container.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IoCRegisterForAttribute(Type targetType) : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IoCRegisterForAttribute"/> class.
    /// </summary>
    /// <param name="targetType">Specifies which type should be registered in the dependency injection container.</param>
    /// <param name="lifetime">Specifies the service lifetime for the registration.</param>
    public IoCRegisterForAttribute(Type targetType, ServiceLifetime lifetime)
        : this(targetType)
    {
        this.Lifetime = lifetime;
    }

    /// <summary>
    /// Gets the type that should be registered in the dependency injection container.
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

    /// <inheritdoc cref="IoCRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.TagOnly"/>
    public bool TagOnly { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.Instance"/>
    public string? Instance { get; init; }
}

#if NET7_0_OR_GREATER

/// <summary>
/// Specifies <typeparamref name="T"/> should be registered in the dependency injection container.
/// </summary>
/// <typeparam name="T">Specifies which type should be registered in the dependency injection container.</typeparam>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IoCRegisterForAttribute<T> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IoCRegisterForAttribute{}"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    public IoCRegisterForAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IoCRegisterForAttribute{}"/> class.
    /// </summary>
    /// <param name="lifetime">Specifies the service lifetime for the registration.</param>
    public IoCRegisterForAttribute(ServiceLifetime lifetime)
    {
        this.Lifetime = lifetime;
    }

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

    /// <inheritdoc cref="IoCRegisterAttribute.Decorators"/>
    public Type[] Decorators { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.TagOnly"/>
    public bool TagOnly { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <inheritdoc cref="IoCRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <inheritdoc cref="IoCRegisterAttribute.Instance"/>
    public string? Instance { get; init; }
}

#endif