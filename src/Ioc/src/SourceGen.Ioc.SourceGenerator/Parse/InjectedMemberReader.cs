using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>
/// The injected members of a type, or the reason why its declaration is skipped. It is a local result of the transform
/// and does not flow through the pipeline.
/// </summary>
internal sealed class InjectedMembers
{
    /// <summary>The members in the order of the steps.</summary>
    public List<InjectedMember> Members { get; } = [];

    /// <summary>The syntax of each dependency of the members, or <see langword="null"/> when it has no syntax.</summary>
    public List<(SitePosition Position, SyntaxReference? Reference)> Sites { get; } = [];

    /// <summary>The diagnostics. Not empty means that the declaration is skipped as an error node.</summary>
    public List<DiagnosticInfo> Errors { get; } = [];

    /// <summary>Whether a member has a shape that a later slice supports: the declaration is skipped without a diagnostic.</summary>
    public bool Unsupported { get; set; }

    /// <summary>Adds a member and the syntax of its dependencies.</summary>
    /// <param name="member">The member.</param>
    /// <param name="sites">The syntax of each dependency.</param>
    public void Add(InjectedMember member, List<SyntaxReference?> sites)
    {
        Members.Add(member);
        for(int i = 0; i < sites.Count; i++)
        {
            Sites.Add((new SitePosition(member.Depth, member.Index, i), sites[i]));
        }
    }

    /// <summary>Sorts the members by step, and inside one step base members first (Annotations.spec, "Order").</summary>
    public void Sort() => Sort(Members);

    /// <summary>Sorts members by step, and inside one step base members first (Annotations.spec, "Order").</summary>
    /// <param name="members">The members.</param>
    public static void Sort(List<InjectedMember> members)
        => members.Sort(static (a, b) => (a.Kind, a.Depth, a.Index).CompareTo((b.Kind, b.Depth, b.Index)));
}

/// <summary>Reads the members with <c>[IocInject]</c> of an implementation type (Annotations.spec, "Injected members").</summary>
internal static class InjectedMemberReader
{
    /// <summary>Reads the injected members of <paramref name="type"/> and its base classes.</summary>
    /// <param name="type">The implementation type.</param>
    /// <param name="registration">The registration attribute, where a problem of a member from metadata is reported.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The members, their sites, and the diagnostics.</returns>
    public static InjectedMembers Read(INamedTypeSymbol type, AttributeSyntax? registration, Compilation compilation, CancellationToken cancellationToken)
    {
        InjectedMembers result = new();
        foreach((ISymbol member, ISymbol owner, int depth, int index) in Levels(type, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MemberSource source = new(
                depth,
                index,
                (memberType, name) => SiteKeyReader.Read(owner.GetAttributes(), memberType, name, registration, compilation, cancellationToken),
                owner,
                AlwaysRequired: false,
                registration,
                ReportAtMember: true);
            ReadMember(member, source, compilation, result, cancellationToken);
            if(result.Unsupported)
            {
                return result;
            }
        }

        result.Sort();
        return result;
    }

    /// <summary>
    /// Reads one member that <c>IocInjectMemberAttribute</c> names (Annotations.spec, "Types that the user does not own").
    /// The member is always required, and its problems are reported at the attribute.
    /// </summary>
    /// <param name="member">The member that <see cref="Lookup"/> found.</param>
    /// <param name="depth">The <see cref="InjectedMember.Depth"/> of the member.</param>
    /// <param name="index">The <see cref="InjectedMember.Index"/> of the member.</param>
    /// <param name="key">Reads the key of a field or property, from the attribute, by the type of the member.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The member, its sites, and the diagnostics.</returns>
    public static InjectedMembers ReadNamed(ISymbol member, int depth, int index, Func<ITypeSymbol, SiteKey> key, AttributeSyntax? attribute, Compilation compilation, CancellationToken cancellationToken)
    {
        InjectedMembers result = new();
        ReadMember(member, new MemberSource(depth, index, (type, _) => key(type), null, AlwaysRequired: true, attribute, ReportAtMember: false), compilation, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// Member lookup by name (Annotations.spec, "Member lookup by name"): the first level from <paramref name="type"/> up
    /// that declares an instance member with that name.
    /// </summary>
    /// <param name="type">The implementation type.</param>
    /// <param name="name">The name.</param>
    /// <returns>
    /// The field, property, or method with its depth and index, or <see langword="null"/> when nothing is found or the
    /// member has another kind. Whether that level has more than one method with the name.
    /// </returns>
    public static (ISymbol? Member, int Depth, int Index, bool Overloaded) Lookup(INamedTypeSymbol type, string name)
    {
        for(INamedTypeSymbol? level = type; level is not null; level = level.BaseType)
        {
            // Any member with the name stops the search, also a static member or a nested type, as in C# member lookup
            ImmutableArray<ISymbol> all = level.GetMembers();
            ISymbol[] named = [.. all.Where(m => m.Name == name && !m.IsImplicitlyDeclared)];
            if(named.Length == 0)
            {
                continue;
            }

            if(named.Count(static m => m is IMethodSymbol) > 1)
            {
                return (null, 0, 0, true);
            }

            ISymbol member = named[0];
            return named.Length == 1 && !member.IsStatic && member is IFieldSymbol or IPropertySymbol { IsIndexer: false } or IMethodSymbol { MethodKind: MethodKind.Ordinary }
                ? (member, Depth(level), all.IndexOf(member), false)
                : (null, 0, 0, false);
        }

        return (null, 0, 0, false);
    }

    /// <summary>The declaration in the override chain that has <c>[IocInject]</c>, or <see langword="null"/>.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The declaration, or <see langword="null"/>.</returns>
    public static ISymbol? InjectOwner(ISymbol member)
    {
        for(ISymbol? current = member; current is not null; current = current switch
        {
            IPropertySymbol property => property.OverriddenProperty,
            IMethodSymbol method => method.OverriddenMethod,
            _ => null,
        })
        {
            if(current.GetAttributes().Any(static a => a.AttributeClass is { Name: "IocInjectAttribute" } c && c.ToDisplayString() == DeclarationReader.InjectAttribute))
            {
                return current;
            }
        }

        return null;
    }
    /// <summary>Reads one parameter of a constructor or of an injection method.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="registration">The registration attribute, where a site from metadata is reported.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="errors">Receives the diagnostics of an invalid key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The dependency, or <see langword="null"/> with no new error when the type cannot be represented (a pointer).
    /// </returns>
    public static Dependency? ReadParameter(IParameterSymbol parameter, AttributeSyntax? registration, Compilation compilation, List<DiagnosticInfo> errors, CancellationToken cancellationToken)
    {
        if(TypeRefFactory.Create(parameter.Type, compilation) is not { } parameterRef)
        {
            return null;
        }

        SiteKey siteKey = SiteKeyReader.Read(parameter.GetAttributes(), parameter.Type, parameter.Name, registration, compilation, cancellationToken);
        errors.AddRange(siteKey.Errors);
        (OptionalMode optional, string? defaultValue) = OptionalDependency.Of(parameter);
        return new Dependency(
            parameterRef,
            parameter.Name,
            parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter,
            optional,
            defaultValue,
            parameter.Type.IsValueType,
            siteKey.Mode,
            siteKey.Key);
    }

    /// <summary>The number of base classes of <paramref name="type"/>.</summary>
    private static int Depth(INamedTypeSymbol type)
    {
        int depth = 0;
        for(INamedTypeSymbol? level = type.BaseType; level is not null; level = level.BaseType)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// The members with <c>[IocInject]</c> of the type and its base classes, base members first. Only the declaration
    /// that is visible by name from the type counts. For an <c>override</c>, the first <c>[IocInject]</c> of the override
    /// chain applies.
    /// </summary>
    private static List<(ISymbol Member, ISymbol AttributeOwner, int Depth, int Index)> Levels(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        List<List<(ISymbol, ISymbol, int, int)>> levels = [];
        Dictionary<string, List<ISymbol>> derived = [];
        int depth = Depth(type);
        for(INamedTypeSymbol? level = type; level is not null; level = level.BaseType, depth--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<(ISymbol, ISymbol, int, int)> injected = [];
            List<ISymbol> declared = [];
            ImmutableArray<ISymbol> all = level.GetMembers();
            for(int index = 0; index < all.Length; index++)
            {
                ISymbol member = all[index];
                if(member is not (IFieldSymbol or IPropertySymbol { IsIndexer: false } or IMethodSymbol { MethodKind: MethodKind.Ordinary })
                    || !member.CanBeReferencedByName
                    || member.IsImplicitlyDeclared)
                {
                    continue;
                }

                declared.Add(member);
                if(!IsHidden(member, derived) && InjectOwner(member) is { } owner)
                {
                    injected.Add((member, owner, depth, index));
                }
            }

            foreach(ISymbol member in declared)
            {
                if(!derived.TryGetValue(member.Name, out List<ISymbol>? sameName))
                {
                    derived[member.Name] = sameName = [];
                }

                sameName.Add(member);
            }

            levels.Add(injected);
        }

        levels.Reverse();
        return [.. levels.SelectMany(static l => l)];
    }

    /// <summary>Whether a member of a more derived level hides or overrides <paramref name="member"/>.</summary>
    private static bool IsHidden(ISymbol member, Dictionary<string, List<ISymbol>> derived)
    {
        if(!derived.TryGetValue(member.Name, out List<ISymbol>? sameName))
        {
            return false;
        }

        foreach(ISymbol other in sameName)
        {
            if(Hides(other, member))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a member of a derived type hides or overrides a member of a base type: the same name, and for two methods
    /// the same parameters.
    /// </summary>
    private static bool Hides(ISymbol derived, ISymbol baseMember)
    {
        if(derived.Name != baseMember.Name)
        {
            return false;
        }

        if(derived is not IMethodSymbol derivedMethod || baseMember is not IMethodSymbol baseMethod)
        {
            return true;
        }

        return derivedMethod.TypeParameters.Length == baseMethod.TypeParameters.Length
            && derivedMethod.Parameters.Length == baseMethod.Parameters.Length
            && derivedMethod.Parameters.Zip(baseMethod.Parameters, static (a, b) => a.RefKind == b.RefKind && SymbolEqualityComparer.Default.Equals(a.Type, b.Type)).All(static same => same);
    }

    /// <summary>Where a member comes from: <c>[IocInject]</c> on the member, or <c>IocInjectMemberAttribute</c>.</summary>
    /// <param name="Depth">The <see cref="InjectedMember.Depth"/>.</param>
    /// <param name="Index">The <see cref="InjectedMember.Index"/>.</param>
    /// <param name="Key">Reads the key of a field or property from its type and name.</param>
    /// <param name="KeyOwner">The declaration with <c>[IocInject]</c>, for the check of a key on a method, or <see langword="null"/>.</param>
    /// <param name="AlwaysRequired">Whether an initializer is ignored: a member that <c>IocInjectMemberAttribute</c> names.</param>
    /// <param name="Attribute">Where a problem is reported when the member is not reported itself.</param>
    /// <param name="ReportAtMember">Whether a problem of a method of this compilation is reported at the method.</param>
    private sealed record MemberSource(int Depth, int Index, Func<ITypeSymbol, string, SiteKey> Key, ISymbol? KeyOwner, bool AlwaysRequired, AttributeSyntax? Attribute, bool ReportAtMember);

    private static void ReadMember(ISymbol member, MemberSource source, Compilation compilation, InjectedMembers result, CancellationToken cancellationToken)
    {
        if(member is IMethodSymbol method)
        {
            ReadMethod(method, source, compilation, result, cancellationToken);
        }
        else
        {
            ReadFieldOrProperty(member, source, compilation, result, cancellationToken);
        }
    }

    private static void ReadFieldOrProperty(ISymbol member, MemberSource source, Compilation compilation, InjectedMembers result, CancellationToken cancellationToken)
    {
        (ITypeSymbol type, bool initializer) = member switch
        {
            IFieldSymbol field => (field.Type, field.IsRequired),
            IPropertySymbol property => (property.Type, property.IsRequired || property.SetMethod is { IsInitOnly: true }),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };

        // A type that the model cannot represent (a pointer) is not supported
        if(TypeRefFactory.Create(type, compilation) is not { } typeRef)
        {
            result.Unsupported = true;
            return;
        }

        SiteKey siteKey = source.Key(type, member.Name);
        result.Errors.AddRange(siteKey.Errors);

        // An object initializer cannot assign conditionally, so a required or init member is required. An initializer
        // counts only for a member of this compilation (Semantics.spec, "Dependency resolution"). The test is the assembly,
        // not the availability of syntax: a project reference in the IDE also gives syntax (Annotations.spec, "Constructor selection")
        SyntaxReference? site = member.DeclaringSyntaxReferences.FirstOrDefault();
        OptionalMode optional = initializer || source.AlwaysRequired ? OptionalMode.Required
            : InCompilation(member, compilation) && HasInitializer(site, cancellationToken) ? OptionalMode.KeepInitializer
            : OptionalMode.Required;
        Dependency dependency = new(typeRef, member.Name, false, optional, null, type.IsValueType, siteKey.Mode, siteKey.Key);
        result.Add(new InjectedMember(initializer ? MemberKind.Initializer : MemberKind.Assignment, Identifier(member.Name), [dependency], source.Depth, source.Index), [site]);
    }

    private static void ReadMethod(IMethodSymbol method, MemberSource source, Compilation compilation, InjectedMembers result, CancellationToken cancellationToken)
    {
        if(source.KeyOwner is not null && SiteKeyReader.CheckMember(source.KeyOwner.GetAttributes(), "method", source.Attribute, cancellationToken) is { } keyError)
        {
            result.Errors.Add(keyError);
            return;
        }

        bool returnsTask = method.ReturnType is INamedTypeSymbol { Arity: 0 } task && task.ToDisplayString() == "System.Threading.Tasks.Task";
        string? reason = method switch
        {
            { IsGenericMethod: true } => "it is generic",
            _ when method.Parameters.Any(static p => p.RefKind != RefKind.None) => "a parameter has ref, out, or in",
            { IsAsync: true, ReturnsVoid: true } => "it is async void",
            { ReturnsVoid: false } when !returnsTask => "the return type must be void or Task",
            _ => null,
        };
        if(reason is not null)
        {
            // A method of another assembly is reported at the attribute, also when a project reference gives its syntax
            Location? location = source.ReportAtMember && InCompilation(method, compilation) ? method.Locations.FirstOrDefault(static l => l.IsInSource) : null;
            result.Errors.Add(location is null && source.Attribute is not null
                ? DiagnosticInfo.Create(Descriptors.InvalidInjectionMethod.Id, source.Attribute, $"{method.ContainingType.ToDisplayString()}.{method.Name}", reason)
                : DiagnosticInfo.Create(Descriptors.InvalidInjectionMethod.Id, location, $"{method.ContainingType.ToDisplayString()}.{method.Name}", reason));
            return;
        }

        // Asynchronous injection methods come in a later slice. Until then the declaration is skipped.
        if(returnsTask)
        {
            result.Unsupported = true;
            return;
        }

        List<Dependency> parameters = [];
        List<SyntaxReference?> sites = [];
        foreach(IParameterSymbol parameter in method.Parameters)
        {
            if(ReadParameter(parameter, source.Attribute, compilation, result.Errors, cancellationToken) is not { } dependency)
            {
                result.Unsupported = true;
                return;
            }

            parameters.Add(dependency);
            sites.Add(parameter.DeclaringSyntaxReferences.FirstOrDefault());
        }

        result.Add(new InjectedMember(MemberKind.Method, Identifier(method.Name), parameters.ToImmutableEquatableArray(), source.Depth, source.Index), sites);
    }
    /// <summary>
    /// Whether the member has an initializer that gives a default value. An initializer that only suppresses the nullable
    /// warning (<c>null!</c>, <c>default!</c>, <c>default(T)!</c>) does not count (ADR IocRewrite/0016).
    /// </summary>
    private static bool HasInitializer(SyntaxReference? site, CancellationToken cancellationToken)
    {
        EqualsValueClauseSyntax? initializer = site?.GetSyntax(cancellationToken) switch
        {
            VariableDeclaratorSyntax variable => variable.Initializer,
            PropertyDeclarationSyntax property => property.Initializer,
            _ => null,
        };

        return initializer is not null
            && !(Unwrap(initializer.Value) is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed
                && Unwrap(suppressed.Operand) is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression or (int)SyntaxKind.DefaultLiteralExpression } or DefaultExpressionSyntax);

        static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while(expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression;
        }
    }

    /// <summary>Whether the member is declared in the current compilation.</summary>
    private static bool InCompilation(ISymbol member, Compilation compilation) => SymbolEqualityComparer.Default.Equals(member.ContainingAssembly, compilation.Assembly);

    /// <summary>A name as a C# identifier: a keyword needs <c>@</c>.</summary>
    private static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
}
