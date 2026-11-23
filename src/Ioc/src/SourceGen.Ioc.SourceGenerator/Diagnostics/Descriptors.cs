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

    /// <summary>SGIOC025: an assembly-or-class attribute is on a class that is neither a module nor a container.</summary>
    public static readonly DiagnosticDescriptor AttributeOnPlainClass = Create(
        "SGIOC025",
        "Attribute on a class that is neither module nor container",
        "[{0}] on a class has effect only when the class has [IocModule] or [IocContainer]",
        DiagnosticSeverity.Error);

    /// <summary>SGIOC108: the <c>Add{Name}</c> method has no valid name.</summary>
    public static readonly DiagnosticDescriptor InvalidAddMethodName = Create(
        "SGIOC108",
        "Invalid Add method name",
        "'{0}' does not give a valid name for the Add method: {1}",
        DiagnosticSeverity.Error);

    /// <summary>Gets the descriptors by ID.</summary>
    public static IReadOnlyDictionary<string, DiagnosticDescriptor> ById { get; } = new[]
    {
        InconsistentRegistration,
        ConstructorNotDetermined,
        InvalidImplementationType,
        InvalidServiceType,
        WrapperServiceType,
        AttributeOnPlainClass,
        InvalidAddMethodName,
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
