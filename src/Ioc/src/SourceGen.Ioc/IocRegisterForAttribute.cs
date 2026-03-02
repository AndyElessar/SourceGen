using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies <paramref name="implementationType"/> should be registered in the dependency injection container.
/// </summary>
/// <param name="implementationType">Specifies which type should be registered in the dependency injection container.</param>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterForAttribute(Type implementationType) : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterForAttribute"/> class.
    /// </summary>
    /// <param name="implementationType">Specifies which type should be registered in the dependency injection container.</param>
    /// <param name="lifetime">Specifies the service lifetime for the registration.</param>
    public IocRegisterForAttribute(Type implementationType, ServiceLifetime lifetime)
        : this(implementationType)
    {
        this.Lifetime = lifetime;
    }

    /// <summary>
    /// Gets the type that should be registered in the dependency injection container.
    /// </summary>
    public Type ImplementationType { get; } = implementationType;

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

    /// <inheritdoc cref="IocRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <summary>
    /// Gets the members to inject via dependency injection.
    /// Each element is either:
    /// <list type="bullet">
    /// <item><description><c>nameof(member)</c>: inject without key</description></item>
    /// <item><description><c>new object[] { nameof(member), key }</c>: inject with keyed service (KeyType = Value)</description></item>
    /// <item><description><c>new object[] { nameof(member), key, KeyType.Csharp }</c>: inject with explicit KeyType</description></item>
    /// </list>
    /// </summary>
    public object[]? InjectMembers { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <summary>
    /// Gets the generic factory type mapping for the factory method.<br/>
    /// The first type is the service type template with placeholders,
    /// subsequent types are placeholder types mapping to factory method type parameters.
    /// </summary>
    /// <remarks>
    /// <code>
    /// Define:
    /// [IocRegisterFor(typeof(IRequestHandler❮❯),
    ///     Factory = nameof(FactoryContainer.Create),
    ///     GenericFactoryTypeMapping = [typeof(IRequestHandler❮Task❮int❯❯), typeof(int)])]
    /// public class FactoryContainer                                 ↑              ↑
    /// {                                                             └--------------┘
    ///                                     "int" is a placeholder, make sure placeholders is unique
    ///                                      in the context of the generic type mapping.
    ///     public static Create❮T❯() = new Handler❮T❯();
    /// }
    ///
    /// Generate:
    /// services.AddSingleton❮IRequestHandler❮Task❮Entity❯❯❯(sp => FactoryContainer.Create❮Entity❯());
    /// </code>
    /// </remarks>
    public Type[]? GenericFactoryTypeMapping { get; init; }

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
    /// Default lifetime is Transient.
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

    /// <inheritdoc cref="IocRegisterAttribute.Tags"/>
    public string[] Tags { get; init; } = [];

    /// <inheritdoc cref="IocRegisterForAttribute.InjectMembers"/>
    public object[]? InjectMembers { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Factory"/>
    public string? Factory { get; init; }

    /// <inheritdoc cref="IocRegisterForAttribute.GenericFactoryTypeMapping"/>
    public Type[]? GenericFactoryTypeMapping { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.Instance"/>
    public string? Instance { get; init; }
}

#endif
