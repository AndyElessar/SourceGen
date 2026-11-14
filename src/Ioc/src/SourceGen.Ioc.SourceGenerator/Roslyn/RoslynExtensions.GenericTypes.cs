namespace SourceGen.Ioc;

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

                // An array or pointer of a type parameter, such as T[] in INode<T[]>, is open too
                if(typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
                {
                    return arrayTypeSymbol.ElementType.ContainsGenericParameters;
                }

                if(typeSymbol is IPointerTypeSymbol pointerTypeSymbol)
                {
                    return pointerTypeSymbol.PointedAtType.ContainsGenericParameters;
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
    }

    extension(INamedTypeSymbol typeSymbol)
    {
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

    extension(ISymbol member)
    {
        /// <summary>
        /// Gets this member as a member of <paramref name="type"/>, so its type uses the type arguments of
        /// <paramref name="type"/>: <c>Node&lt;T&gt;.Repo</c> for <c>Node&lt;int&gt;.Repo</c> named by
        /// <c>nameof(Node&lt;int&gt;.Repo)</c>, the member of a base class as <paramref name="type"/> inherits it, and the
        /// implementation of an interface member, such as <c>Node&lt;T&gt;.Repo</c> for <c>INode&lt;int&gt;.Repo</c> (an
        /// explicit implementation for an explicitly implemented member).
        /// </summary>
        /// <param name="type">The registered type; an unbound generic type is read as its definition.</param>
        /// <returns><see langword="null"/> when <paramref name="type"/> does not have or implement the member.</returns>
        public ISymbol? AsMemberOf(INamedTypeSymbol type)
        {
            if(member.ContainingType is not { } containingType)
            {
                return null;
            }

            var target = type.IsUnboundGenericType ? type.OriginalDefinition : type;
            if(containingType.TypeKind == TypeKind.Interface)
            {
                // The interface as the type implements it: the same one first, otherwise the first with its definition
                INamedTypeSymbol? implemented = null;
                foreach(var iface in target.AllInterfaces)
                {
                    if(SymbolEqualityComparer.Default.Equals(iface, containingType))
                    {
                        implemented = iface;
                        break;
                    }

                    if(implemented is null && SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, containingType.OriginalDefinition))
                    {
                        implemented = iface;
                    }
                }

                // A default interface implementation is not a member of the type
                return implemented is not null
                    && FindMember(implemented) is { } interfaceMember
                    && target.FindImplementationForInterfaceMember(interfaceMember) is { ContainingType.TypeKind: not TypeKind.Interface } implementation
                        ? implementation
                        : null;
            }

            for(var current = target; current is not null; current = current.BaseType)
            {
                if(SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, containingType.OriginalDefinition))
                {
                    return FindMember(current);
                }
            }

            return null;

            ISymbol? FindMember(INamedTypeSymbol owner)
            {
                var memberDefinition = member.OriginalDefinition;
                foreach(var candidate in owner.GetMembers(member.Name))
                {
                    if(SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, memberDefinition))
                    {
                        return candidate;
                    }
                }

                return null;
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