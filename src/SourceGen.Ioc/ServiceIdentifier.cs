using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Identifies a service by its type and key.
/// </summary>
/// <param name="ServiceType">Service type.</param>
/// <param name="Key">Service key. Use <see cref="KeyedService.AnyKey"/> for non-keyed services.</param>
public readonly record struct ServiceIdentifier(Type ServiceType, object Key)
{
    /// <inheritdoc/>
    public bool Equals(ServiceIdentifier other)
    {
        // Fast path: same key reference (e.g., both KeyedService.AnyKey for non-keyed services)
        if(ReferenceEquals(Key, other.Key))
        {
            return ServiceType == other.ServiceType;
        }

        return ServiceType == other.ServiceType && Key.Equals(other.Key);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // For non-keyed services (Key is KeyedService.AnyKey sentinel),
        // only hash ServiceType — avoids virtual dispatch on Key.GetHashCode()
        if(ReferenceEquals(Key, KeyedService.AnyKey))
        {
            return ServiceType.GetHashCode();
        }

        return HashCode.Combine(ServiceType, Key);
    }
}
