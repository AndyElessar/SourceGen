using System.Globalization;

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

            // Get the type parameters from the original definition for unbound generics
            var typeParamsSource = typeSymbol.IsUnboundGenericType
                ? typeSymbol.OriginalDefinition?.TypeParameters ?? typeSymbol.TypeParameters
                : typeSymbol.TypeParameters;

            if(typeSymbol.IsUnboundGenericType && typeParamsSource.Length > 0)
            {
                // Build the type name with actual type parameter names for unbound generics
                var nameWithoutGeneric = GetNameWithoutGeneric(typeSymbol.FullyQualifiedName);
                var typeParamNames = typeParamsSource.Select(tp => tp.Name).ToArray();
                typeName = $"{nameWithoutGeneric}<{string.Join(", ", typeParamNames)}>";
            }
            else
            {
                typeName = typeSymbol.FullyQualifiedName;
            }

            int arity = typeSymbol.Arity;
            bool isNestedOpenGeneric = typeSymbol.IsNestedOpenGeneric;

            // Extract type parameters (unified type arguments and constraints)
            ImmutableEquatableArray<TypeParameter>? typeParameters = null;
            if(typeSymbol.IsGenericType && typeSymbol.TypeArguments.Length > 0)
            {
                typeParameters = typeSymbol.ExtractTypeParameters();
            }

            ImmutableEquatableArray<ParameterData>? constructorParams = null;
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
                IsTypeParameter: false, // Named types are not type parameters
                typeParameters,
                constructorParams,
                allInterfaces,
                allBaseClasses);
        }

        /// <summary>
        /// Extracts type parameters with their resolved types and constraints.
        /// </summary>
        public ImmutableEquatableArray<TypeParameter> ExtractTypeParameters()
        {
            // Get type parameters from original definition for unbound generic types
            var typeParams = typeSymbol.IsUnboundGenericType
                ? typeSymbol.OriginalDefinition?.TypeParameters ?? typeSymbol.TypeParameters
                : typeSymbol.TypeParameters;

            if(typeParams.Length == 0)
            {
                return [];
            }

            var typeArgs = typeSymbol.TypeArguments;
            List<TypeParameter> parameters = new(typeParams.Length);

            for(int i = 0; i < typeParams.Length; i++)
            {
                var typeParam = typeParams[i];
                var typeArg = i < typeArgs.Length ? typeArgs[i] : null;

                // Get type data - don't use extractHierarchy to avoid circular dependency
                // Instead, directly get AllInterfaces for constraint checking
                TypeData typeData;
                ImmutableEquatableArray<TypeData>? allInterfaces = null;

                if(typeArg is INamedTypeSymbol namedArg && typeArg.TypeKind != TypeKind.TypeParameter)
                {
                    // For concrete types, get basic type data and all interfaces separately
                    typeData = typeArg.GetTypeData();
                    allInterfaces = namedArg.AllInterfaces
                        .Select(iface => new TypeData(
                            iface.FullyQualifiedName,
                            GetNameWithoutGeneric(iface.FullyQualifiedName),
                            iface.ContainsGenericParameters,
                            iface.Arity,
                            false))
                        .ToImmutableEquatableArray();
                }
                else if(typeArg is INamedTypeSymbol namedType)
                {
                    // Use CreateBasicTypeData to avoid circular dependency via ExtractTypeParameters
                    typeData = namedType.CreateBasicTypeData();
                }
                else if(typeArg is not null)
                {
                    var argName = typeArg.FullyQualifiedName;
                    typeData = new TypeData(argName, GetNameWithoutGeneric(argName), typeArg.ContainsGenericParameters, 0, false);
                }
                else
                {
                    typeData = new TypeData(typeParam.Name, typeParam.Name, true, 0, false);
                }

                // If we extracted interfaces, create a new TypeData with them
                if(allInterfaces is not null && allInterfaces.Length > 0)
                {
                    typeData = typeData with { AllInterfaces = allInterfaces };
                }

                // Extract constraints - use basic type data to avoid circular dependencies
                var constraintTypes = typeParam.ConstraintTypes
                    .Select(ct => ct is INamedTypeSymbol namedCt
                        ? namedCt.CreateBasicTypeData()
                        : new TypeData(ct.FullyQualifiedName, GetNameWithoutGeneric(ct.FullyQualifiedName), ct.ContainsGenericParameters, 0, false))
                    .ToImmutableEquatableArray();

                parameters.Add(new TypeParameter(
                    typeParam.Name,
                    typeData,
                    constraintTypes,
                    typeParam.HasValueTypeConstraint,
                    typeParam.HasReferenceTypeConstraint,
                    typeParam.HasUnmanagedTypeConstraint,
                    typeParam.HasNotNullConstraint,
                    typeParam.HasConstructorConstraint));
            }

            return parameters.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Gets all interfaces implemented by a type.
        /// Creates basic TypeData without recursive type parameter extraction to avoid circular dependencies.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllInterfaces() =>
            typeSymbol.AllInterfaces.Select(CreateBasicTypeData).ToImmutableEquatableArray();

        /// <summary>
        /// Gets all base classes of a type, excluding System.Object.
        /// Creates basic TypeData without recursive type parameter extraction to avoid circular dependencies.
        /// </summary>
        public ImmutableEquatableArray<TypeData> GetAllBaseClasses()
        {
            List<TypeData> result = [];
            var baseType = typeSymbol.BaseType;
            while(baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                result.Add(baseType.CreateBasicTypeData());
                baseType = baseType.BaseType;
            }
            return result.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Creates a basic TypeData with type parameters extracted recursively.
        /// </summary>
        public TypeData CreateBasicTypeData(int depth = 0)
        {
            const int MaxDepth = 10; // Prevent infinite recursion for pathological cases

            var typeName = typeSymbol.FullyQualifiedName;

            // Extract type parameters recursively
            ImmutableEquatableArray<TypeParameter>? typeParameters = null;
            if(typeSymbol.IsGenericType && typeSymbol.TypeArguments.Length > 0 && depth < MaxDepth)
            {
                var typeParams = typeSymbol.IsUnboundGenericType
                    ? typeSymbol.OriginalDefinition?.TypeParameters ?? typeSymbol.TypeParameters
                    : typeSymbol.TypeParameters;

                var typeArgs = typeSymbol.TypeArguments;
                List<TypeParameter> parameters = new(typeParams.Length);

                for(int i = 0; i < typeParams.Length; i++)
                {
                    var typeParam = typeParams[i];
                    var typeArg = i < typeArgs.Length ? typeArgs[i] : null;

                    // Create TypeData for the type argument, recursively extracting nested type parameters
                    TypeData typeData;
                    ImmutableEquatableArray<TypeData>? allInterfaces = null;

                    if(typeArg is INamedTypeSymbol argNamed && typeArg.TypeKind != TypeKind.TypeParameter)
                    {
                        // Recursively extract type parameters for nested generics
                        typeData = argNamed.CreateBasicTypeData(depth + 1);

                        // Extract interfaces for concrete types (needed for constraint checking)
                        if(argNamed.AllInterfaces.Length > 0)
                        {
                            allInterfaces = argNamed.AllInterfaces
                                .Select(iface => new TypeData(
                                    iface.FullyQualifiedName,
                                    GetNameWithoutGeneric(iface.FullyQualifiedName),
                                    iface.ContainsGenericParameters,
                                    iface.Arity))
                                .ToImmutableEquatableArray();
                        }

                        // Add interfaces if not already present
                        if(allInterfaces is not null && allInterfaces.Length > 0)
                        {
                            typeData = typeData with { AllInterfaces = allInterfaces };
                        }
                    }
                    else if(typeArg is not null)
                    {
                        var argName = typeArg.FullyQualifiedName;
                        var isTypeParam = typeArg.TypeKind == TypeKind.TypeParameter;
                        typeData = new TypeData(
                            argName,
                            GetNameWithoutGeneric(argName),
                            typeArg.ContainsGenericParameters,
                            0,
                            IsNestedOpenGeneric: false,
                            IsTypeParameter: isTypeParam);
                    }
                    else
                    {
                        // No type argument available, this is a type parameter placeholder
                        typeData = new TypeData(typeParam.Name, typeParam.Name, true, 0, IsNestedOpenGeneric: false, IsTypeParameter: true);
                    }

                    parameters.Add(new TypeParameter(
                        typeParam.Name,
                        typeData,
                        null,  // Skip constraint types to avoid recursion
                        typeParam.HasValueTypeConstraint,
                        typeParam.HasReferenceTypeConstraint,
                        typeParam.HasUnmanagedTypeConstraint,
                        typeParam.HasNotNullConstraint,
                        typeParam.HasConstructorConstraint));
                }

                typeParameters = parameters.ToImmutableEquatableArray();
            }

            return new TypeData(
                typeName,
                GetNameWithoutGeneric(typeName),
                typeSymbol.ContainsGenericParameters,
                typeSymbol.Arity,
                typeSymbol.IsNestedOpenGeneric,
                IsTypeParameter: false, // Named types are not type parameters
                typeParameters);
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
        public ImmutableEquatableArray<ParameterData> ExtractConstructorParameters(
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
            List<ParameterData> parameters = [];
            foreach(var param in constructor.Parameters)
            {
                var paramType = param.Type;

                // Get TypeData using the unified method with recursive constructor extraction
                var paramTypeData = paramType is INamedTypeSymbol namedParamType
                    ? namedParamType.GetTypeData(extractConstructorParams: true, visited: visited)
                    : paramType.GetTypeData();

                // Check if parameter is optional (has default value or is nullable)
                var isOptional = param.HasExplicitDefaultValue || param.NullableAnnotation == NullableAnnotation.Annotated;

                // Check for [FromKeyedServices] or [Inject] attribute with key
                var serviceKey = GetServiceKey(param);

                parameters.Add(new ParameterData(param.Name, paramTypeData, IsOptional: isOptional,
                    ServiceKey: serviceKey));
            }

            return parameters.ToImmutableEquatableArray();
        }

        /// <summary>
        /// Gets the service key from [FromKeyedServices] or [Inject] attribute on a parameter if present.
        /// [FromKeyedServices] takes precedence over [Inject].
        /// </summary>
        private static string? GetServiceKey(IParameterSymbol param)
        {
            foreach(var attribute in param.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if(attrClass is null)
                    continue;

                // Check for Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute (higher priority)
                if(attrClass.Name == "FromKeyedServicesAttribute"
                    && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    // The key is the first constructor argument
                    if(attribute.ConstructorArguments.Length > 0)
                    {
                        var keyArg = attribute.ConstructorArguments[0];
                        if(!keyArg.IsNull && keyArg.Value is not null)
                        {
                            return keyArg.GetPrimitiveConstantString();
                        }
                    }
                }

                // Check for InjectAttribute (by name only, to support third-party attributes)
                if(attrClass.Name == "InjectAttribute")
                {
                    var (key, _) = attribute.GetKey();
                    if(key is not null)
                    {
                        return key;
                    }
                }
            }
            return null;
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

        /// <summary>
        /// Tries to get the original syntax for a named argument, especially for <see langword="nameof"/> expressions.
        /// </summary>
        /// <param name="argumentName">The name of the argument to find.</param>
        /// <returns>The original syntax string if it's a <see langword="nameof"/> expression; otherwise, null.</returns>
        public string? TryGetNameof(string argumentName)
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
                            return nameofArgument.ToFullString().Trim();
                        }
                    }
                }
            }

            return null;
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
        return SubstituteTypeArgumentsCore(typeNameSpan, [(typeParam, actualArg)]);
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
        ReadOnlySpan<(string Key, string Value)> sortedEntries)
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
        ReadOnlySpan<(string Key, string Value)> sortedEntries,
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

/// <summary>
/// A lightweight structure for mapping type parameter names to their actual type arguments.
/// Entries are stored sorted by key length descending to ensure correct matching priority
/// (e.g., "TValue" is matched before "T").
/// </summary>
internal struct TypeArgMap
{
    private (string Key, string Value)[] _entries;
    private int _count;

    /// <summary>
    /// Creates an empty TypeArgMap with the specified initial capacity.
    /// </summary>
    public TypeArgMap(int capacity)
    {
        _entries = capacity > 0 ? new (string, string)[capacity] : [];
        _count = 0;
    }

    /// <summary>
    /// Gets whether the map is uninitialized or contains no elements.
    /// </summary>
    public readonly bool IsDefaultOrEmpty => _entries is null || _count == 0;

    /// <summary>
    /// Gets whether the map is empty.
    /// </summary>
    public readonly bool IsEmpty => _count == 0;

    /// <summary>
    /// Gets the number of entries in the map.
    /// </summary>
    public readonly int Count => _count;

    /// <summary>
    /// Adds a type parameter mapping, maintaining sorted order by key length descending.
    /// </summary>
    public void Add(string typeParam, string actualArg)
    {
        // Ensure capacity
        if(_entries.Length == 0)
        {
            _entries = new (string, string)[4];
        }
        else if(_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        // Find insertion position to maintain sorted order (by key length descending)
        int insertIndex = _count;
        for(int i = 0; i < _count; i++)
        {
            if(typeParam.Length > _entries[i].Key.Length)
            {
                insertIndex = i;
                break;
            }
        }

        // Shift elements to make room
        if(insertIndex < _count)
        {
            Array.Copy(_entries, insertIndex, _entries, insertIndex + 1, _count - insertIndex);
        }

        _entries[insertIndex] = (typeParam, actualArg);
        _count++;
    }

    /// <summary>
    /// Indexer for setting values. Maintains sorted order.
    /// </summary>
    public string this[string typeParam]
    {
        set => Add(typeParam, value);
    }

    /// <summary>
    /// Tries to get a value by key.
    /// </summary>
    public readonly bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        for(int i = 0; i < _count; i++)
        {
            if(_entries[i].Key == key)
            {
                value = _entries[i].Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Gets a span of the entries, sorted by key length descending.
    /// </summary>
    public readonly ReadOnlySpan<(string Key, string Value)> AsSpan() => _entries.AsSpan(0, _count);

    /// <summary>
    /// Returns an enumerator for the entries.
    /// </summary>
    public readonly Enumerator GetEnumerator() => new(_entries, _count);

    public ref struct Enumerator(ReadOnlySpan<(string Key, string Value)> entries, int count)
    {
        private readonly ReadOnlySpan<(string Key, string Value)> _entries = entries[..count];
        private int _index = -1;

        public readonly (string Key, string Value) Current => _entries[_index];
        public bool MoveNext() => ++_index < _entries.Length;
    }
}
