using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Specifies that a class should be registered with a dependency injection container, Default lifetime is Singleton.
/// </summary>
/// <remarks>
/// Apply this attribute to a class to indicate that it should be registered for dependency injection.<br/>
/// You can specify one or more service types to register the class as, and optionally provide a key for keyed registrations.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    /// <param name="serviceTypes">The service types to register the class as.</param>
    public IocRegisterAttribute(params Type[] serviceTypes)
    {
        this.ServiceTypes = serviceTypes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute"/> class.
    /// </summary>
    /// <param name="lifetime">The service lifetime for the registration.</param>
    /// <param name="serviceTypes">The service types to register the class as.</param>
    public IocRegisterAttribute(ServiceLifetime lifetime, params Type[] serviceTypes)
    {
        this.Lifetime = lifetime;
        this.ServiceTypes = serviceTypes;
    }

    /// <summary>
    /// Gets the lifetime with which the service should be registered in the dependency injection container.
    /// Determines the scope of the service instance.
    /// </summary>
    public ServiceLifetime Lifetime { get; init; }

    /// <summary>
    /// Gets a value indicating whether to register all interfaces implemented.
    /// </summary>
    public bool RegisterAllInterfaces { get; init; }

    /// <summary>
    /// Gets a value indicating whether to register all base classes inherited.
    /// </summary>
    public bool RegisterAllBaseClasses { get; init; }

    /// <summary>
    /// Gets the collection of service types to register the class as.
    /// </summary>
    public Type[] ServiceTypes { get; init; } = [];

    /// <summary>
    /// Gets a value specifying how to interpret <see cref="Key"/>.
    /// </summary>
    public KeyType KeyType { get; init; } = KeyType.Value;

    /// <summary>
    /// Gets a key for keyed registrations. The interpretation of the key depends on the <see cref="KeyType"/>.<br/>
    /// When <see cref="KeyType"/> is <see cref="KeyType.Value"/>, this should be a primitive value, like <see cref="int"/>, <see cref="string"/> or <see langword="enum"/>.<br/>
    /// When <see cref="KeyType"/> is <see cref="KeyType.Csharp"/>, this should be a C# code snippet. You can use <see langword="nameof"/> for compile time safety.
    /// </summary>
    public object? Key { get; init; }

    /// <summary>
    /// Gets the collection of decorator types to apply to the target type.
    /// </summary>
    /// <remarks>
    /// Each type in the collection should represent a decorator that implements service types.<br/>
    /// The order of decorators in the array determines the order of execute (which means the first decorator in the array is the outermost).
    /// </remarks>
    public Type[] Decorators { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this registration should only appear in tagged extension methods.
    /// When <see langword="true"/>, this service is excluded from the default registration method
    /// and will only be registered in tag-specific methods defined by <see cref="Tags"/>.
    /// </summary>
    public bool TagOnly { get; init; }

    /// <summary>
    /// Gets the collection of tags associated with this registration.<br/>
    /// Will generate registrations for each tag specified.
    /// </summary>
    public string[] Tags { get; init; } = [];

    /// <summary>
    /// Gets the factory method name to be used for creating instances of the service.<br/>
    /// Use string or nameof() to specify the factory method.<br/>
    /// </summary>
    public string? Factory { get; init; }

    /// <summary>
    /// Gets the instance name to be used for creating instances of the service.<br/>
    /// Use string or nameof() to specify the instance name.<br/>
    /// </summary>
    public string? Instance { get; init; }
}

#if NET7_0_OR_GREATER

/// <inheritdoc cref="IocRegisterAttribute"/>
/// <typeparam name="T">The service type to register the class as.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterAttribute<T> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T}"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    public IocRegisterAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T}"/> class.
    /// </summary>
    /// <param name="lifetime">The service lifetime for the registration.</param>
    public IocRegisterAttribute(ServiceLifetime lifetime)
    {
        this.Lifetime = lifetime;
    }

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

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

/// <inheritdoc cref="IocRegisterAttribute"/>
/// <typeparam name="T1"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
/// <typeparam name="T2"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterAttribute<T1, T2> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T1,T2}"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    public IocRegisterAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T1,T2}"/> class.
    /// </summary>
    /// <param name="lifetime">The service lifetime for the registration.</param>
    public IocRegisterAttribute(ServiceLifetime lifetime)
    {
        this.Lifetime = lifetime;
    }

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

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

/// <inheritdoc cref="IocRegisterAttribute"/>
/// <typeparam name="T1"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
/// <typeparam name="T2"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
/// <typeparam name="T3"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterAttribute<T1, T2, T3> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T1,T2,T3}"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    public IocRegisterAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T1,T2,T3}"/> class.
    /// </summary>
    /// <param name="lifetime">The service lifetime for the registration.</param>
    public IocRegisterAttribute(ServiceLifetime lifetime)
    {
        this.Lifetime = lifetime;
    }

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

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

/// <inheritdoc cref="IocRegisterAttribute"/>
/// <typeparam name="T1"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
/// <typeparam name="T2"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
/// <typeparam name="T3"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
/// <typeparam name="T4"><inheritdoc cref="IocRegisterAttribute{T}"/></typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocRegisterAttribute<T1, T2, T3, T4> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T1,T2,T3,T4}"/> class. <br/>
    /// Default lifetime is Singleton.
    /// </summary>
    public IocRegisterAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IocRegisterAttribute{T1,T2,T3,T4}"/> class.
    /// </summary>
    /// <param name="lifetime">The service lifetime for the registration.</param>
    public IocRegisterAttribute(ServiceLifetime lifetime)
    {
        this.Lifetime = lifetime;
    }

    /// <inheritdoc cref="IocRegisterAttribute.Lifetime"/>
    public ServiceLifetime Lifetime { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllInterfaces"/>
    public bool RegisterAllInterfaces { get; init; }

    /// <inheritdoc cref="IocRegisterAttribute.RegisterAllBaseClasses"/>
    public bool RegisterAllBaseClasses { get; init; }

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
