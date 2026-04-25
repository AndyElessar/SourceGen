namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(AttributeData attribute)
    {
        /// <summary>
        /// Extracts <see cref="GenericFactoryTypeMapping"/> from the registration attribute's
        /// <c>GenericFactoryTypeMapping</c> named property.
        /// Used as a fallback when <c>[IocGenericFactory]</c> is not present on the factory method.
        /// </summary>
        /// <returns>The generic factory type mapping, or null if not specified or invalid.</returns>
        public GenericFactoryTypeMapping? ExtractGenericFactoryMappingFromAttributeProperty()
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key != "GenericFactoryTypeMapping")
                    continue;

                if(namedArg.Value.Kind != TypedConstantKind.Array || namedArg.Value.IsNull)
                    return null;

                var typeArray = namedArg.Value.Values;
                if(typeArray.Length < 2)
                    return null;

                if(typeArray[0].Value is not INamedTypeSymbol serviceTypeTemplate)
                    return null;

                var serviceTypeTemplateData = serviceTypeTemplate.GetTypeData();

                var placeholderMap = new Dictionary<string, int>(StringComparer.Ordinal);
                for(int i = 1; i < typeArray.Length; i++)
                {
                    if(typeArray[i].Value is ITypeSymbol placeholderType)
                    {
                        var placeholderTypeName = placeholderType.FullyQualifiedName;
                        if(placeholderMap.ContainsKey(placeholderTypeName))
                            return null; // Duplicate placeholder
                        placeholderMap[placeholderTypeName] = i - 1;
                    }
                }

                if(placeholderMap.Count != typeArray.Length - 1)
                    return null;

                return new GenericFactoryTypeMapping(
                    serviceTypeTemplateData,
                    placeholderMap.ToImmutableEquatableDictionary());
            }

            return null;
        }

        /// <summary>
        /// Gets the Factory method data from the attribute, including parameter and return type information.
        /// When the resolved factory method is generic but has no <c>[IocGenericFactory]</c> attribute,
        /// falls back to the <c>GenericFactoryTypeMapping</c> property on the registration attribute.
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
                        var factoryData = CreateFactoryMethodData(methodSymbol);

                        // Fallback: if method is generic but has no [IocGenericFactory], check attribute's GenericFactoryTypeMapping
                        if(factoryData.GenericTypeMapping is null && methodSymbol.TypeParameters.Length > 0)
                        {
                            var mappingFromAttr = attribute.ExtractGenericFactoryMappingFromAttributeProperty();
                            if(mappingFromAttr is not null)
                                factoryData = factoryData with { GenericTypeMapping = mappingFromAttr };
                        }

                        return factoryData;
                    }

                    // Fallback: get path from nameof expression
                    var nameofPath = ResolveNameofExpression(nameofArgument, semanticModel)
                                     ?? nameofArgument.ToFullString().Trim();
                    return new FactoryMethodData(nameofPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null, AdditionalParameters: []);
                }

                // String literal - cannot determine parameters, assume full signature
                if(argument.Expression is LiteralExpressionSyntax literal &&
                   literal.Token.Value is string literalPath)
                {
                    return new FactoryMethodData(literalPath, HasServiceProvider: true, HasKey: false, ReturnTypeName: null, AdditionalParameters: []);
                }
            }

            return null;
        }
    }

    extension(IMethodSymbol methodSymbol)
    {
        /// <summary>
        /// Creates FactoryMethodData from a method symbol.
        /// Analyzes factory method parameters:
        /// - IServiceProvider: Will be passed the service provider directly
        /// - [ServiceKey] attribute: Will be passed the registration key value
        /// - Other parameters: Will be resolved from the service provider using the same logic as [IocInject] methods
        /// Also extracts [IocGenericFactory] attribute if present for generic factory method support.
        /// </summary>
        public FactoryMethodData CreateFactoryMethodData()
        {
            var path = methodSymbol.FullAccessPath;
            bool hasServiceProvider = false;
            bool hasKey = false;
            List<ParameterData>? additionalParameters = null;

            foreach(var param in methodSymbol.Parameters)
            {
                var paramTypeName = param.Type.FullyQualifiedName;

                // Check for IServiceProvider
                if(paramTypeName is "global::System.IServiceProvider" or "System.IServiceProvider")
                {
                    hasServiceProvider = true;
                    continue;
                }

                // Check for [ServiceKey] attribute
                bool hasServiceKeyAttribute = false;
                foreach(var attribute in param.GetAttributes())
                {
                    var attrClass = attribute.AttributeClass;
                    if(attrClass is null)
                        continue;

                    if(attrClass.Name == "ServiceKeyAttribute"
                        && attrClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                    {
                        hasServiceKeyAttribute = true;
                        hasKey = true;
                        break;
                    }
                }

                // Skip [ServiceKey] parameters from additional parameters
                if(hasServiceKeyAttribute)
                    continue;

                // Collect additional parameter info using the same logic as [IocInject] methods
                var (serviceKey, hasInjectAttribute, _, hasFromKeyedServicesAttribute) = param.GetServiceKeyAndAttributeInfo();
                var parameterData = new ParameterData(
                    param.Name,
                    param.Type.GetTypeData(),
                    IsNullable: param.NullableAnnotation == NullableAnnotation.Annotated,
                    HasDefaultValue: param.HasExplicitDefaultValue,
                    DefaultValue: param.HasExplicitDefaultValue ? ToDefaultValueCodeString(param.ExplicitDefaultValue) : null,
                    ServiceKey: serviceKey,
                    HasInjectAttribute: hasInjectAttribute,
                    HasServiceKeyAttribute: false, // Already handled above
                    HasFromKeyedServicesAttribute: hasFromKeyedServicesAttribute);

                additionalParameters ??= [];
                additionalParameters.Add(parameterData);
            }

            // Always store the return type for runtime comparison
            var returnTypeName = methodSymbol.ReturnType.FullyQualifiedName;

            // Extract [IocGenericFactory] attribute if present
            var genericTypeMapping = methodSymbol.ExtractGenericFactoryMapping();
            var typeParameterCount = methodSymbol.TypeParameters.Length;

            return new FactoryMethodData(
                path,
                hasServiceProvider,
                hasKey,
                returnTypeName,
                additionalParameters?.ToImmutableEquatableArray() ?? [],
                genericTypeMapping,
                typeParameterCount);
        }

        /// <summary>
        /// Extracts [IocGenericFactory] attribute from the method symbol and builds the type mapping.
        /// </summary>
        public GenericFactoryTypeMapping? ExtractGenericFactoryMapping()
        {
            // Only applicable to generic methods
            if(methodSymbol.TypeParameters.Length == 0)
            {
                return null;
            }

            // Find [IocGenericFactory] attribute
            AttributeData? genericFactoryAttr = null;
            foreach(var attr in methodSymbol.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if(attrClass is null)
                    continue;

                var fullName = attrClass.ToDisplayString();
                if(fullName == Constants.IocGenericFactoryAttributeFullName)
                {
                    genericFactoryAttr = attr;
                    break;
                }
            }

            if(genericFactoryAttr is null)
            {
                return null;
            }

            // Extract GenericTypeMap array from constructor argument
            // [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
            // - First type: service type template with placeholders
            // - Following types: map to factory method type parameters in order
            if(genericFactoryAttr.ConstructorArguments.Length == 0)
            {
                return null;
            }

            var firstArg = genericFactoryAttr.ConstructorArguments[0];
            if(firstArg.Kind != TypedConstantKind.Array || firstArg.Values.IsDefaultOrEmpty)
            {
                return null;
            }

            var typeArray = firstArg.Values;
            if(typeArray.Length < 2)
            {
                return null; // Need at least service type template and one placeholder mapping
            }

            // First type is the service type template
            if(typeArray[0].Value is not INamedTypeSymbol serviceTypeTemplate)
            {
                return null;
            }

            var serviceTypeTemplateData = serviceTypeTemplate.GetTypeData();

            // Build placeholder to type parameter index map
            // Following types (index 1, 2, ...) map to factory method's type parameters (index 0, 1, ...)
            var placeholderMap = new Dictionary<string, int>(StringComparer.Ordinal);
            var expectedPlaceholderCount = typeArray.Length - 1;
            for(int i = 1; i < typeArray.Length; i++)
            {
                if(typeArray[i].Value is ITypeSymbol placeholderType)
                {
                    var placeholderTypeName = placeholderType.FullyQualifiedName;

                    // If the same placeholder type is used multiple times, the mapping is invalid
                    // because we cannot distinguish which type argument maps to which type parameter
                    if(placeholderMap.ContainsKey(placeholderTypeName))
                    {
                        return null;
                    }

                    // Map placeholder type to factory method's type parameter index (0-based)
                    placeholderMap[placeholderTypeName] = i - 1;
                }
            }

            // All placeholder types must be unique and present
            if(placeholderMap.Count != expectedPlaceholderCount)
            {
                return null;
            }

            return new GenericFactoryTypeMapping(
                serviceTypeTemplateData,
                placeholderMap.ToImmutableEquatableDictionary());
        }
    }
}