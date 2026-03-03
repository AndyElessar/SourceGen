namespace SourceGen.Ioc;

partial class IocSourceGenerator
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
        // Pass semanticModel to GetTypeData for proper nameof() expression resolution in constructor parameter keys
        var implementationType = typeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true, semanticModel: semanticModel);

        var (hasExplicitLifetime, lifetime) = attributeData.TryGetLifetime();
        var (hasExplicitRegisterAllInterfaces, registerAllInterfaces) = attributeData.TryGetRegisterAllInterfaces();
        var (hasExplicitRegisterAllBaseClasses, registerAllBaseClasses) = attributeData.TryGetRegisterAllBaseClasses();
        var serviceTypes = attributeData.GetServiceTypes();
        var decorators = attributeData.GetDecorators();
        var tags = attributeData.GetTags();
        var (key, keyType, keyValueTypeSymbol) = attributeData.GetKeyInfo(semanticModel);
        var keyValueType = keyValueTypeSymbol?.GetTypeData();
        var instance = attributeData.GetInstance(semanticModel);

        // Get factory method data with parameter information
        FactoryMethodData? factory = null;
        if(semanticModel is not null)
        {
            factory = attributeData.GetFactoryMethodData(semanticModel);
        }

        var injectionMembers = ExtractAndMergeInjectionMembers(typeSymbol, attributeData, semanticModel);

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
            keyValueType,
            hasExplicitLifetime,
            hasExplicitRegisterAllInterfaces,
            hasExplicitRegisterAllBaseClasses,
            validOpenGenericServiceTypes,
            decorators,
            tags,
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
        // Pass semanticModel to GetTypeData for proper nameof() expression resolution in constructor parameter keys
        var implementationType = typeSymbol.GetTypeData(extractConstructorParams: true, extractHierarchy: true, semanticModel: semanticModel);

        var (hasExplicitLifetime, lifetime) = attributeData.TryGetLifetime();
        var (hasExplicitRegisterAllInterfaces, registerAllInterfaces) = attributeData.TryGetRegisterAllInterfaces();
        var (hasExplicitRegisterAllBaseClasses, registerAllBaseClasses) = attributeData.TryGetRegisterAllBaseClasses();

        // Extract service types from generic type arguments instead of named argument
        var serviceTypes = attributeData.GetServiceTypesFromGenericAttribute();

        var decorators = attributeData.GetDecorators();
        var tags = attributeData.GetTags();
        var (key, keyType, keyValueTypeSymbol) = attributeData.GetKeyInfo(semanticModel);
        var keyValueType = keyValueTypeSymbol?.GetTypeData();
        var instance = attributeData.GetInstance(semanticModel);

        // Get factory method data with parameter information
        FactoryMethodData? factory = null;
        if(semanticModel is not null)
        {
            factory = attributeData.GetFactoryMethodData(semanticModel);
        }

        var injectionMembers = ExtractAndMergeInjectionMembers(typeSymbol, attributeData, semanticModel);

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
            keyValueType,
            hasExplicitLifetime,
            hasExplicitRegisterAllInterfaces,
            hasExplicitRegisterAllBaseClasses,
            validOpenGenericServiceTypes,
            decorators,
            tags,
            injectionMembers,
            factory,
            instance);
    }

    /// <summary>
    /// Extracts injection members (properties, fields, methods) marked with IocInjectAttribute/InjectAttribute.
    /// </summary>
    /// <param name="typeSymbol">The type symbol to extract injection members from.</param>
    /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
    /// <returns>An array of injection member data.</returns>
    private static ImmutableEquatableArray<InjectionMemberData> ExtractInjectionMembers(INamedTypeSymbol typeSymbol, SemanticModel? semanticModel = null)
    {
        List<InjectionMemberData>? injectionMembers = null;

        foreach(var (member, injectAttribute) in typeSymbol.GetInjectedMembers())
        {
            // Extract key information from IocInjectAttribute/InjectAttribute
            var (key, _, _) = injectAttribute.GetKeyInfo(semanticModel);

            InjectionMemberData? memberData = member switch
            {
                IPropertySymbol property => CreatePropertyInjection(property, key),
                IFieldSymbol field => CreateFieldInjection(field, key),
                IMethodSymbol method => CreateMethodInjection(method, key, semanticModel),
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
    /// <param name="method">The method symbol to create injection data for.</param>
    /// <param name="key">The key for keyed service resolution from [IocInject] attribute.</param>
    /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
    private static InjectionMemberData CreateMethodInjection(IMethodSymbol method, string? key, SemanticModel? semanticModel = null)
    {
        var parameters = method.Parameters
            .Select(p =>
            {
                var (serviceKey, hasInjectAttribute, hasServiceKeyAttribute, hasFromKeyedServicesAttribute) = p.GetServiceKeyAndAttributeInfo(semanticModel);
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
            if(iface is GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false } genericInterface)
            {
                // Use NameWithoutGeneric + Arity as key to handle different arities
                result.Add($"{genericInterface.NameWithoutGeneric}`{genericInterface.GenericArity}");
            }
        }

        foreach(var baseClass in allBaseClasses)
        {
            if(baseClass is GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false } genericBaseClass)
            {
                result.Add($"{genericBaseClass.NameWithoutGeneric}`{genericBaseClass.GenericArity}");
            }
        }

        return result.ToImmutableEquatableSet();
    }

    /// <summary>
    /// Extracts and merges injection members from both <c>[IocInject]</c> attributes on the type
    /// and the <c>InjectMembers</c> attribute property. <c>[IocInject]</c> takes priority.
    /// </summary>
    private static ImmutableEquatableArray<InjectionMemberData> ExtractAndMergeInjectionMembers(
        INamedTypeSymbol typeSymbol, AttributeData attributeData, SemanticModel? semanticModel)
    {
        var iocInjectMembers = ExtractInjectionMembers(typeSymbol, semanticModel);

        ImmutableEquatableArray<InjectionMemberData> attrInjectMembers = semanticModel is not null
            ? ExtractInjectMembersFromAttribute(attributeData, semanticModel)
            : [];

        return MergeInjectionMembers(iocInjectMembers, attrInjectMembers);
    }

    /// <summary>
    /// Merges injection members from <c>[IocInject]</c> attributes and the <c>InjectMembers</c> attribute property.
    /// <c>[IocInject]</c> on the member takes priority: if the same member appears in both, the <c>[IocInject]</c> entry wins.
    /// </summary>
    private static ImmutableEquatableArray<InjectionMemberData> MergeInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData> iocInjectMembers,
        ImmutableEquatableArray<InjectionMemberData> attributeMembers)
    {
        if(attributeMembers.Length == 0)
            return iocInjectMembers;

        if(iocInjectMembers.Length == 0)
            return attributeMembers;

        // [IocInject] takes priority – only add attribute members whose name is not already in [IocInject] set
        var iocInjectNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var m in iocInjectMembers)
            iocInjectNames.Add(m.Name);

        List<InjectionMemberData> merged = new(iocInjectMembers);
        foreach(var m in attributeMembers)
        {
            if(!iocInjectNames.Contains(m.Name))
                merged.Add(m);
        }

        return merged.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Extracts injection members from the registration attribute's <c>InjectMembers</c> property.
    /// Uses <see cref="AttributeSyntax"/> + <see cref="SemanticModel"/> to resolve <c>nameof()</c> expressions.
    /// </summary>
    private static ImmutableEquatableArray<InjectionMemberData> ExtractInjectMembersFromAttribute(
        AttributeData attributeData,
        SemanticModel semanticModel)
    {
        var syntaxReference = attributeData.ApplicationSyntaxReference;
        if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
            return [];

        var argumentList = attributeSyntax.ArgumentList;
        if(argumentList is null)
            return [];

        AttributeArgumentSyntax? injectMembersArg = null;
        foreach(var arg in argumentList.Arguments)
        {
            if(arg.NameEquals?.Name.Identifier.Text == "InjectMembers")
            {
                injectMembersArg = arg;
                break;
            }
        }

        if(injectMembersArg is null)
            return [];

        var elements = GetInjectMembersArrayElements(injectMembersArg.Expression);
        if(elements is null || elements.Length == 0)
            return [];

        List<InjectionMemberData>? result = null;
        foreach(var elementExpr in elements)
        {
            var memberData = ParseInjectMemberElement(elementExpr, semanticModel);
            if(memberData is not null)
            {
                result ??= [];
                result.Add(memberData);
            }
        }

        return result?.ToImmutableEquatableArray() ?? [];
    }

    private static ExpressionSyntax[]? GetInjectMembersArrayElements(ExpressionSyntax expression)
        => expression switch
        {
            ArrayCreationExpressionSyntax { Initializer: not null } arr
                => [.. arr.Initializer.Expressions],
            ImplicitArrayCreationExpressionSyntax implicitArr
                => [.. implicitArr.Initializer.Expressions],
            CollectionExpressionSyntax coll
                => [.. coll.Elements.OfType<ExpressionElementSyntax>().Select(static e => e.Expression)],
            _ => null
        };

    private static InjectionMemberData? ParseInjectMemberElement(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Case 1: nameof(X) — inject without key
        if(IsNameofInvocation(expression))
        {
            var symbol = ResolveInjectMemberSymbolFromNameof(expression, semanticModel);
            return symbol is not null && IsInjectableMember(symbol) ? CreateInjectionMemberFromSymbol(symbol, key: null, semanticModel) : null;
        }

        // Case 2: { nameof(X), key [, KeyType] } — keyed injection
        var nested = GetInjectMembersArrayElements(expression);
        if(nested is null || nested.Length < 2)
            return null;

        if(!IsNameofInvocation(nested[0]))
            return null;

        // Reject arrays with more than 3 elements
        if(nested.Length > 3)
            return null;

        var memberSymbol = ResolveInjectMemberSymbolFromNameof(nested[0], semanticModel);
        if(memberSymbol is null || !IsInjectableMember(memberSymbol))
            return null;

        // Parse KeyType from optional element [2] (default = 0 = Value)
        int keyType = 0;
        if(nested.Length > 2)
        {
            var ktConst = semanticModel.GetConstantValue(nested[2]);
            if(ktConst.HasValue && ktConst.Value is int kt && kt is 0 or 1)
                keyType = kt;
            else
                return null; // Invalid KeyType — skip this element
        }

        // Parse key value from element [1]
        string? key;
        if(keyType == 1) // Csharp
        {
            if(IsNameofInvocation(nested[1]))
            {
                var inner = ((InvocationExpressionSyntax)nested[1]).ArgumentList.Arguments[0].Expression;
                // Resolve to fully qualified path, consistent with TryGetNameof
                key = RoslynExtensions.ResolveNameofExpression(inner, semanticModel)
                    ?? inner.ToString();
            }
            else
            {
                key = nested[1].ToFullString().Trim();
            }
        }
        else // Value
        {
            var constVal = semanticModel.GetConstantValue(nested[1]);
            if(constVal.HasValue && constVal.Value is not null)
            {
                var typeInfo = semanticModel.GetTypeInfo(nested[1]);
                key = typeInfo.Type is not null
                    ? RoslynExtensions.FormatPrimitiveConstant(typeInfo.Type, constVal.Value)
                    : constVal.Value.ToString();
            }
            else
            {
                return null;
            }
        }

        return CreateInjectionMemberFromSymbol(memberSymbol, key, semanticModel);
    }

    /// <summary>
    /// Checks whether a symbol is injectable, matching the same filters applied in
    /// <see cref="TypeSymbolExtensions.GetInjectedMembers"/>.
    /// </summary>
    private static bool IsInjectableMember(ISymbol symbol)
    {
        if(symbol.IsStatic)
            return false;

        return symbol switch
        {
            IPropertySymbol property => property.SetMethod is not null
                && property.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
                && property.SetMethod.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal,
            IFieldSymbol field => !field.IsReadOnly
                && field.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal,
            IMethodSymbol method => method.MethodKind == MethodKind.Ordinary
                && method.ReturnsVoid
                && !method.IsGenericMethod
                && method.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal,
            _ => false
        };
    }

    private static bool IsNameofInvocation(ExpressionSyntax expression)
        => expression is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } } invocation
            && invocation.ArgumentList.Arguments.Count == 1;

    private static ISymbol? ResolveInjectMemberSymbolFromNameof(ExpressionSyntax nameofExpr, SemanticModel semanticModel)
    {
        if(nameofExpr is not InvocationExpressionSyntax invocation)
            return null;
        var inner = invocation.ArgumentList.Arguments[0].Expression;
        var info = semanticModel.GetSymbolInfo(inner);
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static InjectionMemberData? CreateInjectionMemberFromSymbol(ISymbol symbol, string? key, SemanticModel? semanticModel)
        => symbol switch
        {
            IPropertySymbol property => CreatePropertyInjection(property, key),
            IFieldSymbol field => CreateFieldInjection(field, key),
            IMethodSymbol method => CreateMethodInjection(method, key, semanticModel),
            _ => null
        };

}
