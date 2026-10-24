namespace SourceGen.Ioc.SourceGenerator.Models;

internal static partial class TransformExtensions
{
    extension(AttributeData attribute)
    {
        /// <summary>
        /// Gets all key-related information from the attribute in a single pass:
        /// key string, key type, and key value type symbol (with optional nameof() resolution).
        /// </summary>
        /// <param name="semanticModel">Optional semantic model to resolve nameof() expression types and full access paths.</param>
        /// <returns>
        /// A tuple containing:
        /// - Key: The key string, or null if no key is specified.
        /// - KeyType: The key type (0 = Value, 1 = Csharp).
        /// - KeyValueTypeSymbol: The type symbol of the key value, or null when the type cannot be determined.
        /// </returns>
        public (string? Key, int KeyType, ITypeSymbol? KeyValueTypeSymbol) GetKeyInfo(SemanticModel? semanticModel = null)
        {
            var keyType = attribute.GetNamedArgument<int>("KeyType", 0);
            var isCsharpKeyType = keyType == 1;

            // First, check if key is passed as a constructor argument (e.g., InjectAttribute(object key))
            if(attribute.ConstructorArguments.Length > 0)
            {
                var ctorArg = attribute.ConstructorArguments[0];
                // Skip if the first argument is a type, lifetime enum, or array (e.g., IoCRegisterDefaultsAttribute)
                if(ctorArg.Type?.Name != nameof(ServiceLifetime)
                    && ctorArg.Kind != TypedConstantKind.Type
                    && ctorArg.Kind != TypedConstantKind.Array
                    && !ctorArg.IsNull)
                {
                    if(isCsharpKeyType)
                    {
                        // Try to get original syntax for nameof() expressions with full access path resolution
                        var key = attribute.TryGetNameofFromConstructorArg(0, semanticModel)
                            ?? ctorArg.Value?.ToString();
                        var keyValueType = TryResolveNameofTypeFromConstructorArg(attribute, 0, semanticModel);
                        return (key, keyType, keyValueType);
                    }

                    // Value key: treat the primitive constant as CSharp code
                    return (ctorArg.GetPrimitiveConstantString(), 1, ctorArg.Type);
                }
            }

            // Fall back to named argument
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key != "Key")
                    continue;

                if(namedArg.Value.IsNull)
                    return (null, keyType, null);

                if(isCsharpKeyType)
                {
                    // Try to get original syntax for nameof() expressions with full access path resolution
                    var key = attribute.TryGetNameof("Key", semanticModel)
                        ?? namedArg.Value.Value?.ToString();
                    var keyValueType = TryResolveNameofTypeFromNamedArg(attribute, "Key", semanticModel);
                    return (key, keyType, keyValueType);
                }

                // Value key: treat the primitive constant as CSharp code
                return (namedArg.Value.GetPrimitiveConstantString(), 1, namedArg.Value.Type);
            }

            return (null, keyType, null);
        }

        /// <summary>
        /// Tries to resolve the type of a nameof() expression in a constructor argument.
        /// Returns null if the argument is not a nameof() expression or cannot be resolved.
        /// </summary>
        private static ITypeSymbol? TryResolveNameofTypeFromConstructorArg(AttributeData attr, int argumentIndex, SemanticModel? semanticModel)
        {
            if(semanticModel is null)
                return null;

            var syntaxReference = attr.ApplicationSyntaxReference;
            if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null || argumentList.Arguments.Count <= argumentIndex)
                return null;

            var argument = argumentList.Arguments[argumentIndex];
            if(argument.NameEquals is not null)
                return null;

            return ResolveNameofExpressionType(argument.Expression, semanticModel);
        }

        /// <summary>
        /// Tries to resolve the type of a nameof() expression in a named argument.
        /// Returns null if the argument is not a nameof() expression or cannot be resolved.
        /// </summary>
        private static ITypeSymbol? TryResolveNameofTypeFromNamedArg(AttributeData attr, string argumentName, SemanticModel? semanticModel)
        {
            if(semanticModel is null)
                return null;

            var syntaxReference = attr.ApplicationSyntaxReference;
            if(syntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
                return null;

            var argumentList = attributeSyntax.ArgumentList;
            if(argumentList is null)
                return null;

            foreach(var argument in argumentList.Arguments)
            {
                if(argument.NameEquals?.Name.Identifier.Text == argumentName)
                {
                    return ResolveNameofExpressionType(argument.Expression, semanticModel);
                }
            }

            return null;
        }

        /// <summary>
        /// If the expression is a nameof() invocation, resolves the referenced symbol's type.
        /// Returns null for non-nameof expressions.
        /// </summary>
        private static ITypeSymbol? ResolveNameofExpressionType(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            if(expression is not InvocationExpressionSyntax invocation
                || invocation.Expression is not IdentifierNameSyntax identifierName
                || identifierName.Identifier.Text != "nameof"
                || invocation.ArgumentList.Arguments.Count != 1)
            {
                return null;
            }

            var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;
            var symbolInfo = semanticModel.GetSymbolInfo(nameofArgument);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            return symbol switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IMethodSymbol method => method.ReturnType,
                ILocalSymbol local => local.Type,
                IParameterSymbol param => param.Type,
                _ => null,
            };
        }
    }
}