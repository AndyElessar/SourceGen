using System.Globalization;

namespace SourceGen.Ioc.SourceGenerator;

/// <summary>
/// Extension methods for Roslyn symbol manipulation.
/// </summary>
internal static class RoslynExtensions
{
    /// <param name="symbol">The symbol</param>
    extension(ISymbol symbol)
    {
        /// <summary>
        /// Builds the fully qualified access path for a symbol, including namespace and containing types. <br/>
        /// For example, for a field <c>Key</c> inside <c>NestClassImpl</c> inside <c>TestNestClass</c> in namespace <c>MyApp.Services</c>,
        /// returns <c>global::MyApp.Services.TestNestClass.NestClassImpl.Key</c>.
        /// </summary>
        /// <returns>The fully qualified access path for the symbol.</returns>
        public string FullAccessPath
        {
            get
            {
                // Build path from the symbol up to its containing types (collect in reverse order, then reverse)
                List<string> pathParts = [symbol.Name];

                var containingType = symbol.ContainingType;
                while(containingType is not null)
                {
                    pathParts.Add(containingType.Name);
                    containingType = containingType.ContainingType;
                }

                // Reverse to get correct order (outermost type first)
                pathParts.Reverse();

                // Add namespace prefix with global::
                var containingNamespace = symbol.ContainingType?.ContainingNamespace ?? symbol.ContainingNamespace;
                if(containingNamespace is not null && !containingNamespace.IsGlobalNamespace)
                {
                    var namespacePath = containingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return $"{namespacePath}.{string.Join(".", pathParts)}";
                }

                // For global namespace, just prepend global::
                return $"global::{string.Join(".", pathParts)}";
            }
        }
    }

    extension(ITypeSymbol typeSymbol)
    {
        /// <summary>
        /// Gets the fully qualified name of a type symbol.
        /// </summary>
        public string FullyQualifiedName => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        public bool IsNullable => !typeSymbol.IsValueType || typeSymbol.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T;

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
        /// Determines whether the type is a built-in type or an array/collection of built-in types.
        /// </summary>
        public bool IsBuiltInTypeOrBuiltInCollection
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
        /// For unbound generic types, returns parameters from the original definition.
        /// </summary>
        public ImmutableArray<ITypeParameterSymbol> TypeParametersSource =>
            typeSymbol.IsUnboundGenericType
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

        public IMethodSymbol? PrimaryConstructor
        {
            get
            {
                foreach(var ctor in typeSymbol.Constructors)
                {
                    if(ctor.IsImplicitlyDeclared)
                        continue;

                    var syntaxRef = ctor.DeclaringSyntaxReferences.FirstOrDefault();
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                        return ctor;
                }

                return null;
            }
        }

        public IMethodSymbol? PrimaryOrMostParametersConstructor
        {
            get
            {
                IMethodSymbol? bestCtor = null;
                int maxParameters = -1;
                foreach(var ctor in typeSymbol.Constructors)
                {
                    if(ctor.IsImplicitlyDeclared)
                        continue;

                    var syntaxRef = ctor.DeclaringSyntaxReferences.FirstOrDefault();
                    // Primary constructor
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                        return ctor;

                    if(ctor.IsStatic)
                        continue;

                    if(ctor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                        continue;

                    // Find constructor with most parameters
                    if(ctor.Parameters.Length > maxParameters)
                    {
                        maxParameters = ctor.Parameters.Length;
                        bestCtor = ctor;
                    }
                }
                return bestCtor;
            }
        }
    }

    extension(AttributeData attributeData)
    {
        /// <summary>
        /// Gets a named argument value from an attribute data.
        /// </summary>
        public T? GetNamedArgument<T>(string name, T? defaultValue = default)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name)
                {
                    if(namedArg.Value.IsNull)
                    {
                        return defaultValue;
                    }

                    return (T?)namedArg.Value.Value;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Tries to get a named argument value from an attribute data.<br/>
        /// If the argument is not found, returns HasArg = false.
        /// </summary>
        public (bool HasArg, T? Value) TryGetNamedArgument<T>(string name, T? defaultValue = default)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name)
                {
                    if(namedArg.Value.IsNull)
                    {
                        return (true, defaultValue);
                    }

                    return (true, (T?)namedArg.Value.Value);
                }
            }

            return (false, defaultValue);
        }

        /// <summary>
        /// Checks if a named argument was explicitly set in an attribute.
        /// </summary>
        public bool HasNamedArgument(string name)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to get the original syntax for a named argument, especially for <see langword="nameof"/> expressions.
        /// When a <see cref="SemanticModel"/> is provided, resolves the full access path of the referenced symbol.
        /// </summary>
        /// <param name="argumentName">The name of the argument to find.</param>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The resolved symbol path if it's a <see langword="nameof"/> expression; otherwise, null.</returns>
        public string? TryGetNameof(string argumentName, SemanticModel? semanticModel = null)
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

                            // If semantic model is provided, try to resolve the full access path
                            if(semanticModel is not null)
                            {
                                var resolvedPath = ResolveNameofExpression(nameofArgument, semanticModel);
                                if(resolvedPath is not null)
                                    return resolvedPath;
                            }

                            return nameofArgument.ToFullString().Trim();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Tries to extract the <see langword="nameof"/> expression from a constructor argument of an attribute.
        /// </summary>
        /// <param name="argumentIndex">The index of the constructor argument to check.</param>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The resolved symbol path if it's a <see langword="nameof"/> expression; otherwise, null.</returns>
        public string? TryGetNameofFromConstructorArg(int argumentIndex, SemanticModel? semanticModel = null)
        {
            var syntaxReference = attributeData.ApplicationSyntaxReference;
            if(syntaxReference is null)
                return null;

            var syntax = syntaxReference.GetSyntax();
            if(syntax is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null || argumentList.Arguments.Count <= argumentIndex)
                return null;

            var argument = argumentList.Arguments[argumentIndex];

            // Skip named arguments (they don't count as constructor arguments)
            if(argument.NameEquals is not null)
                return null;

            // Check if the expression is a nameof() invocation
            if(argument.Expression is InvocationExpressionSyntax invocation &&
               invocation.Expression is IdentifierNameSyntax identifierName &&
               identifierName.Identifier.Text == "nameof")
            {
                // Extract the argument inside nameof() and return just that expression
                if(invocation.ArgumentList.Arguments.Count == 1)
                {
                    var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;

                    // If semantic model is provided, try to resolve the full access path
                    if(semanticModel is not null)
                    {
                        var resolvedPath = ResolveNameofExpression(nameofArgument, semanticModel);
                        if(resolvedPath is not null)
                            return resolvedPath;
                    }

                    return nameofArgument.ToFullString().Trim();
                }
            }

            return null;
        }
    }

    extension(TypedConstant constant)
    {
        public string GetPrimitiveConstantString() => FormatPrimitiveConstant(constant.Type, constant.Value);
    }

    public static string FormatPrimitiveConstant(ITypeSymbol? type, object? value)
    {
        if(type?.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
        {
            var elementType = ((INamedTypeSymbol)type).TypeArguments[0];
            return value is null ? "null" : FormatPrimitiveConstant(elementType, value);
        }

        if(type?.TypeKind is TypeKind.Enum)
        {
            return FormatEnumLiteral((INamedTypeSymbol)type, value!);
        }

        return value switch
        {
            null => type?.IsNullable is null or true ? "null!" : "default",
            false => "false",
            true => "true",

            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            char c => SymbolDisplay.FormatLiteral(c, quote: true),

            double.NaN => "double.NaN",
            double.NegativeInfinity => "double.NegativeInfinity",
            double.PositiveInfinity => "double.PositiveInfinity",
            double d => $"{d.ToString("G17", CultureInfo.InvariantCulture)}d",

            float.NaN => "float.NaN",
            float.NegativeInfinity => "float.NegativeInfinity",
            float.PositiveInfinity => "float.PositiveInfinity",
            float f => $"{f.ToString("G9", CultureInfo.InvariantCulture)}f",

            decimal d => $"{d.ToString(CultureInfo.InvariantCulture)}m",

            // Must be one of the other numeric types or an enum
            object num => Convert.ToString(num, CultureInfo.InvariantCulture),
        };

        static string FormatEnumLiteral(INamedTypeSymbol enumType, object value)
        {
            Debug.Assert(enumType.TypeKind is TypeKind.Enum);

            foreach(ISymbol member in enumType.GetMembers())
            {
                if(member is IFieldSymbol { IsConst: true, ConstantValue: { } constantValue } field)
                {
                    if(Equals(constantValue, value))
                    {
                        return FormatEnumField(field);
                    }
                }
            }

            bool isFlagsEnum = enumType.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == "FlagsAttribute" &&
                attr.AttributeClass.ContainingNamespace.ToDisplayString() == "System");

            if(isFlagsEnum)
            {
                // Convert the value to ulong for bitwise operations
                ulong numericValue = ConvertToUInt64(value);
                var fields = enumType.GetMembers().OfType<IFieldSymbol>()
                    .Select((f, i) => (Index: i, Symbol: f, NumericValue: ConvertToUInt64(f.ConstantValue!)))
                    .ToArray();

                // Check for any zero numeric values.
                if(numericValue == 0)
                {
                    foreach(var field in fields)
                    {
                        if(field.NumericValue == 0)
                        {
                            return FormatEnumField(field.Symbol);
                        }
                    }
                }
                else
                {
                    List<int>? matches = null;
                    foreach(var field in fields.OrderByDescending(f => f.NumericValue))
                    {
                        // Greedy match of flag values from highest to lowest numeric value.
                        if(field.NumericValue != 0 && (numericValue & field.NumericValue) == field.NumericValue)
                        {
                            (matches ??= []).Add(field.Index);
                            numericValue &= ~field.NumericValue;
                            if(numericValue == 0)
                            {
                                break; // All bits accounted for
                            }
                        }
                    }

                    if(numericValue == 0)
                    {
                        matches!.Sort(); // Format components using the original declaration order.
                        return string.Join(" | ", matches.Select(i => FormatEnumField(fields[i].Symbol)));
                    }
                }
            }

            // Value does not correspond to any combination of defined constants, just cast the numeric value.
            return $"({enumType.FullyQualifiedName})({Convert.ToString(value, CultureInfo.InvariantCulture)!})";

            static string FormatEnumField(IFieldSymbol field)
            {
                return $"{field.ContainingType.FullyQualifiedName}.{field.Name}";
            }

            static ulong ConvertToUInt64(object value)
            {
                return value switch
                {
                    byte b => b,
                    sbyte sb => (ulong)sb,
                    short s => (ulong)s,
                    ushort us => us,
                    char c => c,
                    int i => (ulong)i,
                    uint ui => ui,
                    long l => (ulong)l,
                    ulong ul => ul,
                    _ => 0
                };
            }
        }
    }

    public static string GetSafeNamespace(string name) => string.IsNullOrWhiteSpace(name) ? "Generated" : name;

    public static string GetSafeMethodName(string name)
    {
        if(string.IsNullOrWhiteSpace(name))
            return "Generated";

        StringBuilder builder = new(name.Length + 1);
        for(int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if(i == 0 && char.IsDigit(ch))
            {
                builder.Append('_');
            }

            if(char.IsLetterOrDigit(ch) || ch == '_')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('_');
            }
        }
        return builder.ToString();
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

    public static bool IsEnumerableType(string nameWithoutGeneric) =>
        nameWithoutGeneric is "global::System.Collections.Generic.IEnumerable" or "System.Collections.Generic.IEnumerable" or "IEnumerable";

    /// <summary>
    /// Read-only collection type names (without generic part).
    /// These should be resolved using GetServices&lt;T&gt;().ToArray().
    /// </summary>
    private static readonly HashSet<string> s_readOnlyCollectionTypes = new(StringComparer.Ordinal)
    {
        "global::System.Collections.Generic.IReadOnlyCollection",
        "global::System.Collections.Generic.IReadOnlyList",
        "System.Collections.Generic.IReadOnlyCollection",
        "System.Collections.Generic.IReadOnlyList",
        "IReadOnlyCollection",
        "IReadOnlyList"
    };
    public static bool IsReadOnlyCollectionType(string nameWithoutGeneric) =>
        s_readOnlyCollectionTypes.Contains(nameWithoutGeneric);

    /// <summary>
    /// Resolves the full access path of a symbol referenced in a nameof() expression.
    /// For example, resolves <c>nameof(Key)</c> to <c>global::Namespace.OuterClass.InnerClass.Key</c>
    /// when Key is a member of InnerClass inside OuterClass.
    /// </summary>
    /// <param name="expression">The expression inside nameof().</param>
    /// <param name="semanticModel">The semantic model to use for symbol resolution.</param>
    /// <returns>The full access path if successfully resolved; otherwise, null.</returns>
    public static string? ResolveNameofExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if(symbol is null)
            return null;

        // If the expression already contains a member access (e.g., KeyHolder.Key),
        // we need to resolve it to ensure we get the fully qualified path
        return symbol.FullAccessPath;
    }

    /// <summary>
    /// Resolves the method symbol from a nameof() or string expression in an attribute.
    /// </summary>
    /// <param name="expression">The expression inside nameof() or a string literal.</param>
    /// <param name="semanticModel">The semantic model to use for symbol resolution.</param>
    /// <returns>The method symbol if found; otherwise, null.</returns>
    public static IMethodSymbol? ResolveMethodSymbol(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        return symbol as IMethodSymbol;
    }

    extension<T>(IEnumerable<T> source)
    {
        public IEnumerable<(int Index, T Item)> Index()
        {
            int index = 0;
            foreach(var item in source)
            {
                yield return (index, item);
                checked { index++; }
            }
        }
    }

    extension<T>(IReadOnlyList<T> source)
    {
        public IEnumerable<(int Index, T Item)> Index()
        {
            for(int i = 0; i < source.Count; i++)
            {
                yield return (i, source[i]);
            }
        }
    }

    #region Type Parameter Substitution

    /// <summary>
    /// Substitutes multiple type parameters in a type name with actual type arguments.
    /// Uses Span-based processing to minimize string allocations.
    /// </summary>
    /// <param name="typeName">The type name containing type parameters to substitute.</param>
    /// <param name="typeArgMap">A map of type parameter names to their actual type arguments.</param>
    /// <returns>The type name with all type parameters substituted.</returns>
    public static string SubstituteTypeArguments(string typeName, TypeArgMap typeArgMap)
    {
        if(typeArgMap.IsEmpty)
        {
            return typeName;
        }

        // Fast path: check if any substitution is needed
        var typeNameSpan = typeName.AsSpan();
        bool needsSubstitution = false;
        foreach(var (key, _) in typeArgMap)
        {
            if(ContainsTypeParameter(typeNameSpan, key.AsSpan()))
            {
                needsSubstitution = true;
                break;
            }
        }

        if(!needsSubstitution)
        {
            return typeName;
        }

        return SubstituteTypeArgumentsCore(typeNameSpan, typeArgMap.AsSpan());
    }

    /// <summary>
    /// Replaces a single type parameter with an actual type argument in a type name.
    /// </summary>
    /// <param name="typeName">The type name containing the type parameter.</param>
    /// <param name="typeParam">The type parameter name to replace (e.g., "T").</param>
    /// <param name="actualArg">The actual type argument to substitute (e.g., "string").</param>
    /// <returns>The type name with the type parameter replaced.</returns>
    public static string ReplaceTypeParameter(string typeName, string typeParam, string actualArg)
    {
        var typeNameSpan = typeName.AsSpan();

        // Fast path: check if substitution is needed
        if(!ContainsTypeParameter(typeNameSpan, typeParam.AsSpan()))
        {
            return typeName;
        }

        // Delegate to core implementation with single-element span
        Span<TypeArgEntry> singleEntry = [new(typeParam, actualArg)];
        return SubstituteTypeArgumentsCore(typeNameSpan, singleEntry);
    }

    /// <summary>
    /// Core implementation for type parameter substitution.
    /// Performs all substitutions in a single pass using StringBuilder.
    /// </summary>
    /// <param name="typeNameSpan">The type name span to process.</param>
    /// <param name="sortedEntries">Entries sorted by key length descending for correct matching priority.</param>
    /// <returns>The type name with all type parameters substituted.</returns>
    private static string SubstituteTypeArgumentsCore(
        ReadOnlySpan<char> typeNameSpan,
        ReadOnlySpan<TypeArgEntry> sortedEntries)
    {
        var result = new StringBuilder(typeNameSpan.Length + 32);
        int i = 0;

        while(i < typeNameSpan.Length)
        {
            // Check if current position is a valid identifier start
            bool isValidStart = i == 0 || !IsIdentifierChar(typeNameSpan[i - 1]);

            if(isValidStart && TryMatchTypeParameter(typeNameSpan, i, sortedEntries, out var match, out int matchLength))
            {
                result.Append(match);
                i += matchLength;
            }
            else
            {
                result.Append(typeNameSpan[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Tries to match a type parameter at the given position.
    /// </summary>
    /// <returns>True if a match was found, with the replacement value and match length.</returns>
    private static bool TryMatchTypeParameter(
        ReadOnlySpan<char> typeNameSpan,
        int position,
        ReadOnlySpan<TypeArgEntry> sortedEntries,
        [NotNullWhen(true)] out string? replacement,
        out int matchLength)
    {
        foreach(var (key, value) in sortedEntries)
        {
            var typeParamSpan = key.AsSpan();
            int paramLength = typeParamSpan.Length;

            if(position + paramLength <= typeNameSpan.Length &&
               typeNameSpan.Slice(position, paramLength).SequenceEqual(typeParamSpan))
            {
                // Check if it's a whole word (ends at identifier boundary)
                bool isEnd = position + paramLength == typeNameSpan.Length
                                || !IsIdentifierChar(typeNameSpan[position + paramLength]);

                if(isEnd)
                {
                    replacement = value;
                    matchLength = paramLength;
                    return true;
                }
            }
        }

        replacement = null;
        matchLength = 0;
        return false;
    }

    /// <summary>
    /// Checks if the type name contains the type parameter as a whole word.
    /// </summary>
    private static bool ContainsTypeParameter(ReadOnlySpan<char> typeName, ReadOnlySpan<char> typeParam)
    {
        int index = 0;
        while(index <= typeName.Length - typeParam.Length)
        {
            int pos = typeName[index..].IndexOf(typeParam, StringComparison.Ordinal);
            if(pos < 0)
            {
                return false;
            }

            int absolutePos = index + pos;
            bool isStart = absolutePos == 0
                            || !IsIdentifierChar(typeName[absolutePos - 1]);
            bool isEnd = absolutePos + typeParam.Length == typeName.Length
                            || !IsIdentifierChar(typeName[absolutePos + typeParam.Length]);

            if(isStart && isEnd)
            {
                return true;
            }

            index = absolutePos + 1;
        }
        return false;
    }

    /// <summary>
    /// Checks if a character can be part of an identifier.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion
}
