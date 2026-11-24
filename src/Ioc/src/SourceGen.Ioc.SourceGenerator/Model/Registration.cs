namespace SourceGen.Ioc.Model;

/// <summary>The lifetime of an instance definition. The values are the same as <c>ServiceLifetime</c>.</summary>
internal enum Lifetime
{
    /// <summary>One instance for the root.</summary>
    Singleton = 0,

    /// <summary>One instance for each scope.</summary>
    Scoped = 1,

    /// <summary>A new instance for each resolution.</summary>
    Transient = 2,
}

/// <summary>The disposal kind of an implementation type (Analysis.Model.spec, "Disposal kind").</summary>
[Flags]
internal enum Disposal
{
    /// <summary>No disposal is necessary.</summary>
    None = 0,

    /// <summary>The type implements <c>IDisposable</c>.</summary>
    Sync = 1,

    /// <summary>The type implements <c>IAsyncDisposable</c>.</summary>
    Async = 2,
}

/// <summary>
/// A registration declaration of an implementation type (Annotations.spec, "Registration declarations"), with the
/// dependencies of its selected constructor.
/// </summary>
/// <param name="Implementation">The implementation type: its identity, with the key.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="ServiceTypes">The service types that the attribute gives. The implementation type itself is not in the list.</param>
/// <param name="ConstructorParameters">The parameters of the selected constructor, in order.</param>
/// <param name="Members">The injected members, in the order of the steps (Annotations.spec, "Order").</param>
/// <param name="Disposal">The disposal kind of the implementation type.</param>
/// <param name="IsError">
/// Whether the declaration is invalid. It keeps only the valid service types and has no parameters. It makes the complete instance
/// definition an error node.
/// </param>
internal sealed record RegistrationDeclaration(
    NamedTypeRef Implementation,
    KeyRef? Key,
    Lifetime Lifetime,
    ImmutableEquatableArray<TypeRef> ServiceTypes,
    ImmutableEquatableArray<Dependency> ConstructorParameters,
    ImmutableEquatableArray<InjectedMember> Members,
    Disposal Disposal = Disposal.None,
    bool IsError = false);

/// <summary>A closed definition after merging: the input of the analysis and of the generated code.</summary>
/// <param name="Implementation">The implementation type.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="ServiceTypes">The service types: the implementation type first, then the others by sort name.</param>
/// <param name="ConstructorParameters">The constructor parameters, in order.</param>
/// <param name="Members">The injected members, in the order of the steps.</param>
/// <param name="Disposal">The disposal kind of the implementation type.</param>
internal sealed record ClosedDefinition(
    NamedTypeRef Implementation,
    KeyRef? Key,
    Lifetime Lifetime,
    ImmutableEquatableArray<TypeRef> ServiceTypes,
    ImmutableEquatableArray<Dependency> ConstructorParameters,
    ImmutableEquatableArray<InjectedMember> Members,
    Disposal Disposal)
{
    /// <summary>
    /// All dependency sites: the constructor parameters, then the dependencies of the members in their order. An index
    /// into this list identifies a dependency site of the definition.
    /// </summary>
    public ImmutableEquatableArray<Dependency> Dependencies { get; } = [.. ConstructorParameters, .. Members.SelectMany(static m => m.Dependencies)];

    /// <summary>Whether a step after <c>new</c> can throw: an assignment or an injection method.</summary>
    public bool HasStepsAfterNew => Members.Any(static m => m.Kind != MemberKind.Initializer);
}

/// <summary>The step of an injected member (Annotations.spec, "Order").</summary>
internal enum MemberKind
{
    /// <summary>A <c>required</c> or <c>init</c> property or field: assigned in the object initializer.</summary>
    Initializer,

    /// <summary>A field or property that is assigned after construction.</summary>
    Assignment,

    /// <summary>A synchronous injection method.</summary>
    Method,
}

/// <summary>An injected member (Annotations.spec, "Injected members").</summary>
/// <param name="Kind">The step.</param>
/// <param name="Name">The name of the member, as a C# identifier (a keyword has <c>@</c>).</param>
/// <param name="Dependencies">The dependency of a field or property, or the parameters of a method.</param>
internal sealed record InjectedMember(MemberKind Kind, string Name, ImmutableEquatableArray<Dependency> Dependencies);

/// <summary>The optional mode of a dependency site (Annotations.spec, "Extraction of dependency sites").</summary>
internal enum OptionalMode
{
    /// <summary>A required dependency.</summary>
    Required,

    /// <summary>A partial accessor that returns a reference type annotated as nullable: it gets <see langword="null"/> when no service is found.</summary>
    Nullable,

    /// <summary>A parameter with a default value: it gets the default value when no service is found.</summary>
    Default,

    /// <summary>
    /// An injected field or property with an initializer: it is not assigned when no service is found, so the initial
    /// value stays.
    /// </summary>
    KeepInitializer,
}

/// <summary>The key specification of a dependency site (Annotations.spec, "Key specification").</summary>
internal enum SiteKeyMode
{
    /// <summary>No key.</summary>
    None,

    /// <summary>The key that the site specifies.</summary>
    Specified,

    /// <summary>The key of the instance definition that owns the site: <c>InheritKey</c>. Resolved with the definition.</summary>
    Inherit,

    /// <summary><c>[ServiceKey]</c>: not a dependency. The parameter receives the key of the instance definition.</summary>
    ServiceKey,
}

/// <summary>
/// A dependency site of a closed definition: a constructor parameter, an injected field or property, or a parameter of
/// an injection method.
/// </summary>
/// <param name="Type">The requested type.</param>
/// <param name="Name">The name of the parameter or member, for the messages of diagnostics.</param>
/// <param name="ByReference">
/// Whether it is an <c>in</c> or <c>ref readonly</c> parameter. The generated code passes it a local variable with
/// <c>in</c>: a value passed to a <c>ref readonly</c> parameter gives warning CS9193.
/// </param>
/// <param name="Optional">The optional mode.</param>
/// <param name="DefaultValue">
/// For <see cref="OptionalMode.Default"/>, the C# expression of the default value, without a cast to the parameter type.
/// </param>
/// <param name="IsValueType">Whether the type is a value type: the generic <c>GetService&lt;T&gt;()</c> cannot ask for it.</param>
/// <param name="KeyMode">The key specification.</param>
/// <param name="Key">The key, for <see cref="SiteKeyMode.Specified"/>.</param>
internal sealed record Dependency(
    TypeRef Type,
    string Name,
    bool ByReference,
    OptionalMode Optional = OptionalMode.Required,
    string? DefaultValue = null,
    bool IsValueType = false,
    SiteKeyMode KeyMode = SiteKeyMode.None,
    KeyRef? Key = null)
{
    /// <summary>The key that the site asks for in <paramref name="owner"/>: <c>InheritKey</c> takes the key of the owner.</summary>
    /// <param name="owner">The closed definition that owns the site.</param>
    /// <returns>The key, or <see langword="null"/> for no key.</returns>
    public KeyRef? KeyIn(ClosedDefinition owner) => KeyMode switch
    {
        SiteKeyMode.Specified => Key,
        SiteKeyMode.Inherit => owner.Key,
        _ => null,
    };
}

/// <summary>The settings of the service collection extension, read from <c>IocServiceCollectionExtensionsAttribute</c>.</summary>
/// <param name="Name">The <c>{Name}</c> part, already reduced to identifier characters.</param>
/// <param name="Namespace">The namespace of the generated class.</param>
/// <param name="AssemblyName">The name of this assembly.</param>
internal sealed record ServiceCollectionExtensionSettings(string Name, string Namespace, string AssemblyName);

/// <summary>Everything that the code of the service collection extension needs.</summary>
/// <param name="Settings">The settings.</param>
/// <param name="Definitions">The closed definitions in their ordering.</param>
internal sealed record ServiceCollectionExtensionModel(ServiceCollectionExtensionSettings Settings, ImmutableEquatableArray<ClosedDefinition> Definitions);
