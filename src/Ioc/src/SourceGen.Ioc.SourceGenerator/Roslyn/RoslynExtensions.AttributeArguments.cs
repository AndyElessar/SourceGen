namespace SourceGen.Ioc.SourceGenerator;

internal static partial class RoslynExtensions
{
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
}