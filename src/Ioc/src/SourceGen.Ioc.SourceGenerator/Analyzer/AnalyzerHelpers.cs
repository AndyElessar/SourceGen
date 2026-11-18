namespace SourceGen.Ioc;

/// <summary>
/// Shared helper methods for IoC analyzers.
/// </summary>
internal static class AnalyzerHelpers
{
    /// <summary>
    /// Checks if the type is a well-known service type that can always be resolved:
    /// <c>System.IServiceProvider</c>, or <c>IServiceScopeFactory</c>, <c>IServiceScope</c> or <c>IKeyedServiceProvider</c>
    /// from <c>Microsoft.Extensions.DependencyInjection</c>. These types are provided by the runtime and do not need explicit registration.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to check.</param>
    /// <returns>True if the type is a well-known service type; otherwise, false.</returns>
    public static bool IsWellKnownServiceType(ITypeSymbol typeSymbol)
        => typeSymbol is INamedTypeSymbol { Arity: 0, ContainingType: null } namedType
            && namedType.Name switch
            {
                "IServiceProvider" => IsSystemNamespace(namedType.ContainingNamespace),
                "IServiceScopeFactory" or "IServiceScope" or "IKeyedServiceProvider" => IsInMicrosoftExtensionsDependencyInjection(namedType),
                _ => false
            };

    /// <summary>
    /// Checks if the attribute class is <c>Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute</c>.
    /// </summary>
    public static bool IsServiceKeyAttribute(INamedTypeSymbol? attributeClass)
        => attributeClass is { Name: "ServiceKeyAttribute" } && IsInMicrosoftExtensionsDependencyInjection(attributeClass);

    /// <summary>
    /// Checks if the attribute class is <c>Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute</c>.
    /// </summary>
    public static bool IsFromKeyedServicesAttribute(INamedTypeSymbol? attributeClass)
        => attributeClass is { Name: "FromKeyedServicesAttribute" } && IsInMicrosoftExtensionsDependencyInjection(attributeClass);

    /// <summary>
    /// Checks if the attribute class is <c>SourceGen.Ioc.IocGenericFactoryAttribute</c>.
    /// </summary>
    public static bool IsIocGenericFactoryAttribute(INamedTypeSymbol? attributeClass)
        => attributeClass is
        {
            Name: "IocGenericFactoryAttribute",
            Arity: 0,
            ContainingType: null,
            ContainingNamespace: { Name: "Ioc", ContainingNamespace: { Name: "SourceGen", ContainingNamespace.IsGlobalNamespace: true } }
        };

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
    /// Returns <see langword="true"/> when one of <paramref name="decorators"/> that the generator applies to
    /// <paramref name="serviceType"/> has an async inject method, which makes the decorated service type async-init even when
    /// its implementation is not. Decorators are matched with the generator's own rules (implemented service type, closed
    /// generic constraints), and only async decorators are converted for matching.
    /// </summary>
    /// <param name="implementationType">The implementation type; a self-registration is never decorated.</param>
    /// <param name="assembly">The assembly being compiled, which selects the decorator constructors like the generator.</param>
    public static bool HasAsyncDecorator(
        INamedTypeSymbol implementationType,
        INamedTypeSymbol serviceType,
        ImmutableArray<INamedTypeSymbol> decorators,
        IocFeatures features,
        IAssemblySymbol assembly)
    {
        if((features & IocFeatures.AsyncMethodInject) == 0
            || decorators.IsDefaultOrEmpty
            || SymbolEqualityComparer.Default.Equals(implementationType, serviceType))
        {
            return false;
        }

        TypeData? serviceTypeData = null;
        foreach(var decorator in decorators)
        {
            if(!IsAsyncInitImplementation(decorator, features))
                continue;

            serviceTypeData ??= serviceType.GetTypeData();
            if(IocSourceGenerator.TryCloseDecorator(decorator.GetTypeData(extractConstructorParams: true, extractHierarchy: true, constructedIn: assembly), serviceTypeData) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// <see cref="HasAsyncDecorator(INamedTypeSymbol, INamedTypeSymbol, ImmutableArray{INamedTypeSymbol}, IocFeatures, IAssemblySymbol)"/>
    /// for decorators already extracted by <c>GetDecorators</c> (with injection members and hierarchy), such as the
    /// decorators of default settings.
    /// </summary>
    public static bool HasAsyncDecorator(
        INamedTypeSymbol implementationType,
        INamedTypeSymbol serviceType,
        ImmutableEquatableArray<TypeData> decorators,
        IocFeatures features)
    {
        if((features & IocFeatures.AsyncMethodInject) == 0
            || decorators.Length == 0
            || SymbolEqualityComparer.Default.Equals(implementationType, serviceType))
        {
            return false;
        }

        TypeData? serviceTypeData = null;
        foreach(var decorator in decorators)
        {
            if(decorator.InjectionMembers is not { } members || !members.Any(static m => m.MemberType == InjectionMemberType.AsyncMethod))
                continue;

            serviceTypeData ??= serviceType.GetTypeData();
            if(IocSourceGenerator.TryCloseDecorator(decorator, serviceTypeData) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the constructor parameters of <paramref name="decorator"/> that can receive the decorated instance when it decorates
    /// <paramref name="serviceType"/>: those whose type is the service type, like the generator's
    /// <c>GetDecoratedParameterIndex</c>. The generator passes the decorated instance to the first one and skips the decorator
    /// when there is none.
    /// </summary>
    /// <param name="decoratorTypeData">The decorator's type data with constructor parameters and hierarchy.</param>
    /// <returns>
    /// <see langword="false"/> when the decorator does not decorate the service type (it does not implement it, or its
    /// constraints are not satisfied); otherwise <see langword="true"/> with the parameter indexes, possibly empty.
    /// </returns>
    public static bool TryGetDecoratedParameterIndexes(
        TypeData decoratorTypeData,
        TypeData serviceType,
        out ImmutableArray<int> parameterIndexes)
    {
        parameterIndexes = [];
        if(IocSourceGenerator.TryCloseDecoratorForServiceType(decoratorTypeData, serviceType) is not { } closed)
            return false;

        var parameters = closed.ConstructorParameters ?? [];
        var builder = ImmutableArray.CreateBuilder<int>();
        for(var i = 0; i < parameters.Length; i++)
        {
            if(IocSourceGenerator.IsDecoratedServiceTypeName(parameters[i].Type.Name, serviceType.Name))
                builder.Add(i);
        }

        parameterIndexes = builder.ToImmutable();
        return true;
    }

    /// <summary>
    /// Gets the decorator types of a registration or default settings attribute (its <c>Decorators</c> named argument).
    /// </summary>
    public static ImmutableArray<INamedTypeSymbol> GetDecoratorSymbols(AttributeData attribute)
    {
        var decorators = attribute.GetTypeSymbolsFromNamedArgument("Decorators");
        return decorators.Length == 0 ? [] : [.. decorators];
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

            if(IsFromKeyedServicesAttribute(attrClass))
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
    /// Enumerates the service types exposed by a registration attribute for the specified implementation type.
    /// Includes self-registration plus any explicit aliases or register-all flags.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateRegisteredServiceTypes(
        INamedTypeSymbol implementationType,
        AttributeData attribute)
        => EnumerateRegisteredServiceTypes(implementationType, attribute, GetExplicitServiceTypes(attribute));

    /// <summary>
    /// Enumerates the service types exposed by a registration attribute, with its explicit service types already parsed
    /// by <see cref="GetExplicitServiceTypes"/>.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> EnumerateRegisteredServiceTypes(
        INamedTypeSymbol implementationType,
        AttributeData attribute,
        List<INamedTypeSymbol> explicitServiceTypes)
    {
        yield return implementationType;

        var attrClass = attribute.AttributeClass;
        if(attrClass is null)
            yield break;

        foreach(var serviceType in explicitServiceTypes)
            yield return serviceType;

        // An unbound implementation (typeof(Repo<>)) has no interfaces or base class of its own: they are its definition's
        var hierarchyType = implementationType.IsUnboundGenericType ? implementationType.OriginalDefinition : implementationType;

        var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        if(registerAllInterfaces)
        {
            foreach(var interfaceType in hierarchyType.AllInterfaces)
                yield return interfaceType;
        }

        var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
        if(!registerAllBaseClasses)
            yield break;

        var baseType = hierarchyType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Gets the service types listed by a registration attribute: its generic type arguments, then its <c>ServiceTypes</c>
    /// (or constructor service types).
    /// </summary>
    public static List<INamedTypeSymbol> GetExplicitServiceTypes(AttributeData attribute)
        => [.. attribute.GetServiceTypeSymbolsFromGenericAttribute(), .. attribute.GetServiceTypeSymbols()];

    /// <summary>A type name with its containing types, such as <c>Outer&lt;O&gt;.Node&lt;T&gt;</c>.</summary>
    public static readonly SymbolDisplayFormat NestedTypeDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// Returns why a generic factory type mapping cannot fill every factory method type parameter,
    /// or <see langword="null"/> when the mapping is valid. Duplicate placeholders are reported separately (SGIOC017).
    /// </summary>
    /// <param name="template">The service type template (first mapping type).</param>
    /// <param name="placeholders">The placeholder types (following mapping types).</param>
    /// <param name="typeParameterCount">The factory method's type parameter count.</param>
    public static string? GetGenericFactoryMappingIssue(
        ITypeSymbol? template,
        IReadOnlyList<ITypeSymbol?> placeholders,
        int typeParameterCount)
    {
        if(template is not INamedTypeSymbol || placeholders.Count == 0)
            return "the mapping must specify a service type template followed by at least one placeholder type";

        if(placeholders.Count != typeParameterCount)
            return $"the mapping specifies {placeholders.Count} placeholder type(s) but the method has {typeParameterCount} type parameter(s)";

        // Each placeholder must appear at least once to determine its type argument; a repeated one takes the same type
        // argument at each position, which the generator checks per service type (SGIOC039)
        foreach(var placeholder in placeholders)
        {
            // Like the generator, only types without type arguments of their own are placeholders: the template is matched
            // into the type arguments of List<int>, int?, (int, string) and int[], so they are never found
            if(placeholder is IArrayTypeSymbol or INamedTypeSymbol { TypeArguments.Length: > 0 })
            {
                return $"placeholder type '{placeholder.ToDisplayString(NestedTypeDisplayFormat)}' has type arguments or is an array, so it cannot be a placeholder";
            }

            if(placeholder is not null && !ContainsType(template, placeholder))
            {
                // Containing types are shown, as a placeholder only in their type arguments (Outer<int>.Inner) is not found
                return $"placeholder type '{placeholder.ToDisplayString(NestedTypeDisplayFormat)}' does not appear in the service type template '{template.ToDisplayString(NestedTypeDisplayFormat)}'";
            }
        }

        return null;

        static bool ContainsType(ITypeSymbol type, ITypeSymbol target)
        {
            if(SymbolEqualityComparer.Default.Equals(type, target))
                return true;

            return type switch
            {
                INamedTypeSymbol named => named.TypeArguments.Any(arg => ContainsType(arg, target)),
                IArrayTypeSymbol array => ContainsType(array.ElementType, target),
                _ => false
            };
        }
    }

    /// <summary>
    /// Returns why an explicit service type is skipped by the generator for the implementation type,
    /// or <see langword="null"/> when the service type can be registered.
    /// Mirrors the generator's open generic service type validation.
    /// </summary>
    public static string? GetServiceTypeIncompatibilityReason(INamedTypeSymbol implementationType, INamedTypeSymbol serviceType)
    {
        var isOpenImplementation = IsOpenGenericType(implementationType);
        var isOpenServiceType = IsOpenGenericType(serviceType);

        if(isOpenImplementation && !isOpenServiceType)
            return "an open generic implementation requires an open generic service type";

        if(!isOpenServiceType)
            return null;

        var implementationDefinition = implementationType.OriginalDefinition;
        var serviceDefinition = serviceType.OriginalDefinition;

        // A closed implementation is registered as each closed form of the open service type it implements or inherits
        if(!isOpenImplementation)
        {
            foreach(var candidate in EnumerateImplicitServiceTypes(implementationType))
            {
                if(SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, serviceDefinition))
                    return null;
            }

            return "the closed implementation does not implement or inherit any form of the open generic service type";
        }
        if(SymbolEqualityComparer.Default.Equals(implementationDefinition, serviceDefinition))
            return null;

        // Any implemented form is enough: when type parameters are reordered or partly closed, only the open
        // generic registration is skipped, and discovered closed service types are still mapped and registered.
        foreach(var candidate in EnumerateImplicitServiceTypes(implementationDefinition))
        {
            if(SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, serviceDefinition))
                return null;
        }

        return "the implementation does not implement or inherit it";
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
            && IsSystemThreadingTasksNamespace(named.ContainingNamespace);

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

        return type is INamedTypeSymbol { Name: "ValueTask", Arity: 0 } named
            && IsSystemThreadingTasksNamespace(named.ContainingNamespace);
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
        => RoslynExtensions.GetFactoryOrInstanceIssue(symbol) is { } issue ? (false, issue) : (true, null);

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

            // [IocInject] or [Inject] with Key - check if it has a key
            if(attrClass.IsInject)
            {
                var (key, _, _) = attribute.GetKeyInfo();
                if(key is not null)
                    return true;
            }

            // [ServiceKey] - injects the registration key
            // [FromKeyedServices] - MS.DI handles this automatically
            if(IsServiceKeyAttribute(attrClass) || IsFromKeyedServicesAttribute(attrClass))
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

            // [IocInject] or [Inject] - user explicitly handles this
            // [ServiceKey] - injects the registration key
            // [FromKeyedServices] - MS.DI handles this automatically
            if(attrClass.IsInject || IsServiceKeyAttribute(attrClass) || IsFromKeyedServicesAttribute(attrClass))
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

    /// <summary>
    /// Enumerates the open generic <c>[IocRegisterFor]</c> registrations of a module and, recursively, of the modules it
    /// imports, like the generator's <c>ExtractOpenGenericEntriesFromModuleRecursive</c>: each implementation type
    /// definition with the open generic service types it exposes (explicit <c>ServiceTypes</c> and interfaces).
    /// </summary>
    /// <param name="visited">The modules already enumerated, shared across calls to enumerate each module once.</param>
    public static IEnumerable<(AttributeData Attribute, INamedTypeSymbol ImplementationType, HashSet<INamedTypeSymbol> ServiceTypes)> EnumerateImportedOpenRegistrations(
        INamedTypeSymbol moduleType,
        IocAttributeSymbols attributeSymbols,
        HashSet<INamedTypeSymbol> visited,
        CancellationToken cancellationToken)
    {
        if(!visited.Add(moduleType))
            yield break;

        foreach(var attribute in moduleType.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(attribute.AttributeClass is not { } attrClass)
                continue;

            if(IsIocImportModuleAttribute(attrClass, attributeSymbols))
            {
                if(attribute.GetImportedModuleType() is { } importedModule)
                {
                    foreach(var registration in EnumerateImportedOpenRegistrations(importedModule, attributeSymbols, visited, cancellationToken))
                        yield return registration;
                }

                continue;
            }

            if(!IsIoCRegisterForAttribute(attrClass, attributeSymbols)
                || attribute.GetTargetTypeFromRegisterForAttribute() is not { IsOpenNestedInGenericType: false } targetType
                || !IsOpenGenericType(targetType))
            {
                continue;
            }

            var implementationType = targetType.OriginalDefinition;
            var explicitServiceTypes = GetExplicitServiceTypes(attribute);
            var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach(var serviceType in explicitServiceTypes.Count > 0
                         ? explicitServiceTypes.Concat(implementationType.AllInterfaces)
                         : implementationType.AllInterfaces)
            {
                if(IsOpenGenericType(serviceType))
                    serviceTypes.Add(serviceType.OriginalDefinition);
            }

            if(serviceTypes.Count > 0)
                yield return (attribute, implementationType, serviceTypes);
        }
    }

    /// <summary>
    /// Gets the index of each syntax tree of the compilation, which orders attributes by source like the generator's
    /// attribute pipelines (see <see cref="GetSourceOrder"/>).
    /// </summary>
    public static Dictionary<SyntaxTree, int> GetSyntaxTreeOrder(Compilation compilation)
    {
        var treeOrder = new Dictionary<SyntaxTree, int>();
        var index = 0;
        foreach(var tree in compilation.SyntaxTrees)
            treeOrder[tree] = index++;

        return treeOrder;
    }

    /// <summary>
    /// Gets the position of a registration attribute's pipeline in the generator's registration order. The generator
    /// concatenates its registration pipelines (<c>IocSourceGenerator.Initialize</c>, <c>CollectAndConcat</c>) in this order:
    /// <c>[IocRegister]</c> (0), <c>[IocRegister&lt;T&gt;]</c> (1), <c>[IocRegisterFor]</c> (2), <c>[IocRegisterFor&lt;T&gt;]</c>
    /// (3), then the <c>ImplementationTypes</c> of default settings (4); each pipeline is in source order (see
    /// <see cref="GetSourceOrder"/>). The last registration of a (service type, key) in this order is the one resolved.
    /// </summary>
    public static int GetGeneratorRegistrationPipeline(INamedTypeSymbol attributeClass, IocAttributeSymbols attributeSymbols)
        => (IsIoCRegisterAttribute(attributeClass, attributeSymbols) ? 0 : 2) + (attributeClass.IsGenericType ? 1 : 0);

    /// <summary>Gets the source position of an attribute; an attribute that is not in the compilation comes last.</summary>
    public static (int Tree, int Position) GetSourceOrder(SyntaxReference? syntax, Dictionary<SyntaxTree, int> treeOrder)
    {
        if(syntax is null)
            return (int.MaxValue, int.MaxValue);

        return (treeOrder.TryGetValue(syntax.SyntaxTree, out var index) ? index : int.MaxValue, syntax.Span.Start);
    }

    /// <summary>
    /// Enumerates the <c>[IocRegisterDefaults]</c> attributes of the modules imported with <c>[IocImportModule]</c>, in the
    /// order the generator appends their settings after those of the compiled assembly (<c>ExtractDefaultSettingsFromModule</c>):
    /// the imports in source order (non-generic attributes, then generic ones), and per module the settings on the module
    /// type, then the assembly-level settings of its assembly. A module of the compiled assembly is skipped: its settings are
    /// already among those of the compiled assembly, which come first.
    /// </summary>
    public static IEnumerable<AttributeData> EnumerateImportedDefaultSettingsAttributes(
        IEnumerable<AttributeData> importAttributes,
        IAssemblySymbol assembly,
        Dictionary<SyntaxTree, int> treeOrder,
        IocAttributeSymbols attributeSymbols,
        CancellationToken cancellationToken)
    {
        foreach(var import in importAttributes
                     .OrderBy(static attribute => attribute.AttributeClass?.IsGenericType == true)
                     .ThenBy(attribute => GetSourceOrder(attribute.ApplicationSyntaxReference, treeOrder)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(import.GetImportedModuleType() is not { } moduleType)
                continue;

            moduleType = moduleType.IsGenericType ? moduleType.OriginalDefinition : moduleType;
            var moduleAssembly = moduleType.ContainingAssembly;
            if(moduleAssembly is null || SymbolEqualityComparer.Default.Equals(moduleAssembly, assembly))
                continue;

            foreach(var attribute in moduleType.GetAttributes().Concat(moduleAssembly.GetAttributes()))
            {
                if(attribute.AttributeClass is { } attrClass && IsIoCRegisterDefaultsAttribute(attrClass, attributeSymbols))
                    yield return attribute;
            }
        }
    }

    /// <summary>
    /// Gets whether the type is an open generic type: unbound (<c>typeof(Repo&lt;&gt;)</c>) or with type parameters as
    /// type arguments.
    /// </summary>
    public static bool IsOpenGenericType(INamedTypeSymbol type)
        => type.IsUnboundGenericType
            || (type.IsGenericType && type.TypeArguments.Any(static arg => arg.TypeKind == TypeKind.TypeParameter));
}

