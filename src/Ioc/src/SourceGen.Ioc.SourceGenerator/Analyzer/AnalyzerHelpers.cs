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
    /// Note: IEnumerable&lt;T&gt; is NOT always resolvable when IntegrateServiceProvider = false,
    /// as T must be registered to have any elements.
    /// </summary>
    /// <param name="serviceType">The service type to check.</param>
    /// <returns>True if the service is always resolvable; otherwise, false.</returns>
    public static bool IsAlwaysResolvable(INamedTypeSymbol serviceType)
    {
        // Well-known service types
        if(IsWellKnownServiceType(serviceType))
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
        if(!IsIEnumerableOfT(enumerableType))
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
        if(attributeClass is null || targetSymbol is null)
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
    public static bool IsIoCRegistrationAttribute(INamedTypeSymbol attributeClass, IocAttributeSymbols attributeSymbols)
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
    public static bool IsIoCRegisterForAttribute(INamedTypeSymbol attributeClass, IocAttributeSymbols attributeSymbols)
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
    public static bool IsIoCRegisterAttribute(INamedTypeSymbol attributeClass, IocAttributeSymbols attributeSymbols)
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
    public static bool IsIoCRegisterDefaultsAttribute(INamedTypeSymbol attributeClass, IocAttributeSymbols attributeSymbols)
    {
        return IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterDefaultsAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocRegisterDefaultsAttribute_T1);
    }

    /// <summary>
    /// Checks if an attribute class is any IocImportModuleAttribute variant.
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is an IocImportModuleAttribute.</returns>
    public static bool IsIocImportModuleAttribute(INamedTypeSymbol? attributeClass, IocAttributeSymbols attributeSymbols)
    {
        if(attributeClass is null)
            return false;

        return IsAttributeMatch(attributeClass, attributeSymbols.IocImportModuleAttribute)
            || IsAttributeMatch(attributeClass, attributeSymbols.IocImportModuleAttribute_T1);
    }

    /// <summary>
    /// Checks if an attribute class matches any IoC attribute variant (IoCRegister, IoCRegisterFor, or IoCRegisterDefaults).
    /// </summary>
    /// <param name="attributeClass">The attribute class to check.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <returns>True if the attribute is any IoC attribute.</returns>
    public static bool IsAnyIoCAttribute(INamedTypeSymbol attributeClass, IocAttributeSymbols attributeSymbols)
    {
        return IsIoCRegistrationAttribute(attributeClass, attributeSymbols)
            || IsIoCRegisterDefaultsAttribute(attributeClass, attributeSymbols);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the implementation contains at least one async inject method.
    /// Mirrors the generator's async-init classification by looking for instance ordinary methods
    /// marked with [IocInject]/[Inject] that return non-generic Task.
    /// </summary>
    public static bool IsAsyncInitImplementation(INamedTypeSymbol implType, IocFeatures features)
    {
        if((features & IocFeatures.AsyncMethodInject) == 0)
            return false;

        var typeToInspect = implType.IsUnboundGenericType ? implType.OriginalDefinition : implType;

        foreach(var member in typeToInspect.GetMembers())
        {
            if(member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false, IsGenericMethod: false } method)
                continue;

            if(!IsNonGenericTaskType(method.ReturnType))
                continue;

            if(method.GetAttributes().Any(static attr => attr.AttributeClass?.IsInject == true))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the keyed service key from a member attribute.
    /// [FromKeyedServices] takes precedence over [IocInject]/[Inject].
    /// </summary>
    public static string? GetServiceKeyFromMember(ISymbol member)
    {
        string? serviceKey = null;

        foreach(var attribute in member.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            if(attrClass.Name == "FromKeyedServicesAttribute"
                && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
            {
                if(attribute.ConstructorArguments.Length > 0)
                {
                    var keyArg = attribute.ConstructorArguments[0];
                    if(!keyArg.IsNull && keyArg.Value is not null)
                        return keyArg.GetPrimitiveConstantString();
                }

                return null;
            }

            if(attrClass.IsInject && serviceKey is null)
            {
                var (key, _, _) = attribute.GetKeyInfo();
                serviceKey = key;
            }
        }

        return serviceKey;
    }

    /// <summary>
    /// Comparer for (service type, key) tuples that uses symbol equality for the type component.
    /// </summary>
    public static IEqualityComparer<(INamedTypeSymbol ServiceType, string? Key)> ServiceTypeAndKeyComparer { get; }
        = new ServiceTypeAndKeySymbolComparer();

    private sealed class ServiceTypeAndKeySymbolComparer : IEqualityComparer<(INamedTypeSymbol ServiceType, string? Key)>
    {
        public bool Equals((INamedTypeSymbol ServiceType, string? Key) x, (INamedTypeSymbol ServiceType, string? Key) y)
            => SymbolEqualityComparer.Default.Equals(x.ServiceType, y.ServiceType)
                && StringComparer.Ordinal.Equals(x.Key, y.Key);

        public int GetHashCode((INamedTypeSymbol ServiceType, string? Key) obj)
            => unchecked((SymbolEqualityComparer.Default.GetHashCode(obj.ServiceType) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Key ?? string.Empty));
    }

    /// <summary>
    /// Enumerates the service types exposed by a registration attribute for the specified implementation type.
    /// Includes self-registration plus any explicit aliases or register-all flags.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateRegisteredServiceTypes(
        INamedTypeSymbol implementationType,
        AttributeData attribute)
    {
        yield return implementationType;

        var attrClass = attribute.AttributeClass;
        if(attrClass is null)
            yield break;

        foreach(var serviceType in attribute.GetServiceTypeSymbolsFromGenericAttribute())
            yield return serviceType;

        foreach(var serviceType in attribute.GetServiceTypeSymbols())
            yield return serviceType;

        var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        if(registerAllInterfaces)
        {
            foreach(var interfaceType in implementationType.AllInterfaces)
                yield return interfaceType;
        }

        var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
        if(!registerAllBaseClasses)
            yield break;

        var baseType = implementationType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Enumerates the implicit service aliases used by the analyzers for an implementation type.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateImplicitServiceTypes(INamedTypeSymbol implementationType)
    {
        yield return implementationType;

        foreach(var interfaceType in implementationType.AllInterfaces)
            yield return interfaceType;

        var baseType = implementationType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Enumerates all assembly-level [IocRegisterFor] / [IocRegisterFor&lt;T&gt;] attributes
    /// and yields (attribute, targetType) tuples. Does not perform abstract/private validation.
    /// </summary>
    /// <param name="compilation">The compilation to query.</param>
    /// <param name="attributeSymbols">The attribute symbols context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An enumerable of (AttributeData, INamedTypeSymbol) tuples.</returns>
    public static IEnumerable<(AttributeData Attribute, INamedTypeSymbol TargetType)> EnumerateAssemblyLevelRegisterForAttributes(
        Compilation compilation,
        IocAttributeSymbols attributeSymbols,
        CancellationToken cancellationToken)
    {
        if(attributeSymbols.IocRegisterForAttribute is null && attributeSymbols.IocRegisterForAttribute_T1 is null)
            yield break;

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            if(!IsIoCRegisterForAttribute(attrClass, attributeSymbols))
                continue;

            var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
            if(targetType is null)
                continue;

            yield return (attribute, targetType);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is the non-generic
    /// <see cref="System.Threading.Tasks.Task"/> class.
    /// </summary>
    public static bool IsNonGenericTaskType(ITypeSymbol? type)
        => UnwrapNullableValueType(type) is INamedTypeSymbol { Arity: 0, Name: "Task" } named
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";

    /// <summary>
    /// Unwraps Nullable&lt;T&gt; to T for value-type async wrapper analysis.
    /// </summary>
    public static ITypeSymbol? UnwrapNullableValueType(ITypeSymbol? type)
        => type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1
        } nullableType
                ? nullableType.TypeArguments[0]
                : type;

    /// <summary>
    /// Returns the single generic argument of <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>, if present.
    /// </summary>
    public static INamedTypeSymbol? TryGetAsyncWrapperElementType(ITypeSymbol? type)
    {
        type = UnwrapNullableValueType(type);

        if(type is null
            || !type.TryGetWrapperInfo(out var wrapperInfo))
            return null;

        if(wrapperInfo.Kind is not WrapperKind.Task and not WrapperKind.ValueTask)
            return null;

        return wrapperInfo.ElementType;
    }

    /// <summary>
    /// Returns true when the return type is not supported for partial accessor resolution.
    /// Unsupported: non-generic Task, non-generic ValueTask.
    /// Note: ValueTask&lt;T&gt; is handled separately via the async-init path (SGIOC029).
    /// </summary>
    public static bool IsUnsupportedPartialAccessorReturnType(ITypeSymbol type)
    {
        // Non-generic Task
        if(IsNonGenericTaskType(type))
            return true;

        if(type is not INamedTypeSymbol { Name: "ValueTask", Arity: 0 } named)
            return false;

        return named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is a direct <c>Task&lt;T&gt;</c>
    /// for the specified service type.
    /// </summary>
    public static bool IsTaskOfServiceType(ITypeSymbol? type, INamedTypeSymbol serviceType)
        => TryGetAsyncWrapperElementType(type) is { } wrappedType
            && UnwrapNullableValueType(type) is INamedTypeSymbol { Name: "Task" }
            && SymbolEqualityComparer.Default.Equals(
                wrappedType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                serviceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

    /// <summary>
    /// Checks if a field type is resolvable for injection.
    /// </summary>
    /// <param name="field">The field to check.</param>
    /// <param name="injectAttribute">The inject attribute on the field.</param>
    /// <returns>True if the field's dependency is resolvable; otherwise, false.</returns>
    public static bool IsFieldAlwaysResolvable(IFieldSymbol field, AttributeData injectAttribute)
    {
        var fieldType = field.Type;

        // Skip well-known service types
        if(IsWellKnownServiceType(fieldType))
            return true;

        // Check if always resolvable (well-known types)
        if(fieldType is INamedTypeSymbol namedType && IsAlwaysResolvable(namedType))
            return true;

        // Note: IEnumerable<T> is handled separately by the caller
        // as it depends on whether T is registered

        // Check if [IocInject] has a Key specified - this makes it resolvable
        var (key, _, _) = injectAttribute.GetKeyInfo();
        if(key is not null)
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
        if(!symbol.IsStatic)
        {
            return (false, "not static");
        }

        // Check accessibility - must be at least internal to be accessible
        // Private members cannot be accessed from the generated code
        switch(symbol.DeclaredAccessibility)
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
        while(containingType is not null)
        {
            if(containingType.DeclaredAccessibility is Accessibility.Private)
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

        // Skip if parameter has default value
        if(param.HasExplicitDefaultValue)
            return true;

        // Skip well-known service types
        if(IsWellKnownServiceType(paramType))
            return true;

        // Note: IEnumerable<T> is handled separately by the caller
        // as it depends on whether T is registered when IntegrateServiceProvider = false

        // Check for special attributes that make the parameter resolvable
        foreach(var attribute in param.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            var attrNamespace = attrClass.ContainingNamespace?.ToDisplayString();

            // [IocInject] or [Inject] with Key - check if it has a key
            if(attrClass.IsInject)
            {
                var (key, _, _) = attribute.GetKeyInfo();
                if(key is not null)
                    return true;
            }

            // [ServiceKey] - injects the registration key
            if(attrClass.Name == "ServiceKeyAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;

            // [FromKeyedServices] - MS.DI handles this automatically
            if(attrClass.Name == "FromKeyedServicesAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
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

        // Skip well-known service types
        if(IsWellKnownServiceType(propertyType))
            return true;

        // Check if IEnumerable<T> - always resolvable
        if(propertyType is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        // Check if [IocInject] has a Key specified - this makes it resolvable
        var (key, _, _) = injectAttribute.GetKeyInfo();
        if(key is not null)
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
        foreach(var attribute in attributes)
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            var attrNamespace = attrClass.ContainingNamespace?.ToDisplayString();

            // [IocInject] or [Inject] - user explicitly handles this
            if(attrClass.IsInject)
                return true;

            // [ServiceKey] - injects the registration key
            if(attrClass.Name == "ServiceKeyAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;

            // [FromKeyedServices] - MS.DI handles this automatically
            if(attrClass.Name == "FromKeyedServicesAttribute" && attrNamespace == "Microsoft.Extensions.DependencyInjection")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the effective tags list for duplicate detection.
    /// Services without tags use an empty string tag for comparison.
    /// Services with tags use their actual tags.
    /// </summary>
    /// <param name="tags">The original tags enumerable.</param>
    /// <returns>The effective tags for comparison.</returns>
    public static ImmutableArray<string> GetEffectiveTags(IEnumerable<string> tags)
    {
        var tagArray = tags.ToImmutableArray();
        return tagArray.IsEmpty ? [""] : tagArray;
    }

    /// <summary>
    /// Checks if a target type is invalid for IoC registration.
    /// A type is invalid if it is private or abstract (unless it's an interface).
    /// </summary>
    /// <param name="targetType">The type to check.</param>
    /// <returns>A tuple indicating whether the type is invalid and the reason if so.</returns>
    public static (bool IsInvalid, string? Reason) GetRegistrationInvalidReason(INamedTypeSymbol targetType)
    {
        // Check if target type is private
        if(targetType.DeclaredAccessibility is Accessibility.Private)
            return (true, "private");

        // Check if target type is abstract (but not interface)
        if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
            return (true, "abstract");

        return (false, null);
    }
}

/// <summary>
/// Holds cached IoC attribute type symbols for efficient comparison in analyzers.
/// </summary>
internal sealed class IocAttributeSymbols
{
    public INamedTypeSymbol? IocContainerAttribute { get; }
    public INamedTypeSymbol? IocRegisterAttribute { get; }
    public INamedTypeSymbol? IocRegisterAttribute_T1 { get; }
    public INamedTypeSymbol? IocRegisterForAttribute { get; }
    public INamedTypeSymbol? IocRegisterForAttribute_T1 { get; }
    public INamedTypeSymbol? IocRegisterDefaultsAttribute { get; }
    public INamedTypeSymbol? IocRegisterDefaultsAttribute_T1 { get; }
    public INamedTypeSymbol? IocImportModuleAttribute { get; }
    public INamedTypeSymbol? IocImportModuleAttribute_T1 { get; }

    public IocAttributeSymbols(Compilation compilation)
    {
        IocContainerAttribute = compilation.GetTypeByMetadataName(Constants.IocContainerAttributeFullName);
        IocRegisterAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterAttributeFullName);
        IocRegisterAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterAttributeFullName_T1);
        IocRegisterForAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterForAttributeFullName);
        IocRegisterForAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterForAttributeFullName_T1);
        IocRegisterDefaultsAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterDefaultsAttributeFullName);
        IocRegisterDefaultsAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterDefaultsAttributeFullName_T1);
        IocImportModuleAttribute = compilation.GetTypeByMetadataName(Constants.IocImportModuleAttributeFullName);
        IocImportModuleAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocImportModuleAttributeFullName_T1);
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
