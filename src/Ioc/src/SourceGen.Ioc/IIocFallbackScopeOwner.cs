using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// Shares the fallback scope of a container scope with the scopes of its imported modules,
/// so fallback scoped services are the same instance whether resolved through a module or the importing container.
/// </summary>
/// <remarks>
/// Implemented by generated containers that integrate an external <see cref="IServiceProvider"/>.
/// </remarks>
[Experimental("SGIOCEXP001", Message = "This type is used by SourceGen.Ioc and may change or be removed in future versions.")]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IIocFallbackScopeOwner
{
    /// <summary>
    /// Gets the fallback service provider of this container scope, creating its fallback scope on first use.
    /// </summary>
    /// <returns>The fallback service provider, or <see langword="null"/> when the container has none.</returns>
    IServiceProvider? GetFallbackServiceProvider();

    /// <summary>
    /// Creates a scope that resolves fallback services through <paramref name="fallbackScopeOwner"/>
    /// instead of opening its own fallback scope.
    /// </summary>
    /// <param name="fallbackScopeOwner">The importing container scope that owns the fallback scope.</param>
    /// <returns>The new scope. It does not dispose the fallback scope of <paramref name="fallbackScopeOwner"/>.</returns>
    IServiceScope CreateScope(IIocFallbackScopeOwner fallbackScopeOwner);
}
