using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Identifies a service by its type and key.
/// </summary>
/// <param name="ServiceType">Service type.</param>
/// <param name="Key">Service key. Use <see cref="NonKeyed"/> for non-keyed services.</param>
[Experimental("SGIOCEXP001", Message = "This type is used by SourceGen.Ioc and may change or be removed in future versions.")]
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct ServiceIdentifier(Type ServiceType, object Key)
{
    /// <summary>
    /// The key of non-keyed services. It is not <see cref="KeyedService.AnyKey"/>, which requests the services registered
    /// under any key (e.g. <c>GetKeyedServices&lt;T&gt;(KeyedService.AnyKey)</c>), like <c>MS.DI</c>.
    /// </summary>
    public static readonly object NonKeyed = new NonKeyedServiceKey();

    /// <inheritdoc/>
    public bool Equals(ServiceIdentifier other)
    {
        // Fast path: same key reference (e.g., both NonKeyed for non-keyed services)
        if(ReferenceEquals(Key, other.Key))
        {
            return ServiceType == other.ServiceType;
        }

        return ServiceType == other.ServiceType && Key.Equals(other.Key);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // For non-keyed services (Key is the NonKeyed sentinel),
        // only hash ServiceType — avoids virtual dispatch on Key.GetHashCode()
        if(ReferenceEquals(Key, NonKeyed))
        {
            return ServiceType.GetHashCode();
        }

        return HashCode.Combine(ServiceType, Key);
    }

    private sealed class NonKeyedServiceKey
    {
        public override string ToString() => nameof(NonKeyed);
    }
}
