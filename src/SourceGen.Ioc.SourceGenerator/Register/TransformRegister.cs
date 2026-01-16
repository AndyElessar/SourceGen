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

        return ExtractRegistrationData(typeSymbol, attributeData, ctx.SemanticModel);
    }

    /// <summary>
    /// Transforms generic IocRegisterAttribute (e.g., IocRegisterAttribute&lt;T&gt;) to extract registration data.
    /// The service types are specified via type parameters instead of constructor arguments.
    /// </summary>
    private static RegistrationData? TransformRegisterGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if(ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attributeData = ctx.Attributes.FirstOrDefault();
        if(attributeData == null)
            return null;

        return ExtractRegistrationDataFromGenericAttribute(typeSymbol, attributeData, ctx.SemanticModel);
    }

    private static IEnumerable<RegistrationData> TransformRegisterFor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            if(attr.ConstructorArguments.Length == 0)
                continue;
            if(attr.ConstructorArguments[0].Value is not INamedTypeSymbol targetType)
                continue;

            var data = ExtractRegistrationData(targetType, attr, ctx.SemanticModel);

            yield return data;
        }
    }

    /// <summary>
    /// Transforms generic IoCRegisterForAttribute (IoCRegisterForAttribute&lt;T&gt;) to extract registration data.
    /// The target type is specified via type parameter instead of constructor argument.
    /// </summary>
    private static IEnumerable<RegistrationData> TransformRegisterForGeneric(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        foreach(var attr in ctx.Attributes)
        {
            var attrClass = attr.AttributeClass;
            if(attrClass?.IsGenericType != true || attrClass.TypeArguments.Length == 0)
                continue;

            if(attrClass.TypeArguments[0] is not INamedTypeSymbol targetType)
                continue;

            // Use ExtractRegistrationData because IoCRegisterForAttribute<T> uses ServiceTypes named argument,
            // not the generic type parameter, for specifying service types.
            var data = ExtractRegistrationData(targetType, attr, ctx.SemanticModel);

            yield return data;
        }
    }

    private static RegistrationData ExtractRegistrationData(INamedTypeSymbol typeSymbol, AttributeData attributeData, SemanticModel? semanticModel = null)
    {
        var implementationType = typeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true);
        var (hasExplicitLifetime, lifetime) = attributeData.TryGetLifetime();
        var (hasExplicitRegisterAllInterfaces, registerAllInterfaces) = attributeData.TryGetRegisterAllInterfaces();
        var (hasExplicitRegisterAllBaseClasses, registerAllBaseClasses) = attributeData.TryGetRegisterAllBaseClasses();
        var serviceTypes = attributeData.GetServiceTypes();
        var decorators = attributeData.GetDecorators();
        var tags = attributeData.GetTags();
        var tagOnly = attributeData.GetTagOnly();
        var (key, keyType) = attributeData.GetKey(semanticModel);
        var instance = attributeData.GetInstance(semanticModel);

        // Get factory method data with parameter information
        FactoryMethodData? factory = null;
        if(semanticModel is not null)
        {
            factory = attributeData.GetFactoryMethodData(semanticModel);
        }

        // Extract injection members (properties, fields, methods marked with IocInjectAttribute/InjectAttribute)
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
            tagOnly,
            injectionMembers,
            factory,
            instance);
    }

    /// <summary>
    /// Extracts registration data from a generic attribute (e.g., IocRegisterAttribute&lt;T&gt;, IocRegisterForAttribute&lt;T&gt;).
    /// The service types are specified via type parameters instead of constructor arguments or named arguments.
    /// </summary>
    private static RegistrationData ExtractRegistrationDataFromGenericAttribute(
        INamedTypeSymbol typeSymbol,
        AttributeData attributeData,
        SemanticModel? semanticModel = null)
    {
        var implementationType = typeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true);
        var (hasExplicitLifetime, lifetime) = attributeData.TryGetLifetime();
        var (hasExplicitRegisterAllInterfaces, registerAllInterfaces) = attributeData.TryGetRegisterAllInterfaces();
        var (hasExplicitRegisterAllBaseClasses, registerAllBaseClasses) = attributeData.TryGetRegisterAllBaseClasses();

        // Extract service types from generic type arguments instead of named argument
        var serviceTypes = attributeData.GetServiceTypesFromGenericAttribute();

        var decorators = attributeData.GetDecorators();
        var tags = attributeData.GetTags();
        var tagOnly = attributeData.GetTagOnly();
        var (key, keyType) = attributeData.GetKey(semanticModel);
        var instance = attributeData.GetInstance(semanticModel);

        // Get factory method data with parameter information
        FactoryMethodData? factory = null;
        if(semanticModel is not null)
        {
            factory = attributeData.GetFactoryMethodData(semanticModel);
        }

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
            tagOnly,
            injectionMembers,
            factory,
            instance);
    }

    /// <summary>
    /// Extracts injection members (properties, fields, methods) marked with IocInjectAttribute/InjectAttribute.
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

            // Check if the member has IocInjectAttribute/InjectAttribute (by name only)
            var injectAttribute = member.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.IsInject == true);

            if(injectAttribute is null)
                continue;

            // Extract key information from IocInjectAttribute/InjectAttribute
            var (key, _) = injectAttribute.GetKey();

            InjectionMemberData? memberData = member switch
            {
                IPropertySymbol property when property.SetMethod is not null =>
                    CreatePropertyInjection(property, key),

                IFieldSymbol field when !field.IsReadOnly =>
                    CreateFieldInjection(field, key),

                IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                    && method.ReturnsVoid
                    && !method.IsGenericMethod =>
                    CreateMethodInjection(method, key),

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
    private static InjectionMemberData CreatePropertyInjection(IPropertySymbol property, string? key)
    {
        var propertyType = property.Type.GetTypeData();
        var isNullable = property.NullableAnnotation == NullableAnnotation.Annotated;

        // Try to get the default value from property initializer
        var (hasDefaultValue, defaultValue) = GetPropertyDefaultValue(property);

        return new InjectionMemberData(
            InjectionMemberType.Property,
            property.Name,
            propertyType,
            null,
            key,
            isNullable,
            hasDefaultValue,
            defaultValue);
    }

    /// <summary>
    /// Creates injection data for a field.
    /// </summary>
    private static InjectionMemberData CreateFieldInjection(IFieldSymbol field, string? key)
    {
        var fieldType = field.Type.GetTypeData();
        var isNullable = field.NullableAnnotation == NullableAnnotation.Annotated;

        // Try to get the default value from field initializer
        var (hasDefaultValue, defaultValue) = GetFieldDefaultValue(field);

        return new InjectionMemberData(
            InjectionMemberType.Field,
            field.Name,
            fieldType,
            null,
            key,
            isNullable,
            hasDefaultValue,
            defaultValue);
    }

    /// <summary>
    /// Gets the default value from a property initializer.
    /// </summary>
    private static (bool HasDefaultValue, string? DefaultValue) GetPropertyDefaultValue(IPropertySymbol property)
    {
        var syntaxRef = property.DeclaringSyntaxReferences.FirstOrDefault();
        if(syntaxRef?.GetSyntax() is not PropertyDeclarationSyntax propertySyntax)
            return (false, null);

        var initializer = propertySyntax.Initializer;
        if(initializer is null)
            return (false, null);

        // Check if it's a null literal or null-forgiving expression (null!)
        if(IsNullExpression(initializer.Value))
        {
            return (true, null);
        }

        return (true, initializer.Value.ToString());
    }

    /// <summary>
    /// Gets the default value from a field initializer.
    /// </summary>
    private static (bool HasDefaultValue, string? DefaultValue) GetFieldDefaultValue(IFieldSymbol field)
    {
        var syntaxRef = field.DeclaringSyntaxReferences.FirstOrDefault();
        var syntax = syntaxRef?.GetSyntax();

        // Field can be declared in VariableDeclaratorSyntax
        EqualsValueClauseSyntax? initializer = syntax switch
        {
            VariableDeclaratorSyntax variableDeclarator => variableDeclarator.Initializer,
            _ => null
        };

        if(initializer is null)
            return (false, null);

        // Check if it's a null literal or null-forgiving expression (null!)
        if(IsNullExpression(initializer.Value))
        {
            return (true, null);
        }

        return (true, initializer.Value.ToString());
    }

    /// <summary>
    /// Checks if an expression is a null literal or null-forgiving expression (null!).
    /// </summary>
    private static bool IsNullExpression(ExpressionSyntax expression)
    {
        // Direct null literal
        if(expression is LiteralExpressionSyntax literal &&
           literal.Kind() == SyntaxKind.NullLiteralExpression)
        {
            return true;
        }

        // Null-forgiving expression: null!
        if(expression is PostfixUnaryExpressionSyntax postfix &&
           postfix.Kind() == SyntaxKind.SuppressNullableWarningExpression &&
           postfix.Operand is LiteralExpressionSyntax innerLiteral &&
           innerLiteral.Kind() == SyntaxKind.NullLiteralExpression)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates injection data for a method.
    /// </summary>
    private static InjectionMemberData CreateMethodInjection(IMethodSymbol method, string? key)
    {
        var parameters = method.Parameters
            .Select(p =>
            {
                var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = p.GetServiceKeyAndAttributeInfo();
                return new ParameterData(
                    p.Name,
                    p.Type.GetTypeData(),
                    IsNullable: p.NullableAnnotation == NullableAnnotation.Annotated,
                    HasDefaultValue: p.HasExplicitDefaultValue,
                    DefaultValue: p.HasExplicitDefaultValue ? ToDefaultValueCodeString(p.ExplicitDefaultValue) : null,
                    ServiceKey: serviceKey,
                    HasInjectAttribute: hasInjectAttribute,
                    HasServiceKeyAttribute: hasServiceKeyAttribute,
                    HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute);
            })
            .ToImmutableEquatableArray();

        return new InjectionMemberData(
            InjectionMemberType.Method,
            method.Name,
            null,
            parameters,
            key);
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
