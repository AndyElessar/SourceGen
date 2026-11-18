using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for SGIOC033: Func factories with arguments ignore the lifetime of the service they create.
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// Reports constructor parameters and injected members of type <c>Func&lt;T1, ..., TReturn&gt;</c> whose
    /// <c>TReturn</c> is registered as Singleton or Scoped: each call creates a new instance from the arguments,
    /// so the registered lifetime does not apply.
    /// </summary>
    private static void AnalyzeFuncWithArgumentsLifetime(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        DependencyIndex dependencyIndex,
        CancellationToken cancellationToken)
    {
        // The diagnostic does not depend on the consumer's lifetime, so it is reported once, at the first registration that
        // calls the constructor or injects the members
        if(serviceInfo.Constructor is { } constructor)
        {
            var location = serviceInfo.GetFirstActivationLocation(injected: false);
            foreach(var parameter in constructor.Parameters)
            {
                Analyze(parameter.Type, parameter, null, location);
            }
        }

        if(!serviceInfo.InjectedMembers.IsEmpty)
        {
            var location = serviceInfo.GetFirstActivationLocation(injected: true);
            foreach(var (member, injectAttribute) in serviceInfo.InjectedMembers)
            {
                switch(member)
                {
                    case IPropertySymbol property:
                        Analyze(property.Type, null, injectAttribute, location);
                        break;
                    case IFieldSymbol field:
                        Analyze(field.Type, null, injectAttribute, location);
                        break;
                    case IMethodSymbol method:
                        foreach(var parameter in method.Parameters)
                        {
                            // An unkeyed parameter of a keyed inject method resolves with the method's key
                            Analyze(parameter.Type, parameter, injectAttribute, location);
                        }
                        break;
                }
            }
        }

        void Analyze(ITypeSymbol type, IParameterSymbol? parameter, AttributeData? injectAttribute, Location? location)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!TryGetFuncWithArgumentsReturnType(type, out var funcType, out var returnType))
                return;

            // TReturn is resolved with the service key of the parameter or member, to one registration per tag group; each
            // lifetime among them is reported once
            var key = parameter?.GetServiceKeyAndAttributeInfo().ServiceKey ?? injectAttribute?.GetKeyInfo().Key;
            var reportedSingleton = false;
            var reportedScoped = false;
            foreach(var dependency in dependencyIndex.Find(returnType, key))
            {
                switch(dependency.Lifetime)
                {
                    case ServiceLifetime.Singleton when !reportedSingleton:
                        reportedSingleton = true;
                        break;
                    case ServiceLifetime.Scoped when !reportedScoped:
                        reportedScoped = true;
                        break;
                    default:
                        continue;
                }

                reportDiagnostic(Diagnostic.Create(
                    FuncWithArgumentsIgnoresLifetime,
                    location,
                    serviceInfo.Type.Name,
                    funcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    returnType.Name,
                    dependency.Lifetime));
            }
        }
    }

    /// <summary>
    /// Gets whether <paramref name="type"/> is <c>System.Func&lt;T1, ..., TReturn&gt;</c> with at least one input parameter.
    /// </summary>
    private static bool TryGetFuncWithArgumentsReturnType(
        ITypeSymbol type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? funcType,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? returnType)
    {
        funcType = null;
        returnType = null;

        if(type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 1 } namedType)
            return false;

        var originalDefinition = namedType.OriginalDefinition;
        if(originalDefinition.ContainingNamespace is not { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            || !originalDefinition.MetadataName.StartsWith("Func`", StringComparison.Ordinal)
            || namedType.TypeArguments[^1] is not INamedTypeSymbol funcReturnType)
        {
            return false;
        }

        funcType = namedType;
        returnType = funcReturnType;
        return true;
    }

    /// <summary>
    /// Gets the registrations that a requested <c>Func&lt;T1, ..., TReturn&gt;</c> with input parameters constructs. Like the
    /// generator (<c>WrapperDemand</c>, <c>CollectFuncEntries</c>), such a Func is requested by a registration (the parameters
    /// of its effective factory method, otherwise of its implementation type's constructor, also with an Instance, and both
    /// when a generic factory's mapping does not match the implementation type, which is then constructed; and its injected
    /// members, [IocInject] and InjectMembers, also with an Instance) or an applied decorator, directly or as the element of a
    /// collection; it is registered for every closed, not async-init registration of <c>TReturn</c> (an unbound service type
    /// registers its closed forms), and constructs the implementation type with the registration's key even for an Instance or
    /// Factory registration. The return type of a partial accessor of a generated container constructs only a registration of
    /// that container (see <see cref="AddConstructedByContainerAccessors"/>). The parameters of a
    /// generic factory are scanned as declared, not closed for each service type. Each registration maps to the distinct requested <c>Func</c>
    /// types that construct it, whose input parameters the generator passes to the parameters of their type (see
    /// <see cref="IsPassedFuncArgument"/>).
    /// </summary>
    private static Dictionary<ServiceActivation, List<INamedTypeSymbol>> CollectRegistrationsConstructedByFunc(
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        // The requested Func types by return type
        var returnTypes = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(RuntimeTypeSymbolComparer.Instance);
        HashSet<INamedTypeSymbol>? uncachedConstructorsScanned = null;
        var hasInjectMembers = !analyzerContext.InjectMembers.IsEmpty;
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The members listed in InjectMembers are injected like the [IocInject] members (scanned below), also with an
            // Instance (ExtractAndMergeInjectionMembers)
            if(hasInjectMembers
                && registration.Attribute.ApplicationSyntaxReference is { } syntax
                && analyzerContext.InjectMembers.TryGetValue((syntax.SyntaxTree, syntax.Span), out var injectMembers))
            {
                foreach(var (member, _) in injectMembers)
                    ScanMember(member);
            }

            // A generic factory whose mapping does not match a closed implementation type constructs it instead
            // (CreateServiceRegistrations), so both the factory and the constructor are scanned
            if(GetEffectiveFactoryMethod(analyzerContext, registration) is { } factoryMethod)
            {
                ScanParameters(factoryMethod.Parameters);
                if(!IsSelfConstructedByGenericFactory(analyzerContext, registration, factoryMethod))
                    continue;
            }

            // The constructor is cached on the ServiceInfo when a registration of the type is constructed; scanning it again
            // for another registration of the type adds nothing
            var implementation = registration.Implementation;
            if(analyzerContext.RegisteredServices.TryGetValue(implementation, out var info) && info.Constructor is { } constructor)
                ScanParameters(constructor.Parameters);
            else if((uncachedConstructorsScanned ??= new(SymbolEqualityComparer.Default)).Add(implementation))
                ScanParameters(implementation.GetSelectedConstructor(analyzerContext.Assembly)?.Parameters ?? []);
        }

        // The injected members of every registered implementation type, cached on its ServiceInfo unless all its registrations
        // have an Instance
        foreach(var kvp in analyzerContext.RegisteredServices)
        {
            var info = kvp.Value;
            ScanMembers(null, info.HasInjection ? info.InjectedMembers : [.. info.Type.GetInjectedMembers()]);
        }

        foreach(var decorator in analyzerContext.AppliedDecorators.Keys)
            ScanMembers(decorator.GetSelectedConstructor(analyzerContext.Assembly), [.. decorator.GetInjectedMembers()]);

        var constructed = new Dictionary<ServiceActivation, List<INamedTypeSymbol>>();
        if(returnTypes.Count > 0)
        {
            foreach(var registration in analyzerContext.DependencyRegistrations)
            {
                if(IsOpenGeneric(registration) || IsAsyncInit(analyzerContext, registration))
                    continue;

                foreach(var serviceType in registration.Registrations)
                {
                    if(!returnTypes.TryGetValue(serviceType.ServiceType, out var funcTypes) || registration.Dependency.Info.AsyncDecoratedRegistrations.Contains(serviceType))
                        continue;

                    // The Funcs of every service type of the registration construct it
                    var activation = registration.Dependency.Activation;
                    if(!constructed.TryGetValue(activation, out var constructingFuncTypes))
                    {
                        constructed[activation] = funcTypes;
                        continue;
                    }

                    if(ReferenceEquals(constructingFuncTypes, funcTypes))
                        continue;

                    foreach(var funcType in funcTypes)
                        AddConstructingFunc(constructed, activation, funcType);
                }
            }
        }

        // The generated containers implement their partial accessors (TransformContainer.ExtractPartialAccessors), whose Funcs
        // construct a registration of the container itself, not the Register output's registrations
        if(!analyzerContext.Containers.IsEmpty)
            AddConstructedByContainerAccessors(analyzerContext, constructed, cancellationToken);

        return constructed;

        void ScanParameters(ImmutableArray<IParameterSymbol> parameters)
        {
            foreach(var parameter in parameters)
                Scan(parameter.Type);
        }

        void ScanMembers(IMethodSymbol? constructor, ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers)
        {
            foreach(var parameter in constructor?.Parameters ?? [])
                Scan(parameter.Type);

            foreach(var (member, _) in injectedMembers)
                ScanMember(member);
        }

        void ScanMember(ISymbol member)
        {
            switch(member)
            {
                case IPropertySymbol property:
                    Scan(property.Type);
                    break;
                case IFieldSymbol field:
                    Scan(field.Type);
                    break;
                case IMethodSymbol method:
                    foreach(var parameter in method.Parameters)
                        Scan(parameter.Type);
                    break;
            }
        }

        void Scan(ITypeSymbol type)
        {
            if(type.TryGetWrapperInfo(out var wrapperInfo)
                && wrapperInfo.Kind is WrapperKind.Enumerable or WrapperKind.ReadOnlyCollection or WrapperKind.Collection
                    or WrapperKind.ReadOnlyList or WrapperKind.List or WrapperKind.Array
                && wrapperInfo.ElementType is { } elementType)
            {
                type = elementType;
            }

            if(!TryGetFuncWithArgumentsReturnType(type, out var funcType, out var returnType))
                return;

            if(!returnTypes.TryGetValue(returnType, out var funcTypes))
                returnTypes[returnType] = [funcType];
            else if(!funcTypes.Contains(funcType, RuntimeTypeSymbolComparer.Instance))
                funcTypes.Add(funcType);
        }
    }

    /// <summary>
    /// Adds the registrations that the <c>Func</c> with input parameters returned by a partial accessor of a generated container
    /// constructs. Like the generator (<c>ResolvePartialAccessorDependencies</c>, <c>MultiParamFuncDependency</c>), the
    /// accessor's <c>Func</c> constructs the last registration of its return type and key (from its <c>[IocInject]</c>) among
    /// the container's own registrations (<c>ExplicitOnly</c>: the <c>[IocRegisterFor]</c> on the container, in their order;
    /// otherwise every registration, with <c>IncludeTags</c> those with one of them), unless that registration is async-init
    /// or the container registers the <c>Func</c> type itself. Otherwise, and for a collection of <c>Func</c>s, the container
    /// resolves it from the fallback provider, where the Register output registers no <c>Func</c> for an accessor.
    /// </summary>
    private static void AddConstructedByContainerAccessors(
        AnalyzerContext analyzerContext,
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructed,
        CancellationToken cancellationToken)
    {
        // The registrations by the span of their attribute, built for the first ExplicitOnly container
        Dictionary<(SyntaxTree Tree, TextSpan Span), DependencyRegistration>? registrationsBySpan = null;

        foreach(var (container, containerAttribute) in analyzerContext.Containers)
        {
            List<DependencyRegistration>? scope = null;
            foreach(var member in container.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var type = member switch
                {
                    IMethodSymbol { IsStatic: false, IsPartialDefinition: true, ReturnsVoid: false, Parameters.Length: 0, IsGenericMethod: false, MethodKind: MethodKind.Ordinary } method => method.ReturnType,
                    IPropertySymbol { IsStatic: false, IsPartialDefinition: true, GetMethod: not null } property => property.Type,
                    _ => null
                };
                if(type is null || !TryGetFuncWithArgumentsReturnType(type, out var funcType, out var returnType))
                    continue;

                scope ??= GetContainerRegistrations(analyzerContext, container, containerAttribute, ref registrationsBySpan);

                var serviceKey = new ServiceSymbolKey(returnType, GetAccessorKey(member));
                var funcKey = new ServiceSymbolKey(funcType, serviceKey.Key);
                DependencyRegistration? winner = null;
                var hasFuncRegistration = false;
                foreach(var registration in scope)
                {
                    if(IsOpenGeneric(registration))
                        continue;

                    if(registration.Registrations.Contains(serviceKey))
                        winner = registration;
                    else if(registration.Registrations.Contains(funcKey))
                        hasFuncRegistration = true;
                }

                if(winner is null
                    || hasFuncRegistration
                    || IsAsyncInit(analyzerContext, winner)
                    || winner.Dependency.Info.AsyncDecoratedRegistrations.Contains(serviceKey))
                {
                    continue;
                }

                AddConstructingFunc(constructed, winner.Dependency.Activation, funcType);
            }
        }

        // Like the generator's ExtractKeyFromInjectAttribute
        static string? GetAccessorKey(ISymbol member)
        {
            foreach(var attribute in member.GetAttributes())
            {
                if(attribute.AttributeClass?.IsInject == true)
                    return attribute.GetKeyInfo().Key;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the registrations of a generated container in the generator's order, like its <c>GroupExplicitOnlyRegistrations</c>
    /// and <c>FilterRegistrationsForContainer</c>: with <c>ExplicitOnly</c>, the <c>[IocRegisterFor]</c> on the container in
    /// their order; otherwise every registration, filtered by <c>IncludeTags</c> when it has any.
    /// </summary>
    private static List<DependencyRegistration> GetContainerRegistrations(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol container,
        AttributeData containerAttribute,
        ref Dictionary<(SyntaxTree Tree, TextSpan Span), DependencyRegistration>? registrationsBySpan)
    {
        var explicitOnly = false;
        HashSet<string>? includeTags = null;
        foreach(var namedArgument in containerAttribute.NamedArguments)
        {
            switch(namedArgument.Key)
            {
                case "ExplicitOnly" when namedArgument.Value.Value is bool value:
                    explicitOnly = value;
                    break;
                case "IncludeTags" when !namedArgument.Value.IsNull && namedArgument.Value.Values.Length > 0:
                    includeTags = new(StringComparer.Ordinal);
                    foreach(var tag in namedArgument.Value.Values)
                    {
                        if(tag.Value is string value)
                            includeTags.Add(value);
                    }
                    break;
            }
        }

        if(explicitOnly)
        {
            if(registrationsBySpan is null)
            {
                registrationsBySpan = new(analyzerContext.DependencyRegistrations.Count);
                foreach(var registration in analyzerContext.DependencyRegistrations)
                {
                    if(registration.Dependency.Activation.Location is { SourceTree: { } tree } location)
                        registrationsBySpan[(tree, location.SourceSpan)] = registration;
                }
            }

            var explicitRegistrations = new List<DependencyRegistration>();
            foreach(var attribute in container.GetAttributes())
            {
                if(attribute.AttributeClass is { } attributeClass
                    && AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols)
                    && attribute.ApplicationSyntaxReference is { } syntax
                    && registrationsBySpan.TryGetValue((syntax.SyntaxTree, syntax.Span), out var registration))
                {
                    explicitRegistrations.Add(registration);
                }
            }

            return explicitRegistrations;
        }

        if(includeTags is null)
            return analyzerContext.DependencyRegistrations;

        return [.. analyzerContext.DependencyRegistrations.Where(registration => registration.Tags.Any(includeTags.Contains))];
    }

    /// <summary>
    /// Records that a <c>Func</c> type constructs a registration. The lists can be shared with other registrations, so a
    /// <c>Func</c> type is added to a copy.
    /// </summary>
    private static void AddConstructingFunc(
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructed,
        ServiceActivation activation,
        INamedTypeSymbol funcType)
    {
        if(!constructed.TryGetValue(activation, out var funcTypes))
        {
            constructed[activation] = [funcType];
            return;
        }

        if(!funcTypes.Contains(funcType, RuntimeTypeSymbolComparer.Instance))
            constructed[activation] = [.. funcTypes, funcType];
    }

    private static bool IsOpenGeneric(DependencyRegistration registration)
        => registration.Dependency.Type is { IsUnboundGenericType: true } or { IsGenericType: true, IsDefinition: true };

    /// <summary>
    /// Gets whether a registration is async-init, so no <c>Func</c> with input parameters constructs it: it cannot initialize
    /// it. An Instance registration is never async-init, and its Func calls only the synchronous [Inject] methods.
    /// </summary>
    private static bool IsAsyncInit(AnalyzerContext analyzerContext, DependencyRegistration registration)
        => !registration.Dependency.Activation.HasInstance
            && AnalyzerHelpers.IsAsyncInitImplementation(registration.Dependency.Type, analyzerContext.Features);

    /// <summary>
    /// Gets whether the effective generic factory of a closed registration does not match its implementation type, which the
    /// generator's <c>CreateServiceRegistrations</c> then constructs for the self-registration, like
    /// <see cref="EnumerateRootDependencies"/>.
    /// </summary>
    private static bool IsSelfConstructedByGenericFactory(AnalyzerContext analyzerContext, AnalyzedRegistration registration, IMethodSymbol factoryMethod)
        => factoryMethod.TypeParameters.Length > 0
            && !registration.IsOpenGeneric
            && registration.Implementation.IsConstructibleIn(analyzerContext.Assembly)
            && GetEffectiveFactory(analyzerContext, registration).Factory is { TypeParameterCount: > 0 } factory
            && IocSourceGenerator.CloseGenericFactory(factory, registration.Implementation.GetTypeData()) is null;

    /// <summary>
    /// Gets whether a <c>Func</c> with input parameters passes one of its arguments to <paramref name="target"/> (a parameter,
    /// or an injected property or field) when it constructs <paramref name="constructor"/>'s type. Like the generator
    /// (<c>WriteFuncFactoryBody</c>, <c>MultiParamFuncDependency</c>), each parameter of the constructor, then each injected
    /// property, then each injected field, then each parameter of the inject methods that do not return <c>Task</c> takes the first unused
    /// argument of the same runtime type; the others are resolved, a <c>[ServiceKey]</c> parameter with the registration's key.
    /// </summary>
    /// <param name="injectedMembers">The members the <c>Func</c> injects (see <see cref="GetFuncInjectedMembers"/>).</param>
    private static bool IsPassedFuncArgument(
        INamedTypeSymbol funcType,
        ISymbol target,
        IMethodSymbol? constructor,
        ImmutableArray<ISymbol> injectedMembers)
    {
        var inputTypes = funcType.TypeArguments;
        Span<bool> used = stackalloc bool[inputTypes.Length - 1];

        foreach(var constructorParameter in constructor?.Parameters ?? [])
        {
            var passed = TryConsume(constructorParameter.Type, used);
            if(SymbolEqualityComparer.Default.Equals(constructorParameter, target))
                return passed;
        }

        // Like the generator, which sorts the injected members by stage (SortInjectionMembersByStage): the properties, then the
        // fields, each in declaration order
        for(var stage = 0; stage < 2; stage++)
        {
            foreach(var member in injectedMembers)
            {
                var type = (stage, member) switch
                {
                    (0, IPropertySymbol property) => property.Type,
                    (1, IFieldSymbol field) => field.Type,
                    _ => null
                };
                if(type is null)
                    continue;

                var passed = TryConsume(type, used);
                if(SymbolEqualityComparer.Default.Equals(member, target))
                    return passed;
            }
        }

        foreach(var member in injectedMembers)
        {
            // The Func never calls a Task-returning inject method, whatever the features
            if(member is not IMethodSymbol method || AnalyzerHelpers.IsNonGenericTaskType(method.ReturnType))
                continue;

            foreach(var methodParameter in method.Parameters)
            {
                var passed = TryConsume(methodParameter.Type, used);
                if(SymbolEqualityComparer.Default.Equals(methodParameter, target))
                    return passed;
            }
        }

        return false;

        bool TryConsume(ITypeSymbol type, Span<bool> used)
        {
            for(var i = 0; i < used.Length; i++)
            {
                if(used[i] || !RuntimeTypeSymbolComparer.Instance.Equals(inputTypes[i], type))
                    continue;

                used[i] = true;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the members a registration injects, like the generator's <c>ExtractAndMergeInjectionMembers</c>: its
    /// <c>[IocInject]</c> members, then the members listed in its <c>InjectMembers</c> (see
    /// <see cref="GetListedInjectMembers"/>) that are not <c>[IocInject]</c> members of the same name.
    /// <see cref="IsPassedFuncArgument"/> takes them by stage, like <c>SortInjectionMembersByStage</c>.
    /// </summary>
    private static ImmutableArray<ISymbol> GetFuncInjectedMembers(
        ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers,
        ImmutableArray<(ISymbol Member, Location Location)> listedMembers)
    {
        var members = ImmutableArray.CreateBuilder<ISymbol>(injectedMembers.Length + listedMembers.Length);
        foreach(var (member, _) in injectedMembers)
            members.Add(member);

        foreach(var (member, _) in listedMembers)
        {
            if(!IsInjectedMemberName(member, injectedMembers))
                members.Add(member);
        }

        return members.ToImmutable();
    }

    /// <summary>
    /// Gets whether an <c>[IocInject]</c> member has the name of <paramref name="member"/>, which then replaces it in
    /// <c>InjectMembers</c> (the generator's <c>MergeInjectionMembers</c>).
    /// </summary>
    private static bool IsInjectedMemberName(ISymbol member, ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers)
    {
        foreach(var (injected, _) in injectedMembers)
        {
            if(injected.Name == member.Name)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the members listed in the <c>InjectMembers</c> of a registration's attribute, which the generator injects like its
    /// <c>[IocInject]</c> members (empty without any).
    /// </summary>
    private static ImmutableArray<(ISymbol Member, Location Location)> GetListedInjectMembers(AnalyzerContext analyzerContext, ServiceActivation activation)
        => !analyzerContext.InjectMembers.IsEmpty
            && activation.Location is { SourceTree: { } tree } location
            && analyzerContext.InjectMembers.TryGetValue((tree, location.SourceSpan), out var members)
                ? members
                : [];
}
