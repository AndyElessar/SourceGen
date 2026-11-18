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
    /// registers its closed forms), except a service type of an <c>Instance</c> registration whose decorators have async inject
    /// methods (see <see cref="DependencyRegistration.AsyncDecoratedRegistrations"/>), and constructs the implementation type with
    /// the registration's key even for an Instance or Factory registration, unless an explicit registration of the <c>Func</c>
    /// type replaces it (<c>ApplyExplicitWrapperSuppression</c>, see <see cref="ExplicitFuncRegistrations"/>). Only with the
    /// Register output (the <c>Register</c> feature). The return type of a partial accessor of a generated container, and a Func nested in another
    /// wrapper, construct only a registration of a container (see <see cref="AddConstructedByContainers"/>). The parameters of a
    /// generic factory are scanned as declared, not closed for each service type. Each registration maps to the distinct requested <c>Func</c>
    /// types that construct it, whose input parameters the generator passes to the parameters of their type (see
    /// <see cref="IsPassedFuncArgument"/>).
    /// </summary>
    private static Dictionary<ServiceActivation, List<INamedTypeSymbol>> CollectRegistrationsConstructedByFunc(
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var constructed = new Dictionary<ServiceActivation, List<INamedTypeSymbol>>();

        // The Register output registers the Funcs; without it, only the generated containers construct them
        if((analyzerContext.Features & IocFeatures.Register) != 0)
            AddConstructedByRegisterOutput(analyzerContext, constructed, cancellationToken);

        // The generated containers construct the Funcs of their partial accessors and the Funcs nested in another wrapper inline,
        // from their own registrations
        if(!analyzerContext.Containers.IsEmpty)
            AddConstructedByContainers(analyzerContext, constructed, cancellationToken);

        return constructed;
    }

    /// <summary>
    /// Adds the registrations that the <c>Func</c> registrations of the Register output construct (see
    /// <see cref="CollectRegistrationsConstructedByFunc"/>), except those an explicit registration of the <c>Func</c> type
    /// replaces (see <see cref="ExplicitFuncRegistrations"/>).
    /// </summary>
    private static void AddConstructedByRegisterOutput(
        AnalyzerContext analyzerContext,
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructed,
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

        if(returnTypes.Count == 0)
            return;

        var explicitFuncs = ExplicitFuncRegistrations.Collect(analyzerContext, returnTypes);
        foreach(var registration in analyzerContext.DependencyRegistrations)
        {
            if(IsOpenGeneric(registration) || IsAsyncInit(analyzerContext, registration))
                continue;

            foreach(var serviceType in registration.Registrations)
            {
                if(!returnTypes.TryGetValue(serviceType.ServiceType, out var funcTypes) || registration.AsyncDecoratedRegistrations.Contains(serviceType))
                    continue;

                // The Funcs of every service type of the registration construct it, except those an explicit registration
                // replaces
                if(explicitFuncs is not null)
                {
                    funcTypes = explicitFuncs.RemoveReplaced(funcTypes, serviceType.Key, registration.Tags);
                    if(funcTypes.Count == 0)
                        continue;
                }

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
    /// The explicit registrations of the requested <c>Func</c> types with input parameters (as a service type), by (type, key),
    /// with their tags, like the generator's <c>CollectExplicitWrapperRegistrations</c>: an open generic or async-init
    /// registration, one under <c>KeyedService.AnyKey</c>, and an Instance that is not Singleton (which the Register output
    /// does not register, see <c>CreateRegisterEntry</c>) do not count. The recorded registrations already leave out the
    /// service types a generic factory does not match.
    /// </summary>
    private sealed class ExplicitFuncRegistrations
    {
        private readonly Dictionary<ServiceSymbolKey, TagCoverage> _coverage = [];

        /// <summary>Returns <see langword="null"/> when no requested <c>Func</c> type is registered explicitly.</summary>
        public static ExplicitFuncRegistrations? Collect(
            AnalyzerContext analyzerContext,
            Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> returnTypes)
        {
            ExplicitFuncRegistrations? explicitFuncs = null;
            foreach(var registration in analyzerContext.DependencyRegistrations)
            {
                foreach(var serviceKey in registration.Registrations)
                {
                    if(!TryGetFuncWithArgumentsReturnType(serviceKey.ServiceType, out _, out var returnType)
                        || !returnTypes.ContainsKey(returnType)
                        || serviceKey.Key?.EndsWith("KeyedService.AnyKey", StringComparison.Ordinal) == true
                        || IsOpenGeneric(registration)
                        || IsAsyncInit(analyzerContext, registration)
                        || registration.AsyncDecoratedRegistrations.Contains(serviceKey)
                        // The Register output does not register an Instance that is not Singleton (SGIOC009)
                        || registration.Dependency.Activation is { HasInstance: true, Lifetime: not ServiceLifetime.Singleton })
                    {
                        continue;
                    }

                    explicitFuncs ??= new();
                    if(!explicitFuncs._coverage.TryGetValue(serviceKey, out var coverage))
                    {
                        coverage = new TagCoverage();
                        explicitFuncs._coverage[serviceKey] = coverage;
                    }

                    coverage.Add(registration.Tags);
                }
            }

            return explicitFuncs;
        }

        /// <summary>
        /// Gets the <c>Func</c> types that construct a registration of <c>TReturn</c> with <paramref name="key"/> and
        /// <paramref name="tags"/>, like <c>ApplyExplicitWrapperSuppression</c>: a generated <c>Func</c> is not registered
        /// while an explicit registration of its type and key is always registered with the registration; one registered
        /// with it only for some passed tags suppresses it for those tags only, so the generated <c>Func</c> still constructs
        /// it. Returns <paramref name="funcTypes"/> itself when none is replaced.
        /// </summary>
        public List<INamedTypeSymbol> RemoveReplaced(List<INamedTypeSymbol> funcTypes, string? key, ImmutableArray<string> tags)
        {
            List<INamedTypeSymbol>? remaining = null;
            for(var i = 0; i < funcTypes.Count; i++)
            {
                var replaced = _coverage.TryGetValue(new ServiceSymbolKey(funcTypes[i], key), out var coverage)
                    && coverage.IsAlwaysRegisteredWith(tags);
                if(replaced)
                    remaining ??= [.. funcTypes.Take(i)];
                else
                    remaining?.Add(funcTypes[i]);
            }

            return remaining ?? funcTypes;
        }
    }

    /// <summary>
    /// The tags of the registrations of a service, like the generator's <c>GroupRegistrationsForRegister.TagCoverage</c>: an
    /// untagged registration is registered only when no tags are passed, a tagged one when any of its tags is.
    /// </summary>
    private sealed class TagCoverage
    {
        private bool _hasUntagged;
        private HashSet<string>? _tags;

        public void Add(ImmutableArray<string> tags)
        {
            if(tags.IsEmpty)
            {
                _hasUntagged = true;
                return;
            }

            _tags ??= new(StringComparer.Ordinal);
            foreach(var tag in tags)
                _tags.Add(tag);
        }

        /// <summary>
        /// Gets whether one of the registrations is registered whenever a registration with <paramref name="tags"/> is: an
        /// untagged one with an untagged registration, or tagged ones that together have every tag of <paramref name="tags"/>.
        /// </summary>
        public bool IsAlwaysRegisteredWith(ImmutableArray<string> tags)
        {
            if(tags.IsEmpty)
                return _hasUntagged;

            if(_tags is null)
                return false;

            foreach(var tag in tags)
            {
                if(!_tags.Contains(tag))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Adds the registrations that a generated container constructs with a <c>Func</c> with input parameters that the Register
    /// output does not register: the return type of a partial accessor (<c>ResolvePartialAccessorDependencies</c>), and a
    /// <c>Func</c> nested in another wrapper that the container constructs inline while the Register output resolves the inner
    /// <c>Func</c> (see <see cref="AddConstructedByContainerFunc"/>). Like the generator's container entries, the container
    /// resolves the dependencies (see <see cref="CollectContainerFuncDependencies"/>) of its registrations without an
    /// Instance, and of the Instance registrations a <c>Func</c> constructs (<c>MultiParamFuncDependency</c>), until no more
    /// is constructed.
    /// </summary>
    private static void AddConstructedByContainers(
        AnalyzerContext analyzerContext,
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructed,
        CancellationToken cancellationToken)
    {
        // The registrations by the span of their attribute, built for the first ExplicitOnly container
        Dictionary<(SyntaxTree Tree, TextSpan Span), DependencyRegistration>? registrationsBySpan = null;
        Dictionary<Location, List<(INamedTypeSymbol Type, string? Key)>>? dependencies = null;

        foreach(var (container, containerAttribute) in analyzerContext.Containers)
        {
            var scope = GetContainerRegistrations(analyzerContext, container, containerAttribute, ref registrationsBySpan);
            dependencies ??= CollectContainerFuncDependencies(analyzerContext, cancellationToken);

            // The registrations whose dependencies the container resolves
            var resolved = new HashSet<Location>();
            var pending = new Queue<Location>();
            foreach(var registration in scope)
            {
                if(!registration.Dependency.Activation.HasInstance && registration.Dependency.Activation.Location is { } location && resolved.Add(location))
                    pending.Enqueue(location);
            }

            foreach(var (member, type) in EnumeratePartialAccessors(container))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if(type is INamedTypeSymbol namedType && ConstructsFuncInline(namedType))
                    ResolveFuncConstructed(AddConstructedByContainerFunc(analyzerContext, constructed, scope, namedType, GetAccessorKey(member)));
            }

            while(pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if(!dependencies.TryGetValue(pending.Dequeue(), out var consumerDependencies))
                    continue;

                foreach(var (type, key) in consumerDependencies)
                    ResolveFuncConstructed(AddConstructedByContainerFunc(analyzerContext, constructed, scope, type, key));
            }

            // A Func constructs an Instance registration inline, resolving its dependencies too
            void ResolveFuncConstructed(DependencyRegistration? registration)
            {
                if(registration?.Dependency.Activation is { HasInstance: true, Location: { } location } && resolved.Add(location))
                    pending.Enqueue(location);
            }
        }

    }

    /// <summary>
    /// Gets the partial accessors of a generated container: its non-static, parameterless partial methods that return a value
    /// and its partial properties with a getter, with their declared type.
    /// </summary>
    private static IEnumerable<(ISymbol Member, ITypeSymbol Type)> EnumeratePartialAccessors(INamedTypeSymbol container)
    {
        foreach(var member in container.GetMembers())
        {
            var type = member switch
            {
                IMethodSymbol { IsStatic: false, IsPartialDefinition: true, ReturnsVoid: false, Parameters.Length: 0, IsGenericMethod: false, MethodKind: MethodKind.Ordinary } method => method.ReturnType,
                IPropertySymbol { IsStatic: false, IsPartialDefinition: true, GetMethod: not null } property => property.Type,
                _ => null
            };
            if(type is not null)
                yield return (member, type);
        }
    }

    /// <summary>
    /// Gets the key of a partial accessor, like the generator's ExtractKeyFromInjectAttribute.
    /// </summary>
    private static string? GetAccessorKey(ISymbol member)
    {
        foreach(var attribute in member.GetAttributes())
        {
            if(attribute.AttributeClass?.IsInject == true)
                return attribute.GetKeyInfo().Key;
        }

        return null;
    }

    /// <summary>
    /// SGIOC046: Reports the partial accessors of the generated containers whose <c>Func</c> with input parameters (also nested
    /// inline, see <see cref="ConstructsFuncInline"/>) the container resolves from the fallback provider because the
    /// registration it resolves for the return type (see <see cref="FindContainerFuncRegistration"/>) is an <c>Instance</c>
    /// registration whose decorators applied to it have async inject methods. When any registration of the return type and the
    /// key is async-init, the ContainerAnalyzer reports SGIOC029 for the accessor instead.
    /// </summary>
    private static void AnalyzeContainerAccessorFuncsOfAsyncDecoratedInstances(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        if(analyzerContext.Containers.IsEmpty
            || !analyzerContext.DependencyRegistrations.Any(static registration =>
                registration.Dependency.Activation.HasInstance && !registration.AsyncDecoratedRegistrations.IsEmpty))
        {
            return;
        }

        Dictionary<(SyntaxTree Tree, TextSpan Span), DependencyRegistration>? registrationsBySpan = null;
        foreach(var (container, containerAttribute) in analyzerContext.Containers)
        {
            List<DependencyRegistration>? scope = null;
            foreach(var (member, type) in EnumeratePartialAccessors(container))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if(type is not INamedTypeSymbol namedType || !ConstructsFuncInline(namedType))
                    continue;

                scope ??= GetContainerRegistrations(analyzerContext, container, containerAttribute, ref registrationsBySpan);
                if(FindContainerFuncRegistration(scope, namedType, GetAccessorKey(member), out _, out var serviceKey) is not { } winner
                    || !winner.Dependency.Activation.HasInstance
                    || !winner.AsyncDecoratedRegistrations.Contains(serviceKey)
                    || HasAsyncInitRegistration(analyzerContext, serviceKey))
                {
                    continue;
                }

                reportDiagnostic(Diagnostic.Create(
                    AccessorFuncOfAsyncDecoratedInstance,
                    member.Locations.FirstOrDefault(),
                    member.Name,
                    namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    serviceKey.ServiceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        // Like the ContainerAnalyzer's BuildAsyncInitServiceKeys, which reports SGIOC029 when any registration is async-init
        static bool HasAsyncInitRegistration(AnalyzerContext analyzerContext, ServiceSymbolKey serviceKey)
        {
            foreach(var registration in analyzerContext.DependencyRegistrations)
            {
                if(!registration.Dependency.Activation.HasInstance
                    && registration.Registrations.Contains(serviceKey)
                    && (IsAsyncInit(analyzerContext, registration) || registration.AsyncDecoratedRegistrations.Contains(serviceKey)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Adds the registration that a container constructs for a dependency of <paramref name="type"/> and
    /// <paramref name="key"/> that is a <c>Func</c> with input parameters or nests one inline (see
    /// <see cref="ConstructsFuncInline"/>). Like the generator (<c>ResolveWrapperDependencyForContainerEntryModel</c>,
    /// <c>MultiParamFuncDependency</c>), a wrapper type the container registers itself resolves as that registration, at any
    /// level; otherwise the <c>Func</c> constructs the last registration of its return type and the key among the container's
    /// own registrations (<paramref name="scope"/>), unless it is async-init or its service type is decorated with async inject
    /// methods (also for an <c>Instance</c> registration). Otherwise the container resolves it from the fallback provider. Returns the constructed registration, if any.
    /// </summary>
    private static DependencyRegistration? AddConstructedByContainerFunc(
        AnalyzerContext analyzerContext,
        Dictionary<ServiceActivation, List<INamedTypeSymbol>> constructed,
        List<DependencyRegistration> scope,
        INamedTypeSymbol type,
        string? key)
    {
        if(FindContainerFuncRegistration(scope, type, key, out var funcType, out var serviceKey) is not { } winner
            || IsAsyncInit(analyzerContext, winner)
            || winner.AsyncDecoratedRegistrations.Contains(serviceKey))
        {
            return null;
        }

        AddConstructingFunc(constructed, winner.Dependency.Activation, funcType!);
        return winner;
    }

    /// <summary>
    /// Finds the registration that a container resolves for the <c>Func</c> with input parameters of a dependency of
    /// <paramref name="type"/> and <paramref name="key"/> (see <see cref="AddConstructedByContainerFunc"/>): the last
    /// registration of its return type and the key among <paramref name="scope"/>, or <see langword="null"/> when the container
    /// registers one of the wrapper types itself, has no registration of the return type, or no such <c>Func</c> is nested inline.
    /// </summary>
    private static DependencyRegistration? FindContainerFuncRegistration(
        List<DependencyRegistration> scope,
        INamedTypeSymbol type,
        string? key,
        out INamedTypeSymbol? funcType,
        out ServiceSymbolKey serviceKey)
    {
        funcType = null;
        serviceKey = default;
        var current = type;
        while(true)
        {
            if(IsRegisteredIn(scope, new ServiceSymbolKey(current, key)))
                return null;

            if(TryGetFuncWithArgumentsReturnType(current, out funcType, out var returnType))
            {
                serviceKey = new ServiceSymbolKey(returnType, key);
                DependencyRegistration? winner = null;
                foreach(var registration in scope)
                {
                    if(!IsOpenGeneric(registration) && registration.Registrations.Contains(serviceKey))
                        winner = registration;
                }

                return winner;
            }

            if(!TryGetInlineWrapperInnerType(current, out var inner))
                return null;

            current = inner;
        }

        static bool IsRegisteredIn(List<DependencyRegistration> scope, ServiceSymbolKey serviceKey)
        {
            foreach(var registration in scope)
            {
                if(!IsOpenGeneric(registration) && registration.Registrations.Contains(serviceKey))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets whether a container constructs a <c>Func</c> with input parameters for a dependency of <paramref name="type"/>: it is
    /// such a <c>Func</c>, or nests one in <c>Lazy&lt;T&gt;</c>, a parameterless <c>Func&lt;T&gt;</c> or
    /// <c>KeyValuePair&lt;TKey, T&gt;</c>, which the container builds inline (<c>ResolveInnerDependencyForContainerEntryModel</c>).
    /// A collection, <c>Task&lt;T&gt;</c> or a dictionary of them is resolved from the fallback provider.
    /// </summary>
    private static bool ConstructsFuncInline(INamedTypeSymbol type)
    {
        var current = type;
        while(true)
        {
            if(TryGetFuncWithArgumentsReturnType(current, out _, out _))
                return true;

            if(!TryGetInlineWrapperInnerType(current, out var inner))
                return false;

            current = inner;
        }
    }

    /// <summary>
    /// Gets the type that a container resolves inline inside <c>Lazy&lt;T&gt;</c>, a parameterless <c>Func&lt;T&gt;</c> or
    /// <c>KeyValuePair&lt;TKey, T&gt;</c>.
    /// </summary>
    private static bool TryGetInlineWrapperInnerType(
        INamedTypeSymbol type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? inner)
    {
        inner = null;
        if(!type.IsGenericType)
            return false;

        var definition = type.OriginalDefinition;
        var index = definition.MetadataName switch
        {
            "Lazy`1" or "Func`1" when definition.ContainingType is null
                && definition.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true } => 0,
            "KeyValuePair`2" when IsCollectionsGenericTopLevelType(definition) => 1,
            _ => -1
        };

        if(index < 0)
            return false;

        inner = type.TypeArguments[index] as INamedTypeSymbol;
        return inner is not null;
    }

    /// <summary>
    /// Collects, by the location of each closed registration, its dependencies that are a <c>Func</c> with input parameters
    /// or nest one inline (see <see cref="ConstructsFuncInline"/>), with their key. Like the generator's container entries
    /// and <c>MultiParamFuncDependency</c>: the parameters of the effective factory method, otherwise of the constructor (both
    /// when a generic factory's mapping does not match the implementation type), the <c>[IocInject]</c> members and the
    /// members listed in <c>InjectMembers</c>, and the constructor parameters and <c>[IocInject]</c> members of the decorators
    /// applied to its service types (an open generic decorator closed for the service type).
    /// </summary>
    private static Dictionary<Location, List<(INamedTypeSymbol Type, string? Key)>> CollectContainerFuncDependencies(
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var dependencies = new Dictionary<Location, List<(INamedTypeSymbol Type, string? Key)>>();
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(registration.IsOpenGeneric || registration.Location is not { } consumer)
                continue;

            List<(INamedTypeSymbol Type, string? Key)>? consumerDependencies = null;
            var factoryMethod = GetEffectiveFactoryMethod(analyzerContext, registration);
            if(factoryMethod is not null)
                AddParameters(factoryMethod.Parameters, methodKey: null);

            if(factoryMethod is null || IsSelfConstructedByGenericFactory(analyzerContext, registration, factoryMethod))
                AddParameters(registration.Implementation.GetSelectedConstructor(analyzerContext.Assembly)?.Parameters ?? [], methodKey: null);

            ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> injectedMembers = [.. registration.Implementation.GetInjectedMembers()];
            foreach(var (member, injectAttribute) in injectedMembers)
                AddMember(member, injectAttribute.GetKeyInfo().Key);

            if(registration.Attribute.ApplicationSyntaxReference is { } syntax
                && analyzerContext.InjectMembers.TryGetValue((syntax.SyntaxTree, syntax.Span), out var listedMembers))
            {
                foreach(var (member, location) in listedMembers)
                {
                    if(!IsInjectedMemberName(member, injectedMembers))
                        AddMember(member, analyzerContext.InjectMemberKeys.TryGetValue((location.SourceTree!, location.SourceSpan), out var key) ? key : null);
                }
            }

            // Like AnalyzeDecorators, the decorators that have a parameter for the decorated instance of a service type, other
            // than the implementation type itself
            if(!registration.Decorators.IsEmpty)
            {
                HashSet<INamedTypeSymbol>? appliedDecorators = null;
                foreach(var serviceKey in registration.Registrations)
                {
                    var serviceType = serviceKey.ServiceType;
                    if(SymbolEqualityComparer.Default.Equals(serviceType, registration.Implementation))
                        continue;

                    var serviceTypeData = serviceType.GetTypeData();
                    foreach(var decorator in registration.Decorators)
                    {
                        if(!AnalyzerHelpers.TryGetDecoratedParameterIndexes(analyzerContext.GetDecoratorTypeData(decorator), serviceTypeData, out var parameterIndexes)
                            || parameterIndexes.IsEmpty)
                        {
                            continue;
                        }

                        var definition = decorator.IsUnboundGenericType ? decorator.OriginalDefinition : decorator;
                        (appliedDecorators ??= new(SymbolEqualityComparer.Default)).Add(TryConstructDecorator(definition, serviceType) ?? definition);
                    }
                }

                foreach(var decorator in appliedDecorators ?? [])
                {
                    AddParameters(decorator.GetSelectedConstructor(analyzerContext.Assembly)?.Parameters ?? [], methodKey: null);
                    foreach(var (member, injectAttribute) in decorator.GetInjectedMembers())
                        AddMember(member, injectAttribute.GetKeyInfo().Key);
                }
            }

            if(consumerDependencies is not null)
                dependencies[consumer] = consumerDependencies;

            void AddMember(ISymbol member, string? key)
            {
                switch(member)
                {
                    case IPropertySymbol property:
                        Add(property.Type, key);
                        break;
                    case IFieldSymbol field:
                        Add(field.Type, key);
                        break;
                    case IMethodSymbol method:
                        AddParameters(method.Parameters, key);
                        break;
                }
            }

            void AddParameters(ImmutableArray<IParameterSymbol> parameters, string? methodKey)
            {
                foreach(var parameter in parameters)
                    Add(parameter.Type, parameter.GetServiceKeyAndAttributeInfo().ServiceKey ?? methodKey);
            }

            void Add(ITypeSymbol type, string? key)
            {
                if(type is INamedTypeSymbol namedType && ConstructsFuncInline(namedType))
                    (consumerDependencies ??= []).Add((namedType, key));
            }
        }

        return dependencies;
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
