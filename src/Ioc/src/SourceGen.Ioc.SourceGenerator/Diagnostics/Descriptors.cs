namespace SourceGen.Ioc.Diagnostics;

/// <summary>
/// The descriptors of the IoC diagnostics (Diagnostics.spec). Each slice of the rewrite adds the descriptors that it
/// reports, together with a row in <c>AnalyzerReleases.Unshipped.md</c>.
/// </summary>
internal static class Descriptors
{
    /// <summary>The category of every descriptor.</summary>
    public const string Category = "SourceGen.Ioc";

    /// <summary>SGIOC001: the attributes of one instance definition do not agree.</summary>
    public static readonly DiagnosticDescriptor InconsistentRegistration = Create(
        "SGIOC001",
        "Inconsistent registration settings",
        "'{0}' is registered with different {1}; all attributes for the same implementation and key must agree",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC002: no single constructor can be selected.</summary>
    public static readonly DiagnosticDescriptor ConstructorNotDetermined = Create(
        "SGIOC002",
        "Constructor cannot be determined",
        "'{0}' has {1}; mark exactly one constructor with [IocInject]",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC003: the implementation type is not valid.</summary>
    public static readonly DiagnosticDescriptor InvalidImplementationType = Create(
        "SGIOC003",
        "Invalid implementation type",
        "'{0}' cannot be an implementation or decorator type: {1}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC004: a service type is not valid.</summary>
    public static readonly DiagnosticDescriptor InvalidServiceType = Create(
        "SGIOC004",
        "Invalid service type",
        "'{0}' cannot be a service type of '{1}': {2}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC006: a wrapper type is given as a service type.</summary>
    public static readonly DiagnosticDescriptor WrapperServiceType = Create(
        "SGIOC006",
        "Wrapper type cannot be registered",
        "'{0}' is generated automatically and cannot be registered; wrap it in a class or depend on it directly",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC007: <c>KeyedService.AnyKey</c> is used as a registration key.</summary>
    public static readonly DiagnosticDescriptor AnyKeyRegistration = Create(
        "SGIOC007",
        "AnyKey cannot be used to register",
        "KeyedService.AnyKey cannot be used as a registration key",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC011: an argument that needs a symbol is not a <c>nameof()</c> expression of the right member.</summary>
    public static readonly DiagnosticDescriptor NameofTargetNotFound = Create(
        "SGIOC011",
        "nameof target not found",
        "{0} must be a nameof() expression that refers to {1}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC022: the type of a key value is not supported.</summary>
    public static readonly DiagnosticDescriptor UnsupportedKey = Create(
        "SGIOC022",
        "Unsupported key value",
        "A key of type '{0}' is not supported",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC025: an assembly-or-class attribute is on a class that is neither a module nor a container.</summary>
    public static readonly DiagnosticDescriptor AttributeOnPlainClass = Create(
        "SGIOC025",
        "Attribute on a class that is neither module nor container",
        "[{0}] on a class has effect only when the class has [IocModule] or [IocContainer]",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC107: the class cannot be a module or a container.</summary>
    public static readonly DiagnosticDescriptor UnsupportedOutputClass = Create(
        "SGIOC107",
        "Unsupported module or container class",
        "'{0}' cannot be a {1}: {2}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC401: a single dependency has more than one candidate.</summary>
    public static readonly DiagnosticDescriptor AmbiguousDependency = Create(
        "SGIOC401",
        "Ambiguous dependency",
        "'{0}' requires a single '{1}' but {2} registrations provide it: {3}; use a key or depend on a collection",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC402: a required dependency has no candidate and the container has no fallback provider.</summary>
    public static readonly DiagnosticDescriptor MissingDependency = Create(
        "SGIOC402",
        "Missing dependency",
        "'{0}' requires '{1}', which is not registered",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC108: the service collection extension has no valid name.</summary>
    public static readonly DiagnosticDescriptor InvalidServiceCollectionExtensionName = Create(
        "SGIOC108",
        "Invalid Add method name",
        "'{0}' does not give a valid name for the Add method: {1}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC301: a cycle of dependencies that are not deferred.</summary>
    public static readonly DiagnosticDescriptor CircularDependency = Create(
        "SGIOC301",
        "Circular dependency",
        "Circular dependency: {0}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC302: a Singleton depends on a Scoped service, also through Transient services.</summary>
    public static readonly DiagnosticDescriptor CaptiveDependency = Create(
        "SGIOC302",
        "Captive dependency",
        "Singleton '{0}' depends on scoped '{1}'{2}",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC403: a Transient closed definition is disposable, and the container does not track it.</summary>
    public static readonly DiagnosticDescriptor DisposableTransient = Create(
        "SGIOC403",
        "Disposable Transient",
        "Transient '{0}' is disposable ({1}); the container does not track transient services",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC501: a required dependency is not registered in the container and comes from the fallback provider.</summary>
    public static readonly DiagnosticDescriptor FallbackDependency = Create(
        "SGIOC501",
        "Dependency resolved from the fallback provider",
        "'{0}' requires '{1}', which is not registered in the container; it is resolved from the fallback service provider",
        DiagnosticSeverity.Info);

    /// <summary>SGIOC502: a Singleton holds a Transient instance.</summary>
    public static readonly DiagnosticDescriptor SingletonHoldsTransient = Create(
        "SGIOC502",
        "Singleton holds a transient service",
        "Singleton '{0}' holds transient '{1}' for the lifetime of the container; depend on Func<{2}> to create instances on demand",
        DiagnosticSeverity.Warning);

    /// <summary>SGIOC503: a Scoped service holds a Transient instance.</summary>
    public static readonly DiagnosticDescriptor ScopedHoldsTransient = Create(
        "SGIOC503",
        "Scoped service holds a transient service",
        "Scoped '{0}' holds transient '{1}' for the lifetime of the scope; depend on Func<{2}> to create instances on demand",
        DiagnosticSeverity.Warning);

    /// <summary>Gets the descriptors by ID.</summary>
    public static IReadOnlyDictionary<string, DiagnosticDescriptor> ById { get; } = new[]
    {
        InconsistentRegistration,
        ConstructorNotDetermined,
        InvalidImplementationType,
        InvalidServiceType,
        WrapperServiceType,
        AnyKeyRegistration,
        NameofTargetNotFound,
        UnsupportedKey,
        AttributeOnPlainClass,
        UnsupportedOutputClass,
        InvalidServiceCollectionExtensionName,
        CircularDependency,
        CaptiveDependency,
        AmbiguousDependency,
        MissingDependency,
        DisposableTransient,
        FallbackDependency,
        SingletonHoldsTransient,
        ScopedHoldsTransient,
    }.ToDictionary(static d => d.Id);

    /// <summary>
    /// Creates a descriptor by the rules of Diagnostics.spec, "Descriptors": an Error has
    /// <see cref="WellKnownDiagnosticTags.NotConfigurable"/>, so that configuration cannot lower or disable it. A Warning
    /// or an Info does not have it, so that <c>#pragma</c> and the configuration work.
    /// </summary>
    /// <param name="id">The diagnostic ID.</param>
    /// <param name="title">The title.</param>
    /// <param name="messageFormat">The message format.</param>
    /// <param name="severity">The severity.</param>
    /// <returns>The descriptor.</returns>
    internal static DiagnosticDescriptor Create(string id, string title, string messageFormat, DiagnosticSeverity severity)
        => new(
            id,
            title,
            messageFormat,
            Category,
            severity,
            isEnabledByDefault: true,
            customTags: severity == DiagnosticSeverity.Error ? [WellKnownDiagnosticTags.NotConfigurable] : []);
}
