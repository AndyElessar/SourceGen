namespace SourceGen.Ioc;

/// <summary>
/// Identifies a registered service by its service type symbol and key.
/// Equality compares the service type as a runtime type (<see cref="RuntimeTypeSymbolComparer"/>: tuple element names are
/// ignored) and the key ordinally, so collections keyed by it need no custom comparer.
/// </summary>
/// <param name="ServiceType">The service type symbol.</param>
/// <param name="Key">The service key, or <see langword="null"/> for a non-keyed service.</param>
internal readonly record struct ServiceSymbolKey(INamedTypeSymbol ServiceType, string? Key)
{
    public bool Equals(ServiceSymbolKey other) =>
        RuntimeTypeSymbolComparer.Instance.Equals(ServiceType, other.ServiceType)
        && StringComparer.Ordinal.Equals(Key, other.Key);

    public override int GetHashCode() =>
        unchecked((RuntimeTypeSymbolComparer.Instance.GetHashCode(ServiceType) * 397)
            ^ StringComparer.Ordinal.GetHashCode(Key ?? string.Empty));
}
