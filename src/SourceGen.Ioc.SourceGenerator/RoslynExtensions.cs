using System.Globalization;
using System.Text;

namespace SourceGen.Ioc.SourceGenerator;

/// <summary>
/// Extension methods for Roslyn symbol manipulation.
/// </summary>
internal static class RoslynExtensions
{
    extension(ITypeSymbol typeSymbol)
    {
        /// <summary>
        /// Gets the fully qualified name of a type symbol.
        /// </summary>
        public string FullyQualifiedName => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        public bool IsNullable => !typeSymbol.IsValueType || typeSymbol.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T;

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

        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null)
        {
            if(typeSymbol is INamedTypeSymbol namedTypeSymbol)
                return namedTypeSymbol.GetTypeData(extractConstructorParams, extractHierarchy, visited);

            var typeName = typeSymbol.FullyQualifiedName;
            return new TypeData(
                typeName,
                GetNameWithoutGeneric(typeName),
                typeSymbol.ContainsGenericParameters,
                0,
                false);
        }
    }

    extension(INamedTypeSymbol typeSymbol)
    {
        public bool IsGenericTypeDefinition => typeSymbol is { IsGenericType: true, IsDefinition: true };

        /// <summary>
        /// Gets the type data for this type symbol.
        /// </summary>
        /// <param name="extractConstructorParams">Whether to extract constructor parameters recursively.</param>
        /// <param name="extractHierarchy">Whether to extract all interfaces and base classes.</param>
        public TypeData GetTypeData(
            bool extractConstructorParams = false,
            bool extractHierarchy = false,
            HashSet<INamedTypeSymbol>? visited = null)
        {
            visited = extractConstructorParams ? new(SymbolEqualityComparer.Default) : null;

            // For unbound generic types (e.g., typeof(Handler<,>)), we need to get the 
            // type parameter names from TypeParameters, not from FullyQualifiedName
            // FullyQualifiedName returns "global::Ns.Handler<,>" but we need "global::Ns.Handler<TRequest, TResponse>"
            // Note: This is different from constructed generic types (e.g., IRepository<T>) which already have proper names.
            string typeName;
            ImmutableEquatableArray<string>? typeParamNamesArray = null;

            // Get the type parameters from the original definition for unbound generics
            var typeParamsSource = typeSymbol.IsUnboundGenericType
                ? typeSymbol.OriginalDefinition?.TypeParameters ?? typeSymbol.TypeParameters
                : typeSymbol.TypeParameters;

            if(typeSymbol.IsUnboundGenericType && typeParamsSource.Length > 0)
            {
                // Build the type name with actual type parameter names for unbound generics
                var nameWithoutGeneric = GetNameWithoutGeneric(typeSymbol.FullyQualifiedName);
                var typeParamNames = typeParamsSource.Select(tp => tp.Name).ToArray();
                typeParamNamesArray = typeParamNames.ToImmutableEquatableArray();
                typeName = $"{nameWithoutGeneric}<{string.Join(", ", typeParamNames)}>";
            }
            else
            {
                typeName = typeSymbol.FullyQualifiedName;
                // For open generic types that contain type parameters, extract the type parameter names
                if(typeSymbol.ContainsGenericParameters && typeSymbol.IsGenericType)
                {
                    var typeParams = typeSymbol.TypeArguments
                        .OfType<ITypeParameterSymbol>()
                        .Select(tp => tp.Name)
                        .ToArray();
                    if(typeParams.Length > 0)
                    {
                        typeParamNamesArray = typeParams.ToImmutableEquatableArray();
                    }
                }
            }

            int arity = typeSymbol.Arity;
            bool isNestedOpenGeneric = typeSymbol.IsNestedOpenGeneric;

            // Extract generic arguments for closed generic types
            ImmutableEquatableArray<string>? genericArguments = null;
            if(typeSymbol.IsGenericType && !typeSymbol.ContainsGenericParameters && typeSymbol.TypeArguments.Length > 0)
            {
                genericArguments = typeSymbol.TypeArguments
                    .Select(arg => arg.FullyQualifiedName)
                    .ToImmutableEquatableArray();
            }

            ImmutableEquatableArray<ConstructorParameterData>? constructorParams = null;
            if(extractConstructorParams && visited != null)
            {
                constructorParams = typeSymbol.ExtractConstructorParameters(visited);
            }

            // Extract hierarchy (interfaces and base classes) if requested
            ImmutableEquatableArray<TypeData>? allInterfaces = null;
            ImmutableEquatableArray<TypeData>? allBaseClasses = null;
            if(extractHierarchy)
            {
                allInterfaces = typeSymbol.GetAllInterfaces();
                allBaseClasses = typeSymbol.GetAllBaseClasses();
            }

            return new TypeData(
                typeName,
                GetNameWithoutGeneric(typeName),
                typeSymbol.ContainsGenericParameters,
                arity,
                isNestedOpenGeneric,
                typeParamNamesArray,
                genericArguments,
                constructorParams,
                allInterfaces,
                allBaseClasses);
        }

        /// <summary>
        /// Gets all interfaces implemented by a type.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllInterfaces() =>
            typeSymbol.AllInterfaces.Select(i => i.GetTypeData()).ToImmutableEquatableArray();

        /// <summary>
        /// Gets all base classes of a type, excluding System.Object.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllBaseClasses()
        {
            List<TypeData> result = [];
            var baseType = typeSymbol.BaseType;
            while(baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                result.Add(baseType.GetTypeData());
                baseType = baseType.BaseType;
            }
            return result.ToImmutableEquatableArray();
        }

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
                foreach(var constructor in typeSymbol.Constructors)
                {
                    if(constructor.IsImplicitlyDeclared)
                        continue;

                    var syntaxRef = constructor.DeclaringSyntaxReferences.FirstOrDefault();
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                        return constructor;
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

                    if(ctor.IsStatic)
                        continue;

                    if(ctor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                        continue;

                    var syntaxRef = ctor.DeclaringSyntaxReferences.FirstOrDefault();
                    if(syntaxRef?.GetSyntax() is TypeDeclarationSyntax)
                        return ctor;

                    if(ctor.Parameters.Length > maxParameters)
                    {
                        maxParameters = ctor.Parameters.Length;
                        bestCtor = ctor;
                    }
                }
                return bestCtor;
            }
        }

        /// <summary>
        /// Extracts constructor parameters from a type.
        /// </summary>
        public ImmutableEquatableArray<ConstructorParameterData> ExtractConstructorParameters(
            HashSet<INamedTypeSymbol>? visited = null)
        {
            // Check if we've already visited this type to prevent infinite recursion
            if(visited is not null && !visited.Add(typeSymbol))
            {
                return [];
            }

            // Get the original definition for open generic types to access constructors
            var typeToInspect = typeSymbol.IsGenericType && typeSymbol.IsDefinition
                ? typeSymbol
                : typeSymbol.OriginalDefinition ?? typeSymbol;

            // Get the primary constructor or the constructor with most parameters
            var constructor = typeToInspect.PrimaryOrMostParametersConstructor;
            if(constructor is null)
            {
                return [];
            }

            visited ??= new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            List<ConstructorParameterData> parameters = [];
            foreach(var param in constructor.Parameters)
            {
                var paramType = param.Type;

                // Get TypeData using the unified method with recursive constructor extraction
                var paramTypeData = paramType is INamedTypeSymbol namedParamType
                    ? namedParamType.GetTypeData(extractConstructorParams: true, visited: visited)
                    : paramType.GetTypeData();

                parameters.Add(new ConstructorParameterData(param.Name, paramTypeData));
            }

            return parameters.ToImmutableEquatableArray();
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
        /// Gets an array of type symbols from a named argument.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgument(string name, bool extractConstructorParams = false)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key.Equals(name, StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is ITypeSymbol typeSymbol)
                        {
                            result.Add(typeSymbol.GetTypeData(extractConstructorParams));
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }
    }

    extension(TypedConstant constant)
    {
        public string GetPrimitiveConstantString() => FormatPrimitiveConstant(constant.Type, constant.Value);
    }

    private static string GetNameWithoutGeneric(string typeName)
    {
        int angleIndex = typeName.IndexOf('<');
        return angleIndex > 0 ? typeName[..angleIndex] : typeName;
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
}
