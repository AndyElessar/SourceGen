namespace SourceGen.Ioc;

/// <summary>
/// Identifies a registered service by its service type symbol and key.
/// Equality uses <see cref="SymbolEqualityComparer.Default"/> for the type and ordinal comparison for the key,
/// so collections keyed by it need no custom comparer.
/// </summary>
/// <param name="ServiceType">The service type symbol.</param>
/// <param name="Key">The service key, or <see langword="null"/> for a non-keyed service.</param>
internal readonly record struct ServiceSymbolKey(INamedTypeSymbol ServiceType, string? Key)
{
    public bool Equals(ServiceSymbolKey other) =>
        SymbolEqualityComparer.Default.Equals(ServiceType, other.ServiceType)
        && StringComparer.Ordinal.Equals(Key, other.Key);

    public override int GetHashCode() =>
        unchecked((SymbolEqualityComparer.Default.GetHashCode(ServiceType) * 397)
            ^ StringComparer.Ordinal.GetHashCode(Key ?? string.Empty));
}
