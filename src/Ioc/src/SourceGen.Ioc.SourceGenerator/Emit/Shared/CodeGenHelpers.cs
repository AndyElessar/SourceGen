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

    /// <summary>
    /// Gets the type name usable in a type pattern: a nullable value type <c>T?</c> is matched by its underlying type,
    /// because a nullable type is not allowed in a pattern.
    /// </summary>
    private static string GetPatternTypeName(string typeName) =>
        typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1] : typeName;

    private static string GetServiceResolutionMethod(string? serviceKey, bool isOptional) =>
        (serviceKey is not null, isOptional) switch
        {
            (true, true) => "GetKeyedService",
            (true, false) => "GetRequiredKeyedService",
            (false, true) => "GetService",
            (false, false) => "GetRequiredService",
        };

    /// <summary>
    /// Builds the generic type arguments string for a generic factory method, in the order of its type parameters, from the
    /// type arguments its mapping gives for the closed service type (see <see cref="MapGenericFactoryTypeArguments(FactoryMethodData, TypeData)"/>).
    /// </summary>
    /// <param name="factory">The factory method data containing the generic type mapping.</param>
    /// <param name="closedServiceType">The closed service type to extract type arguments from.</param>
    /// <returns>The generic type arguments string (e.g., "Entity, Dto"), or null if not a generic factory or not determined.</returns>
    private static string? BuildGenericFactoryTypeArgs(FactoryMethodData factory, TypeData closedServiceType)
    {
        if(factory.TypeParameterNames is not { Length: > 0 } names
            || MapGenericFactoryTypeArguments(factory, closedServiceType) is not { } typeArguments)
        {
            return null;
        }

        return string.Join(", ", names.Select(name => typeArguments[name].Name));
    }
}
