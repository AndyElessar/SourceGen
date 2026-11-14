namespace SourceGen.Ioc;

/// <summary>
/// Compares type symbols as runtime types: tuple element names are ignored, also in type arguments and array element
/// types, so <c>IHandler&lt;(Entity X, string Y)[]&gt;</c> equals <c>IHandler&lt;(Entity, string)[]&gt;</c>. Mirrors the
/// generator's <see cref="TypeNameComparer"/> for the container's service lookups.
/// </summary>
internal sealed class RuntimeTypeSymbolComparer : IEqualityComparer<ITypeSymbol?>
{
    public static RuntimeTypeSymbolComparer Instance { get; } = new();

    /// <summary>Compares service keys by their service type like <see cref="Instance"/>, and by their key ordinally.</summary>
    public static IEqualityComparer<ServiceSymbolKey> ServiceKeyComparer { get; } = new ServiceSymbolKeyComparer();

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

    // Types equal by Default have the same definition; tuples with other element names share theirs (ValueTuple<T1, T2>)
    public int GetHashCode(ITypeSymbol? obj) => obj switch
    {
        IArrayTypeSymbol array => unchecked((GetHashCode(array.ElementType) * 31) + array.Rank),
        INamedTypeSymbol { IsGenericType: true } named => SymbolEqualityComparer.Default.GetHashCode(named.OriginalDefinition),
        _ => SymbolEqualityComparer.Default.GetHashCode(obj),
    };

    private sealed class ServiceSymbolKeyComparer : IEqualityComparer<ServiceSymbolKey>
    {
        public bool Equals(ServiceSymbolKey x, ServiceSymbolKey y)
            => Instance.Equals(x.ServiceType, y.ServiceType) && StringComparer.Ordinal.Equals(x.Key, y.Key);

        public int GetHashCode(ServiceSymbolKey obj)
            => unchecked((Instance.GetHashCode(obj.ServiceType) * 397) ^ StringComparer.Ordinal.GetHashCode(obj.Key ?? string.Empty));
    }
}
