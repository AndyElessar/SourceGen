namespace SourceGen.Ioc;

/// <summary>
/// Shared helper methods for IoC analyzers.
/// </summary>
internal static class AnalyzerHelpers
{
    /// <summary>
    /// Well-known service types that can always be resolved from the DI container.
    /// These types are provided by the runtime and do not need explicit registration.
    /// </summary>
    private static readonly ImmutableHashSet<string> WellKnownServiceTypes =
    [
        "global::System.IServiceProvider",
        "global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        "global::Microsoft.Extensions.DependencyInjection.IServiceScope",
        "global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider"
    ];

    /// <summary>
    /// Checks if the type is a well-known service type that can always be resolved.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check.</param>
    /// <returns>True if the type is a well-known service type; otherwise, false.</returns>
    public static bool IsWellKnownServiceType(ITypeSymbol typeSymbol)
    {
        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return WellKnownServiceTypes.Contains(fullName);
    }

    /// <summary>
    /// Checks if a service type can always be resolved (well-known types only).
    /// Note: IEnumerable&lt;T&gt; is NOT always resolvable when ResolveIServiceCollection = false,
    /// as T must be registered to have any elements.
    /// </summary>
    /// <param name="serviceType">The service type to check.</param>
    /// <returns>True if the service is always resolvable; otherwise, false.</returns>
    public static bool IsAlwaysResolvable(INamedTypeSymbol serviceType)
    {
        // Well-known service types
        if (IsWellKnownServiceType(serviceType))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a service type is IEnumerable&lt;T&gt;.
    /// </summary>
    /// <param name="serviceType">The service type to check.</param>
    /// <returns>True if the service type is IEnumerable&lt;T&gt;; otherwise, false.</returns>
    public static bool IsIEnumerableOfT(INamedTypeSymbol serviceType)
    {
        return serviceType.IsGenericType
            && serviceType.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T;
    }

    /// <summary>
    /// Gets the element type T from IEnumerable&lt;T&gt;.
    /// </summary>
    /// <param name="enumerableType">The IEnumerable&lt;T&gt; type.</param>
    /// <returns>The element type T, or null if not applicable.</returns>
    public static INamedTypeSymbol? GetEnumerableElementType(INamedTypeSymbol enumerableType)
    {
        if (!IsIEnumerableOfT(enumerableType))
            return null;

        return enumerableType.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
    }

    /// <summary>
    /// Compares an attribute class with a target symbol, handling generic types correctly.
    /// For generic types, compares the original unbound definition.
    /// </summary>
    /// <param name="attributeClass">The attribute class to compare.</param>
    /// <param name="targetSymbol">The target symbol to compare against.</param>
    /// <returns>True if the attribute class matches the target symbol.</returns>
    public static bool IsAttributeMatch(INamedTypeSymbol? attributeClass, INamedTypeSymbol? targetSymbol)
    {
        if (attributeClass is null || targetSymbol is null)
            return false;

        // For generic types, get the original unbound definition for comparison
        var typeToCompare = attributeClass.IsGenericType ? attributeClass.OriginalDefinition : attributeClass;
        return SymbolEqualityComparer.Default.Equals(typeToCompare, targetSymbol);
    }

    /// <summary>
    /// Checks if an attribute class matches any of the IoC registration attribute variants.
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is an IoC registration attribute.</returns>
    public static bool IsIoCRegistrationAttribute(INamedTypeSymbol attributeClass, IoCAttributeSymbols attributeSymbols)
    {
        return IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterAttribute_T1)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterForAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterForAttribute_T1);
    }

    /// <summary>
    /// Checks if an attribute class is any IoCRegisterForAttribute variant.
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is an IoCRegisterForAttribute.</returns>
    public static bool IsIoCRegisterForAttribute(INamedTypeSymbol attributeClass, IoCAttributeSymbols attributeSymbols)
    {
        return IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterForAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterForAttribute_T1);
    }

    /// <summary>
    /// Checks if an attribute class is any IoCRegisterAttribute variant.
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is an IoCRegisterAttribute.</returns>
    public static bool IsIoCRegisterAttribute(INamedTypeSymbol attributeClass, IoCAttributeSymbols attributeSymbols)
    {
        return IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterAttribute_T1);
    }

    /// <summary>
    /// Checks if an attribute class is any IoCRegisterDefaultsAttribute variant.
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is an IoCRegisterDefaultsAttribute.</returns>
    public static bool IsIoCRegisterDefaultsAttribute(INamedTypeSymbol attributeClass, IoCAttributeSymbols attributeSymbols)
    {
        return IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterDefaultsAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterDefaultsAttribute_T1);
    }

    /// <summary>
    /// Checks if an attribute class matches any IoC attribute variant (IoCRegister, IoCRegisterFor, or IoCRegisterDefaults).
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is any IoC attribute.</returns>
    public static bool IsAnyIoCAttribute(INamedTypeSymbol attributeClass, IoCAttributeSymbols attributeSymbols)
    {
        return IsIoCRegistrationAttribute(attributeClass, attributeSymbols)
            || IsIoCRegisterDefaultsAttribute(attributeClass, attributeSymbols);
    }

    /// <summary>
    /// Extracts registered service types from an IoC registration attribute's ServiceTypes property.
    /// </summary>
    /// <param name="attribute">The attribute to extract service types from.</param>
    /// <returns>An enumerable of service type symbols.</returns>
    public static IEnumerable<INamedTypeSymbol> GetServiceTypesFromAttribute(AttributeData attribute)
    {
        foreach (var namedArg in attribute.NamedArguments)
        {
            if (namedArg.Key is not "ServiceTypes")
                continue;

            if (namedArg.Value.Kind is not TypedConstantKind.Array)
                continue;

            foreach (var element in namedArg.Value.Values)
            {
                if (element.Value is INamedTypeSymbol serviceType)
                    yield return serviceType;
            }
        }
    }

    /// <summary>
    /// Gets the target implementation type from an IoCRegisterForAttribute.
    /// Supports both generic and non-generic variants.
    /// </summary>
    /// <param name="attribute">The attribute data.</param>
    /// <returns>The target type symbol, or null if not found.</returns>
    public static INamedTypeSymbol? GetTargetTypeFromRegisterFor(AttributeData attribute)
    {
        var attrClass = attribute.AttributeClass;
        if (attrClass is null)
            return null;

        // Generic variant: IocRegisterForAttribute<T>
        if (attrClass.IsGenericType && attrClass.TypeArguments.Length >= 1)
        {
            return attrClass.TypeArguments[0] as INamedTypeSymbol;
        }

        // Non-generic variant: IocRegisterForAttribute(typeof(T))
        if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol argType)
        {
            return argType;
        }

        return null;
    }

    /// <summary>
    /// Checks if a field type is resolvable for injection.
    /// </summary>
    /// <param name="field">The field to check.</param>
    /// <param name="injectAttribute">The inject attribute on the field.</param>
    /// <returns>True if the field's dependency is resolvable; otherwise, false.</returns>
    public static bool IsFieldAlwaysResolvable(IFieldSymbol field, AttributeData injectAttribute)
    {
        var fieldType = field.Type;

        // Skip built-in types
        if (fieldType.IsBuiltInTypeOrBuiltInCollection)
            return true;

        // Skip well-known service types
        if (IsWellKnownServiceType(fieldType))
            return true;

        // Check if always resolvable (well-known types)
        if (fieldType is INamedTypeSymbol namedType && IsAlwaysResolvable(namedType))
            return true;

        // Note: IEnumerable<T> is handled separately by the caller
        // as it depends on whether T is registered

        // Check if [IocInject] has a Key specified - this makes it resolvable
        var (key, _) = injectAttribute.GetKey();
        if (key is not null)
            return true;

        return false;
    }

    /// <summary>
    /// Validates that a symbol referenced by Factory or Instance via nameof() is valid.
    /// </summary>
    /// <param name="symbol">The symbol to validate.</param>
    /// <returns>A tuple indicating whether the symbol is valid and the error reason if not.</returns>
    public static (bool IsValid, string? ErrorReason) ValidateFactoryOrInstanceSymbol(ISymbol symbol)
    {
        // Check if the symbol is static
        if (!symbol.IsStatic)
        {
            return (false, "not static");
        }

        // Check accessibility - must be at least internal to be accessible
        // Private members cannot be accessed from the generated code
        switch (symbol.DeclaredAccessibility)
        {
            case Accessibility.Private:
                return (false, "private");
            case Accessibility.ProtectedAndInternal:
            case Accessibility.Protected:
                // Protected members are only accessible in derived classes
                // Since generated code is not a derived class, treat as inaccessible
                return (false, "protected and not accessible from generated code");
        }

        // Also check containing type accessibility
        var containingType = symbol.ContainingType;
        while (containingType is not null)
        {
            if (containingType.DeclaredAccessibility is Accessibility.Private)
            {
                return (false, "declared in a private type");
            }
            containingType = containingType.ContainingType;
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if a dependency type is resolvable for a parameter.
    /// A dependency is resolvable if:
    /// - It is a well-known service type (IServiceProvider, etc.)
    /// - It is IEnumerable&lt;T&gt;
    /// - It has a default value
    /// - It has special attributes ([IocInject] with key, [FromKeyedServices], [ServiceKey])
    /// </summary>
    /// <param name="param">The parameter to check.</param>
    /// <returns>True if the parameter's dependency is resolvable; otherwise, false.</returns>
    public static bool IsParameterAlwaysResolvable(IParameterSymbol param)
    {
        var paramType = param.Type;

        // Skip built-in types (handled by SGIOC015)
        if (paramType.IsBuiltInTypeOrBuiltInCollection)
            return true;

        // Skip if parameter has default value
        if (param.HasExplicitDefaultValue)
            return true;

        // Skip well-known service types
        if (IsWellKnownServiceType(paramType))
            return true;

        // Note: IEnumerable<T> is handled separately by the caller
        // as it depends on whether T is registered when ResolveIServiceCollection = false

        // Check for special attributes that make the parameter resolvable
        foreach (var attribute in param.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null)
                continue;

            var attrNamespace = attrClass.ContainingNamespace?.ToDisplayString();

            // [IocInject] or [Inject] with Key - check if it has a key
            if (attrClass.IsInject)
            {
                var (key, _) = attribute.GetKey();
                if (key is not null)
                    return true;
            }

            // [ServiceKey] - injects the registration key
            if (attrClass.Name == "ServiceKeyAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;

            // [FromKeyedServices] - MS.DI handles this automatically
            if (attrClass.Name == "FromKeyedServicesAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a property type is resolvable for injection.
    /// </summary>
    /// <param name="property">The property to check.</param>
    /// <param name="injectAttribute">The inject attribute on the property.</param>
    /// <returns>True if the property's dependency is resolvable; otherwise, false.</returns>
    public static bool IsPropertyAlwaysResolvable(IPropertySymbol property, AttributeData injectAttribute)
    {
        var propertyType = property.Type;

        // Skip built-in types
        if (propertyType.IsBuiltInTypeOrBuiltInCollection)
            return true;

        // Skip well-known service types
        if (IsWellKnownServiceType(propertyType))
            return true;

        // Check if IEnumerable<T> - always resolvable
        if (propertyType is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        // Check if [IocInject] has a Key specified - this makes it resolvable
        var (key, _) = injectAttribute.GetKey();
        if (key is not null)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if an attribute collection contains any attribute that makes a member resolvable.
    /// Used for constructor parameters to check for [IocInject] (any), [ServiceKey], or [FromKeyedServices].
    /// </summary>
    /// <param name="attributes">The attributes to check.</param>
    /// <returns>True if the member has a resolvable attribute; otherwise, false.</returns>
    public static bool HasResolvableAttribute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null)
                continue;

            var attrNamespace = attrClass.ContainingNamespace?.ToDisplayString();

            // [IocInject] or [Inject] - user explicitly handles this
            if (attrClass.IsInject)
                return true;

            // [ServiceKey] - injects the registration key
            if (attrClass.Name == "ServiceKeyAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;

            // [FromKeyedServices] - MS.DI handles this automatically
            if (attrClass.Name == "FromKeyedServicesAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;
        }

        return false;
    }
}

/// <summary>
/// Holds cached IoC attribute type symbols for efficient comparison in analyzers.
/// </summary>
internal sealed class IoCAttributeSymbols
{
    public INamedTypeSymbol? IocContainerAttribute { get; }
    public INamedTypeSymbol? IocRegisterAttribute { get; }
    public INamedTypeSymbol? IocRegisterAttribute_T1 { get; }
    public INamedTypeSymbol? IocRegisterForAttribute { get; }
    public INamedTypeSymbol? IocRegisterForAttribute_T1 { get; }
    public INamedTypeSymbol? IocRegisterDefaultsAttribute { get; }
    public INamedTypeSymbol? IocRegisterDefaultsAttribute_T1 { get; }

    public IoCAttributeSymbols(Compilation compilation)
    {
        IocContainerAttribute = compilation.GetTypeByMetadataName(Constants.IocContainerAttributeFullName);
        IocRegisterAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterAttributeFullName);
        IocRegisterAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterAttributeFullName_T1);
        IocRegisterForAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterForAttributeFullName);
        IocRegisterForAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterForAttributeFullName_T1);
        IocRegisterDefaultsAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterDefaultsAttributeFullName);
        IocRegisterDefaultsAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterDefaultsAttributeFullName_T1);
    }

    /// <summary>
    /// Checks if any IoC registration attribute is available in the compilation.
    /// </summary>
    public bool HasAnyRegistrationAttribute =>
        IocRegisterAttribute is not null
        || IocRegisterAttribute_T1 is not null
        || IocRegisterForAttribute is not null
        || IocRegisterForAttribute_T1 is not null;

    /// <summary>
    /// Checks if the IocContainerAttribute is available.
    /// </summary>
    public bool HasContainerAttribute => IocContainerAttribute is not null;
}
