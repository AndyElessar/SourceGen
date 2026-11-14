using System.Text.RegularExpressions;

namespace SourceGen.Ioc;

partial class RegisterAnalyzer
{
    /// <summary>
    /// Reports SGIOC036 or SGIOC037 for each dependency of an open generic registration, of its decorators, or of its
    /// factory that the generator does not close because it nests type arguments deeper on every round of a cycle. The
    /// open generic registrations are analyzed with the generator's own analysis (<c>FindExpandingDependencies</c>), so the
    /// same dependencies are reported.
    /// </summary>
    private static void AnalyzeExpandingDependencies(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        if(analyzerContext.Registrations.IsEmpty && analyzerContext.DefaultAttributes.IsEmpty)
            return;

        var index = new Dictionary<string, List<OpenGenericRegistrationInfo>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, (INamedTypeSymbol Symbol, TypeData TypeData, List<(ISymbol Member, Location Location)> InjectMembers)>(StringComparer.Ordinal);
        var factoryOwners = new Dictionary<string, (IMethodSymbol Method, FactoryMethodData Factory)>(StringComparer.Ordinal);
        var decoratorTypeData = new Dictionary<INamedTypeSymbol, TypeData>(SymbolEqualityComparer.Default);
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(!registration.IsOpenGeneric)
                continue;

            var (implementation, registrations, decorators, attribute, _, hasInstance, _, _) = registration;

            // Built like the generator's open generic entries (ProcessSingleRegistration.CreateOpenGenericEntries)
            if(implementation.GetTypeData(extractConstructorParams: true, extractHierarchy: true) is not GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false } implementationType)
                continue;

            // InjectMembers are merged like the generator's ExtractAndMergeInjectionMembers: [IocInject] takes precedence
            var injectedMembers = IocSourceGenerator.ExtractInjectionMembers(implementation);
            var injectMembers = GetInjectMembers(attribute.ApplicationSyntaxReference, injectedMembers);
            var injectionMembers = injectMembers.Count == 0
                ? injectedMembers
                : IocSourceGenerator.MergeInjectionMembers(
                    injectedMembers,
                    [.. injectMembers.Select(static m => IocSourceGenerator.CreateInjectionMemberFromSymbol(m.Member, key: null, semanticModel: null)).OfType<InjectionMemberData>()]);

            var (factoryMethod, factory) = GetEffectiveFactory(analyzerContext, registration);

            ImmutableEquatableArray<TypeData> decoratorTypes = [.. decorators.Select(GetDecoratorTypeData)];
            ImmutableEquatableArray<TypeData> serviceTypes = [.. registrations.Select(static registration => registration.ServiceType.GetTypeData())];
            var info = new OpenGenericRegistrationInfo(
                implementationType,
                serviceTypes,
                implementationType.AllInterfaces ?? [],
                ServiceLifetime.Transient,
                Key: null,
                KeyType: 0,
                KeyValueType: null,
                decoratorTypes,
                Tags: [],
                injectionMembers,
                factory,
                // Only whether there is an instance matters
                Instance: hasInstance ? nameof(OpenGenericRegistrationInfo.Instance) : null);

            AddToIndex(info, serviceTypes.Concat(implementationType.AllInterfaces ?? []).Append(implementationType));
            AddFactoryOwner(factoryMethod, factory, implementationType);

            // An implementation registered by several attributes reports the InjectMembers of each
            if(owners.TryGetValue(implementationType.Name, out var owner))
                owner.InjectMembers.AddRange(injectMembers);
            else
                owners[implementationType.Name] = (implementation, implementationType, injectMembers);

            for(var i = 0; i < decorators.Length; i++)
            {
                // A decorator also registered as an implementation keeps the InjectMembers of its registrations
                if(decoratorTypes[i] is GenericTypeData { IsOpenGeneric: true } decoratorType && !owners.ContainsKey(decoratorType.Name))
                    owners[decoratorType.Name] = (decorators[i], decoratorType, []);
            }
        }

        // Default settings with a Factory and an open generic target type register the target type, which the factory creates,
        // like the generator's CreateOpenGenericEntriesFromDefaultSettings
        for(var i = 0; i < analyzerContext.DefaultAttributes.Length; i++)
        {
            var attribute = analyzerContext.DefaultAttributes[i];
            if(!HasFactory(attribute)
                || analyzerContext.DefaultSettings[i].TargetServiceType is not GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false } targetType)
            {
                continue;
            }

            var (factoryMethod, factory) = GetFactory(analyzerContext, attribute);
            var info = new OpenGenericRegistrationInfo(
                targetType,
                [targetType],
                AllInterfaces: [],
                ServiceLifetime.Transient,
                Key: null,
                KeyType: 0,
                KeyValueType: null,
                [.. analyzerContext.DefaultDecorators[i].Select(GetDecoratorTypeData)],
                Tags: [],
                InjectionMembers: [],
                factory,
                Instance: null);

            AddToIndex(info, [targetType]);
            AddFactoryOwner(factoryMethod, factory, targetType);
        }

        if(index.Count == 0
            || IocSourceGenerator.FindExpandingDependencies(index, EnumerateRootDependencies(analyzerContext, GetDecoratorTypeData), cancellationToken) is not { } expandingDependencies)
        {
            return;
        }

        // A factory parameter is reported once for all registrations the factory serves: SGIOC036 when it is eagerly
        // expanding for any of them
        var diagnostics = new Dictionary<Location, (DiagnosticDescriptor Descriptor, string Owner, string Dependency)>();
        var dependencyTypes = new List<(TypeData Type, bool IsDeferred)>();
        foreach(var entry in expandingDependencies)
        {
            if(factoryOwners.TryGetValue(entry.Key, out var factoryOwner))
            {
                var (method, factory) = factoryOwner;
                var factoryTypeParams = new HashSet<string>(method.TypeParameters.Select(static typeParam => typeParam.Name), StringComparer.Ordinal);
                var additionalParameters = new HashSet<string>(factory.AdditionalParameters.Select(static parameter => parameter.Name), StringComparer.Ordinal);
                var factoryName = GetFactoryDisplayName(method);

                // IServiceProvider and [ServiceKey] parameters are not resolved
                foreach(var parameter in method.Parameters)
                {
                    if(additionalParameters.Contains(parameter.Name))
                        Report(entry.Value, factoryTypeParams, factoryName, parameter.Locations.FirstOrDefault(), parameter.Type.GetTypeData());
                }

                continue;
            }

            if(!owners.TryGetValue(entry.Key, out var owner) || owner.TypeData is not GenericTypeData { TypeParameters: { } typeParameters })
                continue;

            var typeParams = new HashSet<string>(typeParameters.Select(static typeParam => typeParam.ParameterName), StringComparer.Ordinal);
            var definition = owner.Symbol.IsUnboundGenericType ? owner.Symbol.OriginalDefinition : owner.Symbol;
            var ownerName = definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // Constructor parameters are extracted from the constructor the generator selects, in order
            var constructorParameters = definition.SpecifiedOrPrimaryOrMostParametersConstructor?.Parameters ?? [];
            var parameters = owner.TypeData.ConstructorParameters ?? [];
            for(var i = 0; i < parameters.Length && i < constructorParameters.Length; i++)
            {
                Report(entry.Value, typeParams, ownerName, constructorParameters[i].Locations.FirstOrDefault(), parameters[i].Type);
            }

            foreach(var (member, _) in definition.GetInjectedMembers())
            {
                ReportMember(member, elementLocation: null);
            }

            // InjectMembers are reported at their attribute elements, like SGIOC018
            foreach(var (member, location) in owner.InjectMembers)
            {
                ReportMember(member, location);
            }

            // Members whose injection feature is disabled are not injected (SGIOC022). Members are reported at themselves (each
            // parameter of a method), or at the InjectMembers element that lists them.
            void ReportMember(ISymbol member, Location? elementLocation)
            {
                switch(member)
                {
                    case IPropertySymbol property when IsEnabled(IocFeatures.PropertyInject):
                        Report(entry.Value, typeParams, ownerName, elementLocation ?? property.Locations.FirstOrDefault(), property.Type.GetTypeData());
                        break;

                    case IFieldSymbol field when IsEnabled(IocFeatures.FieldInject):
                        Report(entry.Value, typeParams, ownerName, elementLocation ?? field.Locations.FirstOrDefault(), field.Type.GetTypeData());
                        break;

                    case IMethodSymbol method when IsEnabled(method.ReturnsVoid ? IocFeatures.MethodInject : IocFeatures.AsyncMethodInject):
                        foreach(var parameter in method.Parameters)
                        {
                            Report(entry.Value, typeParams, ownerName, elementLocation ?? parameter.Locations.FirstOrDefault(), parameter.Type.GetTypeData());
                        }
                        break;
                }
            }
        }

        foreach(var diagnostic in diagnostics)
        {
            var (descriptor, owner, dependency) = diagnostic.Value;
            reportDiagnostic(Diagnostic.Create(descriptor, diagnostic.Key, owner, GetDisplayName(dependency)));
        }

        // Records the expanding dependency a dependency type becomes, preferring SGIOC036 for a location
        void Report(List<IocSourceGenerator.DependencySite> sites, HashSet<string> typeParams, string owner, Location? location, TypeData dependencyType)
        {
            if(location is not { IsInSource: true })
                return;

            dependencyTypes.Clear();
            IocSourceGenerator.CollectDependencyTypes(dependencyType, typeParams, isDeferred: false, dependencyTypes);
            foreach(var (type, _) in dependencyTypes)
            {
                foreach(var site in sites)
                {
                    if(!site.IsExpanding || site.Dependency != type.Name)
                        continue;

                    var descriptor = site.IsDeferred ? DeferredExpandingGenericDependency : InfiniteGenericDependency;
                    if(!diagnostics.TryGetValue(location, out var existing)
                        || (existing.Descriptor == DeferredExpandingGenericDependency && descriptor == InfiniteGenericDependency))
                    {
                        diagnostics[location] = (descriptor, owner, site.Dependency);
                    }
                }
            }
        }

        void AddToIndex(OpenGenericRegistrationInfo info, IEnumerable<TypeData> types)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach(var serviceType in types)
            {
                if(serviceType is GenericTypeData { IsOpenGeneric: true } genericServiceType && keys.Add(genericServiceType.NameWithoutGeneric))
                {
                    if(!index.TryGetValue(genericServiceType.NameWithoutGeneric, out var list))
                    {
                        list = [];
                        index[genericServiceType.NameWithoutGeneric] = list;
                    }
                    list.Add(info);
                }
            }
        }

        void AddFactoryOwner(IMethodSymbol? method, FactoryMethodData? factory, TypeData implementation)
        {
            if(method is not null && factory is not null)
                factoryOwners[IocSourceGenerator.GetFactoryOwner(factory, implementation)] = (method, factory);
        }

        // Gets the InjectMembers of a registration attribute, already resolved on the implementation (Node<T>.Next for
        // nameof(Node<int>.Next)) by AnalyzeInjectMembersOnAttribute, without those that [IocInject] already injects
        List<(ISymbol Member, Location Location)> GetInjectMembers(
            SyntaxReference? attribute,
            ImmutableEquatableArray<InjectionMemberData> injectedMembers)
        {
            if(attribute is null || !analyzerContext.InjectMembers.TryGetValue((attribute.SyntaxTree, attribute.Span), out var members))
                return [];

            var injectedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach(var injectedMember in injectedMembers)
                injectedNames.Add(injectedMember.Name);

            var result = new List<(ISymbol Member, Location Location)>(members.Length);
            foreach(var (member, location) in members)
            {
                if(!injectedNames.Contains(member.Name))
                    result.Add((member, location));
            }

            return result;
        }

        TypeData GetDecoratorTypeData(INamedTypeSymbol decorator)
        {
            if(!decoratorTypeData.TryGetValue(decorator, out var typeData))
            {
                typeData = decorator.GetTypeData(extractConstructorParams: true, extractHierarchy: true, extractInjectionMembers: true);
                decoratorTypeData[decorator] = typeData;
            }

            return typeData;
        }

        bool IsEnabled(IocFeatures feature) => (analyzerContext.Features & feature) != 0;
    }

    private static bool HasFactory(AttributeData attribute)
        => attribute.NamedArguments.Any(static argument => argument.Key == "Factory" && !argument.Value.IsNull);

    /// <summary>
    /// Gets the factory a registration creates its instances with, like <c>ProcessSingleRegistration</c>: none with an
    /// <c>Instance</c>, otherwise its own Factory, which takes precedence over the Factory of its best default settings.
    /// </summary>
    private static (IMethodSymbol? Method, FactoryMethodData? Factory) GetEffectiveFactory(AnalyzerContext analyzerContext, AnalyzedRegistration registration)
    {
        if(registration.HasInstance)
            return default;

        var attribute = registration.HasFactory ? registration.Attribute
            : registration.DefaultIndex >= 0 && HasFactory(analyzerContext.DefaultAttributes[registration.DefaultIndex]) ? analyzerContext.DefaultAttributes[registration.DefaultIndex]
            : null;
        return attribute is null ? default : GetFactory(analyzerContext, attribute);
    }

    /// <summary>
    /// Like the generator's GetFactoryMethodData: a generic factory without [IocGenericFactory] takes the attribute's
    /// GenericFactoryTypeMapping, and a Factory given as a string has no known parameters.
    /// </summary>
    private static (IMethodSymbol? Method, FactoryMethodData Factory) GetFactory(AnalyzerContext analyzerContext, AttributeData attribute)
    {
        if(attribute.ApplicationSyntaxReference is not { } syntax
            || !analyzerContext.FactoryMethods.TryGetValue((syntax.SyntaxTree, syntax.Span), out var method))
        {
            return (null, new FactoryMethodData(string.Empty, ServiceProviderPosition: -1, KeyPosition: -1, ReturnTypeName: null, AdditionalParameters: []));
        }

        var factory = method.CreateFactoryMethodData();
        if(factory.GenericTypeMapping is null && method.TypeParameters.Length > 0 && attribute.ExtractGenericFactoryMappingFromAttributeProperty() is { } mapping)
            factory = factory with { GenericTypeMapping = mapping };

        return (method, factory);
    }

    /// <summary>
    /// SGIOC039: Reports the service types of registrations whose factory is a generic factory with a mapping that the mapping
    /// does not match, which the generator does not register (see <c>IocSourceGenerator.IsUnmatchedByGenericFactory</c>).
    /// </summary>
    private static void AnalyzeGenericFactoryServiceTypes(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(GetEffectiveFactory(analyzerContext, registration) is not ({ } method, { TypeParameterCount: > 0, GenericTypeMapping: { } mapping } factory))
                continue;

            var definition = GetDefinition(registration.Implementation);
            var reported = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach(var serviceType in EnumerateRegisteredTypes(registration))
            {
                if(!reported.Add(serviceType))
                    continue;

                // The implementation type is constructed unless it cannot be; an open implementation registers only its
                // service types with type parameters, matched per closed type, so a type parameter may still match
                if(SymbolEqualityComparer.Default.Equals(serviceType, definition) ? definition.IsConstructible
                    : registration.IsOpenGeneric && !serviceType.ContainsGenericParameters)
                {
                    continue;
                }

                if(!IocSourceGenerator.IsUnmatchedByGenericFactory(factory, serviceType.GetTypeData()))
                    continue;

                reportDiagnostic(Diagnostic.Create(
                    ServiceTypeUnmatchedByGenericFactory,
                    registration.Location,
                    serviceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    GetDisplayName(mapping.ServiceTypeTemplate.Name),
                    GetFactoryDisplayName(method)));
            }
        }
    }

    /// <summary>Gets a type written with its own type parameters (Node&lt;T&gt;) instead of unbound (Node&lt;&gt;).</summary>
    private static INamedTypeSymbol GetDefinition(INamedTypeSymbol type) => type.IsUnboundGenericType ? type.OriginalDefinition : type;

    /// <summary>
    /// Enumerates the types a registration registers, including its implementation type (first), like the generator: an
    /// unbound service type, such as typeof(IRequestHandler&lt;&gt;), is registered as the forms of it that the implementation
    /// implements, closed (IRequestHandler&lt;Task&lt;Entity&gt;&gt;) or written with the type parameters of an open implementation
    /// (IRequestHandler&lt;Task&lt;T&gt;&gt;).
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> EnumerateRegisteredTypes(AnalyzedRegistration registration)
    {
        var implementation = GetDefinition(registration.Implementation);
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { implementation };
        yield return implementation;

        foreach(var serviceKey in registration.Registrations)
        {
            var serviceType = serviceKey.ServiceType;
            if(!serviceType.IsUnboundGenericType)
            {
                if(seen.Add(serviceType))
                    yield return serviceType;
                continue;
            }

            var definition = serviceType.OriginalDefinition;
            if(SymbolEqualityComparer.Default.Equals(definition, implementation.OriginalDefinition))
                continue;
            foreach(var interfaceType in implementation.AllInterfaces)
            {
                if(SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, definition) && seen.Add(interfaceType))
                    yield return interfaceType;
            }

            for(var baseType = implementation.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                if(SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, definition) && seen.Add(baseType))
                    yield return baseType;
            }
        }
    }

    /// <summary>Gets the name of a factory method with its type parameters, such as <c>NodeFactory.Create&lt;TF&gt;</c>.</summary>
    private static string GetFactoryDisplayName(IMethodSymbol method)
    {
        var name = $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{method.Name}";
        return method.TypeParameters.Length == 0 ? name : $"{name}<{string.Join(", ", method.TypeParameters.Select(static typeParam => typeParam.Name))}>";
    }

    /// <summary>Gets a fully qualified type name without its <c>global::</c> namespaces, such as <c>INode&lt;List&lt;T&gt;&gt;</c>.</summary>
    private static string GetDisplayName(string typeName) => Regex.Replace(typeName, @"global::(?:\w+\.)*", string.Empty);

    /// <summary>
    /// Enumerates the closed generic dependencies of closed registrations, which the generator closes open generic
    /// registrations for, like its <c>CollectClosedGenericDependenciesFromRegistration</c>: by how each registration creates
    /// its instances (the constructor and injected members of a constructed implementation type, the factory parameters,
    /// closed for each registered type a generic factory's mapping matches, none for an instance), and the dependencies of
    /// the closed decorators applied to its service types.
    /// </summary>
    private static List<ClosedGenericDependency> EnumerateRootDependencies(
        AnalyzerContext analyzerContext,
        Func<INamedTypeSymbol, TypeData> getDecoratorTypeData)
    {
        var dependencyTypes = new List<(TypeData Type, bool IsDeferred)>();
        foreach(var registration in analyzerContext.Registrations)
        {
            // An instance is neither constructed nor decorated
            if(registration.IsOpenGeneric || registration.HasInstance)
                continue;

            var implementation = registration.Implementation;
            var implementationType = implementation.GetTypeData(extractConstructorParams: true);
            var factory = GetEffectiveFactory(analyzerContext, registration).Factory;
            var registeredTypes = EnumerateRegisteredTypes(registration);

            // The implementation type is constructed without a factory, or with a generic factory its mapping does not match
            var isConstructed = factory is null
                || (factory.TypeParameterCount > 0 && implementation.IsConstructible
                    && IocSourceGenerator.CloseGenericFactory(factory, implementationType) is null);
            if(isConstructed)
            {
                foreach(var parameter in implementationType.ConstructorParameters ?? [])
                    Collect(parameter.Type);

                foreach(var member in GetInjectionMembers(registration))
                    CollectMember(member);
            }

            ImmutableEquatableArray<TypeData> decorators = [.. registration.Decorators.Select(getDecoratorTypeData)];
            foreach(var serviceType in registeredTypes)
            {
                var serviceTypeData = serviceType.GetTypeData();
                var isImplementation = SymbolEqualityComparer.Default.Equals(serviceType, implementation);

                // A generic factory is closed for each registered type; a type it cannot be called for is not registered
                var serviceFactory = factory is { TypeParameterCount: > 0 } ? IocSourceGenerator.CloseGenericFactory(factory, serviceTypeData) : factory;
                if(factory is { TypeParameterCount: > 0 } && serviceFactory is null && !isImplementation)
                    continue;

                foreach(var parameter in serviceFactory?.AdditionalParameters ?? [])
                    Collect(parameter.Type);

                if(isImplementation || decorators.Length == 0)
                    continue;

                foreach(var decorator in IocSourceGenerator.CloseDecoratorsForServiceType(decorators, serviceTypeData))
                {
                    foreach(var parameter in IocSourceGenerator.EnumerateDecoratorDependencyParameters(decorator, serviceTypeData))
                        Collect(parameter.Type);

                    foreach(var member in decorator.InjectionMembers ?? [])
                        CollectMember(member);
                }
            }
        }

        var dependencies = new List<ClosedGenericDependency>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach(var (type, _) in dependencyTypes)
        {
            if(type is GenericTypeData { IsOpenGeneric: false } genericType && addedKeys.Add(type.Name))
                dependencies.Add(new ClosedGenericDependency(type.Name, type, genericType.NameWithoutGeneric));
        }

        return dependencies;

        // Like the generator's ExtractAndMergeInjectionMembers: [IocInject] members, then the InjectMembers of the attribute
        IEnumerable<InjectionMemberData> GetInjectionMembers(AnalyzedRegistration registration)
        {
            var injectedMembers = IocSourceGenerator.ExtractInjectionMembers(registration.Implementation);
            if(registration.Attribute.ApplicationSyntaxReference is not { } syntax
                || !analyzerContext.InjectMembers.TryGetValue((syntax.SyntaxTree, syntax.Span), out var members)
                || members.Length == 0)
            {
                return injectedMembers;
            }

            return IocSourceGenerator.MergeInjectionMembers(
                injectedMembers,
                [.. members.Select(static m => IocSourceGenerator.CreateInjectionMemberFromSymbol(m.Member, key: null, semanticModel: null)).OfType<InjectionMemberData>()]);
        }

        void Collect(TypeData type)
            => IocSourceGenerator.CollectDependencyTypes(type, [], isDeferred: false, dependencyTypes);

        void CollectMember(InjectionMemberData member)
        {
            switch(member)
            {
                case PropertyOrFieldInjectionData propertyOrField:
                    Collect(propertyOrField.Type);
                    break;
                case MethodInjectionData method:
                    foreach(var parameter in method.Parameters)
                        Collect(parameter.Type);
                    break;
            }
        }
    }
}
