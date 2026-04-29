namespace SourceGen.Ioc.SourceGenerator;

internal static partial class RoslynExtensions
{
    extension(ITypeSymbol typeSymbol)
    {
        /// <summary>
        /// Determines whether the type is a built-in/primitive type that cannot be resolved from dependency injection.
        /// This includes numeric types, string, bool, char, DateTime, Guid, TimeSpan, Uri, Type, etc.
        /// </summary>
        public bool IsBuiltInType
        {
            get
            {
                // Check if it's a special type (primitives, string, object, etc.)
                var specialType = typeSymbol.SpecialType;
                if(specialType is not SpecialType.None)
                {
                    // These special types are built-in and cannot be resolved from DI
                    return specialType is
                        SpecialType.System_Boolean or
                        SpecialType.System_Char or
                        SpecialType.System_SByte or
                        SpecialType.System_Byte or
                        SpecialType.System_Int16 or
                        SpecialType.System_UInt16 or
                        SpecialType.System_Int32 or
                        SpecialType.System_UInt32 or
                        SpecialType.System_Int64 or
                        SpecialType.System_UInt64 or
                        SpecialType.System_Decimal or
                        SpecialType.System_Single or
                        SpecialType.System_Double or
                        SpecialType.System_String or
                        SpecialType.System_IntPtr or
                        SpecialType.System_UIntPtr or
                        SpecialType.System_Object or
                        SpecialType.System_DateTime;
                }

                // Check for common System types by name
                if(typeSymbol.ContainingNamespace?.ToDisplayString() is "System")
                {
                    return typeSymbol.Name is
                        "Guid" or
                        "TimeSpan" or
                        "DateTimeOffset" or
                        "DateOnly" or
                        "TimeOnly" or
                        "Uri" or
                        "Type" or
                        "Version" or
                        "Half" or
                        "Int128" or
                        "UInt128";
                }

                return false;
            }
        }

        /// <summary>
        /// Determines whether the type is a built-in type, or an array/collection whose element type is built-in.
        /// </summary>
        public bool IsBuiltInTypeOrBuiltInElement
        {
            get
            {
                // Check if it's directly a built-in type
                if(typeSymbol.IsBuiltInType)
                    return true;

                // Check if it's an array of built-in type
                if(typeSymbol is IArrayTypeSymbol arrayType)
                    return arrayType.ElementType.IsBuiltInType;

                // Check if it's a generic collection of built-in type
                if(typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
                {
                    var typeArgs = namedType.TypeArguments;
                    if(typeArgs.Length == 1)
                    {
                        var elementType = typeArgs[0];
                        // Check if the element type is a built-in type
                        return elementType.IsBuiltInType;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Attempts to classify the given type symbol as a supported wrapper type and extract its element type.
        /// </summary>
        public bool TryGetWrapperInfo(out WrapperInfo wrapperInfo)
        {
            // Array: T[]
            if(typeSymbol is IArrayTypeSymbol arrayType)
            {
                wrapperInfo = new(WrapperKind.Array, arrayType.ElementType as INamedTypeSymbol);
                return true;
            }

            if(typeSymbol is not INamedTypeSymbol namedType)
            {
                wrapperInfo = default;
                return false;
            }

            // Func<T>, Func<T1, TResult>, ... (last type argument is return type)
            if(namedType.Arity >= 1
                && IsType(namedType, "System", "Func"))
            {
                wrapperInfo = new(WrapperKind.Func, namedType.TypeArguments[^1] as INamedTypeSymbol);
                return true;
            }

            // Arity-2 wrappers where TValue is the element type.
            if(namedType.Arity == 2
                && IsTypeInNamespace(namedType, "System.Collections.Generic"))
            {
                var kind = namedType.Name switch
                {
                    "IDictionary" or "IReadOnlyDictionary" or "Dictionary" => WrapperKind.Dictionary,
                    "KeyValuePair" => WrapperKind.KeyValuePair,
                    _ => WrapperKind.None
                };

                if(kind is not WrapperKind.None)
                {
                    wrapperInfo = new(kind, namedType.TypeArguments[1] as INamedTypeSymbol);
                    return true;
                }
            }

            // Arity-1 wrappers where T is the element type.
            if(namedType.Arity == 1)
            {
                var elementType = namedType.TypeArguments[0] as INamedTypeSymbol;

                if(namedType.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    wrapperInfo = new(WrapperKind.Enumerable, elementType);
                    return true;
                }

                if(IsTypeInNamespace(namedType, "System.Collections.Generic"))
                {
                    var kind = namedType.Name switch
                    {
                        "IReadOnlyCollection" => WrapperKind.ReadOnlyCollection,
                        "ICollection" => WrapperKind.Collection,
                        "IReadOnlyList" => WrapperKind.ReadOnlyList,
                        "IList" => WrapperKind.List,
                        _ => WrapperKind.None
                    };

                    if(kind is not WrapperKind.None)
                    {
                        wrapperInfo = new(kind, elementType);
                        return true;
                    }
                }

                if(IsType(namedType, "System", "Lazy"))
                {
                    wrapperInfo = new(WrapperKind.Lazy, elementType);
                    return true;
                }

                if(IsTypeInNamespace(namedType, "System.Threading.Tasks"))
                {
                    var kind = namedType.Name switch
                    {
                        "Task" => WrapperKind.Task,
                        "ValueTask" => WrapperKind.ValueTask,
                        _ => WrapperKind.None
                    };

                    if(kind is not WrapperKind.None)
                    {
                        wrapperInfo = new(kind, elementType);
                        return true;
                    }
                }
            }

            wrapperInfo = default;
            return false;

            static bool IsType(INamedTypeSymbol symbol, string @namespace, string name)
                => symbol.Name == name && IsTypeInNamespace(symbol, @namespace);

            static bool IsTypeInNamespace(INamedTypeSymbol symbol, string @namespace)
                => symbol.ContainingNamespace.ToDisplayString() == @namespace;
        }
    }

    extension(WrapperKind kind)
    {
        /// <summary>
        /// Returns <see langword="true"/> when the wrapper kind is a collection wrapper.
        /// </summary>
        public bool IsCollectionWrapperKind()
            => kind is WrapperKind.Enumerable
                or WrapperKind.ReadOnlyCollection
                or WrapperKind.Collection
                or WrapperKind.ReadOnlyList
                or WrapperKind.List
                or WrapperKind.Array;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the wrapper nesting should be downgraded to unsupported.
    /// </summary>
    public static bool IsUnsupportedWrapperNesting(WrapperKind outerKind, WrapperKind innerKind, bool isAfterCollection)
    {
        // Task<Wrapper>
        if(outerKind is WrapperKind.Task && innerKind is not WrapperKind.None)
            return true;

        // Wrapper<Task>
        if(outerKind is not WrapperKind.Task && innerKind is WrapperKind.Task)
            return true;

        // ValueTask is only allowed at root; nested ValueTask is unsupported.
        if(innerKind is WrapperKind.ValueTask)
            return true;

        // A collection wrapper was seen earlier, and we now have a non-collection wrapper
        // containing another non-collection wrapper. This is 2+ non-collection layers after
        // collection, e.g., IEnumerable<Lazy<Func<T>>>.
        if(isAfterCollection
            && !outerKind.IsCollectionWrapperKind()
            && !innerKind.IsCollectionWrapperKind()
            && innerKind is not WrapperKind.None)
            return true;

        return false;
    }

    public static bool IsEnumerableType(string nameWithoutGeneric) =>
        nameWithoutGeneric is "global::System.Collections.Generic.IEnumerable" or "System.Collections.Generic.IEnumerable" or "IEnumerable";
}