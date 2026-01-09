namespace SourceGen.Ioc.SourceGenerator.Models;

internal static class Constants
{
    public const string IoCRegisterAttributeFullName = "SourceGen.Ioc.IoCRegisterAttribute";
    public const string IoCRegisterForAttributeFullName = "SourceGen.Ioc.IoCRegisterForAttribute";
    public const string IoCRegisterDefaultsAttributeFullName = "SourceGen.Ioc.IoCRegisterDefaultsAttribute";
    public const string ImportModuleAttributeFullName = "SourceGen.Ioc.ImportModuleAttribute";
    public const string DiscoverAttributeFullName = "SourceGen.Ioc.DiscoverAttribute";

    /// <summary>
    /// The MSBuild property name for the root namespace.
    /// </summary>
    public const string RootNamespaceProperty = "build_property.RootNamespace";

    /// <summary>
    /// The MSBuild property name for customizing the generated method name.
    /// </summary>
    /// <remarks>
    /// Usage in .csproj:
    /// <code>
    /// &lt;PropertyGroup&gt;
    ///     &lt;SourceGenIocName&gt;CustomName&lt;/SourceGenIocName&gt;
    /// &lt;/PropertyGroup&gt;
    /// &lt;ItemGroup&gt;
    ///     &lt;CompilerVisibleProperty Include="SourceGenIocName" /&gt;
    /// &lt;/ItemGroup&gt;
    /// </code>
    /// </remarks>
    public const string SourceGenIocNameProperty = "build_property.SourceGenIocName";

    public const string Category_Usage = "Usage";
    public const string Category_Design = "Design";

    extension(ServiceLifetime lifetime)
    {
        public string Name =>
            lifetime switch
            {
                ServiceLifetime.Singleton => "Singleton",
                ServiceLifetime.Scoped => "Scoped",
                ServiceLifetime.Transient => "Transient",
                _ => lifetime.ToString()
            };
    }

    extension(AttributeData attribute)
    {
        public (bool HasArg, ServiceLifetime Lifetime) TryGetLifetime()
        {
            var (hasArg, val) = attribute.TryGetNamedArgument<int>("Lifetime", 0);// Default is ServiceLifetime.Singleton
            return (hasArg, (ServiceLifetime)val);
        }

        public (bool HasArg, bool Value) TryGetRegisterAllInterfaces() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllInterfaces", false);

        public (bool HasArg, bool Value) TryGetRegisterAllBaseClasses() =>
            attribute.TryGetNamedArgument<bool>("RegisterAllBaseClasses", false);

        public ImmutableEquatableArray<TypeData> GetServiceTypes() =>
            attribute.GetTypeArrayArgument("ServiceTypes");

        public ImmutableEquatableArray<TypeData> GetDecorators() =>
            attribute.GetTypeArrayArgument("Decorators", extractConstructorParams: true);

        /// <summary>
        /// Gets the Tags array from the attribute.
        /// </summary>
        public ImmutableEquatableArray<string> GetTags()
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key.Equals("Tags", StringComparison.Ordinal) && !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array)
                {
                    List<string> result = [];
                    foreach(var value in namedArg.Value.Values)
                    {
                        if(value.Value is string tag)
                        {
                            result.Add(tag);
                        }
                    }
                    return result.ToImmutableEquatableArray();
                }
            }

            return [];
        }

        /// <summary>
        /// Gets the ExcludeFromDefault value from the attribute.
        /// </summary>
        public bool GetExcludeFromDefault() =>
            attribute.GetNamedArgument<bool>("ExcludeFromDefault", false);

        /// <summary>
        /// Gets the Key and KeyType from the attribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>A tuple containing the key string and key type.</returns>
        public (string? Key, int KeyType) GetKey(SemanticModel? semanticModel = null)
        {
            var keyType = attribute.GetNamedArgument<int>("KeyType", 0);
            string? key = null;

            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Key")
                {
                    if(namedArg.Value.IsNull)
                    {
                        key = null;
                    }
                    else
                    {
                        if(keyType == 1) // KeyType.Csharp
                        {
                            // Try to get original syntax for nameof() expressions with full access path resolution
                            key = attribute.TryGetNameof("Key", semanticModel)
                                ?? namedArg.Value.Value?.ToString();
                        }
                        else
                        {
                            key = namedArg.Value.GetPrimitiveConstantString();
                            keyType = 1; // Treat as CSharp code
                        }
                    }
                    break;
                }
            }

            return (key, keyType);
        }

        /// <summary>
        /// Gets the Factory method data from the attribute, including parameter and return type information.
        /// </summary>
        /// <param name="semanticModel">Semantic model to resolve method symbols.</param>
        /// <returns>The factory method data, or null if not specified.</returns>
        public FactoryMethodData? GetFactoryMethodData(SemanticModel semanticModel)
        {
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null)
                return null;

            foreach(var argument in argumentList.Arguments)
            {
                if(argument.NameEquals?.Name.Identifier.Text != "Factory")
                    continue;

                // Check if the expression is a nameof() invocation
                if(argument.Expression is InvocationExpressionSyntax invocation &&
                   invocation.Expression is IdentifierNameSyntax identifierName &&
                   identifierName.Identifier.Text == "nameof" &&
                   invocation.ArgumentList.Arguments.Count == 1)
                {
                    var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;
                    var methodSymbol = ResolveMethodSymbol(nameofArgument, semanticModel);

                    if(methodSymbol is not null)
                    {
                        return CreateFactoryMethodData(methodSymbol);
                    }

                    // Fallback: get path from nameof expression
                    var nameofPath = ResolveNameofExpression(nameofArgument, semanticModel)
                                     ?? nameofArgument.ToFullString().Trim();
                    return new FactoryMethodData(nameofPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null);
                }

                // String literal - cannot determine parameters, assume full signature
                if(argument.Expression is LiteralExpressionSyntax literal &&
                   literal.Token.Value is string literalPath)
                {
                    return new FactoryMethodData(literalPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null);
                }
            }

            return null;
        }

        /// <summary>
        /// Creates FactoryMethodData from a method symbol.
        /// </summary>
        private static FactoryMethodData CreateFactoryMethodData(IMethodSymbol methodSymbol)
        {
            var path = methodSymbol.FullAccessPath;
            bool hasServiceProvider = false;
            bool hasKey = false;

            foreach(var param in methodSymbol.Parameters)
            {
                var paramTypeName = param.Type.FullyQualifiedName;

                // Check for IServiceProvider
                if(paramTypeName is "global::System.IServiceProvider" or "System.IServiceProvider")
                {
                    hasServiceProvider = true;
                }
                // Check for object key/serviceKey parameter
                else if(paramTypeName is "object" or "global::System.Object")
                {
                    var paramName = param.Name;
                    if(paramName.Equals("key", StringComparison.OrdinalIgnoreCase)
                        || paramName.Equals("serviceKey", StringComparison.OrdinalIgnoreCase))
                    {
                        hasKey = true;
                    }
                }
            }

            // Always store the return type for runtime comparison
            var returnTypeName = methodSymbol.ReturnType.FullyQualifiedName;

            return new FactoryMethodData(path, hasServiceProvider, hasKey, returnTypeName);
        }

        /// <summary>
        /// Gets the Instance path from the attribute.
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve full access paths for nameof() expressions.</param>
        /// <returns>The static instance path (e.g., "MyService.Default"), or null if not specified.</returns>
        public string? GetInstance(SemanticModel? semanticModel = null)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Instance")
                {
                    if(namedArg.Value.IsNull)
                        return null;

                    // Try to get original syntax for nameof() expressions with full access path resolution
                    return attribute.TryGetNameof("Instance", semanticModel)
                        ?? namedArg.Value.Value?.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if the attribute will cause registration of interfaces or base classes.
        /// For open generic types, nested open generics are only a problem when registering interfaces/base classes.
        /// </summary>
        public bool WillRegisterInterfacesOrBaseClasses()
        {
            // Check if ServiceTypes is specified
            var serviceTypes = attribute.GetServiceTypes();
            if(serviceTypes.Length > 0)
                return true;

            // Check if RegisterAllInterfaces is true
            var (hasRegisterAllInterfaces, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            if(hasRegisterAllInterfaces && registerAllInterfaces)
                return true;

            // Check if RegisterAllBaseClasses is true
            var (hasRegisterAllBaseClasses, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            if(hasRegisterAllBaseClasses && registerAllBaseClasses)
                return true;

            // Only registering self, no interfaces/base classes
            return false;
        }

        /// <summary>
        /// Extracts default settings from an IoCRegisterDefaultSettingsAttribute.
        /// </summary>
        /// <returns>The default settings model, or null if the attribute data is invalid.</returns>
        public DefaultSettingsModel? ExtractDefaultSettings()
        {
            if(attribute.ConstructorArguments.Length < 2)
                return null;
            if(attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetServiceType)
                return null;
            if(attribute.ConstructorArguments[1].Value is not int lifetime)
                return null;

            var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
            var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
            var serviceTypes = attribute.GetServiceTypes();
            var typeData = targetServiceType.GetTypeData();
            var decorators = attribute.GetDecorators();
            var tags = attribute.GetTags();
            var excludeFromDefault = attribute.GetExcludeFromDefault();

            return new DefaultSettingsModel(
                typeData,
                (ServiceLifetime)lifetime,
                registerAllInterfaces,
                registerAllBaseClasses,
                serviceTypes,
                decorators,
                tags,
                excludeFromDefault);
        }
    }

    /// <param name="typeData">The type data to check.</param>
    extension(TypeData typeData)
    {
        /// <summary>
        /// Tries to extract the element type from a collection type (IEnumerable&lt;T&gt;, IList&lt;T&gt;, etc.).
        /// </summary>
        /// <param name="typeData">The type data to check.</param>
        /// <returns>The element type if this is a collection type with exactly one generic argument; otherwise, null.</returns>
        public TypeData? TryGetCollectionElementType()
        {
            // Must be a generic type with exactly one type argument
            if(typeData.GenericArity != 1)
                return null;

            // Check if this is an enumerable-compatible collection type
            if(!IsEnumerableCompatibleType(typeData.NameWithoutGeneric))
                return null;

            // Get the element type from type parameters
            var typeParams = typeData.TypeParameters;
            if(typeParams is null || typeParams.Length != 1)
                return null;

            return typeParams[0].Type;
        }

        /// <summary>
        /// Checks if the type is an array type (e.g., T[]).
        /// </summary>
        public bool IsArrayType() =>
            typeData.Name.EndsWith("[]", StringComparison.Ordinal);

        /// <summary>
        /// Tries to extract the element type from an array type (T[]).
        /// </summary>
        /// <returns>The element type if this is an array type; otherwise, null.</returns>
        public TypeData? TryGetArrayElementType()
        {
            // Check if this is an array type by name pattern
            if(!IsArrayType(typeData))
                return null;

            // Get the element type from type parameters (set by RoslynExtensions.GetTypeData for arrays)
            var typeParams = typeData.TypeParameters;
            if(typeParams is null || typeParams.Length != 1)
                return null;

            return typeParams[0].Type;
        }

        /// <summary>
        /// Tries to extract the element type from a collection or array type.
        /// Handles IEnumerable&lt;T&gt;, IList&lt;T&gt;, T[], etc.
        /// </summary>
        /// <returns>The element type if this is a collection or array type; otherwise, null.</returns>
        public TypeData? TryGetEnumerableElementType() =>
            typeData.TryGetCollectionElementType() ?? typeData.TryGetArrayElementType();

    }
}
