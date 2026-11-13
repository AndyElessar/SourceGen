using System.Text.RegularExpressions;

namespace SourceGen.Ioc;

partial class RegisterAnalyzer
{
    /// <summary>
    /// Reports SGIOC036 or SGIOC037 for each dependency of an open generic registration or of its decorators that the
    /// generator does not close because it nests type arguments deeper on every round of a cycle. The open generic
    /// registrations are analyzed with the generator's own analysis (<c>FindExpandingDependencies</c>), so the same
    /// dependencies are reported.
    /// </summary>
    private static void AnalyzeExpandingDependencies(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        if(analyzerContext.OpenGenericRegistrations.IsEmpty)
            return;

        var index = new Dictionary<string, List<OpenGenericRegistrationInfo>>(StringComparer.Ordinal);
        var owners = new Dictionary<string, (INamedTypeSymbol Symbol, TypeData TypeData)>(StringComparer.Ordinal);
        var decoratorTypeData = new Dictionary<INamedTypeSymbol, TypeData>(SymbolEqualityComparer.Default);
        foreach(var (implementation, registrations, decorators) in analyzerContext.OpenGenericRegistrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Built like the generator's open generic entries (ProcessSingleRegistration.CreateOpenGenericEntries)
            if(implementation.GetTypeData(extractConstructorParams: true, extractHierarchy: true) is not GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false } implementationType)
                continue;

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
                IocSourceGenerator.ExtractInjectionMembers(implementation),
                Factory: null,
                Instance: null);

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach(var serviceType in serviceTypes.Concat(implementationType.AllInterfaces ?? []).Append(implementationType))
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

            owners[implementationType.Name] = (implementation, implementationType);
            for(var i = 0; i < decorators.Length; i++)
            {
                if(decoratorTypes[i] is GenericTypeData { IsOpenGeneric: true } decoratorType)
                    owners[decoratorType.Name] = (decorators[i], decoratorType);
            }
        }

        if(IocSourceGenerator.FindExpandingDependencies(index, EnumerateRootDependencies(analyzerContext), cancellationToken) is not { } expandingDependencies)
            return;

        var reported = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var dependencyTypes = new List<(TypeData Type, bool IsDeferred)>();
        foreach(var entry in expandingDependencies)
        {
            if(!owners.TryGetValue(entry.Key, out var owner) || owner.TypeData is not GenericTypeData { TypeParameters: { } typeParameters })
                continue;

            var typeParams = new HashSet<string>(typeParameters.Select(static typeParam => typeParam.ParameterName), StringComparer.Ordinal);
            var definition = owner.Symbol.IsUnboundGenericType ? owner.Symbol.OriginalDefinition : owner.Symbol;

            // Constructor parameters are extracted from the constructor the generator selects, in order
            var constructorParameters = definition.SpecifiedOrPrimaryOrMostParametersConstructor?.Parameters ?? [];
            var parameters = owner.TypeData.ConstructorParameters ?? [];
            for(var i = 0; i < parameters.Length && i < constructorParameters.Length; i++)
            {
                Report(constructorParameters[i], parameters[i].Type);
            }

            // Members whose injection feature is disabled are not injected (SGIOC022)
            foreach(var (member, _) in definition.GetInjectedMembers())
            {
                switch(member)
                {
                    case IPropertySymbol property when IsEnabled(IocFeatures.PropertyInject):
                        Report(property, property.Type.GetTypeData());
                        break;

                    case IFieldSymbol field when IsEnabled(IocFeatures.FieldInject):
                        Report(field, field.Type.GetTypeData());
                        break;

                    case IMethodSymbol method when IsEnabled(method.ReturnsVoid ? IocFeatures.MethodInject : IocFeatures.AsyncMethodInject):
                        foreach(var parameter in method.Parameters)
                        {
                            Report(parameter, parameter.Type.GetTypeData());
                        }
                        break;
                }
            }

            void Report(ISymbol dependencySymbol, TypeData dependencyType)
            {
                dependencyTypes.Clear();
                IocSourceGenerator.CollectDependencyTypes(dependencyType, typeParams, isDeferred: false, dependencyTypes);
                foreach(var (type, _) in dependencyTypes)
                {
                    foreach(var site in entry.Value)
                    {
                        if(!site.IsExpanding
                            || site.Dependency != type.Name
                            || dependencySymbol.Locations.FirstOrDefault() is not { IsInSource: true } location
                            || !reported.Add(dependencySymbol))
                        {
                            continue;
                        }

                        reportDiagnostic(Diagnostic.Create(
                            site.IsDeferred ? DeferredExpandingGenericDependency : InfiniteGenericDependency,
                            location,
                            definition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            GetDisplayName(site.Dependency)));
                        return;
                    }
                }
            }
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

    /// <summary>Gets a fully qualified type name without its <c>global::</c> namespaces, such as <c>INode&lt;List&lt;T&gt;&gt;</c>.</summary>
    private static string GetDisplayName(string typeName) => Regex.Replace(typeName, @"global::(?:\w+\.)*", string.Empty);

    /// <summary>
    /// Enumerates the closed generic dependencies of the constructors of closed registrations, which the generator closes
    /// open generic registrations for.
    /// </summary>
    private static IEnumerable<ClosedGenericDependency> EnumerateRootDependencies(AnalyzerContext analyzerContext)
    {
        var dependencyTypes = new List<(TypeData Type, bool IsDeferred)>();
        foreach(var service in analyzerContext.RegisteredServices.Values)
        {
            if(service.Type.IsUnboundGenericType || service.Type is { IsGenericType: true, IsDefinition: true })
                continue;

            foreach(var parameter in service.Constructor?.Parameters ?? [])
            {
                dependencyTypes.Clear();
                IocSourceGenerator.CollectDependencyTypes(parameter.Type.GetTypeData(), [], isDeferred: false, dependencyTypes);
                foreach(var (type, _) in dependencyTypes)
                {
                    if(type is GenericTypeData genericType)
                        yield return new ClosedGenericDependency(type.Name, type, genericType.NameWithoutGeneric);
                }
            }
        }
    }
}
