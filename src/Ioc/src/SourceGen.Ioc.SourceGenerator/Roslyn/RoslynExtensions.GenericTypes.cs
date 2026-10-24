namespace SourceGen.Ioc.SourceGenerator;

internal static partial class RoslynExtensions
{
    extension(ITypeSymbol typeSymbol)
    {
        public bool ContainsGenericParameters
        {
            get
            {
                if(typeSymbol.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
                {
                    return true;
                }

                if(typeSymbol is INamedTypeSymbol namedTypeSymbol)
                {
                    if(namedTypeSymbol.IsUnboundGenericType)
                    {
                        return true;
                    }

                    for(; namedTypeSymbol != null; namedTypeSymbol = namedTypeSymbol.ContainingType)
                    {
                        if(namedTypeSymbol.TypeArguments.Any(arg => arg.ContainsGenericParameters))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        public INamedTypeSymbol? GetCompatibleGenericBaseType([NotNullWhen(true)] INamedTypeSymbol? genericType)
        {
            if(genericType is null)
            {
                return null;
            }

            Debug.Assert(genericType.IsGenericTypeDefinition);

            if(genericType.TypeKind is TypeKind.Interface)
            {
                foreach(INamedTypeSymbol interfaceType in typeSymbol.AllInterfaces)
                {
                    if(IsMatchingGenericType(interfaceType, genericType))
                    {
                        return interfaceType;
                    }
                }
            }

            for(INamedTypeSymbol? current = typeSymbol as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if(IsMatchingGenericType(current, genericType))
                {
                    return current;
                }
            }

            return null;

            static bool IsMatchingGenericType(INamedTypeSymbol candidate, INamedTypeSymbol baseType)
            {
                return candidate.IsGenericType && SymbolEqualityComparer.Default.Equals(candidate.ConstructedFrom, baseType);
            }
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
        public bool IsGenericTypeDefinition => typeSymbol is { IsGenericType: true, IsDefinition: true };

        /// <summary>
        /// Gets the type parameters source for this type symbol.
        /// For unbound generic types and constructed generic types, returns parameters from the original definition.
        /// This allows matching type parameter names (TRequest, TResponse) with type arguments (TestRequest, List&lt;string&gt;).
        /// </summary>
        public ImmutableArray<ITypeParameterSymbol> TypeParametersSource =>
            typeSymbol.IsGenericType
                ? typeSymbol.OriginalDefinition?.TypeParameters ?? typeSymbol.TypeParameters
                : typeSymbol.TypeParameters;

        /// <summary>
        /// Determines whether the type is a nested open generic type.
        /// A nested open generic is a generic type where any type argument itself contains generic parameters.
        /// For example: IGeneric&lt;IGeneric2&lt;T&gt;&gt; is a nested open generic.
        /// But IGeneric&lt;T&gt; or IGeneric&lt;int&gt; are not.
        /// </summary>
        public bool IsNestedOpenGeneric
        {
            get
            {
                if(!typeSymbol.IsGenericType)
                {
                    return false;
                }

                // For unbound generic types (e.g., IRepository<>), TypeArguments contains error types
                // which should not be considered as nested open generics
                if(typeSymbol.IsUnboundGenericType)
                {
                    return false;
                }

                // Check if any type argument contains generic parameters
                foreach(var typeArg in typeSymbol.TypeArguments)
                {
                    // If the type argument is not a simple type parameter (T, T1, etc.)
                    // but contains generic parameters, it's a nested open generic
                    if(typeArg.TypeKind != TypeKind.TypeParameter && typeArg.ContainsGenericParameters)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Checks if sourceType is assignable to targetType.
    /// Returns true if a value of sourceType can be assigned to a variable of targetType.
    /// </summary>
    /// <param name="targetType">The target type (e.g., parameter type).</param>
    /// <param name="sourceType">The source type (e.g., value type).</param>
    /// <returns>True if sourceType is assignable to targetType.</returns>
    public static bool IsAssignable(ITypeSymbol targetType, ITypeSymbol sourceType)
    {
        // Exact match
        if(SymbolEqualityComparer.Default.Equals(targetType, sourceType))
            return true;

        // Handle nullable types - if target is nullable and source type is the underlying type
        if(targetType.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T
            && targetType is INamedTypeSymbol nullableTarget)
        {
            var underlyingType = nullableTarget.TypeArguments.FirstOrDefault();
            if(underlyingType is not null && SymbolEqualityComparer.Default.Equals(underlyingType, sourceType))
                return true;
        }

        // Handle object type - any type is assignable to object
        if(targetType.SpecialType is SpecialType.System_Object)
            return true;

        // Handle inheritance - target type should be a base type or interface of source type
        if(sourceType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, targetType)))
            return true;

        var currentBase = sourceType.BaseType;
        while(currentBase is not null)
        {
            if(SymbolEqualityComparer.Default.Equals(currentBase, targetType))
                return true;
            currentBase = currentBase.BaseType;
        }

        return false;
    }
}