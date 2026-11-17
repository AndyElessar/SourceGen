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
            analyzerContext.PendingRegistrations.Add(new(attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location));
        }

        return registrations;
    }

    /// <summary>
    /// SGIOC001, SGIOC009 and SGIOC032 for an assembly-level IoCRegisterFor attribute.
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
    /// First pass: collect registrations, type-level default settings and module imports, and do the immediate validation
    /// that does not depend on default settings (SGIOC001, SGIOC009, SGIOC032). Resolving the registrations
    /// against the default settings and the dependency analysis (SGIOC002, SGIOC003-005) are deferred to CompilationEnd.
    /// </summary>
    private static void CollectAndValidateNamedType(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if(!TryGetIoCAttribute(attribute, analyzerContext, out var isIoCRegisterFor))
            {
                if(attribute.AttributeClass is not { } attributeClass)
                    continue;

                if(AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, analyzerContext.AttributeSymbols))
                    analyzerContext.TypeLevelDefaultAttributes.Add(attribute);
                else if(AnalyzerHelpers.IsIocImportModuleAttribute(attributeClass, analyzerContext.AttributeSymbols))
                    analyzerContext.ModuleImportAttributes.Add(attribute);

                continue;
            }

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

            // Registered in CompilationEnd, once all default settings are known
            analyzerContext.PendingRegistrations.Add(new(attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location));
        }
    }

    /// <summary>
    /// Resolves every collected registration against the default settings and registers it for the CompilationEnd
    /// analyses, in source order so that the analysis does not depend on the order of the symbol actions. Also reports the
    /// checks that depend on the default settings: duplicated registrations with their effective tags (SGIOC011), and the
    /// open types nested in a generic type the registrations would be registered as (SGIOC038).
    /// </summary>
    private static void ResolveRegistrations(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        Dictionary<SyntaxTree, int> treeOrder,
        CancellationToken cancellationToken)
    {
        var pending = analyzerContext.PendingRegistrations.ToArray();
        Array.Sort(pending, (x, y) => GetSourceOrder(x.Location).CompareTo(GetSourceOrder(y.Location)));

        foreach(var (attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bestDefaultIndex = FindBestDefaultIndex(analyzerContext, targetType);

            // SGIOC011: Check for duplicated registrations (same implementation type, key and a tag). Like the generator, a
            // registration without Tags takes those of its best default settings.
            var tags = attribute.GetTags();
            if(tags.Length == 0 && bestDefaultIndex >= 0)
                tags = analyzerContext.DefaultSettings[bestDefaultIndex].Tags;

            AnalyzeDuplicatedRegistration(reportDiagnostic, analyzerContext, targetType, key, tags, location);

            // SGIOC038: Check the interfaces and base classes an open implementation is registered as
            AnalyzeImplicitOpenTypesNestedInGenericType(reportDiagnostic, analyzerContext, attribute, targetType, explicitServiceTypes, location);

            RegisterService(analyzerContext, attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location);
        }

        (int Tree, int Position) GetSourceOrder(Location? location)
            => location is { SourceTree: { } tree } && treeOrder.TryGetValue(tree, out var index)
                ? (index, location.SourceSpan.Start)
                : (int.MaxValue, int.MaxValue);
    }

    /// <summary>
    /// The checks that run for every registration attribute, type-level or assembly-level, and do not depend on default settings.
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

        // SGIOC032: Check explicit service types that would be skipped
        AnalyzeIncompatibleServiceTypes(reportDiagnostic, explicitServiceTypes, targetType, location);

        // SGIOC009: Check an Instance has no explicit non-Singleton lifetime
        AnalyzeInstanceLifetime(reportDiagnostic, attribute, location);
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
            RuntimeTypeSymbolComparer.Instance);
        AddDefaultSettingsServiceTypes(analyzerContext, attribute, targetType, serviceTypes);

        foreach(var serviceType in serviceTypes)
        {
            if(!serviceType.IsOpenNestedInGenericType)
                continue;

            var definition = serviceType.OriginalDefinition;
            reportDiagnostic(Diagnostic.Create(
                ImplicitOpenTypeNestedInGenericType,
                location,
                definition.ToDisplayString(AnalyzerHelpers.NestedTypeDisplayFormat),
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

        // Check for Factory and Instance (used by ServiceInfo)
        var (hasFactory, hasInstance) = attribute.HasFactoryOrInstance();

        // Service types are compared as runtime types, like every service type set of the analyzer
        var serviceTypes = new HashSet<INamedTypeSymbol>(
            AnalyzerHelpers.EnumerateRegisteredServiceTypes(targetType, attribute, explicitServiceTypes),
            RuntimeTypeSymbolComparer.Instance);
        var bestDefaultIndex = AddDefaultSettingsServiceTypes(analyzerContext, attribute, targetType, serviceTypes);

        var lifetime = GetEffectiveLifetime(analyzerContext, attribute, hasInstance, bestDefaultIndex);

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

        // SGIOC036/SGIOC037: open generic registrations are closed on demand for the dependencies the generator discovers,
        // starting from the dependencies of closed registrations; SGIOC039: the service types of generic factories
        analyzerContext.Registrations.Add(new(targetType, registrations, decorators, attribute, hasFactory, hasInstance, bestDefaultIndex, location));

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
            existingInfo.AddActivation(hasFactory, hasInstance, analyzerContext.Assembly);
            return;
        }

        var hasKey = serviceKey is not null;
        var serviceInfo = new ServiceInfo(targetType, lifetime, location, serviceKey, keyTypeSymbol, hasKey, hasFactory, hasInstance, registrations, asyncDecoratedRegistrations, analyzerContext.Assembly);
        if(!analyzerContext.RegisteredServices.TryAdd(targetType, serviceInfo))
        {
            // Another registration of the type was added concurrently
            existingInfo = analyzerContext.RegisteredServices[targetType];
            existingInfo.AddRegistrations(registrations, asyncDecoratedRegistrations);
            existingInfo.AddActivation(hasFactory, hasInstance, analyzerContext.Assembly);
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
    /// Collects the default settings in the order of the generator's <c>DefaultSettingsMap</c>, where the first settings for
    /// a target type win: the <c>[IocRegisterDefaults]</c> of the compiled assembly, assembly-level and type-level
    /// (non-generic attributes, then generic attributes, each in source order), then those of the imported modules (see
    /// <see cref="AnalyzerHelpers.EnumerateImportedDefaultSettingsAttributes"/>). Reports SGIOC012 for each settings of the
    /// compiled assembly whose target type and a tag were already defined by earlier settings; imported settings are not
    /// reported.
    /// </summary>
    private static void CollectDefaults(
        Action<Diagnostic> reportDiagnostic,
        Compilation compilation,
        AnalyzerContext analyzerContext,
        Dictionary<SyntaxTree, int> treeOrder,
        CancellationToken cancellationToken)
    {
        var attributeSymbols = analyzerContext.AttributeSymbols;
        if(attributeSymbols.IocRegisterDefaultsAttribute is null && attributeSymbols.IocRegisterDefaultsAttribute_T1 is null)
            return;

        var attributes = new List<AttributeData>(analyzerContext.TypeLevelDefaultAttributes);
        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            if(attribute.AttributeClass is { } attributeClass && AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, attributeSymbols))
                attributes.Add(attribute);
        }

        if(attributes.Count == 0 && analyzerContext.ModuleImportAttributes.IsEmpty)
            return;

        attributes.Sort((x, y) =>
        {
            var byKind = (x.AttributeClass?.IsGenericType == true).CompareTo(y.AttributeClass?.IsGenericType == true);
            return byKind != 0
                ? byKind
                : AnalyzerHelpers.GetSourceOrder(x.ApplicationSyntaxReference, treeOrder)
                    .CompareTo(AnalyzerHelpers.GetSourceOrder(y.ApplicationSyntaxReference, treeOrder));
        });

        var settings = ImmutableArray.CreateBuilder<DefaultSettingsModel>();
        var decorators = ImmutableArray.CreateBuilder<ImmutableArray<INamedTypeSymbol>>();
        var settingsAttributes = ImmutableArray.CreateBuilder<AttributeData>();
        var seenTargetTypes = new HashSet<(string TargetTypeName, string Tag)>();
        foreach(var attribute in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Like the generator, settings whose Factory is given with nameof() of a member of an unbound generic type are
            // ignored as a whole (SGIOC041)
            if(attribute.ApplicationSyntaxReference is { } reference
                && analyzerContext.IgnoredDefaultSettings.ContainsKey((reference.SyntaxTree, reference.Span)))
            {
                continue;
            }

            if(ExtractDefaultSettings(attribute) is not { } defaults)
                continue;

            // SGIOC012: the target type and at least one effective tag are already defined by earlier settings
            var targetTypeName = defaults.TargetServiceType.Name;
            foreach(var tag in AnalyzerHelpers.GetEffectiveTags(defaults.Tags))
            {
                if(!seenTargetTypes.Add((targetTypeName, tag)))
                {
                    reportDiagnostic(Diagnostic.Create(
                        DuplicatedDefaultSettings,
                        attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                        targetTypeName));
                    break;
                }
            }

            // Like the generator, every settings is kept: the DefaultSettingsMap only matches the first ones for a target type,
            // while the ImplementationTypes and the Factory of an open generic target of the others are still registered
            Add(attribute, defaults);
            settingsAttributes.Add(attribute);
        }

        // The settings of imported modules follow. Like the generator, which reads them without a semantic model, their
        // Factory is absent, and their ImplementationTypes add no registration, so they have no entry in DefaultAttributes.
        foreach(var attribute in AnalyzerHelpers.EnumerateImportedDefaultSettingsAttributes(
            analyzerContext.ModuleImportAttributes, compilation.Assembly, treeOrder, attributeSymbols, cancellationToken))
        {
            if(ExtractDefaultSettings(attribute) is { } defaults)
                Add(attribute, defaults);
        }

        analyzerContext.SetDefaultSettings(new DefaultSettingsMap(settings.ToImmutable()), decorators.ToImmutable(), settingsAttributes.ToImmutable());

        void Add(AttributeData attribute, DefaultSettingsModel defaults)
        {
            settings.Add(defaults);
            decorators.Add(defaults.Decorators.Length > 0 ? AnalyzerHelpers.GetDecoratorSymbols(attribute) : []);
        }

        DefaultSettingsModel? ExtractDefaultSettings(AttributeData attribute)
            => attribute.AttributeClass?.IsGenericType == true
                ? attribute.ExtractDefaultSettingsFromGenericAttribute(compilation.Assembly)
                : attribute.ExtractDefaultSettings(compilation.Assembly);
    }

    /// <summary>
    /// Finds the best default settings of an implementation, like the generator's <c>FindMatchingDefaults</c>: the first
    /// base class, then interface, that has default settings. Returns -1 when none matches.
    /// </summary>
    private static int FindBestDefaultIndex(AnalyzerContext analyzerContext, INamedTypeSymbol targetType)
    {
        if(analyzerContext.DefaultSettings.IsEmpty)
            return -1;

        foreach(var candidate in EnumerateBaseClasses(targetType).Concat(targetType.AllInterfaces))
        {
            if(TryGetDefaultSettingsIndex(analyzerContext, candidate, out var index))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// Gets the lifetime of a registration like the generator's <c>MergeSettings</c>: its explicit lifetime, otherwise
    /// Singleton for an <c>Instance</c> registration, otherwise the lifetime of its best default settings, otherwise the
    /// MSBuild <c>SourceGenIocDefaultLifetime</c> (Transient when unset).
    /// </summary>
    private static ServiceLifetime GetEffectiveLifetime(AnalyzerContext analyzerContext, AttributeData attribute, bool hasInstance, int bestDefaultIndex)
    {
        var (hasExplicitLifetime, lifetime) = attribute.TryGetLifetime();
        if(hasExplicitLifetime)
            return lifetime;

        if(hasInstance)
            return ServiceLifetime.Singleton;

        return bestDefaultIndex >= 0 ? analyzerContext.DefaultSettings[bestDefaultIndex].Lifetime : analyzerContext.FallbackLifetime;
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
            => RuntimeTypeSymbolComparer.Instance.Equals(x.Type, y.Type)
                && StringComparer.Ordinal.Equals(x.Key, y.Key)
                && StringComparer.Ordinal.Equals(x.Tag, y.Tag);

        public int GetHashCode((INamedTypeSymbol Type, string? Key, string Tag) obj)
            => unchecked((((RuntimeTypeSymbolComparer.Instance.GetHashCode(obj.Type) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Key ?? string.Empty)) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Tag));
    }

    /// <summary>
    /// A registration attribute collected before the default settings are known, registered at compilation end.
    /// </summary>
    private readonly record struct PendingRegistration(
        AttributeData Attribute,
        INamedTypeSymbol TargetType,
        string? Key,
        ITypeSymbol? KeyTypeSymbol,
        List<INamedTypeSymbol> ExplicitServiceTypes,
        Location? Location);

    /// <summary>
    /// A registration analyzed at compilation end: the expanding dependencies of open generic registrations, the root
    /// dependencies of closed ones (SGIOC036/SGIOC037), and the service types of generic factories (SGIOC039).
    /// </summary>
    /// <param name="DefaultIndex">The index of its best default settings, or -1 when none matches.</param>
    /// <param name="Location">The location of the registration attribute.</param>
    private sealed record class AnalyzedRegistration(
        INamedTypeSymbol Implementation,
        ImmutableArray<ServiceSymbolKey> Registrations,
        ImmutableArray<INamedTypeSymbol> Decorators,
        AttributeData Attribute,
        bool HasFactory,
        bool HasInstance,
        int DefaultIndex,
        Location? Location)
    {
        /// <summary>Gets whether the implementation is an open generic type.</summary>
        public bool IsOpenGeneric => Implementation.IsUnboundGenericType || Implementation is { IsGenericType: true, IsDefinition: true };
    }

    private sealed class AnalyzerContext(
        IocAttributeSymbols attributeSymbols,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> serviceTypeIndex,
        IocFeatures features,
        ServiceLifetime fallbackLifetime,
        IAssemblySymbol assembly)
    {
        public IocAttributeSymbols AttributeSymbols { get; } = attributeSymbols;

        /// <summary>
        /// The lifetime of a registration that neither sets one nor matches default settings: the MSBuild
        /// <c>SourceGenIocDefaultLifetime</c>, otherwise Transient, like the generator's <c>DefaultSettingsMap.FallbackLifetime</c>.
        /// </summary>
        public ServiceLifetime FallbackLifetime { get; } = fallbackLifetime;

        /// <summary>The assembly being compiled, which the generated code is compiled into.</summary>
        public IAssemblySymbol Assembly { get; } = assembly;
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> RegisteredServices { get; } = registeredServices;

        /// <summary>
        /// Index mapping service types (interfaces/base classes) to their implementations.
        /// Enables O(1) lookup instead of O(n) linear search.
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> ServiceTypeIndex { get; } = serviceTypeIndex;

        /// <summary>
        /// The registrations collected by the symbol and compilation start actions, resolved against the default settings at
        /// CompilationEnd (<see cref="ResolveRegistrations"/>).
        /// </summary>
        public ConcurrentBag<PendingRegistration> PendingRegistrations { get; } = [];

        /// <summary>The type-level IoCRegisterDefaultsAttribute (class, struct, interface), collected by the symbol action.</summary>
        public ConcurrentBag<AttributeData> TypeLevelDefaultAttributes { get; } = [];

        /// <summary>The IocImportModuleAttribute of the compilation, whose modules' default settings apply to registrations.</summary>
        public ConcurrentBag<AttributeData> ModuleImportAttributes { get; } = [];

        /// <summary>
        /// Default settings of the compiled assembly, then of the imported modules, using DefaultSettingsMap for efficient
        /// lookups. Set at CompilationEnd by <see cref="CollectDefaults"/>; empty before.
        /// </summary>
        public DefaultSettingsMap DefaultSettings { get; private set; } = new([]);

        /// <summary>
        /// The <c>Decorators</c> symbols of each default setting, by the index of the setting in <see cref="DefaultSettings"/>.
        /// </summary>
        public ImmutableArray<ImmutableArray<INamedTypeSymbol>> DefaultDecorators { get; private set; } = [];

        /// <summary>
        /// The attribute of each default setting of the compiled assembly, by the index of the setting in
        /// <see cref="DefaultSettings"/>. The imported settings follow them in <see cref="DefaultSettings"/> without an
        /// attribute: like the generator, their <c>Factory</c> and <c>ImplementationTypes</c> are not used.
        /// </summary>
        public ImmutableArray<AttributeData> DefaultAttributes { get; private set; } = [];

        public void SetDefaultSettings(
            DefaultSettingsMap defaultSettings,
            ImmutableArray<ImmutableArray<INamedTypeSymbol>> defaultDecorators,
            ImmutableArray<AttributeData> defaultAttributes)
        {
            DefaultSettings = defaultSettings;
            DefaultDecorators = defaultDecorators;
            DefaultAttributes = defaultAttributes;
        }

        /// <summary>
        /// Tracks registered (ImplementationType, Key, single Tag) tuples to detect duplicates (SGIOC011).
        /// Each tag is tracked separately; duplicates are detected when any single tag matches.
        /// Services without tags use an empty string tag for comparison.
        /// Implementation types are compared as runtime types. Filled at CompilationEnd, in source order.
        /// </summary>
        public HashSet<(INamedTypeSymbol Type, string? Key, string Tag)> RegistrationKeys { get; } = new(new RegistrationKeyComparer());

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
        public List<AnalyzedRegistration> Registrations { get; } = [];

        /// <summary>
        /// The <c>Factory</c> methods given with <c>nameof()</c> in registration and default settings attributes, by the span of
        /// the attribute. Populated by RegisterSyntaxNodeAction, which has the semantic model that <c>nameof()</c> needs;
        /// consumed by SGIOC036/SGIOC037 in CompilationEnd.
        /// </summary>
        public ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), IMethodSymbol> FactoryMethods { get; } = new();

        /// <summary>
        /// The registration attributes, by their span, that the generator ignores (SGIOC041) or does not register open for a Key
        /// that uses the implementation's type parameters (SGIOC040), so their Factory does not omit an open registration
        /// (SGIOC042). Populated by RegisterSyntaxNodeAction, which has the semantic model that <c>nameof()</c> needs; consumed in
        /// CompilationEnd.
        /// </summary>
        public ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte> RegistrationsNotRegisteredOpen { get; } = new();

        /// <summary>
        /// The default settings attributes, by their span, whose <c>Factory</c> is given with <c>nameof()</c> of a member of an
        /// unbound generic type (SGIOC041), which the generator ignores as a whole. Populated by RegisterSyntaxNodeAction,
        /// which has the semantic model that <c>nameof()</c> needs; consumed by <c>CollectDefaults</c> in CompilationEnd.
        /// </summary>
        public ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte> IgnoredDefaultSettings { get; } = new();

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
                typeData = decorator.GetTypeData(extractConstructorParams: true, extractHierarchy: true, constructedIn: Assembly);
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
        /// Cached constructor to avoid repeated GetSelectedConstructor lookups; null unless a registration of this
        /// implementation type has neither Instance nor Factory, since the container constructs only such a registration.
        /// </summary>
        public IMethodSymbol? Constructor => Volatile.Read(ref _constructor);

        private IMethodSymbol? _constructor;
        private int _hasConstructorActivation;

        /// <summary>
        /// Cached list of members with [IocInject] or [Inject] attribute to avoid repeated GetMembers() and attribute lookups;
        /// empty unless a registration of this implementation type has no Instance, since the container does not inject an Instance.
        /// </summary>
        public ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> InjectedMembers => _injectedMembers;

        private ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> _injectedMembers = [];
        private int _hasInjection;

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
            ImmutableArray<ServiceSymbolKey> asyncDecoratedRegistrations,
            IAssemblySymbol assembly)
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

            AddActivation(hasFactory, hasInstance, assembly);
        }

        /// <summary>
        /// Records how another registration of this implementation type is activated, so <see cref="Constructor"/> and
        /// <see cref="InjectedMembers"/> cover every registration of the type, not only the first one.
        /// </summary>
        public void AddActivation(bool hasFactory, bool hasInstance, IAssemblySymbol assembly)
        {
            // Like the generator, an Instance or Factory registration is not constructed, so its constructor has no dependencies
            if(!hasInstance && !hasFactory && Interlocked.Exchange(ref _hasConstructorActivation, 1) == 0)
                Volatile.Write(ref _constructor, Type.GetSelectedConstructor(assembly));

            // An Instance is not injected; a Factory's result keeps its injected members, since the generator calls the inject
            // methods of an async-init factory result
            if(!hasInstance && Interlocked.Exchange(ref _hasInjection, 1) == 0)
                ImmutableInterlocked.InterlockedExchange(ref _injectedMembers, [.. Type.GetInjectedMembers()]);
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
