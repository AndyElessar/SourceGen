namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Gets the name of the owner of a factory's type parameters and dependencies for an open generic registration, so the
    /// factory's type parameters are nodes of their own for each registration it serves (see <see cref="FindExpandingDependencies"/>).
    /// </summary>
    internal static string GetFactoryOwner(FactoryMethodData factory, TypeData implementation) => $"{factory.Path}|{implementation.Name}";

    /// <summary>
    /// Gets the service type template of a generic factory's mapping with each placeholder replaced by the factory type
    /// parameter it stands for, such as <c>IRequestHandler&lt;Task&lt;TF&gt;&gt;</c> for
    /// <c>[IocGenericFactory(typeof(IRequestHandler&lt;Task&lt;int&gt;&gt;), typeof(int))]</c> on <c>Create&lt;TF&gt;</c>.
    /// </summary>
    /// <returns>The pattern, or <see langword="null"/> when the factory is not generic or has no mapping.</returns>
    private static TypeData? GetGenericFactoryPattern(FactoryMethodData factory)
    {
        if(factory is not { GenericTypeMapping: { } mapping, TypeParameterNames: { Length: > 0 } names })
        {
            return null;
        }

        return Replace(mapping.ServiceTypeTemplate);

        // Like BuildGenericFactoryTypeArgs, only type arguments without type arguments of their own are placeholders
        TypeData Replace(TypeData type)
        {
            if(type is GenericTypeData { TypeParameters: { Length: > 0 } typeArguments } genericType)
            {
                return genericType with
                {
                    TypeParameters = [.. typeArguments.Select(typeArgument => typeArgument with { Type = Replace(typeArgument.Type) })],
                };
            }

            return mapping.PlaceholderToTypeParamMap.TryGetValue(type.Name, out var index) && index >= 0 && index < names.Length
                ? TypeData.CreateTypeParameter(names[index])
                : type;
        }
    }

    /// <summary>
    /// Maps the type parameters of a generic factory to their type arguments for a closed service type: the type arguments
    /// the factory is called with (<c>BuildGenericFactoryTypeArgs</c>), and its parameters are substituted with.
    /// </summary>
    /// <returns>The type argument of each factory type parameter, or <see langword="null"/> when one is not determined.</returns>
    private static Dictionary<string, TypeData>? MapGenericFactoryTypeArguments(FactoryMethodData factory, TypeData closedServiceType)
        => MapGenericFactoryTypeArguments(factory, closedServiceType, out _);

    /// <summary>
    /// Gets whether a generic factory with a mapping cannot be called for a service type, which is then not registered: its
    /// mapping does not determine every factory type parameter, or a repeated placeholder takes different type arguments. A
    /// service type with type parameters is not reported when some of its closings may match: a type parameter stands where
    /// the template has a constructed type, or in a type argument that a repeated placeholder takes.
    /// </summary>
    internal static bool IsUnmatchedByGenericFactory(FactoryMethodData factory, TypeData serviceType)
        => factory.TypeParameterCount > 0
            && GetGenericFactoryPattern(factory) is not null
            && MapGenericFactoryTypeArguments(factory, serviceType, out var hasTypeParameter) is null
            && !hasTypeParameter;

    /// <param name="hasTypeParameter">
    /// Whether some closing of the service type's type parameters may still match: a type parameter stands where the template
    /// has a constructed type, or a repeated placeholder takes type arguments that may become equal (<c>T</c> and <c>int</c>).
    /// </param>
    private static Dictionary<string, TypeData>? MapGenericFactoryTypeArguments(FactoryMethodData factory, TypeData serviceType, out bool hasTypeParameter)
    {
        hasTypeParameter = false;
        if(GetGenericFactoryPattern(factory) is not { } pattern)
        {
            return null;
        }

        var typeArguments = new Dictionary<string, TypeData>(StringComparer.Ordinal);
        var blocked = false;
        var conflicting = false;
        Match(pattern, serviceType);
        hasTypeParameter = blocked;
        return !conflicting && typeArguments.Count == factory.TypeParameterCount ? typeArguments : null;

        // Generic types are matched by their definition position by position; other types of the template are not compared.
        // A placeholder repeated in the template, such as int in IPair<int, int>, MUST take the same type argument at each
        // position: IPair<A, B> does not match.
        void Match(TypeData patternType, TypeData closedType)
        {
            if(patternType is TypeParameterTypeData)
            {
                if(typeArguments.TryGetValue(patternType.Name, out var existing) && existing.Name != closedType.Name)
                {
                    conflicting = true;

                    // Closings of a type parameter, such as IPair<T, int> with T = int, may still take the same type argument
                    blocked |= MayBecomeEqual(existing, closedType);
                    return;
                }

                typeArguments[patternType.Name] = closedType;
                return;
            }

            if(closedType is TypeParameterTypeData)
            {
                blocked |= patternType is GenericTypeData { TypeParameters.Length: > 0 };
                return;
            }

            if(patternType is GenericTypeData { TypeParameters: { Length: > 0 } patternArguments } genericPattern
                && closedType is GenericTypeData { TypeParameters: { } closedArguments } genericClosed
                && genericPattern.NameWithoutGeneric == genericClosed.NameWithoutGeneric
                && patternArguments.Length == closedArguments.Length)
            {
                for(var i = 0; i < patternArguments.Length; i++)
                {
                    Match(patternArguments[i].Type, closedArguments[i].Type);
                }
            }
        }

        // Whether two types written with type parameters may become the same type for some type arguments: a type parameter
        // may become any type, while generic types must share their definition and each type argument may become equal
        static bool MayBecomeEqual(TypeData left, TypeData right)
        {
            if(left is TypeParameterTypeData || right is TypeParameterTypeData)
                return true;

            // Arrays (T[] and int[]) may become equal when their element types may; their rank is conservatively ignored.
            // An array never becomes a type that is not an array.
            if(left is ArrayTypeData || right is ArrayTypeData)
            {
                return left is ArrayTypeData { TypeParameters: [var leftElement] }
                    && right is ArrayTypeData { TypeParameters: [var rightElement] }
                    && MayBecomeEqual(leftElement.Type, rightElement.Type);
            }

            if(left is GenericTypeData { TypeParameters: { Length: > 0 } leftArguments } leftGeneric
                && right is GenericTypeData { TypeParameters: { Length: > 0 } rightArguments } rightGeneric)
            {
                if(leftGeneric.NameWithoutGeneric != rightGeneric.NameWithoutGeneric || leftArguments.Length != rightArguments.Length)
                    return false;

                for(var i = 0; i < leftArguments.Length; i++)
                {
                    if(!MayBecomeEqual(leftArguments[i].Type, rightArguments[i].Type))
                        return false;
                }

                return true;
            }

            // Other types with type parameters that are not generic types with type arguments are conservatively assumed to
            // may become equal
            return left.Name == right.Name || IsOtherOpenType(left) || IsOtherOpenType(right);

            // A constructed generic type such as List<T> never becomes a type of another definition, such as int
            static bool IsOtherOpenType(TypeData type)
                => type is GenericTypeData { IsOpenGeneric: true, TypeParameters: null or { Length: 0 } };
        }
    }

    /// <summary>
    /// Closes the parameters of a generic factory for a closed service type, such as <c>IRepo&lt;Entity&gt;</c> for
    /// <c>Create&lt;T&gt;(IRepo&lt;T&gt; repo)</c> called as <c>Create&lt;Entity&gt;</c>, so they are resolved and collected
    /// as closed generic dependencies.
    /// </summary>
    /// <returns>
    /// The closed factory, <paramref name="factory"/> when it is not generic, or <see langword="null"/> when the factory
    /// cannot be called for the service type (see <see cref="IsUnmatchedByGenericFactory"/>).
    /// </returns>
    internal static FactoryMethodData? CloseGenericFactory(FactoryMethodData factory, TypeData closedServiceType)
    {
        if(factory.TypeParameterCount == 0)
        {
            return factory;
        }

        if(MapGenericFactoryTypeArguments(factory, closedServiceType) is not { } typeArguments)
        {
            return null;
        }

        if(factory.AdditionalParameters.Length == 0)
        {
            return factory;
        }

        var typeArgMap = ToTypeArgMap(typeArguments);
        return factory with
        {
            AdditionalParameters = [.. factory.AdditionalParameters.Select(parameter => parameter with { Type = SubstituteTypeData(parameter.Type, typeArgMap, typeArguments) })],
        };
    }

    /// <summary>Gets the closing source of the dependencies collected from the factory of a closed registration.</summary>
    private static ClosingSource GetFactoryClosingSource(FactoryMethodData factory, OpenGenericRegistrationInfo registration, TypeData closedServiceType)
    {
        var typeArgMap = factory.TypeParameterCount > 0 && MapGenericFactoryTypeArguments(factory, closedServiceType) is { } typeArguments
            ? ToTypeArgMap(typeArguments)
            : [];

        return new ClosingSource(GetFactoryOwner(factory, registration.ImplementationType), typeArgMap);
    }
}
