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

/// <summary>The disposal kind of the object that a source gives (Analysis.Model.spec, "Disposal kind").</summary>
[Flags]
internal enum Disposal
{
    /// <summary>No disposal is necessary.</summary>
    None = 0,

    /// <summary>The type implements <c>IDisposable</c>.</summary>
    Sync = 1,

    /// <summary>The type implements <c>IAsyncDisposable</c>.</summary>
    Async = 2,

    /// <summary>
    /// The return type of a factory does not show whether the object implements one of the interfaces: the disposal
    /// tests the type of the object at run time.
    /// </summary>
    Runtime = 4,
}

/// <summary>The kind of the source of an instance definition (Constitution, "Terms").</summary>
internal enum SourceKind
{
    /// <summary>An implementation type, created with <c>new</c>.</summary>
    Implementation,

    /// <summary>A static factory method.</summary>
    Factory,

    /// <summary>A static field or property that holds the instance.</summary>
    Instance,
}

/// <summary>
/// The source of an instance definition. With the key, it is the identity of the instance definition (Analysis.Model.spec,
/// "Identity").
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="Type">The implementation type, or the type that contains the factory method or the instance member.</param>
/// <param name="Member">The name of the factory method or of the instance member. Empty for an implementation type.</param>
/// <param name="Parameters">The parameters of the factory method. Empty for the other kinds.</param>
/// <param name="Arity">The number of type parameters of the factory method.</param>
internal sealed record DefinitionSource(SourceKind Kind, NamedTypeRef Type, string Member, ImmutableEquatableArray<SourceParameter> Parameters, int Arity = 0)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionSource"/> class for a <c>with</c> expression. It copies the
    /// members but not the cached strings: the copy can have other members, so it builds its own.
    /// </summary>
    /// <param name="original">The original.</param>
    private DefinitionSource(DefinitionSource original)
    {
        Kind = original.Kind;
        Type = original.Type;
        Member = original.Member;
        Parameters = original.Parameters;
        Arity = original.Arity;
    }

    /// <summary>Gets the source of an implementation type.</summary>
    /// <param name="implementation">The implementation type.</param>
    /// <returns>The source.</returns>
    public static DefinitionSource Of(NamedTypeRef implementation) => new(SourceKind.Implementation, implementation, string.Empty, []);

    /// <summary>
    /// Gets the sort name (Analysis.Model.spec, "Ordering"): the sort name of the implementation type,
    /// <c>{containing type}.{method name}({parameter type,...})</c> for a factory, <c>{containing type}.{member name}</c>
    /// for an instance.
    /// </summary>
    /// <remarks>The string is built on the first read and cached. The cache is not part of the equality.</remarks>
    public string SortName => field ??= Build(withAssembly: false);

    /// <summary>Gets the sort name written with the full canonical strings, which the ordering compares last.</summary>
    /// <remarks>The string is built on the first read and cached. The cache is not part of the equality.</remarks>
    public string CanonicalString => field ??= Build(withAssembly: true);

    /// <summary>Compares by value. The cached strings are not part of the equality.</summary>
    /// <param name="other">The other source.</param>
    /// <returns>Whether the sources are equal.</returns>
    public bool Equals(DefinitionSource? other)
        => other is not null && Kind == other.Kind && Arity == other.Arity && Member == other.Member && Type.Equals(other.Type) && Parameters.Equals(other.Parameters);

    /// <inheritdoc/>
    public override int GetHashCode() => ((((int)Kind * 397) ^ Type.GetHashCode()) * 397) ^ Member.GetHashCode();

    private string Build(bool withAssembly)
    {
        string type = withAssembly ? Type.CanonicalString : Type.SortName;
        return Kind switch
        {
            SourceKind.Implementation => type,
            SourceKind.Factory => $"{type}.{Member}{(Arity > 0 ? $"`{Arity}" : string.Empty)}({string.Join(",", Parameters.Select(p => p.Prefix + (withAssembly ? p.Type.CanonicalString : p.Type.SortName)))})",
            _ => $"{type}.{Member}",
        };
    }
}

/// <summary>A parameter of a factory method in the identity of its source.</summary>
/// <param name="Type">The parameter type.</param>
/// <param name="Prefix">The ref kind: empty, <c>in </c>, <c>ref readonly </c>, <c>ref </c>, or <c>out </c>.</param>
internal readonly record struct SourceParameter(TypeRef Type, string Prefix);

/// <summary>
/// A registration declaration (Annotations.spec, "Registration declarations"), with the dependencies of its selected
/// constructor.
/// </summary>
/// <param name="Source">The source: the identity of the declaration, with the key.</param>
/// <param name="InstanceType">The type of the object that the source gives: the implementation type.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="ServiceTypes">The service types that the attribute gives. The implementation type itself is not in the list.</param>
/// <param name="ConstructorParameters">The parameters of the selected constructor, in order.</param>
/// <param name="Members">The injected members, in the order of the steps (Annotations.spec, "Order").</param>
/// <param name="Disposal">The disposal kind of the object that the source gives.</param>
/// <param name="IsError">
/// Whether the declaration is invalid. It keeps only the valid service types and has no parameters. It makes the complete instance
/// definition an error node.
/// </param>
internal sealed record RegistrationDeclaration(
    DefinitionSource Source,
    TypeRef InstanceType,
    KeyRef? Key,
    Lifetime Lifetime,
    ImmutableEquatableArray<TypeRef> ServiceTypes,
    ImmutableEquatableArray<Dependency> ConstructorParameters,
    ImmutableEquatableArray<InjectedMember> Members,
    Disposal Disposal = Disposal.None,
    bool IsError = false)
{
    /// <summary>Gets all service types: the implementation type itself first, then <see cref="ServiceTypes"/>.</summary>
    public IEnumerable<TypeRef> AllServiceTypes => Source.Kind == SourceKind.Implementation ? ServiceTypes.Prepend(InstanceType) : ServiceTypes;
}

/// <summary>A closed definition after merging: the input of the analysis and of the generated code.</summary>
/// <param name="Source">The source: the identity, with the key.</param>
/// <param name="InstanceType">The type of the object that the source gives: the implementation type.</param>
/// <param name="Key">The key, or <see langword="null"/> for no key.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="ServiceTypes">The service types: the implementation type first, then the others by sort name.</param>
/// <param name="ConstructorParameters">The constructor parameters, in order.</param>
/// <param name="Members">The injected members, in the order of the steps.</param>
/// <param name="Disposal">The disposal kind of the object that the source gives.</param>
internal sealed record ClosedDefinition(
    DefinitionSource Source,
    TypeRef InstanceType,
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

    /// <summary>The key of the dependency site at <paramref name="index"/> of <see cref="Dependencies"/>.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The key.</returns>
    public SitePosition SiteOf(int index)
    {
        if(index < ConstructorParameters.Length)
        {
            return new SitePosition(-1, 0, index);
        }

        index -= ConstructorParameters.Length;
        foreach(InjectedMember member in Members)
        {
            if(index < member.Dependencies.Length)
            {
                return new SitePosition(member.Depth, member.Index, index);
            }

            index -= member.Dependencies.Length;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

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
/// <param name="Depth">
/// The number of base classes of the type that declares the member. With <paramref name="Index"/>, it identifies the
/// member and gives its order inside one step: base members first (Annotations.spec, "Order").
/// </param>
/// <param name="Index">The position of the member in <c>GetMembers()</c> of the type that declares it.</param>
internal sealed record InjectedMember(MemberKind Kind, string Name, ImmutableEquatableArray<Dependency> Dependencies, int Depth, int Index);

/// <summary>
/// Identifies a dependency site of an implementation type, independent of which other members are injected: a
/// constructor parameter, or a parameter of an injected member (a field or property has one, at position 0).
/// </summary>
/// <param name="Depth">The <see cref="InjectedMember.Depth"/> of the member, or -1 for a constructor parameter.</param>
/// <param name="Member">The <see cref="InjectedMember.Index"/> of the member, or 0 for a constructor parameter.</param>
/// <param name="Parameter">The position of the parameter.</param>
internal readonly record struct SitePosition(int Depth, int Member, int Parameter);

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
