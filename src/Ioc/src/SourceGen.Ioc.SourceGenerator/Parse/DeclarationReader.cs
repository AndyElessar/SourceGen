using Microsoft.CodeAnalysis.Text;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>Where a declaration is written, for the check of merged declarations (SGIOC001).</summary>
/// <param name="Source">The source: the identity of the declaration, with the key.</param>
/// <param name="Key">The key, or <see langword="null"/>.</param>
/// <param name="Name">The name of the instance definition, for the message.</param>
/// <param name="Lifetime">The lifetime.</param>
/// <param name="FilePath">The path of the syntax tree of the attribute.</param>
/// <param name="Span">The span of the attribute.</param>
internal sealed record MergeSite(DefinitionSource Source, KeyRef? Key, string Name, Lifetime Lifetime, string FilePath, TextSpan Span);

/// <summary>
/// Where a dependency site is written, for the definite check (Analysis.Model.spec, "Two levels of checks"). A
/// parameter of a type from metadata has no syntax, so its site is the attribute of the declaration.
/// </summary>
/// <param name="Source">The source that owns the dependency site.</param>
/// <param name="Key">The dependency site.</param>
/// <param name="InSource">Whether the site is declared in this assembly (and the span is the parameter or member).</param>
/// <param name="FilePath">The path of the syntax tree.</param>
/// <param name="Span">The span of the parameter, or of the attribute when <paramref name="InSource"/> is false.</param>
internal sealed record ParameterSite(DefinitionSource Source, SitePosition Key, bool InSource, string FilePath, TextSpan Span);

/// <summary>
/// What one attribute target gives. The generation pipeline uses only <see cref="Declarations"/>, so a change of
/// position, which changes only the sites and the diagnostics, does not run the analysis again.
/// </summary>
/// <param name="Declarations">The valid declarations.</param>
/// <param name="Sites">The sites of the valid declarations.</param>
/// <param name="ParameterSites">The sites of the constructor parameters of the valid declarations.</param>
/// <param name="Diagnostics">The diagnostics of the invalid declarations.</param>
/// <param name="RegisteredTypes">
/// The types that a registration attribute names, also when the attribute gives no declaration (an invalid type, or a
/// shape of a later slice). The check of <c>IocInjectMemberAttribute</c> (SGIOC021) uses them.
/// </param>
internal sealed record DeclarationResult(
    ImmutableEquatableArray<RegistrationDeclaration> Declarations,
    ImmutableEquatableArray<MergeSite> Sites,
    ImmutableEquatableArray<ParameterSite> ParameterSites,
    ImmutableEquatableArray<DiagnosticInfo> Diagnostics,
    ImmutableEquatableArray<NamedTypeRef> RegisteredTypes);

/// <summary>Reads registration declarations from the attributes (Annotations.spec, "Registration declarations").</summary>
internal static class DeclarationReader
{
    /// <summary>The metadata name of <c>IocRegisterAttribute</c>.</summary>
    public const string RegisterAttribute = "SourceGen.Ioc.IocRegisterAttribute";

    /// <summary>The metadata name of <c>IocRegisterForAttribute</c>.</summary>
    public const string RegisterForAttribute = "SourceGen.Ioc.IocRegisterForAttribute";

    /// <summary>The metadata name of <c>IocFactoryAttribute</c>.</summary>
    public const string FactoryAttribute = "SourceGen.Ioc.IocFactoryAttribute";

    /// <summary>The metadata name of <c>IocFactoryForAttribute</c>.</summary>
    public const string FactoryForAttribute = "SourceGen.Ioc.IocFactoryForAttribute";

    /// <summary>The metadata name of <c>IocInjectAttribute</c>.</summary>
    internal const string InjectAttribute = "SourceGen.Ioc.IocInjectAttribute";
    private const string ActivatorUtilitiesConstructorAttribute = "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute";
    private const string ModuleAttribute = "SourceGen.Ioc.IocModuleAttribute";
    private const string ContainerAttribute = "SourceGen.Ioc.IocContainerAttribute";

    /// <summary>Reads each <c>IocRegisterAttribute</c> on a class.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declarations and the diagnostics.</returns>
    public static DeclarationResult ReadRegister(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        Builder builder = new(context.SemanticModel.Compilation, cancellationToken);
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributeSyntax? syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
            if(attribute.ConstructorArguments is [{ Value: int lifetime }])
            {
                builder.Add((INamedTypeSymbol)context.TargetSymbol, lifetime, attribute, syntax, syntax);
            }
        }

        return builder.ToResult();
    }

    /// <summary>Reads each <c>IocRegisterForAttribute</c> on the assembly or on a class.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declarations and the diagnostics.</returns>
    public static DeclarationResult ReadRegisterFor(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        Builder builder = new(context.SemanticModel.Compilation, cancellationToken);
        bool onPlainClass = IsPlainClass(context.TargetSymbol);
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributeSyntax? syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
            if(onPlainClass)
            {
                // The registration scope of an ExplicitOnly container comes in a later slice. Until then a registration
                // on a module or container class belongs to the full assembly.
                builder.Report(Descriptors.AttributeOnPlainClass.Id, syntax, "IocRegisterFor");
            }
            else if(attribute.ConstructorArguments is [{ Value: ITypeSymbol implementation }, { Value: int lifetime }])
            {
                builder.Add(implementation, lifetime, attribute, syntax, ImplementationTypeSyntax(syntax));
            }
        }

        return builder.ToResult();
    }

    /// <summary>Reads each <c>IocFactoryAttribute</c> on a method.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declarations and the diagnostics.</returns>
    public static DeclarationResult ReadFactory(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        Builder builder = new(context.SemanticModel.Compilation, cancellationToken);
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributeSyntax? syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
            if(attribute.ConstructorArguments is [{ Value: int lifetime }])
            {
                builder.AddFactory((IMethodSymbol)context.TargetSymbol, lifetime, attribute, syntax);
            }
        }

        return builder.ToResult();
    }

    /// <summary>Reads each <c>IocFactoryForAttribute</c> on the assembly or on a class.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The declarations and the diagnostics.</returns>
    public static DeclarationResult ReadFactoryFor(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        Builder builder = new(context.SemanticModel.Compilation, cancellationToken);
        bool onPlainClass = IsPlainClass(context.TargetSymbol);
        foreach(AttributeData attribute in context.Attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttributeSyntax? syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
            if(onPlainClass)
            {
                builder.Report(Descriptors.AttributeOnPlainClass.Id, syntax, "IocFactoryFor");
            }
            else if(attribute.ConstructorArguments is [_, { Value: int lifetime }]
                && builder.ReadNameof(ArgumentSyntax(syntax, "factory", 0), "Factory", "a method", static s => s is IMethodSymbol) is IMethodSymbol method)
            {
                builder.AddFactory(method, lifetime, attribute, syntax);
            }
        }

        return builder.ToResult();
    }

    /// <summary>Whether an assembly-or-class attribute is on a class that is neither a module nor a container (SGIOC025).</summary>
    /// <param name="target">The target of the attribute.</param>
    /// <returns>Whether the target is such a class.</returns>
    internal static bool IsPlainClass(ISymbol target)
        => target is INamedTypeSymbol host
            && !host.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() is ModuleAttribute or ContainerAttribute);

    /// <summary>Collects the declarations and the diagnostics of one attribute target.</summary>
    private sealed class Builder(Compilation compilation, CancellationToken cancellationToken)
    {
        private readonly List<RegistrationDeclaration> declarations = [];
        private readonly List<MergeSite> sites = [];
        private readonly List<ParameterSite> parameterSites = [];
        private readonly List<DiagnosticInfo> diagnostics = [];
        private readonly List<NamedTypeRef> registeredTypes = [];

        public DeclarationResult ToResult()
            => new(declarations.ToImmutableEquatableArray(), sites.ToImmutableEquatableArray(), parameterSites.ToImmutableEquatableArray(), diagnostics.ToImmutableEquatableArray(), registeredTypes.ToImmutableEquatableArray());

        public void Report(string id, SyntaxNode? node, params string[] arguments)
            => diagnostics.Add(node is null ? DiagnosticInfo.Create(id, (Location?)null, arguments) : DiagnosticInfo.Create(id, node, arguments));

        /// <summary>Reads one declaration. An invalid one is reported and skipped.</summary>
        /// <param name="implementation">The implementation type.</param>
        /// <param name="lifetime">The lifetime value.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="syntax">The attribute syntax.</param>
        /// <param name="typeSyntax">Where an error of the implementation type is reported.</param>
        public void Add(ITypeSymbol type, int lifetime, AttributeData attribute, AttributeSyntax? syntax, SyntaxNode? typeSyntax)
        {
            // The compiler already reports an error type
            if(type.TypeKind == TypeKind.Error)
            {
                return;
            }

            if(type is INamedTypeSymbol named && TypeRefFactory.Create(named, compilation) is NamedTypeRef registered)
            {
                registeredTypes.Add(registered);
            }

            string name = type.ToDisplayString();
            if(ImplementationError(type) is { } reason)
            {
                Report(Descriptors.InvalidImplementationType.Id, typeSyntax, name, reason);
                return;
            }

            INamedTypeSymbol implementation = (INamedTypeSymbol)type;

            // Open generics and other unsupported shapes come in later slices. Until then they are skipped.
            if(lifetime is < 0 or > 2
                || ContainsTypeParameter(implementation)
                || TypeRefFactory.Create(implementation, compilation) is not NamedTypeRef implementationRef)
            {
                return;
            }

            DefinitionSource source = DefinitionSource.Of(implementationRef);

            // The service types first: an invalid declaration still records the valid ones, so that a dependency on
            // them finds the error node and causes no other diagnostic
            (ImmutableEquatableArray<TypeRef> serviceTypes, bool validServiceTypes) = ReadServiceTypes(implementation, name, attribute, syntax);
            KeyResult key = KeyReader.Read(attribute, syntax, compilation);
            if(key.ErrorId is { } keyError)
            {
                Report(keyError, key.ErrorNode, key.ErrorArguments ?? []);
            }

            (IMethodSymbol? constructor, string? constructorError) = SelectConstructor(implementation);
            if(constructor is null)
            {
                Report(Descriptors.ConstructorNotDetermined.Id, syntax, name, constructorError!);
            }

            if(constructor is null || !validServiceTypes || key.ErrorId is not null)
            {
                AddError(source, implementationRef, key.ErrorId is null ? key.Key : KeyRef.Invalid(implementationRef), lifetime, serviceTypes);
                return;
            }

            // A key on the constructor itself (SGIOC019), an invalid key of a parameter, or a required [ServiceKey]
            // parameter on a registration without a key (SGIOC023) skips the declaration
            if(SiteKeyReader.CheckMember(constructor.GetAttributes(), "constructor", syntax, cancellationToken) is { } memberError)
            {
                diagnostics.Add(memberError);
                AddError(source, implementationRef, key.Key, lifetime, serviceTypes);
                return;
            }

            (ImmutableEquatableArray<Dependency>? parameters, bool keyErrors) = ReadParameters(constructor, syntax);
            if(keyErrors)
            {
                AddError(source, implementationRef, key.Key, lifetime, serviceTypes);
                return;
            }

            if(parameters is null)
            {
                return;
            }

            // An invalid member (SGIOC016, SGIOC019, an invalid key) skips the declaration. A member that a later slice
            // supports (an asynchronous injection method) skips it without a diagnostic.
            InjectedMembers members = InjectedMemberReader.Read(implementation, syntax, compilation, cancellationToken);
            if(members.Unsupported)
            {
                return;
            }

            if(members.Errors.Count > 0)
            {
                diagnostics.AddRange(members.Errors);
                AddError(source, implementationRef, key.Key, lifetime, serviceTypes);
                return;
            }

            if(key.Key is null
                && parameters.Concat(members.Members.SelectMany(static m => m.Dependencies)).FirstOrDefault(static p => p is { KeyMode: SiteKeyMode.ServiceKey, Optional: OptionalMode.Required }) is { } serviceKey)
            {
                Report(Descriptors.ServiceKeyWithoutKey.Id, syntax, serviceKey.Name, KeyNames.Definition(source, null));
                AddError(source, implementationRef, key.Key, lifetime, serviceTypes);
                return;
            }

            Add(new RegistrationDeclaration(source, implementationRef, key.Key, (Lifetime)lifetime, serviceTypes, parameters, members.Members.ToImmutableEquatableArray(), DisposalOf(implementation)), syntax, constructor.Parameters, members.Sites);
        }

        /// <summary>
        /// Binds the operand of a <c>nameof()</c> argument (Annotations.spec, "How the generator reads <c>nameof()</c>").
        /// An argument that is not a direct <c>nameof()</c>, or that names no member of the right kind, is reported
        /// (SGIOC011), and so is a method group with more than one method (SGIOC010).
        /// </summary>
        /// <param name="argument">The argument.</param>
        /// <param name="name">The name of the argument in the message.</param>
        /// <param name="kind">The kind of member in the message.</param>
        /// <param name="isKind">Whether a symbol has the right kind.</param>
        /// <returns>The symbol, or <see langword="null"/> when it is reported.</returns>
        public ISymbol? ReadNameof(AttributeArgumentSyntax? argument, string name, string kind, Func<ISymbol, bool> isKind)
        {
            if(argument?.Expression is not InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [ArgumentSyntax operand] } invocation)
            {
                Report(Descriptors.NameofTargetNotFound.Id, argument, name, kind);
                return null;
            }

            SemanticModel model = compilation.GetSemanticModel(invocation.SyntaxTree);

            // A method named "nameof" binds to a symbol. The operator does not
            SymbolInfo info = model.GetSymbolInfo(operand.Expression, cancellationToken);
            ISymbol[] candidates = info.Symbol is { } symbol ? [symbol] : [.. info.CandidateSymbols];
            if(model.GetSymbolInfo(invocation, cancellationToken).Symbol is not null || candidates.Length == 0 || !candidates.All(isKind))
            {
                Report(Descriptors.NameofTargetNotFound.Id, argument, name, kind);
                return null;
            }

            if(candidates.Length > 1)
            {
                Report(Descriptors.NameofOverloaded.Id, invocation, $"{candidates[0].ContainingType.ToDisplayString()}.{candidates[0].Name}");
                return null;
            }

            return candidates[0];
        }

        /// <summary>Reads one factory declaration. An invalid one is reported and skipped.</summary>
        /// <param name="method">The factory method.</param>
        /// <param name="lifetime">The lifetime value.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="syntax">The attribute syntax, where the errors of the method are reported.</param>
        public void AddFactory(IMethodSymbol method, int lifetime, AttributeData attribute, AttributeSyntax? syntax)
        {
            // An invalid member (SGIOC026) still gives an error node with the service types that can be read, so that a
            // dependency on it is not reported again (Diagnostics.spec, "Consequences")
            // A signature can use a file type only in a file type (CS9051), so the containing type covers the return
            // type and the parameter types too
            string? memberError = !method.IsStatic ? "it is not static"
                : ContainsTypeParameter(method.ContainingType) ? "its containing type is an open generic"
                : ContainsFileLocal(method.ContainingType) ? "it uses a file type"
                : null;
            if(memberError is not null)
            {
                Report(Descriptors.InvalidFactoryOrInstance.Id, syntax, method.ToDisplayString(), "factory", memberError);
            }

            if(lifetime is < 0 or > 2
                || method.ReturnType.TypeKind == TypeKind.Error
                || TypeRefFactory.Create(method.ContainingType, compilation) is not NamedTypeRef containing
                || SourceParameters(method) is not { } sourceParameters)
            {
                return;
            }

            DefinitionSource source = new(SourceKind.Factory, containing, method.Name, sourceParameters, method.Arity);
            string name = source.SortName;
            TypedConstant serviceTypesArgument = attribute.NamedArguments.FirstOrDefault(static a => a.Key == "ServiceTypes").Value;
            bool hasServiceTypes = serviceTypesArgument is { IsNull: false, Kind: TypedConstantKind.Array, Values.Length: > 0 };

            // A generic factory comes with open generics, and Task<T> with asynchronous services. Until then they are
            // skipped. A generic factory that specifies ServiceTypes is an error already
            if(method.IsGenericMethod)
            {
                if(hasServiceTypes)
                {
                    Report(Descriptors.GenericFactoryServiceTypes.Id, (SyntaxNode?)NamedArgumentSyntax(syntax, "ServiceTypes") ?? syntax, name);
                }

                return;
            }

            ITypeSymbol returnType = method.ReturnType;
            if(returnType is INamedTypeSymbol { IsGenericType: true } generic && generic.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
            {
                return;
            }

            KeyResult key = KeyReader.Read(attribute, syntax, compilation);
            if(key.ErrorId is { } keyError)
            {
                Report(keyError, key.ErrorNode, key.ErrorArguments ?? []);
            }

            KeyRef? keyRef = key.ErrorId is null ? key.Key : KeyRef.Invalid(containing);
            (ImmutableEquatableArray<TypeRef> serviceTypes, bool validServiceTypes) = hasServiceTypes ? ReadServiceTypes(null, name, attribute, syntax) : ([], true);
            TypeRef? returnTypeRef = null;
            if(ReturnTypeError(returnType) is { } reason)
            {
                Report(Descriptors.InvalidServiceType.Id, syntax, returnType.ToDisplayString(), name, reason);
                validServiceTypes = false;
            }
            else if((returnTypeRef = TypeRefFactory.Create(returnType, compilation)) is null)
            {
                return;
            }
            else if(!hasServiceTypes)
            {
                // Without ServiceTypes the return type is the service type, with all the checks of a service type
                if(ServiceTypeError(returnType) is { } error)
                {
                    ReportServiceTypeError(error, syntax, returnType, name);
                    validServiceTypes = false;
                }
                else
                {
                    serviceTypes = [returnTypeRef];
                }
            }

            if(memberError is not null || !validServiceTypes || key.ErrorId is not null)
            {
                AddError(source, containing, keyRef, lifetime, serviceTypes);
                return;
            }

            (ImmutableEquatableArray<Dependency>? parameters, bool keyErrors) = ReadParameters(method, syntax);
            if(keyErrors)
            {
                AddError(source, containing, keyRef, lifetime, serviceTypes);
                return;
            }

            if(parameters is null)
            {
                return;
            }

            if(keyRef is null && parameters.FirstOrDefault(static p => p is { KeyMode: SiteKeyMode.ServiceKey, Optional: OptionalMode.Required }) is { } serviceKey)
            {
                Report(Descriptors.ServiceKeyWithoutKey.Id, syntax, serviceKey.Name, KeyNames.Definition(source, null));
                AddError(source, containing, keyRef, lifetime, serviceTypes);
                return;
            }

            Add(new RegistrationDeclaration(source, returnTypeRef!, keyRef, (Lifetime)lifetime, serviceTypes, parameters, [], FactoryDisposalOf(returnType)), syntax, method.Parameters);
        }

        /// <summary>Records a valid declaration with its merge site and the sites of its parameters.</summary>
        private void Add(RegistrationDeclaration declaration, AttributeSyntax? syntax, IEnumerable<IParameterSymbol> parameters, IEnumerable<(SitePosition, SyntaxReference?)>? members = null)
        {
            DefinitionSource source = declaration.Source;
            declarations.Add(declaration);
            if(syntax is not null)
            {
                sites.Add(new MergeSite(source, declaration.Key, KeyNames.Definition(source, declaration.Key), declaration.Lifetime, syntax.SyntaxTree.FilePath, syntax.Span));
            }

            // A site of another assembly is reported at the attribute, also when a project reference gives its syntax
            IEnumerable<(SitePosition, SyntaxReference?)> references = parameters
                .Select(static (p, i) => (new SitePosition(-1, 0, i), p.DeclaringSyntaxReferences.FirstOrDefault()))
                .Concat(members ?? []);
            foreach((SitePosition position, SyntaxReference? reference) in references)
            {
                if(reference is not null && compilation.ContainsSyntaxTree(reference.SyntaxTree))
                {
                    parameterSites.Add(new ParameterSite(source, position, true, reference.SyntaxTree.FilePath, reference.Span));
                }
                else if(syntax is not null)
                {
                    parameterSites.Add(new ParameterSite(source, position, false, syntax.SyntaxTree.FilePath, syntax.Span));
                }
            }
        }

        /// <summary>The parameters of a factory method for its identity, or <see langword="null"/> for a type that the model cannot represent.</summary>
        private ImmutableEquatableArray<SourceParameter>? SourceParameters(IMethodSymbol method)
        {
            List<SourceParameter> parameters = [];
            foreach(IParameterSymbol parameter in method.Parameters)
            {
                if(TypeRefFactory.Create(parameter.Type, compilation) is not { } type)
                {
                    return null;
                }

                string prefix = parameter.RefKind switch
                {
                    RefKind.In => "in ",
                    RefKind.RefReadOnlyParameter => "ref readonly ",
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    _ => string.Empty,
                };
                parameters.Add(new SourceParameter(type, prefix));
            }

            return parameters.ToImmutableEquatableArray();
        }

        /// <summary>
        /// The checks of the return type of a factory that apply also when <c>ServiceTypes</c> is set (Annotations.spec,
        /// "Service types of a factory").
        /// </summary>
        private static string? ReturnTypeError(ITypeSymbol type)
        {
            if(type is IPointerTypeSymbol or IFunctionPointerTypeSymbol || type.SpecialType == SpecialType.System_Void)
            {
                return "it is a pointer or void";
            }

            string definition = (type.OriginalDefinition as INamedTypeSymbol)?.ToDisplayString() ?? string.Empty;
            if(definition == "System.Threading.Tasks.Task")
            {
                return "it is a Task without a result";
            }

            if(definition is "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask<TResult>")
            {
                return "ValueTask is not supported";
            }

            // Also KeyValuePair: only the generator creates it (Constitution, "Limits")
            if(type.IsValueType)
            {
                return "it is a value type";
            }

            return IsContainer(type) ? "it is a container class" : null;
        }

        private void ReportServiceTypeError((string Id, string? Reason) error, SyntaxNode? node, ITypeSymbol serviceType, string name)
        {
            if(error.Id == Descriptors.WrapperServiceType.Id)
            {
                Report(error.Id, node, serviceType.ToDisplayString());
            }
            else
            {
                Report(error.Id, node, serviceType.ToDisplayString(), name, error.Reason!);
            }
        }

        /// <summary>
        /// Records an invalid declaration. It skips the complete instance definition, also its valid declarations
        /// (Diagnostics.spec, "Consequences").
        /// </summary>
        private void AddError(DefinitionSource source, TypeRef instanceType, KeyRef? key, int lifetime, ImmutableEquatableArray<TypeRef> validServiceTypes)
            => declarations.Add(new RegistrationDeclaration(source, instanceType, key, (Lifetime)lifetime, validServiceTypes, [], [], IsError: true));

        /// <summary>The checks of the implementation type (Annotations.spec, "Checks of the implementation type").</summary>
        private static string? ImplementationError(ITypeSymbol type)
        {
            if(type is not INamedTypeSymbol { TypeKind: TypeKind.Class })
            {
                return "it is not a class";
            }

            if(type.IsStatic)
            {
                return "it is static";
            }

            if(type.IsAbstract)
            {
                return "it is abstract";
            }

            if(IsContainer(type))
            {
                return "it is a container class";
            }

            // Only a file type: the generated code cannot name it. Other accessibility errors are reported by the
            // compiler (Constitution, "Diagnostic policy").
            return ContainsFileLocal(type) ? "it uses a file type" : null;
        }

        /// <summary>Expands and checks the service types (Annotations.spec, "Expansion of service types").</summary>
        /// <param name="implementation">
        /// The implementation type, or <see langword="null"/> for a factory: its service types can only be concrete types.
        /// </param>
        /// <returns>The valid service types, and whether all of them are valid.</returns>
        private (ImmutableEquatableArray<TypeRef> Types, bool Valid) ReadServiceTypes(INamedTypeSymbol? implementation, string name, AttributeData attribute, AttributeSyntax? syntax)
        {
            TypedConstant argument = attribute.NamedArguments.FirstOrDefault(static a => a.Key == "ServiceTypes").Value;
            if(argument.IsNull || argument.Kind != TypedConstantKind.Array)
            {
                return ([], true);
            }

            List<TypeRef> serviceTypes = [];
            bool valid = true;
            for(int i = 0; i < argument.Values.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(argument.Values[i].Value is not ITypeSymbol element)
                {
                    continue;
                }

                SyntaxNode? elementSyntax = (SyntaxNode?)ServiceTypeSyntax(syntax, i) ?? syntax;
                bool unboundElement = element is INamedTypeSymbol { IsUnboundGenericType: true };
                if(unboundElement && implementation is null)
                {
                    Report(Descriptors.InvalidServiceType.Id, elementSyntax, element.ToDisplayString(), name, "it is an unbound generic type");
                    valid = false;
                    continue;
                }

                List<ITypeSymbol> expanded = unboundElement
                    ? [.. Supertypes(implementation!).Where(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, element.OriginalDefinition))]
                    : [element];
                if(expanded.Count == 0)
                {
                    Report(Descriptors.InvalidServiceType.Id, elementSyntax, element.ToDisplayString(), name, "the implementation type has no base type or interface of this generic type");
                    valid = false;
                    continue;
                }

                foreach(ITypeSymbol serviceType in expanded)
                {
                    if(ServiceTypeError(serviceType) is { } error)
                    {
                        ReportServiceTypeError(error, elementSyntax, serviceType, name);
                        valid = false;
                    }
                    else if(TypeRefFactory.Create(serviceType, compilation) is { } serviceRef)
                    {
                        serviceTypes.Add(serviceRef);
                    }
                    else
                    {
                        // An error type: the compiler already reports it
                        valid = false;
                    }
                }
            }

            return (serviceTypes.ToImmutableEquatableArray(), valid);
        }

        /// <summary>The checks of one service type after expansion.</summary>
        private static (string Id, string? Reason)? ServiceTypeError(ITypeSymbol type)
        {
            if(type is IPointerTypeSymbol or IFunctionPointerTypeSymbol || type.SpecialType == SpecialType.System_Void)
            {
                return (Descriptors.InvalidServiceType.Id, "it is a pointer or void");
            }

            // A wrapper first: KeyValuePair is a wrapper and also a value type
            if(WrapperTypes.IsWrapper(type))
            {
                return (Descriptors.WrapperServiceType.Id, null);
            }

            if(type.IsValueType)
            {
                return (Descriptors.InvalidServiceType.Id, "it is a value type");
            }

            if(IsContainer(type))
            {
                return (Descriptors.InvalidServiceType.Id, "it is a container class");
            }

            return BuiltInServices.IsBuiltIn(type) ? (Descriptors.InvalidServiceType.Id, "it is a built-in service") : null;
        }

        /// <summary>
        /// A container class is a built-in service of its own container and cannot be registered (Annotations.spec,
        /// "Checks of the implementation type").
        /// </summary>
        private static bool IsContainer(ITypeSymbol type)
            => type.GetAttributes().Any(static a => a.AttributeClass is { Name: "IocContainerAttribute" } c && c.ToDisplayString() == ContainerReader.Attribute);

        /// <summary>The constructor parameters, with the key of each one.</summary>
        /// <param name="constructor">The constructor.</param>
        /// <param name="syntax">The registration attribute, where a site from metadata is reported.</param>
        /// <returns>
        /// The parameters, or <see langword="null"/> when a parameter cannot take a generated argument. Whether a key of a
        /// parameter is invalid: its diagnostics are already reported.
        /// </returns>
        private (ImmutableEquatableArray<Dependency>? Parameters, bool KeyErrors) ReadParameters(IMethodSymbol constructor, AttributeSyntax? syntax)
        {
            List<Dependency> parameters = [];
            bool keyErrors = false;
            foreach(IParameterSymbol parameter in constructor.Parameters)
            {
                // A ref or out parameter gives a compile error in the generated code. The compiler reports it
                // (Constitution, "Diagnostic policy"). A type that the model cannot represent (a pointer) is skipped.
                int errors = diagnostics.Count;
                if(InjectedMemberReader.ReadParameter(parameter, syntax, compilation, diagnostics, cancellationToken) is not { } dependency)
                {
                    return (null, keyErrors);
                }

                if(diagnostics.Count > errors)
                {
                    keyErrors = true;
                    continue;
                }

                parameters.Add(dependency);
            }

            return (parameters.ToImmutableEquatableArray(), keyErrors);
        }

        /// <summary>Selects the constructor (Annotations.spec, "Constructor selection").</summary>
        /// <returns>The constructor, or the reason for SGIOC002.</returns>
        private (IMethodSymbol? Constructor, string? Error) SelectConstructor(INamedTypeSymbol type)
        {
            IMethodSymbol? marked = null;
            foreach(IMethodSymbol constructor in type.InstanceConstructors)
            {
                if(constructor.GetAttributes().Any(static a => a.AttributeClass is { Name: "IocInjectAttribute" or "ActivatorUtilitiesConstructorAttribute" } attributeClass
                    && attributeClass.ToDisplayString() is InjectAttribute or ActivatorUtilitiesConstructorAttribute))
                {
                    if(marked is not null)
                    {
                        return (null, "multiple marked constructors");
                    }

                    marked = constructor;
                }
            }

            if(marked is not null)
            {
                return (marked, null);
            }

            // A primary constructor is found from syntax, so only for a type of this compilation. The test is the assembly,
            // not the availability of syntax: a project reference in the IDE also gives syntax. An implicitly declared
            // constructor (the default constructor, the copy constructor of a record) has no syntax, so it never matches.
            if(SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                foreach(IMethodSymbol constructor in type.InstanceConstructors)
                {
                    if(constructor.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is TypeDeclarationSyntax))
                    {
                        return (constructor, null);
                    }
                }
            }

            IMethodSymbol[] publicConstructors = [.. type.InstanceConstructors.Where(static c => c.DeclaredAccessibility == Accessibility.Public)];
            return publicConstructors.Length switch
            {
                1 => (publicConstructors[0], null),
                0 => (null, "no public constructor"),
                _ => (null, "multiple public constructors and no primary constructor"),
            };
        }
    }

    /// <summary>
    /// The type itself, its base class chain, and all its interfaces. The type itself is included because writing the
    /// implementation type in <c>ServiceTypes</c> is not an error.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> Supertypes(INamedTypeSymbol type)
    {
        for(INamedTypeSymbol? baseType = type; baseType is not null; baseType = baseType.BaseType)
        {
            yield return baseType;
        }

        foreach(INamedTypeSymbol interfaceType in type.AllInterfaces)
        {
            yield return interfaceType;
        }
    }

    /// <summary>
    /// Whether a part of <paramref name="type"/> is a <c>file</c> type: the type, an outer type, a type argument, or the
    /// element of an array. Accessibility within the assembly does not cover <c>file</c> types.
    /// </summary>
    private static bool ContainsFileLocal(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => ContainsFileLocal(array.ElementType),
        INamedTypeSymbol named => named.IsFileLocal
            || (named.ContainingType is { } outer && ContainsFileLocal(outer))
            || named.TypeArguments.Any(ContainsFileLocal),
        _ => false,
    };

    /// <summary>The syntax of the <c>implementationType</c> argument: by its name, or else the first positional argument.</summary>
    private static ExpressionSyntax? ImplementationTypeSyntax(AttributeSyntax? syntax)
    {
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments = syntax?.ArgumentList?.Arguments ?? default;
        return (arguments.FirstOrDefault(static a => a.NameColon?.Name.Identifier.ValueText == "implementationType")
            ?? arguments.FirstOrDefault(static a => a.NameColon is null && a.NameEquals is null))?.Expression;
    }

    /// <summary>A constructor argument: by its name, or else the positional argument at <paramref name="position"/>.</summary>
    private static AttributeArgumentSyntax? ArgumentSyntax(AttributeSyntax? syntax, string name, int position)
    {
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments = syntax?.ArgumentList?.Arguments ?? default;
        return arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name)
            ?? arguments.Where(static a => a.NameColon is null && a.NameEquals is null).ElementAtOrDefault(position);
    }

    /// <summary>A named argument (<c>Name = value</c>), or <see langword="null"/>.</summary>
    private static AttributeArgumentSyntax? NamedArgumentSyntax(AttributeSyntax? syntax, string name)
        => syntax?.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name);

    /// <summary>
    /// The disposal kind of the object that a factory returns, decided from the return type for each interface
    /// (Analysis.Model.spec, "Disposal kind"): the type implements it, a sealed class does not, and any other type can.
    /// </summary>
    private static Disposal FactoryDisposalOf(ITypeSymbol returnType)
    {
        Disposal known = returnType is INamedTypeSymbol named ? DisposalOf(named) | DisposalOfInterface(named) : Disposal.None;
        // Another type (an interface, a class that is not sealed) can implement an interface that it does not show
        bool sealedClass = returnType is { TypeKind: TypeKind.Class, IsSealed: true };
        return sealedClass || known == (Disposal.Sync | Disposal.Async) ? known : known | Disposal.Runtime;
    }

    /// <summary>The disposal kind of the interface itself, when the type is <c>IDisposable</c> or <c>IAsyncDisposable</c>.</summary>
    private static Disposal DisposalOfInterface(INamedTypeSymbol type) => type.ToDisplayString() switch
    {
        "System.IDisposable" => Disposal.Sync,
        "System.IAsyncDisposable" => Disposal.Async,
        _ => Disposal.None,
    };

    /// <summary>
    /// The disposal kind of a type that is created with <c>new</c>: its run-time type is known, so the interfaces decide
    /// (Analysis.Model.spec, "Disposal kind").
    /// </summary>
    private static Disposal DisposalOf(INamedTypeSymbol type)
    {
        Disposal disposal = Disposal.None;
        foreach(INamedTypeSymbol interfaceType in type.AllInterfaces)
        {
            disposal |= interfaceType.ToDisplayString() switch
            {
                "System.IDisposable" => Disposal.Sync,
                "System.IAsyncDisposable" => Disposal.Async,
                _ => Disposal.None,
            };
        }

        return disposal;
    }

    private static bool ContainsTypeParameter(INamedTypeSymbol type)
    {
        for(INamedTypeSymbol? level = type; level is not null; level = level.ContainingType)
        {
            if(level.IsUnboundGenericType || level.TypeArguments.Any(static a => a is ITypeParameterSymbol))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The syntax of element <paramref name="index"/> of <c>ServiceTypes</c>, or <see langword="null"/>.</summary>
    private static ExpressionSyntax? ServiceTypeSyntax(AttributeSyntax? syntax, int index)
    {
        ExpressionSyntax? value = syntax?.ArgumentList?.Arguments
            .FirstOrDefault(static a => a.NameEquals?.Name.Identifier.ValueText == "ServiceTypes")?.Expression;
        return value switch
        {
            CollectionExpressionSyntax collection when index < collection.Elements.Count => (collection.Elements[index] as ExpressionElementSyntax)?.Expression,
            ArrayCreationExpressionSyntax { Initializer: { } initializer } when index < initializer.Expressions.Count => initializer.Expressions[index],
            ImplicitArrayCreationExpressionSyntax creation when index < creation.Initializer.Expressions.Count => creation.Initializer.Expressions[index],
            _ => null,
        };
    }
}
