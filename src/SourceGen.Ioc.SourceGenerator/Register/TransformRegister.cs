namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    private static RegistrationData? TransformRegister(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if(ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attributeData = ctx.Attributes.FirstOrDefault();
        if(attributeData == null)
            return null;

        return ExtractRegistrationData(typeSymbol, attributeData);
    }

    private static IEnumerable<RegistrationData> TransformRegisterFor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            if(attr.ConstructorArguments.Length == 0)
                continue;
            if(attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType)
                continue;

            var data = ExtractRegistrationData(targetType, attr);

            yield return data;
        }
    }

    private static RegistrationData ExtractRegistrationData(INamedTypeSymbol typeSymbol, AttributeData attributeData)
    {
        var implementationType = typeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true);
        var (hasExplicitLifetime, lifetime) = attributeData.TryGetLifetime();
        var (hasExplicitRegisterAllInterfaces, registerAllInterfaces) = attributeData.TryGetRegisterAllInterfaces();
        var (hasExplicitRegisterAllBaseClasses, registerAllBaseClasses) = attributeData.TryGetRegisterAllBaseClasses();
        var serviceTypes = attributeData.GetServiceTypes();
        var decorators = attributeData.GetDecorators();
        var tags = attributeData.GetTags();
        var excludeFromDefault = attributeData.GetExcludeFromDefault();
        var (key, keyType) = attributeData.GetKey();

        // Extract injection members (properties, fields, methods marked with InjectAttribute)
        var injectionMembers = ExtractInjectionMembers(typeSymbol);

        // Build set of valid open generic service types (non-nested) for quick lookup
        var validOpenGenericServiceTypes = BuildValidOpenGenericServiceTypes(
            implementationType.AllInterfaces ?? [],
            implementationType.AllBaseClasses ?? []);

        return new RegistrationData(
            implementationType,
            lifetime,
            registerAllInterfaces,
            registerAllBaseClasses,
            serviceTypes,
            key,
            keyType,
            hasExplicitLifetime,
            hasExplicitRegisterAllInterfaces,
            hasExplicitRegisterAllBaseClasses,
            validOpenGenericServiceTypes,
            decorators,
            tags,
            excludeFromDefault,
            injectionMembers);
    }

    /// <summary>
    /// Extracts injection members (properties, fields, methods) marked with InjectAttribute.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to extract injection members from.</param>
    /// <returns>An array of injection member data.</returns>
    private static ImmutableEquatableArray<InjectionMemberData> ExtractInjectionMembers(INamedTypeSymbol typeSymbol)
    {
        List<InjectionMemberData>? injectionMembers = null;

        foreach(var member in typeSymbol.GetMembers())
        {
            // Skip static members
            if(member.IsStatic)
                continue;

            // Check if the member has InjectAttribute (by name only)
            var injectAttribute = member.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "InjectAttribute");

            if(injectAttribute is null)
                continue;

            // Extract key information from InjectAttribute
            var (key, keyType) = injectAttribute.GetKey();

            InjectionMemberData? memberData = member switch
            {
                IPropertySymbol property when property.SetMethod is not null =>
                    CreatePropertyInjection(property, key, keyType),

                IFieldSymbol field when !field.IsReadOnly =>
                    CreateFieldInjection(field, key, keyType),

                IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                    && method.ReturnsVoid
                    && !method.IsGenericMethod =>
                    CreateMethodInjection(method, key, keyType),

                _ => null
            };

            if(memberData is not null)
            {
                injectionMembers ??= [];
                injectionMembers.Add(memberData);
            }
        }

        return injectionMembers?.ToImmutableEquatableArray() ?? [];
    }

    /// <summary>
    /// Creates injection data for a property.
    /// </summary>
    private static InjectionMemberData CreatePropertyInjection(IPropertySymbol property, string? key, int keyType)
    {
        var propertyType = property.Type.GetTypeData();
        return new InjectionMemberData(
            InjectionMemberType.Property,
            property.Name,
            propertyType,
            null,
            key,
            keyType);
    }

    /// <summary>
    /// Creates injection data for a field.
    /// </summary>
    private static InjectionMemberData CreateFieldInjection(IFieldSymbol field, string? key, int keyType)
    {
        var fieldType = field.Type.GetTypeData();
        return new InjectionMemberData(
            InjectionMemberType.Field,
            field.Name,
            fieldType,
            null,
            key,
            keyType);
    }

    /// <summary>
    /// Creates injection data for a method.
    /// </summary>
    private static InjectionMemberData CreateMethodInjection(IMethodSymbol method, string? key, int keyType)
    {
        var parameters = method.Parameters
            .Select(p => new ConstructorParameterData(
                p.Name,
                p.Type.GetTypeData(),
                IsOptional: p.HasExplicitDefaultValue || p.NullableAnnotation == NullableAnnotation.Annotated))
            .ToImmutableEquatableArray();

        return new InjectionMemberData(
            InjectionMemberType.Method,
            method.Name,
            null,
            parameters,
            key,
            keyType);
    }

    /// <summary>
    /// Builds a set of valid open generic service type names (NameWithoutGeneric + Arity) that can be properly registered.
    /// This includes only interfaces/base classes that are open generic but not nested open generic.
    /// </summary>
    private static ImmutableEquatableSet<string> BuildValidOpenGenericServiceTypes(
        ImmutableEquatableArray<TypeData> allInterfaces,
        ImmutableEquatableArray<TypeData> allBaseClasses)
    {
        var result = new HashSet<string>();

        foreach(var iface in allInterfaces)
        {
            if(iface.IsOpenGeneric && !iface.IsNestedOpenGeneric)
            {
                // Use NameWithoutGeneric + Arity as key to handle different arities
                result.Add($"{iface.NameWithoutGeneric}`{iface.GenericArity}");
            }
        }

        foreach(var baseClass in allBaseClasses)
        {
            if(baseClass.IsOpenGeneric && !baseClass.IsNestedOpenGeneric)
            {
                result.Add($"{baseClass.NameWithoutGeneric}`{baseClass.GenericArity}");
            }
        }

        return result.ToImmutableEquatableSet();
    }
}
