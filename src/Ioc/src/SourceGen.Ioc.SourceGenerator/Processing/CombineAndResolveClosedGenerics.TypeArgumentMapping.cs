namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Builds a type argument map from open service type to closed service type.
    /// For nested open generics like IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt;,
    /// this extracts the actual type parameters (T -> Entity) by comparing
    /// the nested type arguments.
    /// </summary>
    private static Dictionary<string, string>? BuildTypeArgumentMapFromServiceType(
        GenericTypeData? openServiceType,
        GenericTypeData? closedServiceType)
    {
        if(openServiceType is null || closedServiceType is null)
        {
            return null;
        }

        var openTypeParams = openServiceType.TypeParameters;
        var closedTypeParams = closedServiceType.TypeParameters;

        if(openTypeParams is null || closedTypeParams is null)
        {
            return null;
        }

        if(openTypeParams.Length != closedTypeParams.Length)
        {
            return null;
        }

        var typeArgMap = new Dictionary<string, string>(openTypeParams.Length, StringComparer.Ordinal);

        // For non-nested open generics (like ILogger<T>), use direct mapping
        if(!openServiceType.IsNestedOpenGeneric)
        {
            for(int i = 0; i < openTypeParams.Length; i++)
            {
                typeArgMap[openTypeParams[i].ParameterName] = closedTypeParams[i].Type.Name;
            }
            return typeArgMap;
        }

        // For nested open generics, we need to extract the actual type parameter mappings
        // by comparing nested type arguments
        // If extraction fails (incompatible structures), return empty map
        if(!ExtractTypeArgumentMappings(openTypeParams, closedTypeParams, typeArgMap))
        {
            return null;
        }

        return typeArgMap;
    }

    /// <summary>
    /// Extracts type argument mappings from nested open generic types using TypeParameters.
    /// For example, for IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt; closed with
    /// IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;,
    /// this extracts T -> Entity.
    /// Returns false if the type structures are incompatible.
    /// </summary>
    private static bool ExtractTypeArgumentMappings(
        ImmutableEquatableArray<TypeParameter> openParams,
        ImmutableEquatableArray<TypeParameter> closedParams,
        Dictionary<string, string> typeArgMap)
    {
        for(int i = 0; i < openParams.Length; i++)
        {
            var openParam = openParams[i];
            var closedParam = closedParams[i];
            var openParamType = openParam.Type;
            var closedParamType = closedParam.Type;

            // If the open param type is the same as the parameter name, it's a direct type parameter reference
            // e.g., TypeParameter { ParameterName = "T", Type = { Name = "T" } }
            if(openParamType.Name == openParam.ParameterName)
            {
                // Direct type parameter, map it to the closed type
                if(!TryAddTypeArgument(typeArgMap, openParam.ParameterName, closedParamType.Name))
                {
                    return false;
                }
                continue;
            }

            // The open param type is a constructed type with nested type parameters
            // e.g., GenericRequest<T> or List<T>
            // Use the recursively extracted TypeParameters from TypeData
            if(!ExtractTypeArgumentMappingsFromTypeData(openParamType, closedParamType, typeArgMap))
            {
                return false; // Incompatible structure
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts type argument mappings by comparing TypeData's TypeParameters recursively.
    /// For example, comparing GenericRequest&lt;T&gt; with GenericRequest&lt;Entity&gt;
    /// extracts T -> Entity.
    /// Returns false if the type structures are incompatible (e.g., different base types).
    /// </summary>
    private static bool ExtractTypeArgumentMappingsFromTypeData(
        TypeData openType,
        TypeData closedType,
        Dictionary<string, string> typeArgMap)
    {
        if(openType is not GenericTypeData openGenericType || closedType is not GenericTypeData closedGenericType)
        {
            return openType.Name == closedType.Name;
        }

        // Verify base type names match (excluding generic arguments)
        // e.g., GenericRequest<T> vs TestRequest should fail because GenericRequest != TestRequest
        if(openGenericType.NameWithoutGeneric != closedGenericType.NameWithoutGeneric)
        {
            return false; // Incompatible type structure
        }

        var openTypeParams = openGenericType.TypeParameters;
        var closedTypeParams = closedGenericType.TypeParameters;

        // If no type parameters, nothing to extract but structure is compatible
        if(openTypeParams is null || closedTypeParams is null)
        {
            return true;
        }

        // Arity must match
        if(openTypeParams.Length != closedTypeParams.Length)
        {
            return false;
        }

        for(int i = 0; i < openTypeParams.Length; i++)
        {
            var openTypeParam = openTypeParams[i];
            var closedTypeParam = closedTypeParams[i];
            var openNestedType = openTypeParam.Type;
            var closedNestedType = closedTypeParam.Type;

            // Check if openNestedType is a type parameter (determined at creation time from TypeKind)
            if(openNestedType is TypeParameterTypeData)
            {
                // Direct mapping: T -> Entity (use the full closed type name)
                if(!TryAddTypeArgument(typeArgMap, openNestedType.Name, closedNestedType.Name))
                {
                    return false;
                }
            }
            else if(openNestedType is GenericTypeData { TypeParameters: { Length: > 0 } })
            {
                // Nested generic type, recurse using TypeParameters
                if(!ExtractTypeArgumentMappingsFromTypeData(openNestedType, closedNestedType, typeArgMap))
                {
                    return false;
                }
            }
            else
            {
                // Non-generic, non-type-parameter type - must match exactly
                if(openNestedType.Name != closedNestedType.Name)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Records the type argument of a type parameter, or checks it against the one already recorded.
    /// </summary>
    /// <returns>False when the type parameter already stands for a different type, such as <c>T</c> in
    /// <c>IHandler&lt;Request&lt;T&gt;, List&lt;T&gt;&gt;</c> closed as <c>IHandler&lt;Request&lt;A&gt;, List&lt;B&gt;&gt;</c>.</returns>
    private static bool TryAddTypeArgument(Dictionary<string, string> typeArgMap, string typeParameter, string typeArgument)
    {
        if(typeArgMap.TryGetValue(typeParameter, out var existing))
        {
            return existing == typeArgument;
        }

        typeArgMap.Add(typeParameter, typeArgument);
        return true;
    }

    /// <summary>
    /// Builds implementation type argument map by mapping service type params to implementation type params.
    /// For nested open generics, this uses the serviceTypeArgMap which already contains
    /// the extracted type parameter mappings (e.g., T -> Entity).
    /// </summary>
    private static Dictionary<string, string>? BuildImplTypeArgMapFromServiceTypeMap(
        GenericTypeData openImplType,
        GenericTypeData? openServiceType,
        Dictionary<string, string> serviceTypeArgMap)
    {
        var implTypeParams = openImplType.TypeParameters;

        if(implTypeParams is null)
        {
            return null;
        }

        // For nested open generics, the serviceTypeArgMap already contains
        // the direct type parameter mappings (e.g., T -> Entity)
        // These should match the implementation type parameters directly
        var implTypeArgMap = new Dictionary<string, string>(implTypeParams.Length, StringComparer.Ordinal);
        foreach(var implParam in implTypeParams)
        {
            // Check if the serviceTypeArgMap contains a mapping for this impl type param
            if(serviceTypeArgMap.TryGetValue(implParam.ParameterName, out var closedTypeArg))
            {
                implTypeArgMap[implParam.ParameterName] = closedTypeArg;
            }
        }

        return implTypeArgMap;
    }

    /// <summary>
    /// Finds the form of a generic type that an open generic implementation declares: one of its interfaces, one of its
    /// base classes, or the implementation itself.
    /// </summary>
    /// <remarks>
    /// The form's type arguments are written with the implementation's type parameters, for example
    /// <c>IHandler&lt;TRequest, string&gt;</c> for <c>TextHandler&lt;TRequest&gt; : IHandler&lt;TRequest, string&gt;</c>.
    /// </remarks>
    private static GenericTypeData? FindImplementedForm(
        GenericTypeData openImplType,
        ImmutableEquatableArray<TypeData> implInterfaces,
        GenericTypeData type)
        => FindDeclaredForm(implInterfaces.Concat(openImplType.AllBaseClasses ?? []).Append(openImplType), type);

    /// <summary>
    /// Finds the candidate with the same generic type definition as <paramref name="type"/>.
    /// </summary>
    private static GenericTypeData? FindDeclaredForm(IEnumerable<TypeData> candidates, GenericTypeData type)
    {
        foreach(var candidate in candidates)
        {
            if(candidate is GenericTypeData genericCandidate
                && genericCandidate.NameWithoutGeneric == type.NameWithoutGeneric
                && genericCandidate.GenericArity == type.GenericArity)
            {
                return genericCandidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Matches a type written with type parameters against a closed type, recording what each type parameter stands for.
    /// </summary>
    /// <returns>
    /// False when the closed type does not have the pattern's shape, for example <c>IHandler&lt;Req, int&gt;</c> against
    /// <c>IHandler&lt;TRequest, string&gt;</c>, or when a type parameter would stand for two different types.
    /// </returns>
    private static bool TryMatchTypeArguments(TypeData pattern, TypeData closed, Dictionary<string, TypeData> typeArguments)
    {
        if(pattern is TypeParameterTypeData)
        {
            if(typeArguments.TryGetValue(pattern.Name, out var mapped))
            {
                return mapped.Name == closed.Name;
            }

            typeArguments[pattern.Name] = closed;
            return true;
        }

        // Arrays are named after their element type, so match the element type and the rank instead
        if(pattern is ArrayTypeData { TypeParameters: { Length: 1 } patternElement })
        {
            return closed is ArrayTypeData { TypeParameters: { Length: 1 } closedElement }
                && GetArrayRank(pattern.Name) == GetArrayRank(closed.Name)
                && TryMatchTypeArguments(patternElement[0].Type, closedElement[0].Type, typeArguments);
        }

        if(pattern is GenericTypeData { TypeParameters: { Length: > 0 } patternArgs } genericPattern)
        {
            if(closed is not GenericTypeData { TypeParameters: { } closedArgs } genericClosed
                || closedArgs.Length != patternArgs.Length
                || genericClosed.NameWithoutGeneric != genericPattern.NameWithoutGeneric)
            {
                return false;
            }

            for(var i = 0; i < patternArgs.Length; i++)
            {
                if(!TryMatchTypeArguments(patternArgs[i].Type, closedArgs[i].Type, typeArguments))
                {
                    return false;
                }
            }

            return true;
        }

        return pattern.Name == closed.Name;

        // The outermost rank specifier, such as "[]" for T[][] or "[,]" for T[,]
        static string GetArrayRank(string arrayName) => arrayName[arrayName.LastIndexOf('[')..];
    }

    /// <summary>
    /// Matches the form an open generic type declares against a closed type and returns the type argument of each of the
    /// open generic type's type parameters.
    /// </summary>
    /// <returns>Null when the forms do not match or a type parameter of <paramref name="openType"/> is not determined.</returns>
    private static Dictionary<string, TypeData>? MatchAllTypeArguments(GenericTypeData openType, TypeData declaredForm, TypeData closed)
    {
        var typeArguments = new Dictionary<string, TypeData>(StringComparer.Ordinal);
        if(!TryMatchTypeArguments(declaredForm, closed, typeArguments))
        {
            return null;
        }

        foreach(var typeParam in openType.TypeParameters ?? [])
        {
            if(!typeArguments.ContainsKey(typeParam.ParameterName))
            {
                return null;
            }
        }

        return typeArguments;
    }

    /// <summary>
    /// Creates a substitution map from type arguments matched by <see cref="TryMatchTypeArguments"/>.
    /// </summary>
    private static Dictionary<string, string> ToTypeArgMap(Dictionary<string, TypeData> typeArguments)
    {
        var typeArgMap = new Dictionary<string, string>(typeArguments.Count, StringComparer.Ordinal);
        foreach(var entry in typeArguments)
        {
            typeArgMap[entry.Key] = entry.Value.Name;
        }

        return typeArgMap;
    }
}
