namespace SourceGen.Ioc;

/// <summary>
/// A syntactic pre-check that lets the analyzer bind only the attributes whose name can refer to an IoC attribute.
/// An attribute refers to a type through its name (with or without the <c>Attribute</c> suffix, optionally qualified)
/// or through a using alias, so the accepted names are the IoC attribute names plus the aliases that target them.
/// </summary>
internal sealed class IocAttributeNameFilter
{
    private readonly Compilation _compilation;
    private readonly HashSet<string> _attributeNames = new(StringComparer.Ordinal);
    private HashSet<string>? _namesWithAliases;

    public IocAttributeNameFilter(Compilation compilation, params INamedTypeSymbol?[] attributeTypes)
    {
        _compilation = compilation;
        foreach(var attributeType in attributeTypes)
        {
            if(attributeType is null)
                continue;

            _attributeNames.Add(attributeType.Name);
            if(attributeType.Name.EndsWith("Attribute", StringComparison.Ordinal))
                _attributeNames.Add(attributeType.Name[..^"Attribute".Length]);
        }
    }

    /// <summary>
    /// Returns <see langword="false"/> when the attribute cannot refer to an IoC attribute.
    /// </summary>
    public bool MayBeIocAttribute(AttributeSyntax attribute, CancellationToken cancellationToken)
    {
        var name = GetSimpleName(attribute.Name);
        if(_attributeNames.Contains(name))
            return true;

        var namesWithAliases = Volatile.Read(ref _namesWithAliases);
        if(namesWithAliases is null)
        {
            // Built once on first use; a concurrent caller may build an identical set
            namesWithAliases = BuildNamesWithAliases(cancellationToken);
            Interlocked.CompareExchange(ref _namesWithAliases, namesWithAliases, null);
        }

        return namesWithAliases.Contains(name);
    }

    /// <summary>
    /// Adds the using aliases of all files whose target is named like an IoC attribute.
    /// A local alias is accepted in every file, which only means that such attributes are bound.
    /// </summary>
    private HashSet<string> BuildNamesWithAliases(CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(_attributeNames, StringComparer.Ordinal);
        foreach(var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(tree.GetRoot(cancellationToken) is CompilationUnitSyntax root)
            {
                AddAliases(names, root.Usings);
                AddNamespaceAliases(names, root.Members);
            }
        }

        return names;
    }

    private void AddNamespaceAliases(HashSet<string> names, SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach(var member in members)
        {
            if(member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                AddAliases(names, namespaceDeclaration.Usings);
                AddNamespaceAliases(names, namespaceDeclaration.Members);
            }
        }
    }

    private void AddAliases(HashSet<string> names, SyntaxList<UsingDirectiveSyntax> usings)
    {
        foreach(var usingDirective in usings)
        {
            if(usingDirective is not { Alias.Name.Identifier.ValueText: var alias, NamespaceOrType: NameSyntax target }
                || !_attributeNames.Contains(GetSimpleName(target)))
            {
                continue;
            }

            // [Alias] looks up both Alias and AliasAttribute
            names.Add(alias);
            if(alias.EndsWith("Attribute", StringComparison.Ordinal))
                names.Add(alias[..^"Attribute".Length]);
        }
    }

    private static string GetSimpleName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString()
    };
}
