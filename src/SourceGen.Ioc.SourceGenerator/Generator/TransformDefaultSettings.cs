namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Transforms IoCRegisterDefaultsAttribute to extract default settings and implementation type registrations.
    /// Returns a combined result containing both the default settings model and any registration data for implementation types.
    /// </summary>
    private static IEnumerable<DefaultSettingsResult> TransformDefaultSettings(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            var defaultSettings = attr.ExtractDefaultSettings(ctx.SemanticModel);
            if(defaultSettings is null)
                continue;

            var implementationTypeSymbols = attr.GetImplementationTypeSymbols();
            var registrations = CreateRegistrationsFromImplementationTypes(implementationTypeSymbols, defaultSettings, ctx.SemanticModel, ct);
            var openGenericEntries = CreateOpenGenericEntriesFromDefaultSettings(defaultSettings);

            yield return new DefaultSettingsResult(defaultSettings, registrations, openGenericEntries);
        }
    }

    /// <summary>
    /// Transforms generic IoCRegisterDefaultsAttribute (IoCRegisterDefaultsAttribute&lt;T&gt;) to extract default settings and implementation type registrations.
    /// The target service type is specified via type parameter instead of constructor argument.
    /// </summary>
    private static IEnumerable<DefaultSettingsResult> TransformDefaultSettingsGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            ct.ThrowIfCancellationRequested();

            var defaultSettings = attr.ExtractDefaultSettingsFromGenericAttribute(ctx.SemanticModel);
            if(defaultSettings is null)
                continue;

            var implementationTypeSymbols = attr.GetImplementationTypeSymbols();
            var registrations = CreateRegistrationsFromImplementationTypes(implementationTypeSymbols, defaultSettings, ctx.SemanticModel, ct);
            var openGenericEntries = CreateOpenGenericEntriesFromDefaultSettings(defaultSettings);

            yield return new DefaultSettingsResult(defaultSettings, registrations, openGenericEntries);
        }
    }

    /// <summary>
    /// Creates OpenGenericEntry from DefaultSettings when it has a Factory and the TargetServiceType is open generic.
    /// This enables factory-based registration discovery without requiring explicit implementation types.
    /// </summary>
    private static ImmutableEquatableArray<OpenGenericEntry> CreateOpenGenericEntriesFromDefaultSettings(
        DefaultSettingsModel defaultSettings)
    {
        // Only create entries when:
        // 1. Factory is specified
        // 2. TargetServiceType is open generic (not nested)
        if(defaultSettings.Factory is null)
        {
            return [];
        }

        var targetServiceType = defaultSettings.TargetServiceType;
        if(targetServiceType is not GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false } targetServiceGenericType)
        {
            return [];
        }

        // Create a factory-only OpenGenericRegistrationInfo
        // For factory-only registrations, we use the service type as the "implementation type"
        // since the factory will create the actual instance
        var info = new OpenGenericRegistrationInfo(
            ImplementationType: targetServiceType, // Use service type as placeholder
            ServiceTypes: [targetServiceType],
            AllInterfaces: [], // No interfaces for factory-only
            defaultSettings.Lifetime,
            Key: null,
            KeyType: 0,
            KeyValueType: null,
            defaultSettings.Decorators,
            defaultSettings.Tags,
            InjectionMembers: [],
            defaultSettings.Factory,
            Instance: null);

        var entry = new OpenGenericEntry(targetServiceGenericType.NameWithoutGeneric, info);
        return [entry];
    }

    /// <summary>
    /// Creates RegistrationData for each implementation type symbol using the default settings.
    /// </summary>
    private static ImmutableEquatableArray<RegistrationData> CreateRegistrationsFromImplementationTypes(
        ImmutableEquatableArray<INamedTypeSymbol> implementationTypeSymbols,
        DefaultSettingsModel defaultSettings,
        SemanticModel? semanticModel,
        CancellationToken ct)
    {
        if(implementationTypeSymbols.Length == 0)
            return [];

        List<RegistrationData> registrations = new(implementationTypeSymbols.Length);
        foreach(var typeSymbol in implementationTypeSymbols)
        {
            ct.ThrowIfCancellationRequested();
            var registration = CreateRegistrationDataFromDefaultSettings(typeSymbol, defaultSettings, semanticModel);
            registrations.Add(registration);
        }

        return registrations.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Creates a RegistrationData from an implementation type symbol and its default settings.
    /// Uses the same parsing logic as IocRegisterAttribute.
    /// </summary>
    private static RegistrationData CreateRegistrationDataFromDefaultSettings(
        INamedTypeSymbol typeSymbol,
        DefaultSettingsModel defaultSettings,
        SemanticModel? semanticModel)
    {
        var implementationType = typeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true);

        // Extract injection members (properties, fields, methods marked with IocInjectAttribute/InjectAttribute)
        var injectionMembers = ExtractInjectionMembers(typeSymbol, semanticModel);

        // Build set of valid open generic service types (non-nested) for quick lookup
        var validOpenGenericServiceTypes = BuildValidOpenGenericServiceTypes(
            implementationType.AllInterfaces ?? [],
            implementationType.AllBaseClasses ?? []);

        // Determine service types based on whether implementation is open or closed generic
        ImmutableEquatableArray<TypeData> serviceTypes;

        if(implementationType is GenericTypeData { IsOpenGeneric: true })
        {
            // For open generic implementations, use the original open generic service types
            serviceTypes = defaultSettings.ServiceTypes.Length > 0
                ? defaultSettings.ServiceTypes.Append(defaultSettings.TargetServiceType).ToImmutableEquatableArray()
                : [defaultSettings.TargetServiceType];
        }
        else
        {
            // For closed generic implementations (e.g., Handler<Entity>), find matching closed service types
            // from the implementation's interfaces/base classes
            serviceTypes = FindClosedServiceTypesFromImplementation(
                implementationType,
                defaultSettings.TargetServiceType,
                defaultSettings.ServiceTypes);

            // Fallback: when ImplementationTypes explicitly specified but no matching interface found
            // (e.g., Razor components where IComponent isn't visible to the source generator),
            // use TargetServiceType directly as the service type
            if(serviceTypes.Length == 0)
            {
                serviceTypes = defaultSettings.ServiceTypes.Length > 0
                    ? defaultSettings.ServiceTypes.Append(defaultSettings.TargetServiceType).ToImmutableEquatableArray()
                    : [defaultSettings.TargetServiceType];
            }
        }

        // For closed generic implementations (e.g., Handler<Entity>), do NOT inherit Factory from defaults
        // Factory should only be used for IocDiscover-based registrations, not explicit ImplementationTypes
        // Priority: [IocRegister] > ImplementationTypes > Factory
        var factory = implementationType is GenericTypeData { IsOpenGeneric: true } ? defaultSettings.Factory : null;

        // Use settings from DefaultSettingsModel
        // These registrations have "explicit" settings from the DefaultSettingsAttribute
        return new RegistrationData(
            implementationType,
            defaultSettings.Lifetime,
            defaultSettings.RegisterAllInterfaces,
            defaultSettings.RegisterAllBaseClasses,
            serviceTypes,
            Key: null,
            KeyType: 0,
            KeyValueType: null,
            HasExplicitLifetime: true,
            HasExplicitRegisterAllInterfaces: true,
            HasExplicitRegisterAllBaseClasses: true,
            validOpenGenericServiceTypes,
            defaultSettings.Decorators,
            defaultSettings.Tags,
            injectionMembers,
            factory,
            Instance: null);
    }

    /// <summary>
    /// Finds closed service types from an implementation type that match the open generic target service types.
    /// For example, if targetServiceType is IRequestHandler&lt;&gt; and implementation implements IRequestHandler&lt;Task&lt;Entity&gt;&gt;,
    /// this returns [IRequestHandler&lt;Task&lt;Entity&gt;&gt;].
    /// </summary>
    private static ImmutableEquatableArray<TypeData> FindClosedServiceTypesFromImplementation(
        TypeData implementationType,
        TypeData targetServiceType,
        ImmutableEquatableArray<TypeData> additionalServiceTypes)
    {
        var result = new List<TypeData>();
        var targetMatchName = targetServiceType is GenericTypeData targetGenericType
            ? targetGenericType.NameWithoutGeneric
            : targetServiceType.Name;

        // Search in all interfaces for matching closed service types
        if(implementationType.AllInterfaces is not null)
        {
            foreach(var iface in implementationType.AllInterfaces)
            {
                if((iface is GenericTypeData { IsOpenGeneric: false } genericInterface && genericInterface.NameWithoutGeneric == targetMatchName)
                    || iface.Name == targetMatchName)
                {
                    result.Add(iface);
                }
            }
        }

        // Search in all base classes for matching closed service types
        if(implementationType.AllBaseClasses is not null)
        {
            foreach(var baseClass in implementationType.AllBaseClasses)
            {
                if((baseClass is GenericTypeData { IsOpenGeneric: false } genericBaseClass && genericBaseClass.NameWithoutGeneric == targetMatchName)
                    || baseClass.Name == targetMatchName)
                {
                    result.Add(baseClass);
                }
            }
        }

        // Also search for additional service types
        foreach(var additionalType in additionalServiceTypes)
        {
            var additionalMatchName = additionalType is GenericTypeData additionalGenericType
                ? additionalGenericType.NameWithoutGeneric
                : additionalType.Name;

            if(implementationType.AllInterfaces is not null)
            {
                foreach(var iface in implementationType.AllInterfaces)
                {
                    if((iface is GenericTypeData { IsOpenGeneric: false } genericInterface && genericInterface.NameWithoutGeneric == additionalMatchName)
                        || iface.Name == additionalMatchName)
                    {
                        result.Add(iface);
                    }
                }
            }

            if(implementationType.AllBaseClasses is not null)
            {
                foreach(var baseClass in implementationType.AllBaseClasses)
                {
                    if((baseClass is GenericTypeData { IsOpenGeneric: false } genericBaseClass && genericBaseClass.NameWithoutGeneric == additionalMatchName)
                        || baseClass.Name == additionalMatchName)
                    {
                        result.Add(baseClass);
                    }
                }
            }
        }

        return result.Count > 0
            ? result.ToImmutableEquatableArray()
            : [];
    }
}
