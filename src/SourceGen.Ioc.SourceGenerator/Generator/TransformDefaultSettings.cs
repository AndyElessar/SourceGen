namespace SourceGen.Ioc;

partial class RegisterSourceGenerator
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

            yield return new DefaultSettingsResult(defaultSettings, registrations);
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

            yield return new DefaultSettingsResult(defaultSettings, registrations);
        }
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
        var injectionMembers = ExtractInjectionMembers(typeSymbol);

        // Build set of valid open generic service types (non-nested) for quick lookup
        var validOpenGenericServiceTypes = BuildValidOpenGenericServiceTypes(
            implementationType.AllInterfaces ?? [],
            implementationType.AllBaseClasses ?? []);

        // Combine TargetServiceType with additional ServiceTypes
        // Order: ServiceTypes first, then TargetServiceType (to match ProcessSingleRegistration behavior)
        var serviceTypes = defaultSettings.ServiceTypes.Length > 0
            ? defaultSettings.ServiceTypes.Append(defaultSettings.TargetServiceType).ToImmutableEquatableArray()
            : [defaultSettings.TargetServiceType];

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
            HasExplicitLifetime: true,
            HasExplicitRegisterAllInterfaces: true,
            HasExplicitRegisterAllBaseClasses: true,
            validOpenGenericServiceTypes,
            defaultSettings.Decorators,
            defaultSettings.Tags,
            defaultSettings.TagOnly,
            injectionMembers,
            defaultSettings.Factory,
            Instance: null);
    }
}
