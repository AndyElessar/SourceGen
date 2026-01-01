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
        var implementationType = typeSymbol.GetTypeData(extractHierarchy: true);
        var (hasExplicitLifetime, lifetime) = attributeData.TryGetLifetime();
        var (hasExplicitRegisterAllInterfaces, registerAllInterfaces) = attributeData.TryGetRegisterAllInterfaces();
        var (hasExplicitRegisterAllBaseClasses, registerAllBaseClasses) = attributeData.TryGetRegisterAllBaseClasses();
        var serviceTypes = attributeData.GetServiceTypes();
        var decorators = attributeData.GetDecorators();
        var tags = attributeData.GetTags();
        var excludeFromDefault = attributeData.GetExcludeFromDefault();

        var keyType = attributeData.GetNamedArgument<int>("KeyType", 0);
        string? key = null;
        foreach(var namedArg in attributeData.NamedArguments)
        {
            if(namedArg.Key == "Key")
            {
                if(namedArg.Value.IsNull)
                {
                    key = null;
                }
                else
                {
                    if(keyType == 1) // KeyType.Csharp
                    {
                        // Try to get original syntax for nameof() expressions
                        key = TryGetOriginalKeySyntax(attributeData, "Key")
                            ?? namedArg.Value.Value?.ToString();
                    }
                    else
                    {
                        key = namedArg.Value.GetPrimitiveConstantString();
                        keyType = 1; // Treat as CSharp code
                    }
                }
                break;
            }
        }

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
            excludeFromDefault);
    }

    /// <summary>
    /// Tries to get the original syntax for a named argument, especially for nameof() expressions.
    /// </summary>
    /// <param name="attributeData">The attribute data.</param>
    /// <param name="argumentName">The name of the argument to find.</param>
    /// <returns>The original syntax string if it's a nameof() expression; otherwise, null.</returns>
    private static string? TryGetOriginalKeySyntax(AttributeData attributeData, string argumentName)
    {
        var syntaxReference = attributeData.ApplicationSyntaxReference;
        if(syntaxReference is null)
            return null;

        var syntax = syntaxReference.GetSyntax();
        if(syntax is not AttributeSyntax attributeSyntax)
            return null;

        var argumentList = attributeSyntax.ArgumentList;
        if(argumentList is null)
            return null;

        foreach(var argument in argumentList.Arguments)
        {
            // Check if this is a named argument with the correct name
            if(argument.NameEquals?.Name.Identifier.Text == argumentName)
            {
                // Check if the expression is a nameof() invocation
                if(argument.Expression is InvocationExpressionSyntax invocation &&
                   invocation.Expression is IdentifierNameSyntax identifierName &&
                   identifierName.Identifier.Text == "nameof")
                {
                    // Extract the argument inside nameof() and return just that expression
                    if(invocation.ArgumentList.Arguments.Count == 1)
                    {
                        var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;
                        return nameofArgument.ToFullString().Trim();
                    }
                }
            }
        }

        return null;
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
