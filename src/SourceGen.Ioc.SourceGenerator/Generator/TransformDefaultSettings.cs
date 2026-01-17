namespace SourceGen.Ioc;

partial class RegisterSourceGenerator
{
    private static IEnumerable<DefaultSettingsModel> TransformDefaultSettings(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            // Use shared method from Constants to extract default settings
            var data = attr.ExtractDefaultSettings(ctx.SemanticModel);

            if(data is not null)
                yield return data;
        }
    }

    /// <summary>
    /// Transforms generic IoCRegisterDefaultsAttribute (IoCRegisterDefaultsAttribute&lt;T&gt;) to extract default settings.
    /// The target service type is specified via type parameter instead of constructor argument.
    /// </summary>
    private static IEnumerable<DefaultSettingsModel> TransformDefaultSettingsGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            // Use shared method from Constants to extract default settings from generic attribute
            var data = attr.ExtractDefaultSettingsFromGenericAttribute(ctx.SemanticModel);

            if(data is not null)
                yield return data;
        }
    }

    /// <summary>
    /// Transforms ImplementationTypes from IoCRegisterDefaultsAttribute to RegistrationData.
    /// This allows direct registration of implementation types via the defaults attribute.
    /// </summary>
    private static IEnumerable<RegistrationData> TransformDefaultSettingsImplementationTypes(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            var implementationTypeSymbols = attr.GetImplementationTypeSymbols();
            if(implementationTypeSymbols.Length == 0)
                continue;

            // Extract default settings for the base configuration
            var defaultSettings = attr.ExtractDefaultSettings(ctx.SemanticModel);
            if(defaultSettings is null)
                continue;

            foreach(var typeSymbol in implementationTypeSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var registration = CreateRegistrationDataFromDefaultSettings(typeSymbol, defaultSettings, ctx.SemanticModel);
                yield return registration;
            }
        }
    }

    /// <summary>
    /// Transforms ImplementationTypes from generic IoCRegisterDefaultsAttribute&lt;T&gt; to RegistrationData.
    /// </summary>
    private static IEnumerable<RegistrationData> TransformDefaultSettingsImplementationTypesGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            var implementationTypeSymbols = attr.GetImplementationTypeSymbols();
            if(implementationTypeSymbols.Length == 0)
                continue;

            // Extract default settings for the base configuration
            var defaultSettings = attr.ExtractDefaultSettingsFromGenericAttribute(ctx.SemanticModel);
            if(defaultSettings is null)
                continue;

            foreach(var typeSymbol in implementationTypeSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var registration = CreateRegistrationDataFromDefaultSettings(typeSymbol, defaultSettings, ctx.SemanticModel);
                yield return registration;
            }
        }
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

        // Use settings from DefaultSettingsModel
        // These registrations have "explicit" settings from the DefaultSettingsAttribute
        return new RegistrationData(
            implementationType,
            defaultSettings.Lifetime,
            defaultSettings.RegisterAllInterfaces,
            defaultSettings.RegisterAllBaseClasses,
            defaultSettings.ServiceTypes,
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
