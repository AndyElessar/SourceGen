using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// A marker that <c>Add{Name}</c> registers for each assembly that it registers, to stop a second registration of the same
/// assembly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001", Message = "This type is only for the code that SourceGen.Ioc generates. Do not use it directly.")]
public sealed class IocRegistrationMarker
{
    private IocRegistrationMarker(string assembly, string method)
    {
        Assembly = assembly;
        Method = method;
    }

    /// <summary>Gets the name of the registered assembly.</summary>
    public string Assembly { get; }

    /// <summary>Gets the method that registered it, for example <c>App.AppServiceCollectionExtensions.AddApp</c>.</summary>
    public string Method { get; }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when one of <paramref name="assemblies"/> already has a marker in
    /// <paramref name="services"/>, and then does not change <paramref name="services"/>. Otherwise adds a marker for each
    /// assembly, registered as an instance so that MS.DI does not dispose it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="method">The full name of the calling method.</param>
    /// <param name="assemblies">The assemblies that the method registers.</param>
    public static void Enter(IServiceCollection services, string method, params ReadOnlySpan<string> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        Dictionary<string, IocRegistrationMarker>? existing = null;
        foreach(ServiceDescriptor descriptor in services)
        {
            if(descriptor.ServiceType == typeof(IocRegistrationMarker)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationInstance is IocRegistrationMarker marker)
            {
                (existing ??= [])[marker.Assembly] = marker;
            }
        }

        if(existing is not null)
        {
            foreach(string assembly in assemblies)
            {
                if(existing.TryGetValue(assembly, out IocRegistrationMarker? previous))
                {
                    throw new InvalidOperationException(
                        $"'{method}' cannot register assembly '{assembly}', because '{previous.Method}' already registered it. "
                        + "Call only the Add method of the final assembly, one time, with all tags in that call: it registers all modules that the assembly imports.");
                }
            }
        }

        foreach(string assembly in assemblies)
        {
            services.Add(ServiceDescriptor.Singleton(new IocRegistrationMarker(assembly, method)));
        }
    }
}
