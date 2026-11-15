namespace SourceGen.Ioc;

/// <summary>
/// Compares type symbols as runtime types: tuple element names are ignored, also in type arguments and array element
/// types, so <c>IHandler&lt;(Entity X, string Y)[]&gt;</c> equals <c>IHandler&lt;(Entity, string)[]&gt;</c>. Mirrors the
/// generator's <see cref="TypeNameComparer"/> for the container's service lookups.
/// </summary>
internal sealed class RuntimeTypeSymbolComparer : IEqualityComparer<ITypeSymbol?>
{
    public static RuntimeTypeSymbolComparer Instance { get; } = new();

    private RuntimeTypeSymbolComparer()
    {
    }

    public bool Equals(ITypeSymbol? x, ITypeSymbol? y)
    {
        if(SymbolEqualityComparer.Default.Equals(x, y))
            return true;

        return (x, y) switch
        {
            (IArrayTypeSymbol left, IArrayTypeSymbol right) => left.Rank == right.Rank && Equals(left.ElementType, right.ElementType),
            (INamedTypeSymbol { IsGenericType: true } left, INamedTypeSymbol { IsGenericType: true } right) =>
                SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition)
                && Equals(left.ContainingType, right.ContainingType)
                && TypeArgumentsEqual(left.TypeArguments, right.TypeArguments),
            _ => false,
        };

        bool TypeArgumentsEqual(ImmutableArray<ITypeSymbol> left, ImmutableArray<ITypeSymbol> right)
        {
            if(left.Length != right.Length)
                return false;

            for(var i = 0; i < left.Length; i++)
            {
                if(!Equals(left[i], right[i]))
                    return false;
            }

            return true;
        }
    }

    // Types equal by Default have the same definition; tuples with other element names share theirs (ValueTuple<T1, T2>).
    // The type arguments are hashed the same way, so the closed forms of one definition, such as IHandler<A> and
    // IHandler<B>, do not share a hash code.
    public int GetHashCode(ITypeSymbol? obj)
    {
        switch(obj)
        {
            case IArrayTypeSymbol array:
                return unchecked((GetHashCode(array.ElementType) * 31) + array.Rank);

            case INamedTypeSymbol { IsGenericType: true } named:
                var hash = SymbolEqualityComparer.Default.GetHashCode(named.OriginalDefinition);
                if(named.ContainingType is { IsGenericType: true } containingType)
                {
                    hash = unchecked((hash * 31) + GetHashCode(containingType));
                }

                foreach(var typeArgument in named.TypeArguments)
                {
                    hash = unchecked((hash * 31) + GetHashCode(typeArgument));
                }

                return hash;

            default:
                return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }
}
