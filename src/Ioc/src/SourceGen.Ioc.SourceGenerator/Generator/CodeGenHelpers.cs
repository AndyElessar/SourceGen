namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Builds a constructor invocation expression with an optional initializer.
    /// </summary>
    private static string BuildConstructorInvocation(string implTypeName, string args, string initializerPart) =>
        $"new {implTypeName}({args}){initializerPart}";

    /// <summary>
    /// Builds an argument list from parameter entries.
    /// Null values are omitted so the target member uses its optional parameter default.
    /// </summary>
    private static string BuildArgumentListFromEntries(IEnumerable<(string Name, string? Value)> entries)
    {
        // Check if any parameter uses default value
        bool hasDefaultValue = entries.Any(e => e.Value is null);

        if(!hasDefaultValue)
        {
            // All values present - use positional arguments
            return string.Join(", ", entries.Select(e => e.Value!));
        }

        // Some values are null - use named arguments for non-null values only
        var namedArgs = entries.Where(e => e.Value is not null).Select(e => $"{e.Name}: {e.Value}");
        return string.Join(", ", namedArgs);
    }

    /// <summary>
    /// Converts a TypeData to typeof() syntax for open generic types.
    /// For example: TypeData with Name="global::Namespace.GenericTest&lt;T&gt;" becomes "typeof(global::Namespace.GenericTest&lt;&gt;)"
    /// </summary>
    private static string ConvertToTypeOf(TypeData typeData)
    {
        if(typeData is not GenericTypeData genericTypeData || genericTypeData.GenericArity == 0)
        {
            return $"typeof({typeData.Name})";
        }

        // Build the open generic typeof
        return $"typeof({genericTypeData.NameWithoutGeneric}{GetGenericString(genericTypeData.GenericArity)})";
    }

    /// <summary>
    /// Cached generic arity strings to avoid repeated allocations.
    /// </summary>
    private static readonly string[] s_genericArityStrings =
    [
        "<>",
        "<,>",
        "<,,>",
        "<,,,>",
        "<,,,,>",
        "<,,,,,>",
        "<,,,,,,>",
        "<,,,,,,,>",
        "<,,,,,,,,>"
    ];

    private static string GetGenericString(int arity) =>
        arity <= 9 ? s_genericArityStrings[arity - 1] : '<' + new string(',', arity - 1) + '>';

    private static string GetServiceResolutionMethod(string? serviceKey, bool isOptional) =>
        (serviceKey is not null, isOptional) switch
        {
            (true, true) => "GetKeyedService",
            (true, false) => "GetRequiredKeyedService",
            (false, true) => "GetService",
            (false, false) => "GetRequiredService",
        };

    /// <summary>
    /// Builds the generic type arguments string for a generic factory method.
    /// Uses the <see cref="GenericFactoryTypeMapping"/> to map placeholder types in the service type template
    /// to the actual types from the closed service type.
    /// </summary>
    /// <param name="factory">The factory method data containing the generic type mapping.</param>
    /// <param name="closedServiceType">The closed service type to extract type arguments from.</param>
    /// <returns>The generic type arguments string (e.g., "Entity, Dto"), or null if not a generic factory.</returns>
    /// <example>
    /// Given:
    /// - ServiceTypeTemplate: IRequestHandler&lt;Task&lt;int&gt;&gt;
    /// - PlaceholderToTypeParamMap: { "int" -> 0 }
    /// - ClosedServiceType: IRequestHandler&lt;Task&lt;Entity&gt;&gt;
    /// Returns: "Entity"
    /// </example>
    private static string? BuildGenericFactoryTypeArgs(FactoryMethodData factory, TypeData closedServiceType)
    {
        var mapping = factory.GenericTypeMapping;
        if(mapping is null || factory.TypeParameterCount == 0)
        {
            return null;
        }

        var template = mapping.ServiceTypeTemplate;
        var placeholderMap = mapping.PlaceholderToTypeParamMap;

        // Build a map from placeholder types to actual types by comparing template with closed service type
        var placeholderToActualType = new Dictionary<string, string>(StringComparer.Ordinal);
        ExtractPlaceholderMappings(template, closedServiceType, placeholderToActualType);

        // Build type arguments array in the order of factory method's type parameters
        var typeArgs = new string[factory.TypeParameterCount];
        foreach(var kvp in placeholderMap)
        {
            var placeholderTypeName = kvp.Key;
            var typeParamIndex = kvp.Value;

            if(typeParamIndex < 0 || typeParamIndex >= typeArgs.Length)
            {
                continue;
            }

            if(placeholderToActualType.TryGetValue(placeholderTypeName, out var actualTypeName))
            {
                typeArgs[typeParamIndex] = actualTypeName;
            }
        }

        // Validate all type arguments are filled
        foreach(var arg in typeArgs)
        {
            if(string.IsNullOrEmpty(arg))
            {
                return null; // Missing type argument, cannot generate generic call
            }
        }

        return string.Join(", ", typeArgs);
    }

    /// <summary>
    /// Recursively extracts mappings from placeholder types in the template to actual types in the closed type.
    /// </summary>
    private static void ExtractPlaceholderMappings(
        TypeData template,
        TypeData closed,
        Dictionary<string, string> placeholderToActualType)
    {
        if(template is not GenericTypeData genericTemplate || closed is not GenericTypeData genericClosed)
        {
            return;
        }

        // If base type names don't match, can't extract mappings
        if(genericTemplate.NameWithoutGeneric != genericClosed.NameWithoutGeneric)
        {
            return;
        }

        var templateParams = genericTemplate.TypeParameters;
        var closedParams = genericClosed.TypeParameters;

        if(templateParams is null || closedParams is null)
        {
            return;
        }

        if(templateParams.Length != closedParams.Length)
        {
            return;
        }

        for(int i = 0; i < templateParams.Length; i++)
        {
            var templateParamType = templateParams[i].Type;
            var closedParamType = closedParams[i].Type;

            // If the template param is a simple type (no nested type parameters),
            // it's a placeholder that maps to the closed param type
            if(templateParamType is not GenericTypeData { TypeParameters.Length: > 0 })
            {
                // Map the template type name to the closed type name
                // e.g., "global::System.Int32" -> "global::TestNamespace.Entity"
                placeholderToActualType[templateParamType.Name] = closedParamType.Name;
            }
            else
            {
                // Nested generic type, recurse
                ExtractPlaceholderMappings(templateParamType, closedParamType, placeholderToActualType);
            }
        }
    }
}