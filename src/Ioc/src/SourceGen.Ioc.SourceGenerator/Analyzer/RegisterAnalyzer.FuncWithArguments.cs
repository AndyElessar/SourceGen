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
    /// of its effective factory method, otherwise of its implementation type's constructor, also with an Instance; and its
    /// injected members, also with an Instance) or an applied decorator, directly or as the element of a collection; it is
    /// registered for every closed, not async-init registration of <c>TReturn</c>, and constructs the implementation type
    /// with the registration's key even for an Instance or Factory registration. The parameters of a generic factory are
    /// scanned as declared, not closed for each service type.
    /// </summary>
    private static HashSet<ServiceActivation> CollectRegistrationsConstructedByFunc(
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var returnTypes = new HashSet<INamedTypeSymbol>(RuntimeTypeSymbolComparer.Instance);
        var constructorsScanned = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var membersScanned = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var implementation = registration.Implementation;
            if(GetEffectiveFactory(analyzerContext, registration).Method is { } factoryMethod)
                ScanParameters(factoryMethod.Parameters);
            else if(constructorsScanned.Add(implementation))
                ScanParameters(implementation.GetSelectedConstructor(analyzerContext.Assembly)?.Parameters ?? []);

            if(membersScanned.Add(implementation))
                ScanMembers(null, [.. implementation.GetInjectedMembers()]);
        }

        foreach(var decorator in analyzerContext.AppliedDecorators.Keys)
            ScanMembers(decorator.GetSelectedConstructor(analyzerContext.Assembly), [.. decorator.GetInjectedMembers()]);

        var constructed = new HashSet<ServiceActivation>();
        if(returnTypes.Count == 0)
            return constructed;

        foreach(var registration in analyzerContext.DependencyRegistrations)
        {
            var implementation = registration.Dependency.Type;
            if(implementation.IsUnboundGenericType || implementation is { IsGenericType: true, IsDefinition: true })
                continue;

            // A Func with input parameters is not registered for an async-init registration, which it cannot initialize; an
            // Instance registration is never async-init, and its Func calls only the synchronous [Inject] methods
            var info = registration.Dependency.Info;
            var isAsyncInit = !registration.Dependency.Activation.HasInstance
                && AnalyzerHelpers.IsAsyncInitImplementation(implementation, analyzerContext.Features);
            foreach(var serviceType in registration.Registrations)
            {
                if(returnTypes.Contains(serviceType.ServiceType) && !isAsyncInit && !info.AsyncDecoratedRegistrations.Contains(serviceType))
                {
                    constructed.Add(registration.Dependency.Activation);
                    break;
                }
            }
        }

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

            if(TryGetFuncWithArgumentsReturnType(type, out _, out var returnType))
                returnTypes.Add(returnType);
        }
    }
}
