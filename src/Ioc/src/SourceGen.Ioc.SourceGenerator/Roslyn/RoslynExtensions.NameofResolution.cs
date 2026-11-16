namespace SourceGen.Ioc;

internal static partial class RoslynExtensions
{
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
        var symbol = ResolveExpressionSymbol(expression, semanticModel);

        if(symbol is null)
            return null;

        // If the expression already contains a member access (e.g., KeyHolder.Key),
        // we need to resolve it to ensure we get the fully qualified path
        return symbol.FullAccessPath;
    }

    /// <summary>
    /// Finds the first <c>Factory</c>, <c>Instance</c> or C# <c>Key</c> argument of a registration attribute given with
    /// <c>nameof()</c> of a member of an unbound generic type whose type parameters the implementation type does not
    /// declare, such as <c>nameof(Factories&lt;&gt;.Create)</c>: its path would keep the unbound <c>Factories&lt;T&gt;</c>, whose
    /// type arguments cannot be determined. The own unbound type of an open generic implementation type, such as
    /// <c>nameof(Node&lt;&gt;.Default)</c> for <c>Node&lt;T&gt;</c>, is closed with it; a closed implementation type, such as
    /// <c>Node&lt;int&gt;</c>, is never closed, so its own unbound type is not either.
    /// </summary>
    /// <param name="implementation">
    /// The registered implementation type, or <see langword="null"/> for default settings, which apply to other types, so
    /// no unbound generic type is closed.
    /// </param>
    /// <param name="isCsharpKey">Whether the Key is a C# expression (<c>KeyType.Csharp</c>); a value Key is the member's name.</param>
    /// <returns>The argument name, the member and the containing type, or <see langword="null"/> when there is none.</returns>
    public static (string ArgumentName, ExpressionSyntax Argument, ISymbol Member, INamedTypeSymbol ContainingType)? FindNameofOfUnboundGenericType(
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol? implementation,
        bool isCsharpKey,
        SemanticModel semanticModel)
    {
        // Only the type parameters of an open generic implementation are closed per closed registration
        var definition = implementation?.OriginalDefinition;
        var ownDefinition = implementation is not null && definition is not null
            && (implementation.IsUnboundGenericType
                || (definition.TypeParameters.Length > 0 && SymbolEqualityComparer.Default.Equals(implementation, definition)))
            ? definition
            : null;

        foreach(var argument in attributeSyntax.ArgumentList?.Arguments ?? default)
        {
            var name = argument.NameEquals?.Name.Identifier.ValueText;
            if(name is not ("Factory" or "Instance" or "Key") || (name is "Key" && !isCsharpKey))
                continue;

            // Only an unbound generic type, such as Factories<>, names type parameters that are not in scope, so the
            // argument is bound only then
            if(argument.Expression is not InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: [var nameofArgument] }
                || !nameofArgument.Expression.DescendantNodes().OfType<OmittedTypeArgumentSyntax>().Any())
            {
                continue;
            }

            // The Factory is the overload the generator calls, or the member whose path it calls when none is resolved
            var member = name is "Factory" && ResolveFactoryMethod(nameofArgument.Expression, attributeSyntax.ArgumentList, semanticModel) is { } factory
                ? factory
                : ResolveExpressionSymbol(nameofArgument.Expression, semanticModel);
            if(member is null)
                continue;

            for(var containingType = member.ContainingType; containingType is not null; containingType = containingType.ContainingType)
            {
                if(UsesOtherTypeParameters(containingType, ownDefinition))
                    return (name, argument.Expression, member, containingType);
            }
        }

        return null;

        // A member looked up on an unbound generic type has error type arguments, which are displayed as the type parameters
        // of its definition, so only the implementation type's own definition is closed with its type arguments
        static bool UsesOtherTypeParameters(ITypeSymbol type, INamedTypeSymbol? implementation) => type switch
        {
            INamedTypeSymbol namedType when namedType.IsUnboundGenericType || namedType.TypeArguments.Any(static typeArgument => typeArgument.TypeKind is TypeKind.Error)
                => implementation is null || !SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, implementation),
            ITypeParameterSymbol typeParameter => implementation is null || !SymbolEqualityComparer.Default.Equals(typeParameter.DeclaringType?.OriginalDefinition, implementation),
            INamedTypeSymbol namedType => namedType.TypeArguments.Any(typeArgument => UsesOtherTypeParameters(typeArgument, implementation)),
            IArrayTypeSymbol arrayType => UsesOtherTypeParameters(arrayType.ElementType, implementation),
            _ => false,
        };
    }

    /// <summary>
    /// Resolves the <c>Factory</c> method named by the expression inside <c>nameof()</c>: the method, or the only usable
    /// overload of a method group (see <see cref="GetFactoryMethod"/>). Shared by the generator and the analyzers.
    /// </summary>
    /// <param name="expression">The expression inside nameof().</param>
    /// <param name="arguments">The arguments of the attribute, whose <c>GenericFactoryTypeMapping</c> makes a generic overload usable.</param>
    /// <param name="semanticModel">The semantic model to use for symbol resolution.</param>
    /// <returns>
    /// The factory method, or <see langword="null"/> when the expression is not a method, or is a method group with no or more
    /// than one usable overload (SGIOC008, SGIOC044); the factory is then called by its path like a Factory given as a string.
    /// </returns>
    public static IMethodSymbol? ResolveFactoryMethod(ExpressionSyntax expression, AttributeArgumentListSyntax? arguments, SemanticModel semanticModel)
        => ResolveFactoryMethod(expression, arguments, semanticModel, out _);

    /// <inheritdoc cref="ResolveFactoryMethod(ExpressionSyntax, AttributeArgumentListSyntax?, SemanticModel)"/>
    /// <param name="isOverloaded">
    /// Whether the expression names a method group with several overloads, so a call of the factory by its path must cast its
    /// arguments to bind the resolved overload (<see cref="FactoryMethodData.CastArguments"/>).
    /// </param>
    public static IMethodSymbol? ResolveFactoryMethod(ExpressionSyntax expression, AttributeArgumentListSyntax? arguments, SemanticModel semanticModel, out bool isOverloaded)
    {
        var candidates = ResolveExpressionSymbols(expression, semanticModel);
        isOverloaded = candidates.Length > 1;
        return GetFactoryMethod(candidates, HasGenericFactoryTypeMapping(arguments), out var method) == 1 ? method : null;
    }

    /// <summary>
    /// Gets the factory method among the symbols a <c>Factory</c> <c>nameof()</c> names: its symbol, or the overloads of a
    /// method group, which <c>nameof()</c> cannot choose between. A single symbol is the factory when it is a method, whether
    /// usable or not (SGIOC008 and SGIOC016 report it). Of several overloads, only a usable one can be the factory: an ordinary
    /// static method returning a value that the generated code can access (<see cref="GetFactoryOrInstanceIssue"/>), and,
    /// when generic, with a type mapping (<c>[IocGenericFactory]</c>, or <c>GenericFactoryTypeMapping</c> on the attribute).
    /// Only a single usable overload is used, never one chosen by the order of the candidates; more than one is reported by
    /// the analyzer (SGIOC044).
    /// </summary>
    /// <param name="candidates">The symbol named by <c>nameof()</c>, or the candidates of its method group.</param>
    /// <param name="hasGenericFactoryTypeMapping">Whether the attribute specifies <c>GenericFactoryTypeMapping</c>.</param>
    /// <param name="method">The factory method, when there is a single one.</param>
    /// <returns>The number of factory methods: 0, 1, or 2 for more than one.</returns>
    public static int GetFactoryMethod(ImmutableArray<ISymbol> candidates, bool hasGenericFactoryTypeMapping, out IMethodSymbol? method)
    {
        if(candidates is [var single])
        {
            method = single as IMethodSymbol;
            return method is null ? 0 : 1;
        }

        method = null;
        foreach(var candidate in candidates)
        {
            if(!IsUsableFactoryOverload(candidate, hasGenericFactoryTypeMapping, out var overload)
                || SymbolEqualityComparer.Default.Equals(overload, method))
            {
                continue;
            }

            if(method is not null)
            {
                method = null;
                return 2;
            }

            method = overload;
        }

        return method is null ? 0 : 1;
    }

    /// <summary>
    /// Gets the return type shared by every usable overload of a <c>Factory</c> method group (see <see cref="GetFactoryMethod"/>),
    /// so that a factory called by its path without a resolved overload (SGIOC044) still casts its result to the
    /// implementation type; <see langword="null"/> when they return different types, or the expression is not a method group.
    /// </summary>
    public static ITypeSymbol? GetFactoryOverloadsReturnType(ExpressionSyntax expression, AttributeArgumentListSyntax? arguments, SemanticModel semanticModel)
    {
        var candidates = ResolveExpressionSymbols(expression, semanticModel);
        if(candidates.Length <= 1)
            return null;

        var hasGenericFactoryTypeMapping = HasGenericFactoryTypeMapping(arguments);
        ITypeSymbol? returnType = null;
        foreach(var candidate in candidates)
        {
            if(!IsUsableFactoryOverload(candidate, hasGenericFactoryTypeMapping, out var overload))
                continue;

            if(returnType is not null && !SymbolEqualityComparer.Default.Equals(returnType, overload.ReturnType))
                return null;

            returnType = overload.ReturnType;
        }

        return returnType;
    }

    /// <summary>Gets whether an overload of a <c>Factory</c> method group can be the factory (see <see cref="GetFactoryMethod"/>).</summary>
    private static bool IsUsableFactoryOverload(ISymbol candidate, bool hasGenericFactoryTypeMapping, [NotNullWhen(true)] out IMethodSymbol? overload)
    {
        overload = candidate as IMethodSymbol;
        return overload is { MethodKind: MethodKind.Ordinary, ReturnsVoid: false }
            && GetFactoryOrInstanceIssue(overload) is null
            && (overload.TypeParameters.Length == 0 || hasGenericFactoryTypeMapping || HasIocGenericFactoryAttribute(overload));

        static bool HasIocGenericFactoryAttribute(IMethodSymbol method)
            => method.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == Constants.IocGenericFactoryAttributeFullName);
    }

    /// <summary>
    /// Gets whether the attribute arguments specify <c>GenericFactoryTypeMapping</c>, which makes a generic <c>Factory</c>
    /// overload usable without <c>[IocGenericFactory]</c>. Whether the mapping is valid is reported on the factory (SGIOC016).
    /// </summary>
    public static bool HasGenericFactoryTypeMapping(AttributeArgumentListSyntax? arguments)
    {
        foreach(var argument in arguments?.Arguments ?? default)
        {
            if(argument.NameEquals?.Name.Identifier.ValueText == "GenericFactoryTypeMapping")
                return !argument.Expression.IsKind(SyntaxKind.NullLiteralExpression);
        }

        return false;
    }

    /// <summary>
    /// Gets why a <c>Factory</c> or <c>Instance</c> member given with <c>nameof()</c> cannot be used by the generated code
    /// (SGIOC008), or <see langword="null"/> when it can: it must be static, not private or protected, and not declared in a
    /// private type.
    /// </summary>
    public static string? GetFactoryOrInstanceIssue(ISymbol symbol)
    {
        if(!symbol.IsStatic)
            return "not static";

        // Private and protected members cannot be accessed from the generated code, which is not a derived class
        switch(symbol.DeclaredAccessibility)
        {
            case Accessibility.Private:
                return "private";
            case Accessibility.ProtectedAndInternal:
            case Accessibility.Protected:
                return "protected and not accessible from generated code";
        }

        for(var containingType = symbol.ContainingType; containingType is not null; containingType = containingType.ContainingType)
        {
            if(containingType.DeclaredAccessibility is Accessibility.Private)
                return "declared in a private type";
        }

        return null;
    }

    /// <summary>
    /// Resolves the symbol an expression refers to. For a method group with several overloads, which <c>nameof()</c> cannot
    /// choose between, it is the overload declared on the most derived type, so its path is that of the type the method
    /// group is looked up on (<c>nameof(Derived.Create)</c> → <c>Derived.Create</c>), whatever the order of the candidates.
    /// </summary>
    private static ISymbol? ResolveExpressionSymbol(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var symbols = ResolveExpressionSymbols(expression, semanticModel);
        if(symbols.Length <= 1)
            return symbols.FirstOrDefault();

        ISymbol? result = null;
        var resultDepth = -1;
        string? resultName = null;
        foreach(var symbol in symbols)
        {
            var depth = 0;
            for(var type = symbol.ContainingType?.BaseType; type is not null; type = type.BaseType)
                depth++;

            var name = symbol.ToDisplayString();
            if(depth > resultDepth || (depth == resultDepth && string.CompareOrdinal(name, resultName) < 0))
            {
                (result, resultDepth, resultName) = (symbol, depth, name);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the symbol an expression refers to, or the candidates of a method group with several overloads.
    /// </summary>
    /// <remarks>
    /// Speculative binding binds only the expression at its own position, so names are looked up in the same scope.
    /// <see cref="ModelExtensions.GetSymbolInfo(SemanticModel, SyntaxNode, CancellationToken)"/> binds the whole
    /// enclosing attribute instead, and transforms receive a new semantic model on every run, so that work repeats
    /// on every edit. It remains the fallback when speculative binding finds nothing.
    /// </remarks>
    public static ImmutableArray<ISymbol> ResolveExpressionSymbols(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var model = semanticModel.ForTreeOf(expression);
        if(expression is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } memberAccess
            && ResolveSingleTypeMember(model, memberAccess.Expression, name.Identifier.ValueText) is { } member)
            return [member];

        var speculative = model.GetSpeculativeSymbolInfo(expression.SpanStart, expression, SpeculativeBindingOption.BindAsExpression);
        if(speculative.Symbol is { } speculativeSymbol)
            return [speculativeSymbol];
        if(!speculative.CandidateSymbols.IsEmpty)
            return speculative.CandidateSymbols;

        var symbolInfo = model.GetSymbolInfo(expression);
        return symbolInfo.Symbol is { } symbol ? [symbol] : symbolInfo.CandidateSymbols;
    }

    /// <summary>
    /// Resolves <c>Type.Member</c> by binding only the qualifier as a type and looking the member up by name.
    /// </summary>
    /// <remarks>
    /// Binding the whole expression of a method group, such as <c>nameof(Factory.Create)</c>, runs overload resolution
    /// that fails on every edit and costs about three times as much. Returns null, so the caller binds the expression,
    /// unless the qualifier is a type and its hierarchy has exactly one member with the name; overloads and hidden members
    /// are left to the compiler's lookup.
    /// </remarks>
    private static ISymbol? ResolveSingleTypeMember(SemanticModel model, ExpressionSyntax qualifier, string memberName)
    {
        if(model.GetSpeculativeSymbolInfo(qualifier.SpanStart, qualifier, SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol
            is not INamedTypeSymbol type)
            return null;

        ISymbol? found = null;
        for(ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach(var member in current.GetMembers(memberName))
            {
                if(found is not null)
                    return null;
                found = member;
            }
        }

        return found;
    }

    extension(SemanticModel semanticModel)
    {
        /// <summary>
        /// Returns a semantic model that can bind <paramref name="node"/>.
        /// </summary>
        /// <remarks>
        /// A transform's semantic model belongs to the file of its target node, but symbols reached through it, such as
        /// members of a partial type declared in another file, carry attribute syntax from other trees. Binding such a
        /// node with the transform's model throws <see cref="ArgumentException"/>.
        /// </remarks>
        public SemanticModel ForTreeOf(SyntaxNode node) => node.SyntaxTree == semanticModel.SyntaxTree
            ? semanticModel
            : semanticModel.Compilation.GetSemanticModel(node.SyntaxTree);
    }
}
