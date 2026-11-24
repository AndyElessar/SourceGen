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

    /// <summary>
    /// The syntax of each dependency of the members, in the order of <see cref="ClosedDefinition.Dependencies"/>, or
    /// <see langword="null"/> when it has no syntax.
    /// </summary>
    public List<SyntaxReference?> Sites { get; } = [];

    /// <summary>The diagnostics. Not empty means that the declaration is skipped as an error node.</summary>
    public List<DiagnosticInfo> Errors { get; } = [];

    /// <summary>Whether a member has a shape that a later slice supports: the declaration is skipped without a diagnostic.</summary>
    public bool Unsupported { get; set; }
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
        List<(ISymbol Member, ISymbol AttributeOwner)> found = [];
        foreach(List<(ISymbol, ISymbol)> level in Enumerable.Reverse(Levels(type, cancellationToken)))
        {
            found.AddRange(level);
        }

        List<(InjectedMember Member, List<SyntaxReference?> Sites)> members = [];
        foreach((ISymbol member, ISymbol owner) in found)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(member is IMethodSymbol method)
            {
                ReadMethod(method, owner, registration, compilation, result, members, cancellationToken);
            }
            else
            {
                ReadFieldOrProperty(member, owner, registration, compilation, result, members, cancellationToken);
            }

            if(result.Unsupported)
            {
                return result;
            }
        }

        // The steps: object initializer, assignments, methods. Inside one step, base members first (OrderBy is stable)
        foreach((InjectedMember member, List<SyntaxReference?> sites) in members.OrderBy(static m => m.Member.Kind))
        {
            result.Members.Add(member);
            result.Sites.AddRange(sites);
        }

        return result;
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

    /// <summary>
    /// The members with <c>[IocInject]</c> of each level, from the type to its last base class. Only the declaration that
    /// is visible by name from the type counts. For an <c>override</c>, the first <c>[IocInject]</c> of the override
    /// chain applies.
    /// </summary>
    private static List<List<(ISymbol Member, ISymbol AttributeOwner)>> Levels(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        List<List<(ISymbol, ISymbol)>> levels = [];
        Dictionary<string, List<ISymbol>> derived = [];
        for(INamedTypeSymbol? level = type; level is not null; level = level.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<(ISymbol, ISymbol)> injected = [];
            List<ISymbol> declared = [];
            foreach(ISymbol member in level.GetMembers())
            {
                if(member is not (IFieldSymbol or IPropertySymbol { IsIndexer: false } or IMethodSymbol { MethodKind: MethodKind.Ordinary })
                    || !member.CanBeReferencedByName
                    || member.IsImplicitlyDeclared)
                {
                    continue;
                }

                declared.Add(member);
                if(!IsHidden(member, derived) && InjectOwner(member) is { } owner)
                {
                    injected.Add((member, owner));
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

        return levels;
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

    /// <summary>The declaration in the override chain that has <c>[IocInject]</c>, or <see langword="null"/>.</summary>
    private static ISymbol? InjectOwner(ISymbol member)
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

    private static void ReadFieldOrProperty(
        ISymbol member,
        ISymbol owner,
        AttributeSyntax? registration,
        Compilation compilation,
        InjectedMembers result,
        List<(InjectedMember, List<SyntaxReference?>)> members,
        CancellationToken cancellationToken)
    {
        (ITypeSymbol type, NullableAnnotation annotation, bool initializer) = member switch
        {
            IFieldSymbol field => (field.Type, field.NullableAnnotation, field.IsRequired),
            IPropertySymbol property => (property.Type, property.NullableAnnotation, property.IsRequired || property.SetMethod is { IsInitOnly: true }),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };

        // A type that the model cannot represent (a pointer) is not supported
        if(TypeRefFactory.Create(type, compilation) is not { } typeRef)
        {
            result.Unsupported = true;
            return;
        }

        SiteKey siteKey = SiteKeyReader.Read(owner.GetAttributes(), type, member.Name, registration, compilation, cancellationToken);
        result.Errors.AddRange(siteKey.Errors);

        // An object initializer cannot assign conditionally, so a required or init member is required. An initializer
        // counts only for a member of this compilation (Semantics.spec, "Dependency resolution"). The test is the assembly,
        // not the availability of syntax: a project reference in the IDE also gives syntax (Annotations.spec, "Constructor selection")
        SyntaxReference? site = member.DeclaringSyntaxReferences.FirstOrDefault();
        OptionalMode optional = initializer ? OptionalMode.Required
            : InCompilation(member, compilation) && HasInitializer(site, cancellationToken) ? OptionalMode.KeepInitializer
            : OptionalDependency.IsNullableReference(type, annotation) ? OptionalMode.Nullable
            : OptionalMode.Required;
        Dependency dependency = new(typeRef, member.Name, false, optional, null, type.IsValueType, siteKey.Mode, siteKey.Key);
        members.Add((new InjectedMember(initializer ? MemberKind.Initializer : MemberKind.Assignment, Identifier(member.Name), [dependency]), [site]));
    }

    private static void ReadMethod(
        IMethodSymbol method,
        ISymbol owner,
        AttributeSyntax? registration,
        Compilation compilation,
        InjectedMembers result,
        List<(InjectedMember, List<SyntaxReference?>)> members,
        CancellationToken cancellationToken)
    {
        if(SiteKeyReader.CheckMember(owner.GetAttributes(), "method", registration, cancellationToken) is { } keyError)
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
            // A method of another assembly is reported at the registration attribute, also when a project reference gives its syntax
            Location? location = InCompilation(method, compilation) ? method.Locations.FirstOrDefault(static l => l.IsInSource) : null;
            result.Errors.Add(location is null && registration is not null
                ? DiagnosticInfo.Create(Descriptors.InvalidInjectionMethod.Id, registration, $"{method.ContainingType.ToDisplayString()}.{method.Name}", reason)
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
            if(ReadParameter(parameter, registration, compilation, result.Errors, cancellationToken) is not { } dependency)
            {
                result.Unsupported = true;
                return;
            }

            parameters.Add(dependency);
            sites.Add(parameter.DeclaringSyntaxReferences.FirstOrDefault());
        }

        members.Add((new InjectedMember(MemberKind.Method, Identifier(method.Name), parameters.ToImmutableEquatableArray()), sites));
    }

    /// <summary>
    /// Whether the member has an initializer that gives a default value. An initializer that only suppresses the nullable
    /// warning (<c>null!</c>, <c>default!</c>, <c>default(T)!</c>) does not count (ADR 2026112503).
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
