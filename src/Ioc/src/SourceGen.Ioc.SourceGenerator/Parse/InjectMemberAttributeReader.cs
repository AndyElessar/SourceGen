using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>
/// One member that <c>IocInjectMemberAttribute</c> adds to all instance definitions of an implementation type.
/// </summary>
/// <param name="Implementation">The implementation type.</param>
/// <param name="Member">
/// The member, or <see langword="null"/> when the attribute is invalid: all instance definitions of the type are then
/// skipped as error nodes (Diagnostics.spec, "Consequences").
/// </param>
internal sealed record InjectMemberDeclaration(NamedTypeRef Implementation, InjectedMember? Member);

/// <summary>Where an <c>IocInjectMemberAttribute</c> is written, for the checks that need the registrations.</summary>
/// <param name="Implementation">The implementation type.</param>
/// <param name="NoRegistration">SGIOC021, reported when the type has no registration attribute.</param>
/// <param name="ServiceKeyWithoutKey">
/// SGIOC023, reported when the member has a required <c>[ServiceKey]</c> parameter and the type has a registration
/// without a key, or <see langword="null"/>.
/// </param>
/// <param name="Member">The member, or <see langword="null"/> when the attribute is invalid.</param>
/// <param name="Conflict">SGIOC024, reported when another attribute names the same member with other settings.</param>
internal sealed record InjectMemberSite(NamedTypeRef Implementation, DiagnosticInfo NoRegistration, DiagnosticInfo? ServiceKeyWithoutKey, InjectedMember? Member, DiagnosticInfo Conflict);

/// <summary>
/// What the <c>IocInjectMemberAttribute</c>s of one attribute target give. The generation pipeline uses only
/// <see cref="Declarations"/>.
/// </summary>
/// <param name="Declarations">The members.</param>
/// <param name="Sites">The positions of the valid attributes.</param>
/// <param name="ParameterSites">The dependency sites of the members, all at the attribute.</param>
/// <param name="Diagnostics">The diagnostics of the invalid attributes.</param>
internal sealed record InjectMemberResult(
    ImmutableEquatableArray<InjectMemberDeclaration> Declarations,
    ImmutableEquatableArray<InjectMemberSite> Sites,
    ImmutableEquatableArray<ParameterSite> ParameterSites,
    ImmutableEquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>Reads <c>IocInjectMemberAttribute</c> (Annotations.spec, "Types that the user does not own").</summary>
internal static class InjectMemberAttributeReader
{
    /// <summary>The metadata name of <c>IocInjectMemberAttribute</c>.</summary>
    public const string Attribute = "SourceGen.Ioc.IocInjectMemberAttribute";

    /// <summary>Reads each <c>IocInjectMemberAttribute</c> on the assembly or on a class.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The members, the sites, and the diagnostics.</returns>
    public static InjectMemberResult Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        Compilation compilation = context.SemanticModel.Compilation;
        List<InjectMemberDeclaration> declarations = [];
        List<InjectMemberSite> sites = [];
        List<ParameterSite> parameterSites = [];
        List<DiagnosticInfo> diagnostics = [];
        bool onPlainClass = DeclarationReader.IsPlainClass(context.TargetSymbol);
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributeSyntax? syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
            if(onPlainClass)
            {
                Report(diagnostics, Descriptors.AttributeOnPlainClass.Id, syntax, "IocInjectMember");
                continue;
            }

            // Open generics and other unsupported shapes come in later slices. Until then they are skipped.
            if(attribute.ConstructorArguments is not [{ Value: INamedTypeSymbol type }, { Value: string name }]
                || type.TypeKind == TypeKind.Error
                || type.IsUnboundGenericType
                || TypeRefFactory.Create(type, compilation) is not NamedTypeRef implementation)
            {
                continue;
            }

            string typeName = type.ToDisplayString();
            DiagnosticInfo noRegistration = Create(Descriptors.InjectMemberWithoutRegistration.Id, syntax, typeName);
            (InjectedMember? member, DiagnosticInfo? serviceKeyWithoutKey, string memberName) = ReadMember(type, implementation, name, attribute, syntax, compilation, diagnostics, cancellationToken);
            declarations.Add(new InjectMemberDeclaration(implementation, member));
            DiagnosticInfo conflict = Create(Descriptors.InjectMemberAlreadyMarked.Id, syntax, memberName, "another [IocInjectMember]");
            sites.Add(new InjectMemberSite(implementation, noRegistration, serviceKeyWithoutKey, member, conflict));
            if(member is not null && syntax is not null)
            {
                for(int i = 0; i < member.Dependencies.Length; i++)
                {
                    parameterSites.Add(new ParameterSite(DefinitionSource.Of(implementation), new SitePosition(member.Depth, member.Index, i), true, syntax.SyntaxTree.FilePath, syntax.Span));
                }
            }
        }

        return new InjectMemberResult(declarations.ToImmutableEquatableArray(), sites.ToImmutableEquatableArray(), parameterSites.ToImmutableEquatableArray(), diagnostics.ToImmutableEquatableArray());
    }

    /// <summary>
    /// Adds the members to the declarations of their implementation types. An invalid attribute makes each declaration
    /// of its type an error node. A member with a required <c>[ServiceKey]</c> parameter makes each declaration without a
    /// key an error node (SGIOC023, see <see cref="Diagnostics"/>).
    /// </summary>
    /// <param name="declarations">The registration declarations.</param>
    /// <param name="members">The members of all attributes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declarations with the members.</returns>
    public static ImmutableEquatableArray<RegistrationDeclaration> Apply(ImmutableEquatableArray<RegistrationDeclaration> declarations, IEnumerable<InjectMemberDeclaration> members, CancellationToken cancellationToken)
    {
        Dictionary<NamedTypeRef, List<InjectedMember>?> byType = [];
        foreach(InjectMemberDeclaration declaration in members)
        {
            if(!byType.TryGetValue(declaration.Implementation, out List<InjectedMember>? list))
            {
                byType[declaration.Implementation] = list = [];
            }

            // An invalid attribute, or two settings of one member (SGIOC024), skips the type. The same setting twice counts once.
            InjectedMember? same = declaration.Member is { } member && list is not null ? list.Find(m => m.Depth == member.Depth && m.Index == member.Index) : null;
            if(list is null || declaration.Member is null || (same is not null && !same.Equals(declaration.Member)))
            {
                byType[declaration.Implementation] = null;
            }
            else if(same is null)
            {
                list.Add(declaration.Member);
            }
        }

        if(byType.Count == 0)
        {
            return declarations;
        }

        List<RegistrationDeclaration> result = [];
        foreach(RegistrationDeclaration declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(declaration.IsError
                || declaration.Source.Kind != SourceKind.Implementation
                || !byType.TryGetValue(declaration.Source.Type, out List<InjectedMember>? added))
            {
                result.Add(declaration);
            }
            else if(added is null || (declaration.Key is null && added.Any(RequiresServiceKey)))
            {
                result.Add(declaration with { ConstructorParameters = [], Members = [], IsError = true });
            }
            else
            {
                List<InjectedMember> all = [.. declaration.Members, .. added];
                InjectedMembers.Sort(all);
                result.Add(declaration with { Members = all.ToImmutableEquatableArray() });
            }
        }

        return result.ToImmutableEquatableArray();
    }

    /// <summary>The checks that need all attributes: SGIOC021, SGIOC023, and SGIOC024 between two attributes.</summary>
    /// <param name="sites">The sites of the attributes.</param>
    /// <param name="registeredTypes">The types that a registration attribute of the assembly names.</param>
    /// <param name="declarations">The registration declarations of the assembly, also the invalid ones.</param>
    /// <returns>The diagnostics.</returns>
    public static IEnumerable<DiagnosticInfo> Diagnostics(IEnumerable<InjectMemberSite> sites, IEnumerable<NamedTypeRef> registeredTypes, IEnumerable<RegistrationDeclaration> declarations)
    {
        HashSet<NamedTypeRef> registered = [.. registeredTypes];
        HashSet<NamedTypeRef> withoutKey = [.. declarations.Where(static d => d is { Key: null, Source.Kind: SourceKind.Implementation }).Select(static d => d.Source.Type)];
        foreach(InjectMemberSite site in sites)
        {
            if(!registered.Contains(site.Implementation))
            {
                yield return site.NoRegistration;
            }
            else if(withoutKey.Contains(site.Implementation) && site.ServiceKeyWithoutKey is { } serviceKey)
            {
                yield return serviceKey;
            }
        }

        // Each attribute of a member that has more than one setting
        foreach(IGrouping<(NamedTypeRef, int, int), InjectMemberSite> group in sites
            .Where(static s => s.Member is not null)
            .GroupBy(static s => (s.Implementation, s.Member!.Depth, s.Member.Index)))
        {
            if(group.Select(static s => s.Member).Distinct().Skip(1).Any())
            {
                foreach(InjectMemberSite site in group)
                {
                    yield return site.Conflict;
                }
            }
        }
    }

    private static bool RequiresServiceKey(InjectedMember member)
        => member.Dependencies.Any(static d => d is { KeyMode: SiteKeyMode.ServiceKey, Optional: OptionalMode.Required });

    /// <summary>Finds and reads the member. An invalid one is reported, and the result is <see langword="null"/>.</summary>
    /// <returns>The member, SGIOC023 for it, and its name for a message.</returns>
    private static (InjectedMember? Member, DiagnosticInfo? ServiceKeyWithoutKey, string Name) ReadMember(
        INamedTypeSymbol type,
        NamedTypeRef implementation,
        string name,
        AttributeData attribute,
        AttributeSyntax? syntax,
        Compilation compilation,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        // The argument "member": by name, or the second positional argument
        SeparatedSyntaxList<AttributeArgumentSyntax>? arguments = syntax?.ArgumentList?.Arguments;
        SyntaxNode? memberSyntax = (SyntaxNode?)(arguments?.FirstOrDefault(static a => a.NameColon?.Name.Identifier.ValueText == "member")
            ?? arguments?.Where(static a => a.NameEquals is null).ElementAtOrDefault(1))?.Expression ?? syntax;
        (ISymbol? found, int depth, int index, bool overloaded) = InjectedMemberReader.Lookup(type, name);
        if(overloaded)
        {
            Report(diagnostics, Descriptors.NameofOverloaded.Id, memberSyntax, $"{type.ToDisplayString()}.{name}");
            return (null, null, $"{type.ToDisplayString()}.{name}");
        }

        if(found is null)
        {
            Report(diagnostics, Descriptors.NameofTargetNotFound.Id, memberSyntax, "Member", $"a field, property, or method of '{type.ToDisplayString()}'");
            return (null, null, $"{type.ToDisplayString()}.{name}");
        }

        string memberName = $"{found.ContainingType.ToDisplayString()}.{found.Name}";
        if(InjectedMemberReader.InjectOwner(found) is not null)
        {
            Report(diagnostics, Descriptors.InjectMemberAlreadyMarked.Id, syntax, memberName, "[IocInject]");
            return (null, null, $"{type.ToDisplayString()}.{name}");
        }

        bool hasKey = attribute.NamedArguments.Any(static a => a is { Key: "Key", Value.IsNull: false });
        if(found is IMethodSymbol && hasKey)
        {
            Report(diagnostics, Descriptors.InjectMemberKeyOnMethod.Id, syntax, memberName);
            return (null, null, $"{type.ToDisplayString()}.{name}");
        }

        InjectedMembers read = InjectedMemberReader.ReadNamed(
            found,
            depth,
            index,
            memberType => SiteKeyReader.FromResult(
                KeyReader.Read(attribute, syntax, compilation, WrapperTypes.IsCollectionOrDictionary(memberType) ? null : KeyReader.SingleService),
                syntax,
                []),
            syntax,
            compilation,
            cancellationToken);
        if(read.Errors.Count > 0 || read.Unsupported)
        {
            // An asynchronous injection method comes in a later slice. Until then its types are skipped without a diagnostic.
            diagnostics.AddRange(read.Errors);
            return (null, null, $"{type.ToDisplayString()}.{name}");
        }

        InjectedMember member = read.Members[0];
        DiagnosticInfo? serviceKeyWithoutKey = RequiresServiceKey(member)
            ? Create(Descriptors.ServiceKeyWithoutKey.Id, syntax, member.Dependencies.First(static d => d is { KeyMode: SiteKeyMode.ServiceKey, Optional: OptionalMode.Required }).Name, KeyNames.Definition(DefinitionSource.Of(implementation), null))
            : null;
        return (member, serviceKeyWithoutKey, memberName);
    }

    private static void Report(List<DiagnosticInfo> diagnostics, string id, SyntaxNode? node, params string[] arguments)
        => diagnostics.Add(Create(id, node, arguments));

    private static DiagnosticInfo Create(string id, SyntaxNode? node, params string[] arguments)
        => node is null ? DiagnosticInfo.Create(id, (Location?)null, arguments) : DiagnosticInfo.Create(id, node, arguments);
}
