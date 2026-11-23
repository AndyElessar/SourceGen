namespace SourceGen.Ioc.Model;

/// <summary>
/// Builds a <see cref="TypeRef"/> from a symbol and normalizes it by the rule that the run-time type decides
/// (Analysis.Model.spec, "Normalization").
/// </summary>
internal static class TypeRefFactory
{
    /// <summary>Builds the type reference of <paramref name="type"/>.</summary>
    /// <param name="type">The type.</param>
    /// <param name="compilation">The compilation, to replace <c>dynamic</c> with <c>System.Object</c>.</param>
    /// <returns>
    /// The type reference, or <see langword="null"/> when the model cannot represent the type: a pointer, a function
    /// pointer, or an error type.
    /// </returns>
    public static TypeRef? Create(ITypeSymbol type, Compilation compilation) => type switch
    {
        IDynamicTypeSymbol => Create(compilation.GetSpecialType(SpecialType.System_Object), compilation),
        ITypeParameterSymbol parameter => new TypeParameterRef(FlatIndex(parameter)),
        IArrayTypeSymbol array => Create(array.ElementType, compilation) is { } element ? new ArrayTypeRef(element, array.Rank) : null,
        INamedTypeSymbol { TypeKind: not TypeKind.Error } named => CreateNamed(named, compilation),
        _ => null,
    };

    private static NamedTypeRef? CreateNamed(INamedTypeSymbol named, Compilation compilation)
    {
        // A tuple with element names is its ValueTuple; nint and nuint are IntPtr and UIntPtr
        named = named.TupleUnderlyingType ?? named;
        named = named.NativeIntegerUnderlyingType ?? named;

        if(named.ContainingAssembly is not { } assembly)
        {
            return null;
        }

        ImmutableEquatableArray<TypeRef>? arguments = null;
        if(!named.IsUnboundGenericType)
        {
            List<TypeRef> flat = [];
            foreach(INamedTypeSymbol level in ContainingChain(named))
            {
                foreach(ITypeSymbol argument in level.TypeArguments)
                {
                    if(Create(argument, compilation) is not { } reference)
                    {
                        return null;
                    }

                    flat.Add(reference);
                }
            }

            if(flat.Count > 0)
            {
                arguments = flat.ToImmutableEquatableArray();
            }
        }

        return new NamedTypeRef(assembly.Name, MetadataName(named), arguments, IsAccessible(named));
    }

    /// <summary>
    /// The index of a type parameter in the flat list of its declaration. A type parameter of a nested type comes after
    /// the type parameters of its outer types, as the type arguments do.
    /// </summary>
    private static int FlatIndex(ITypeParameterSymbol parameter)
    {
        int index = parameter.Ordinal;
        for(INamedTypeSymbol? outer = parameter.DeclaringType?.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            index += outer.TypeParameters.Length;
        }

        return index;
    }

    /// <summary>The full metadata name: the namespace, then each type from the outermost, separated by <c>+</c>.</summary>
    private static string MetadataName(INamedTypeSymbol named)
    {
        StringBuilder builder = new();
        List<INamedTypeSymbol> chain = ContainingChain(named);
        AppendNamespace(builder, chain[0].ContainingNamespace);
        for(int i = 0; i < chain.Count; i++)
        {
            if(i > 0)
            {
                builder.Append('+');
            }

            builder.Append(chain[i].MetadataName);
        }

        return builder.ToString();
    }

    /// <summary>Appends the metadata name of each namespace from the outermost, each followed by a dot. Keywords are not escaped.</summary>
    private static void AppendNamespace(StringBuilder builder, INamespaceSymbol? ns)
    {
        if(ns is null or { IsGlobalNamespace: true })
        {
            return;
        }

        AppendNamespace(builder, ns.ContainingNamespace);
        builder.Append(ns.MetadataName).Append('.');
    }

    /// <summary>A named type is accessible when it and each of its outer types are public. <c>InternalsVisibleTo</c> does not count.</summary>
    private static bool IsAccessible(INamedTypeSymbol named)
    {
        for(INamedTypeSymbol? level = named; level is not null; level = level.ContainingType)
        {
            if(level.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The type and its outer types, from the outermost.</summary>
    private static List<INamedTypeSymbol> ContainingChain(INamedTypeSymbol named)
    {
        List<INamedTypeSymbol> chain = [];
        for(INamedTypeSymbol? level = named; level is not null; level = level.ContainingType)
        {
            chain.Add(level);
        }

        chain.Reverse();
        return chain;
    }
}
