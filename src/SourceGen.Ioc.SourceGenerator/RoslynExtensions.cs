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

        public bool IsGenericTypeDefinition => typeSymbol is INamedTypeSymbol { IsGenericType: true, IsDefinition: true };

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

        public bool IsNullable => !typeSymbol.IsValueType || typeSymbol.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T;

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

        public TypeData GetTypeData()
        {
            var typeName = typeSymbol.FullyQualifiedName;
            int arity = (typeSymbol as INamedTypeSymbol)?.Arity ?? 0;
            bool isNestedOpenGeneric = typeSymbol.IsNestedOpenGeneric;
            return new TypeData(typeName, GetNameWithoutGeneric(typeName), typeSymbol.ContainsGenericParameters, arity, isNestedOpenGeneric);
        }

        /// <summary>
        /// Gets all interfaces implemented by a type.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllInterfaces()
        {
            List<TypeData> result = [];
            foreach(var iface in typeSymbol.AllInterfaces)
            {
                var typeName = iface.FullyQualifiedName;
                bool isNestedOpenGeneric = iface.IsNestedOpenGeneric;
                result.Add(new(typeName, GetNameWithoutGeneric(typeName), iface.ContainsGenericParameters, iface.Arity, isNestedOpenGeneric));
            }
            return result.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Gets all base classes of a type, excluding System.Object.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllBaseClasses()
        {
            List<TypeData> result = [];
            var baseType = typeSymbol.BaseType;
            while(baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                var typeName = baseType.FullyQualifiedName;
                bool isNestedOpenGeneric = baseType.IsNestedOpenGeneric;
                result.Add(new(typeName, GetNameWithoutGeneric(typeName), baseType.ContainsGenericParameters, baseType.Arity, isNestedOpenGeneric));
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
                if(typeSymbol is not INamedTypeSymbol namedType || !namedType.IsGenericType)
                {
                    return false;
                }

                // Check if any type argument contains generic parameters
                foreach(var typeArg in namedType.TypeArguments)
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
        public ImmutableEquatableArray<TypeData> GetTypeArrayArgument(string name)
        {
            foreach(var namedArg in attributeData.NamedArguments)
            {
                if(namedArg.Key == name && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<TypeData> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is ITypeSymbol typeSymbol)
                        {
                            var typeName = typeSymbol.FullyQualifiedName;
                            int arity = (typeSymbol as INamedTypeSymbol)?.Arity ?? 0;
                            result.Add(new(typeName, GetNameWithoutGeneric(typeName), typeSymbol.ContainsGenericParameters, arity));
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
}
