using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for service collection, registration, and context types.
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// An assembly-level IoCRegisterFor attribute, bound and parsed once at compilation start.
    /// </summary>
    private readonly record struct AssemblyRegistration(
        AttributeData Attribute,
        INamedTypeSymbol TargetType,
        string? Key,
        List<INamedTypeSymbol> ExplicitServiceTypes);

    /// <summary>
    /// Registers the assembly-level IoCRegisterFor attributes and indexes them by their syntax,
    /// so the attribute syntax action reports their diagnostics without binding them again.
    /// </summary>
    private static Dictionary<(SyntaxTree Tree, TextSpan Span), AssemblyRegistration> CollectAssemblyLevelRegistrations(
        Compilation compilation,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var registrations = new Dictionary<(SyntaxTree Tree, TextSpan Span), AssemblyRegistration>();

        foreach(var (attribute, targetType) in AnalyzerHelpers.EnumerateAssemblyLevelRegisterForAttributes(
            compilation, analyzerContext.AttributeSymbols, cancellationToken))
        {
            var (key, _, keyTypeSymbol) = attribute.GetKeyInfo();
            var explicitServiceTypes = AnalyzerHelpers.GetExplicitServiceTypes(attribute);

            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference is not null)
            {
                registrations[(syntaxReference.SyntaxTree, syntaxReference.Span)] = new(attribute, targetType, key, explicitServiceTypes);
            }

            var location = syntaxReference?.GetSyntax(cancellationToken).GetLocation();
            RegisterService(analyzerContext, attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location);
        }

        return registrations;
    }

    /// <summary>
    /// SGIOC001, SGIOC009, SGIOC011 and SGIOC032 for an assembly-level IoCRegisterFor attribute.
    /// </summary>
    private static void AnalyzeAssemblyLevelRegistration(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AssemblyRegistration registration,
        Location location)
        => AnalyzeRegistrationAttribute(
            reportDiagnostic,
            analyzerContext,
            registration.Attribute,
            registration.TargetType,
            registration.Key,
            registration.ExplicitServiceTypes,
            location);

    /// <summary>
    /// First pass: collect services and do immediate validation (SGIOC001, SGIOC009, SGIOC011, SGIOC032).
    /// Dependency analysis (SGIOC002, SGIOC003-005) is deferred to CompilationEnd.
    /// </summary>
    private static void CollectAndValidateNamedType(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if(!TryGetIoCAttribute(attribute, analyzerContext, out var isIoCRegisterFor))
                continue;

            INamedTypeSymbol targetType;

            if(isIoCRegisterFor)
            {
                // Use extension method to get target type (supports both generic and non-generic variants)
                var target = attribute.GetTargetTypeFromRegisterForAttribute();
                if(target is null)
                    continue;

                targetType = target;
            }
            else
            {
                targetType = typeSymbol;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();

            // Parsed once for the immediate checks and the registration
            var (key, _, keyTypeSymbol) = attribute.GetKeyInfo();
            var explicitServiceTypes = AnalyzerHelpers.GetExplicitServiceTypes(attribute);

            AnalyzeRegistrationAttribute(context.ReportDiagnostic, analyzerContext, attribute, targetType, key, explicitServiceTypes, location);

            // Register service with index for faster lookup
            // Dependency analysis will be done in CompilationEnd after all services are collected
            RegisterService(analyzerContext, attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location);
        }
    }

    /// <summary>
    /// The checks that run for every registration attribute, type-level or assembly-level.
    /// </summary>
    private static void AnalyzeRegistrationAttribute(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        string? key,
        List<INamedTypeSymbol> explicitServiceTypes,
        Location? location)
    {
        // SGIOC001: Check if target type is private or abstract
        AnalyzeInvalidAttributeUsage(reportDiagnostic, targetType, location);

        // SGIOC009: Check Instance requires Singleton lifetime
        AnalyzeInstanceLifetime(reportDiagnostic, attribute, location);

        // SGIOC011: Check for duplicated registrations (same implementation type and key)
        AnalyzeDuplicatedRegistration(reportDiagnostic, analyzerContext, attribute, targetType, key, location);

        // SGIOC032: Check explicit service types that would be skipped
        AnalyzeIncompatibleServiceTypes(reportDiagnostic, explicitServiceTypes, targetType, location);

        // SGIOC038: Check the interfaces and base classes an open implementation is registered as
        AnalyzeImplicitOpenTypesNestedInGenericType(reportDiagnostic, analyzerContext, attribute, targetType, explicitServiceTypes, location);
    }

    /// <summary>
    /// SGIOC038: Reports the open types nested in a generic type that an open implementation would be registered as through
    /// <c>RegisterAllInterfaces</c> / <c>RegisterAllBaseClasses</c> (its own or those of its default settings), such as
    /// <c>Outer&lt;T&gt;.INode&lt;T&gt;</c> of <c>Node&lt;T&gt;</c>. The generator does not register them.
    /// </summary>
    private static void AnalyzeImplicitOpenTypesNestedInGenericType(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        List<INamedTypeSymbol> explicitServiceTypes,
        Location? location)
    {
        // Closed implementations implement closed types; a target that is itself nested is reported as a whole
        if(targetType.IsOpenNestedInGenericType || !(targetType.IsUnboundGenericType || targetType is { IsGenericType: true, IsDefinition: true }))
            return;

        var serviceTypes = new HashSet<INamedTypeSymbol>(
            AnalyzerHelpers.EnumerateRegisteredServiceTypes(targetType, attribute, explicitServiceTypes),
            SymbolEqualityComparer.Default);
        AddDefaultSettingsServiceTypes(analyzerContext, attribute, targetType, serviceTypes);

        foreach(var serviceType in serviceTypes)
        {
            if(!serviceType.IsOpenNestedInGenericType)
                continue;

            var definition = serviceType.OriginalDefinition;
            reportDiagnostic(Diagnostic.Create(
                OpenTypeNestedInGenericType,
                location,
                definition.ToDisplayString(NestedTypeDisplayFormat),
                GetGenericContainingType(definition).ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    /// <summary>
    /// Records a registration of a valid target type for the CompilationEnd analyses.
    /// </summary>
    private static void RegisterService(
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        string? key,
        ITypeSymbol? keyTypeSymbol,
        List<INamedTypeSymbol> explicitServiceTypes,
        Location? location)
    {
        // Skip registration if type is invalid
        if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
            return;
        if(targetType.DeclaredAccessibility is Accessibility.Private)
            return;
        if(targetType.IsOpenNestedInGenericType)
            return;

        // Get lifetime of current service (considering default settings)
        var (hasExplicitLifetime, explicitLifetime) = attribute.TryGetLifetime();
        var lifetime = GetEffectiveLifetime(analyzerContext, targetType, hasExplicitLifetime, explicitLifetime);

        // Check for Factory and Instance (used by ServiceInfo)
        var (hasFactory, hasInstance) = attribute.HasFactoryOrInstance();

        var serviceTypes = new HashSet<INamedTypeSymbol>(
            AnalyzerHelpers.EnumerateRegisteredServiceTypes(targetType, attribute, explicitServiceTypes),
            SymbolEqualityComparer.Default);
        var bestDefaultIndex = AddDefaultSettingsServiceTypes(analyzerContext, attribute, targetType, serviceTypes);

        // Like the generator, open types nested in a generic type are not registered (SGIOC038)
        serviceTypes.RemoveWhere(static serviceType => serviceType.IsOpenNestedInGenericType);

        ImmutableArray<ServiceSymbolKey> registrations = [.. serviceTypes.Select(serviceType => new ServiceSymbolKey(serviceType, key))];

        // Like the generator, the registration's own decorators replace the decorators of its best default settings
        var decorators = AnalyzerHelpers.GetDecoratorSymbols(attribute);
        if(decorators.IsEmpty && bestDefaultIndex >= 0)
            decorators = analyzerContext.DefaultDecorators[bestDefaultIndex];

        var asyncDecoratedRegistrations = hasInstance
            ? []
            : AnalyzeDecorators(analyzerContext, decorators, targetType, registrations);

        // SGIOC036/SGIOC037: open generic registrations are closed on demand for the dependencies the generator discovers
        if(targetType.IsUnboundGenericType || targetType is { IsGenericType: true, IsDefinition: true })
            analyzerContext.OpenGenericRegistrations.Add((targetType, registrations, decorators, attribute.ApplicationSyntaxReference));

        RegisterServiceWithIndex(analyzerContext, targetType, lifetime, location, key, keyTypeSymbol, hasFactory, hasInstance, registrations, asyncDecoratedRegistrations);
    }

    /// <summary>
    /// Adds the service types that default settings give a registration, like the generator's <c>ProcessSingleRegistration</c>:
    /// each base class (first) and interface of the implementation that has default settings, then the <c>ServiceTypes</c> of
    /// the first match (the best default), and its <c>RegisterAllInterfaces</c> / <c>RegisterAllBaseClasses</c> when the
    /// registration does not set them. Returns the index of the best default, or -1 when none matches.
    /// </summary>
    private static int AddDefaultSettingsServiceTypes(
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        HashSet<INamedTypeSymbol> serviceTypes)
    {
        if(analyzerContext.DefaultSettings.IsEmpty)
            return -1;

        var bestIndex = -1;
        foreach(var candidate in EnumerateBaseClasses(targetType).Concat(targetType.AllInterfaces))
        {
            if(!TryGetDefaultSettingsIndex(analyzerContext, candidate, out var index))
                continue;

            serviceTypes.Add(candidate);
            if(bestIndex < 0)
                bestIndex = index;
        }

        if(bestIndex < 0)
            return -1;

        var best = analyzerContext.DefaultSettings[bestIndex];

        // The default's ServiceTypes are matched by name to the implementation's hierarchy; an open generic one adds each
        // closed form the implementation implements or inherits
        foreach(var serviceType in best.ServiceTypes)
        {
            foreach(var candidate in EnumerateBaseClasses(targetType).Concat(targetType.AllInterfaces))
            {
                if(MatchesServiceType(analyzerContext, candidate, serviceType))
                    serviceTypes.Add(candidate);
            }
        }

        var (hasRegisterAllInterfaces, _) = attribute.TryGetRegisterAllInterfaces();
        if(!hasRegisterAllInterfaces && best.RegisterAllInterfaces)
            serviceTypes.UnionWith(targetType.AllInterfaces);

        var (hasRegisterAllBaseClasses, _) = attribute.TryGetRegisterAllBaseClasses();
        if(!hasRegisterAllBaseClasses && best.RegisterAllBaseClasses)
            serviceTypes.UnionWith(EnumerateBaseClasses(targetType));

        return bestIndex;

        static bool MatchesServiceType(AnalyzerContext analyzerContext, INamedTypeSymbol candidate, TypeData serviceType)
        {
            var name = analyzerContext.TypeNames.GetOrAdd(candidate, static type => type.BuildTypeName());
            if(name == serviceType.Name)
                return true;

            return serviceType is GenericTypeData { IsOpenGeneric: true } openServiceType
                && candidate is { IsGenericType: true, TypeKind: not TypeKind.Error }
                && candidate.Arity == openServiceType.GenericArity
                && candidate.GetNameWithoutGeneric(name) == openServiceType.NameWithoutGeneric;
        }
    }

    /// <summary>
    /// Closes a generic decorator definition for a closed service type, like the generator's <c>TryCloseDecorator</c>: the
    /// type arguments are found by matching the service type as the decorator implements or inherits it against the service type.
    /// </summary>
    /// <returns>The closed decorator, or <see langword="null"/> when the decorator is not generic or not every type argument is found.</returns>
    private static INamedTypeSymbol? TryConstructDecorator(INamedTypeSymbol definition, INamedTypeSymbol serviceType)
    {
        if(!definition.IsGenericType || serviceType.IsUnboundGenericType)
            return null;

        var serviceDefinition = serviceType.OriginalDefinition;
        foreach(var form in definition.AllInterfaces.Concat(EnumerateBaseClasses(definition)))
        {
            if(!SymbolEqualityComparer.Default.Equals(form.OriginalDefinition, serviceDefinition))
                continue;

            var typeArguments = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
            if(!MatchTypeArguments(form, serviceType, typeArguments))
                continue;

            var arguments = new ITypeSymbol[definition.TypeParameters.Length];
            for(var i = 0; i < arguments.Length; i++)
            {
                if(!typeArguments.TryGetValue(definition.TypeParameters[i], out var argument))
                    return null;

                arguments[i] = argument;
            }

            return definition.Construct(arguments);
        }

        return null;

        static bool MatchTypeArguments(ITypeSymbol pattern, ITypeSymbol actual, Dictionary<ITypeParameterSymbol, ITypeSymbol> typeArguments)
        {
            switch(pattern)
            {
                case ITypeParameterSymbol typeParameter:
                    if(typeArguments.TryGetValue(typeParameter, out var existing))
                        return SymbolEqualityComparer.Default.Equals(existing, actual);

                    typeArguments[typeParameter] = actual;
                    return true;

                case INamedTypeSymbol { IsGenericType: true } genericPattern
                    when actual is INamedTypeSymbol { IsGenericType: true } genericActual
                        && SymbolEqualityComparer.Default.Equals(genericPattern.OriginalDefinition, genericActual.OriginalDefinition):
                    for(var i = 0; i < genericPattern.TypeArguments.Length; i++)
                    {
                        if(!MatchTypeArguments(genericPattern.TypeArguments[i], genericActual.TypeArguments[i], typeArguments))
                            return false;
                    }

                    return true;

                case IArrayTypeSymbol arrayPattern when actual is IArrayTypeSymbol arrayActual:
                    return arrayPattern.Rank == arrayActual.Rank
                        && MatchTypeArguments(arrayPattern.ElementType, arrayActual.ElementType, typeArguments);

                default:
                    return SymbolEqualityComparer.Default.Equals(pattern, actual);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateBaseClasses(INamedTypeSymbol type)
    {
        for(var baseType = type.BaseType; baseType is not null && baseType.SpecialType is not SpecialType.System_Object; baseType = baseType.BaseType)
            yield return baseType;
    }

    /// <summary>
    /// Matches the decorators to the registration's service types with the generator's rules, recording each match in
    /// <see cref="AnalyzerContext.DecoratorUsages"/> (SGIOC034/SGIOC035) and each applied decorator in
    /// <see cref="AnalyzerContext.AppliedDecorators"/> (SGIOC030). Returns the (service type, key) pairs that an applied
    /// decorator with async inject methods makes async-init (SGIOC030).
    /// </summary>
    private static ImmutableArray<ServiceSymbolKey> AnalyzeDecorators(
        AnalyzerContext analyzerContext,
        ImmutableArray<INamedTypeSymbol> decorators,
        INamedTypeSymbol targetType,
        ImmutableArray<ServiceSymbolKey> registrations)
    {
        if(decorators.IsEmpty)
            return [];

        ImmutableArray<ServiceSymbolKey>.Builder? asyncDecorated = null;
        foreach(var registration in registrations)
        {
            // A self-registration is never decorated
            var serviceType = registration.ServiceType;
            if(SymbolEqualityComparer.Default.Equals(serviceType, targetType))
                continue;

            var serviceTypeData = serviceType.GetTypeData();
            var serviceTypeForDiagnostics = serviceType.IsGenericType ? serviceType.OriginalDefinition : serviceType;
            var isAsyncDecorated = false;
            foreach(var decorator in decorators)
            {
                var definition = decorator.IsUnboundGenericType ? decorator.OriginalDefinition : decorator;
                if(!AnalyzerHelpers.TryGetDecoratedParameterIndexes(analyzerContext.GetDecoratorTypeData(decorator), serviceTypeData, out var parameterIndexes))
                    continue;

                analyzerContext.DecoratorUsages
                    .GetOrAdd(definition, static _ => new(SymbolEqualityComparer.Default))
                    .TryAdd(serviceTypeForDiagnostics, parameterIndexes);

                // The generator skips a decorator without a parameter for the decorated instance
                if(parameterIndexes.IsEmpty)
                    continue;

                // An open generic decorator is analyzed as closed for the service type, so its dependencies have closed types
                var appliedDecorator = TryConstructDecorator(definition, serviceType) ?? definition;
                analyzerContext.AppliedDecorators
                    .GetOrAdd(appliedDecorator, static _ => new())
                    .TryAdd(parameterIndexes[0], 0);

                isAsyncDecorated |= AnalyzerHelpers.IsAsyncInitImplementation(decorator, analyzerContext.Features);
            }

            if(isAsyncDecorated)
                (asyncDecorated ??= ImmutableArray.CreateBuilder<ServiceSymbolKey>()).Add(registration);
        }

        return asyncDecorated?.ToImmutable() ?? [];
    }

    /// <summary>
    /// Resolves key type symbols for KeyType.Csharp registrations using AttributeSyntax + SemanticModel.
    /// This runs in RegisterSyntaxNodeAction to avoid RS1030 (no Compilation.GetSemanticModel calls).
    /// </summary>
    /// <param name="attributeData">The attribute data when it is already known, as for assembly-level registrations.</param>
    private static void ResolveCsharpKeyTypes(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol attributeClass,
        AttributeData? attributeData,
        AnalyzerContext analyzerContext)
    {
        if(!AnalyzerHelpers.IsIoCRegistrationAttribute(attributeClass, analyzerContext.AttributeSymbols))
            return;

        // KeyType can only be set as a named argument and defaults to Value, so skip the AttributeData lookup
        // (a scan of all assembly attributes for assembly-level registrations) when it is absent
        if(!HasNamedArgument(attributeSyntax, "KeyType"))
            return;

        attributeData ??= GetAttributeDataFromSyntax(context, attributeSyntax, attributeClass);
        if(attributeData is null)
            return;

        if(attributeData.GetNamedArgument<int>("KeyType", 0) != 1)
            return;

        var (_, _, resolvedKeyType) = attributeData.GetKeyInfo(context.SemanticModel);
        if(resolvedKeyType is null)
            return;

        var targetType = AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols)
            ? attributeData.GetTargetTypeFromRegisterForAttribute()
            : GetTypeLevelTargetType(context, attributeSyntax);

        if(targetType is null)
            return;

        analyzerContext.ResolvedCsharpKeyTypes[(targetType, attributeSyntax.GetLocation())] = resolvedKeyType;
    }

    private static bool HasNamedArgument(AttributeSyntax attributeSyntax, string name)
    {
        if(attributeSyntax.ArgumentList is not { } argumentList)
            return false;

        foreach(var argument in argumentList.Arguments)
        {
            if(argument.NameEquals?.Name.Identifier.Text == name)
                return true;
        }

        return false;
    }

    private static AttributeData? GetAttributeDataFromSyntax(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol attributeClass)
    {
        if(attributeSyntax.Parent is not AttributeListSyntax attributeList)
            return null;

        var syntaxTree = attributeSyntax.SyntaxTree;

        if(attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) is true)
        {
            return context.SemanticModel.Compilation.Assembly.GetAttributes()
                .FirstOrDefault(attr =>
                    SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeClass)
                    && attr.ApplicationSyntaxReference?.SyntaxTree == syntaxTree
                    && attr.ApplicationSyntaxReference?.Span == attributeSyntax.Span);
        }

        var targetType = GetTypeLevelTargetType(context, attributeSyntax);
        if(targetType is null)
            return null;

        return targetType.GetAttributes()
            .FirstOrDefault(attr =>
                SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeClass)
                && attr.ApplicationSyntaxReference?.SyntaxTree == syntaxTree
                && attr.ApplicationSyntaxReference?.Span == attributeSyntax.Span);
    }

    /// <summary>
    /// Gets the type a registration attribute registers: the target of <c>[IocRegisterFor]</c>, otherwise the type the
    /// attribute is applied to.
    /// </summary>
    /// <param name="attributeData">The attribute data when known (assembly-level registrations); otherwise looked up.</param>
    private static INamedTypeSymbol? GetRegistrationTargetType(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol attributeClass,
        AttributeData? attributeData,
        AnalyzerContext analyzerContext)
    {
        if(!AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols))
            return GetTypeLevelTargetType(context, attributeSyntax);

        // A target type that does not compile is already reported by the compiler
        attributeData ??= GetAttributeDataFromSyntax(context, attributeSyntax, attributeClass);
        return attributeData?.GetTargetTypeFromRegisterForAttribute() is { TypeKind: not TypeKind.Error } targetType ? targetType : null;
    }

    private static INamedTypeSymbol? GetTypeLevelTargetType(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax)
    {
        var declaration = attributeSyntax.Parent?.Parent;

        return declaration switch
        {
            TypeDeclarationSyntax typeDeclaration => context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken),
            _ => null
        };
    }

    /// <summary>
    /// Checks if the attribute is an IoC registration attribute and returns which type.
    /// Supports both non-generic and generic variants.
    /// </summary>
    private static bool TryGetIoCAttribute(AttributeData attribute, AnalyzerContext analyzerContext, out bool isIoCRegisterFor)
    {
        isIoCRegisterFor = false;
        var attributeClass = attribute.AttributeClass;
        if(attributeClass is null)
            return false;

        // Check IoCRegisterAttribute variants (non-generic and generic)
        if(AnalyzerHelpers.IsIoCRegisterAttribute(attributeClass, analyzerContext.AttributeSymbols))
        {
            return true;
        }

        // Check IoCRegisterForAttribute variants (non-generic and generic)
        if(AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols))
        {
            isIoCRegisterFor = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers a service and builds the service type index for fast lookups.
    /// </summary>
    private static void RegisterServiceWithIndex(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime lifetime,
        Location? location,
        string? serviceKey,
        ITypeSymbol? keyTypeSymbol,
        bool hasFactory,
        bool hasInstance,
        ImmutableArray<ServiceSymbolKey> registrations,
        ImmutableArray<ServiceSymbolKey> asyncDecoratedRegistrations)
    {
        // Same implementation type already registered (e.g., multiple assembly-level IocRegisterFor
        // attributes targeting the same impl type with different keys). Record the additional
        // (service type, key) pairs on the existing entry so SGIOC030 analysis sees all registrations.
        if(analyzerContext.RegisteredServices.TryGetValue(targetType, out var existingInfo))
        {
            existingInfo.AddRegistrations(registrations, asyncDecoratedRegistrations);
            return;
        }

        var hasKey = serviceKey is not null;
        var serviceInfo = new ServiceInfo(targetType, lifetime, location, serviceKey, keyTypeSymbol, hasKey, hasFactory, hasInstance, registrations, asyncDecoratedRegistrations);
        if(!analyzerContext.RegisteredServices.TryAdd(targetType, serviceInfo))
        {
            // Another registration of the type was added concurrently
            analyzerContext.RegisteredServices[targetType].AddRegistrations(registrations, asyncDecoratedRegistrations);
            return;
        }

        // Only build type index for non-keyed services.
        // Keyed services are resolved by key + type, not by type alone,
        // so including them in the type-only index could lead to false positive
        // circular dependency (SGIOC002) or lifetime conflict (SGIOC003-005) diagnostics.
        if(hasKey)
            return;

        // Build index for interfaces
        foreach(var iface in targetType.AllInterfaces)
        {
            analyzerContext.ServiceTypeIndex.TryAdd(iface, serviceInfo);
        }

        // Build index for base classes
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            analyzerContext.ServiceTypeIndex.TryAdd(baseType, serviceInfo);
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Collects default settings from IoCRegisterDefaultSettingsAttribute on the assembly.
    /// Uses the shared ExtractDefaultSettings method from Constants.
    /// Also tracks duplicated target types for SGIOC012 reporting.
    /// </summary>
    /// <param name="decoratorTypes">Receives the <c>Decorators</c> of each collected setting, in the order of the settings.</param>
    private static DefaultSettingsMap CollectDefaults(
        Compilation compilation,
        IocAttributeSymbols attributeSymbols,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> seenTargetTypes,
        ImmutableArray<ImmutableArray<INamedTypeSymbol>>.Builder decoratorTypes,
        CancellationToken cancellationToken)
    {
        if(attributeSymbols.IocRegisterDefaultsAttribute is null && attributeSymbols.IocRegisterDefaultsAttribute_T1 is null)
            return new DefaultSettingsMap([]);

        var settingsBuilder = ImmutableArray.CreateBuilder<DefaultSettingsModel>();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            // Check if this is an IoCRegisterDefaultsAttribute (non-generic or generic)
            if(!AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, attributeSymbols))
            {
                continue;
            }

            // Use shared method to extract default settings
            // Use different extraction method for generic vs non-generic attributes
            var settings = attributeClass.IsGenericType
                ? attribute.ExtractDefaultSettingsFromGenericAttribute()
                : attribute.ExtractDefaultSettings();
            if(settings is not null)
            {
                var targetTypeName = settings.TargetServiceType.Name;
                var tags = settings.Tags;

                // Build effective tags list using helper method
                var effectiveTags = AnalyzerHelpers.GetEffectiveTags(tags);

                // SGIOC012: Check each effective tag for duplicates
                var hasDuplicate = false;
                foreach(var tag in effectiveTags)
                {
                    var defaultKey = (targetTypeName, tag);
                    if(!seenTargetTypes.TryAdd(defaultKey, attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()))
                    {
                        hasDuplicate = true;
                        break; // Only need to find one duplicate
                    }
                }

                if(hasDuplicate)
                {
                    var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                    duplicatedDefaults.Add((targetTypeName, location));
                }
                else
                {
                    settingsBuilder.Add(settings);
                    decoratorTypes.Add(settings.Decorators.Length > 0 ? AnalyzerHelpers.GetDecoratorSymbols(attribute) : []);
                }
            }
        }

        return new DefaultSettingsMap(settingsBuilder.ToImmutable());
    }

    /// <summary>
    /// Gets the effective lifetime for a type, considering default settings.
    /// Uses DefaultSettingsMap for efficient lookups, consistent with RegisterSourceGenerator.
    /// </summary>
    private static ServiceLifetime GetEffectiveLifetime(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        bool hasExplicitLifetime,
        ServiceLifetime explicitLifetime)
    {
        // If lifetime is explicitly set, use it
        if(hasExplicitLifetime)
            return explicitLifetime;

        var defaultSettings = analyzerContext.DefaultSettings;
        if(defaultSettings.IsEmpty)
            return explicitLifetime;

        // Check default settings for matching interfaces
        foreach(var iface in targetType.AllInterfaces)
        {
            if(TryGetDefaultSettingsIndex(analyzerContext, iface, out var index))
                return defaultSettings[index].Lifetime;
        }

        // Check default settings for matching base classes
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            if(TryGetDefaultSettingsIndex(analyzerContext, baseType, out var index))
                return defaultSettings[index].Lifetime;

            baseType = baseType.BaseType;
        }

        // Default lifetime is Transient (as defined in TryGetLifetime)
        return explicitLifetime;
    }

    /// <summary>
    /// Finds the default settings for a service type by exact name, then by generic name and arity
    /// (e.g., <c>IGenericTest&lt;&gt;</c> matches <c>IGenericTest&lt;T&gt;</c>). Uses the same names as
    /// the generator's <c>GetTypeData</c> without building the type data.
    /// </summary>
    private static bool TryGetDefaultSettingsIndex(AnalyzerContext analyzerContext, INamedTypeSymbol type, out int index)
    {
        var defaultSettings = analyzerContext.DefaultSettings;
        var name = analyzerContext.TypeNames.GetOrAdd(type, static type => type.BuildTypeName());

        if(defaultSettings.TryGetExactMatches(name, out index))
            return true;

        // Error types have no generic type data
        return type is { IsGenericType: true, TypeKind: not TypeKind.Error }
            && defaultSettings.TryGetGenericMatches(type.GetNameWithoutGeneric(name), type.Arity, out index);
    }

    private readonly struct SymbolLocationComparer : IEqualityComparer<(INamedTypeSymbol Type, Location? Location)>
    {
        public bool Equals((INamedTypeSymbol Type, Location? Location) x, (INamedTypeSymbol Type, Location? Location) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && x.Location == y.Location;

        public int GetHashCode((INamedTypeSymbol Type, Location? Location) obj)
            => unchecked((SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397) ^ (obj.Location?.GetHashCode() ?? 0));
    }

    private readonly struct RegistrationKeyComparer : IEqualityComparer<(INamedTypeSymbol Type, string? Key, string Tag)>
    {
        public bool Equals((INamedTypeSymbol Type, string? Key, string Tag) x, (INamedTypeSymbol Type, string? Key, string Tag) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type)
                && StringComparer.Ordinal.Equals(x.Key, y.Key)
                && StringComparer.Ordinal.Equals(x.Tag, y.Tag);

        public int GetHashCode((INamedTypeSymbol Type, string? Key, string Tag) obj)
            => unchecked((((SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Key ?? string.Empty)) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Tag));
    }

    private sealed class AnalyzerContext(
        IocAttributeSymbols attributeSymbols,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> serviceTypeIndex,
        DefaultSettingsMap defaultSettings,
        ImmutableArray<ImmutableArray<INamedTypeSymbol>> defaultDecorators,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> seenDefaultTargetTypes,
        IocFeatures features)
    {
        public IocAttributeSymbols AttributeSymbols { get; } = attributeSymbols;
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> RegisteredServices { get; } = registeredServices;

        /// <summary>
        /// Index mapping service types (interfaces/base classes) to their implementations.
        /// Enables O(1) lookup instead of O(n) linear search.
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> ServiceTypeIndex { get; } = serviceTypeIndex;

        /// <summary>
        /// Default settings from IoCRegisterDefaultSettingsAttribute, using DefaultSettingsMap for efficient lookups.
        /// </summary>
        public DefaultSettingsMap DefaultSettings { get; } = defaultSettings;

        /// <summary>
        /// The <c>Decorators</c> symbols of each default setting, by the index of the setting in <see cref="DefaultSettings"/>.
        /// </summary>
        public ImmutableArray<ImmutableArray<INamedTypeSymbol>> DefaultDecorators { get; } = defaultDecorators;

        /// <summary>
        /// List of duplicated IoCRegisterDefaults for SGIOC012 reporting.
        /// </summary>
        public ConcurrentBag<(string TargetTypeName, Location? Location)> DuplicatedDefaults { get; } = duplicatedDefaults;

        /// <summary>
        /// Tracks seen IoCRegisterDefaults (target type name, single tag) pairs across assembly and type-level attributes (SGIOC012).
        /// Services without tags use an empty string tag for comparison.
        /// </summary>
        public ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> SeenDefaultTargetTypes { get; } = seenDefaultTargetTypes;

        /// <summary>
        /// Tracks registered (ImplementationType, Key, single Tag) tuples to detect duplicates (SGIOC011).
        /// Each tag is tracked separately; duplicates are detected when any single tag matches.
        /// Services without tags use an empty string tag for comparison.
        /// Value is the first registration location for diagnostic reporting.
        /// </summary>
        public ConcurrentDictionary<(INamedTypeSymbol Type, string? Key, string Tag), Location?> RegistrationKeys { get; } = new(new RegistrationKeyComparer());

        /// <summary>
        /// Stores resolved key type symbols for KeyType.Csharp registrations where nameof() was used.
        /// Populated by RegisterSyntaxNodeAction, consumed by SGIOC015 in CompilationEnd.
        /// Key: (TargetType, AttributeLocation) to handle multiple registrations on the same type.
        /// </summary>
        public ConcurrentDictionary<(INamedTypeSymbol Type, Location? Location), ITypeSymbol> ResolvedCsharpKeyTypes { get; } = new(new SymbolLocationComparer());

        /// <summary>
        /// Type names used to match default settings, cached because registrations share interfaces and base classes.
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, string> TypeNames { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Enabled generation features parsed from SourceGenIocFeatures.
        /// </summary>
        public IocFeatures Features { get; } = features;

        /// <summary>
        /// The decorators applied to registrations (open generic decorators closed for each closed service type, otherwise
        /// their definitions), with the indexes of their constructor parameters that receive a decorated instance. SGIOC030
        /// checks their other dependencies.
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, ConcurrentDictionary<int, byte>> AppliedDecorators { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// For each decorator (definition) and service type it implements (definition for generic service types) of a
        /// registration it decorates, the indexes of the constructor parameters that can receive the decorated instance
        /// (SGIOC034/SGIOC035).
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<int>>> DecoratorUsages { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// The open generic registrations, with their service types and decorators, whose expanding dependencies are
        /// reported (SGIOC036/SGIOC037).
        /// </summary>
        public ConcurrentBag<(INamedTypeSymbol Implementation, ImmutableArray<ServiceSymbolKey> Registrations, ImmutableArray<INamedTypeSymbol> Decorators, SyntaxReference? Attribute)> OpenGenericRegistrations { get; } = [];

        /// <summary>
        /// The injectable members listed in <c>InjectMembers</c> of registration attributes, with the locations of their
        /// elements, by the span of the attribute. Populated by RegisterSyntaxNodeAction, which has the semantic model that
        /// <c>nameof()</c> needs; consumed by SGIOC036/SGIOC037 in CompilationEnd.
        /// </summary>
        public ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), ImmutableArray<(ISymbol Member, Location Location)>> InjectMembers { get; } = new();

        private readonly ConcurrentDictionary<INamedTypeSymbol, TypeData> decoratorTypeData = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Gets the type data of a decorator (with constructor parameters and hierarchy), cached because registrations share
        /// decorators.
        /// </summary>
        public TypeData GetDecoratorTypeData(INamedTypeSymbol decorator)
        {
            if(!decoratorTypeData.TryGetValue(decorator, out var typeData))
            {
                typeData = decorator.GetTypeData(extractConstructorParams: true, extractHierarchy: true);
                decoratorTypeData.TryAdd(decorator, typeData);
            }

            return typeData;
        }
    }

    private sealed record ServiceInfo
    {
        public INamedTypeSymbol Type { get; }
        public ServiceLifetime Lifetime { get; }
        public Location? Location { get; }

        /// <summary>
        /// The registration key string, or null if the service is unkeyed.
        /// </summary>
        public string? ServiceKey { get; }

        /// <summary>
        /// The type symbol of the registration key, or null if no key is specified or KeyType is Csharp.
        /// </summary>
        public ITypeSymbol? KeyTypeSymbol { get; }

        /// <summary>
        /// Indicates whether a Key is specified (regardless of KeyType).
        /// </summary>
        public bool HasKey { get; }

        /// <summary>
        /// Indicates whether a Factory method is specified.
        /// </summary>
        public bool HasFactory { get; }

        /// <summary>
        /// Indicates whether an Instance is specified.
        /// </summary>
        public bool HasInstance { get; }

        /// <summary>
        /// All (service type, key) pairs from every registration that maps to this implementation type.
        /// Populated during collection to capture all registrations including assembly-level duplicates
        /// where <see cref="RegisteredServices"/> only stores the first entry per implementation type.
        /// Used by SGIOC030 analysis to correctly identify all async-init-only service type/key pairs.
        /// </summary>
        public ImmutableArray<ServiceSymbolKey> AllRegistrations => _allRegistrations;

        private ImmutableArray<ServiceSymbolKey> _allRegistrations;

        /// <summary>
        /// The (service type, key) pairs of <see cref="AllRegistrations"/> that a decorator with async inject methods makes
        /// async-init, even when the implementation type is not (SGIOC030).
        /// </summary>
        public ImmutableArray<ServiceSymbolKey> AsyncDecoratedRegistrations => _asyncDecoratedRegistrations;

        private ImmutableArray<ServiceSymbolKey> _asyncDecoratedRegistrations;

        /// <summary>
        /// Cached constructor to avoid repeated SpecifiedOrPrimaryOrMostParametersConstructor lookups.
        /// </summary>
        public IMethodSymbol? Constructor { get; }

        /// <summary>
        /// Cached list of members with [IocInject] or [Inject] attribute to avoid repeated GetMembers() and attribute lookups.
        /// </summary>
        public ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> InjectedMembers { get; }

        public ServiceInfo(
            INamedTypeSymbol type,
            ServiceLifetime lifetime,
            Location? location,
            string? serviceKey,
            ITypeSymbol? keyTypeSymbol,
            bool hasKey,
            bool hasFactory,
            bool hasInstance,
            ImmutableArray<ServiceSymbolKey> registrations,
            ImmutableArray<ServiceSymbolKey> asyncDecoratedRegistrations)
        {
            Type = type;
            Lifetime = lifetime;
            Location = location;
            _allRegistrations = registrations;
            _asyncDecoratedRegistrations = asyncDecoratedRegistrations;
            ServiceKey = serviceKey;
            KeyTypeSymbol = keyTypeSymbol;
            HasKey = hasKey;
            HasFactory = hasFactory;
            HasInstance = hasInstance;

            // Cache constructor lookup
            Constructor = type.SpecifiedOrPrimaryOrMostParametersConstructor;

            // Cache injected members
            InjectedMembers = [.. type.GetInjectedMembers()];
        }

        /// <summary>
        /// Adds the (service type, key) pairs of another registration of this implementation type.
        /// </summary>
        public void AddRegistrations(ImmutableArray<ServiceSymbolKey> registrations, ImmutableArray<ServiceSymbolKey> asyncDecoratedRegistrations)
        {
            ImmutableInterlocked.Update(ref _allRegistrations, static (current, added) => current.AddRange(added), registrations);

            if(!asyncDecoratedRegistrations.IsEmpty)
                ImmutableInterlocked.Update(ref _asyncDecoratedRegistrations, static (current, added) => current.AddRange(added), asyncDecoratedRegistrations);
        }
    }
}
