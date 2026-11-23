using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc;

/// <summary>
/// A container that SourceGen.Ioc generates. The strongly typed methods behave like the related extension methods of
/// <see cref="IServiceProvider"/>.
/// </summary>
public interface IIocContainer :
    IServiceProvider, ISupportRequiredService, IKeyedServiceProvider,
    IServiceProviderIsService, IServiceProviderIsKeyedService,
    IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    /// <summary>Gets the service of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service, or <see langword="null"/> when there is none.</returns>
    T? GetService<T>()
        where T : class;

    /// <summary>Gets the service of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service.</returns>
    /// <exception cref="InvalidOperationException">There is no service of type <typeparamref name="T"/>.</exception>
    T GetRequiredService<T>()
        where T : notnull;

    /// <summary>Gets all services of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The services. The collection is empty when there is none.</returns>
    IEnumerable<T> GetServices<T>();

    /// <summary>Gets the service of type <typeparamref name="T"/> with a key.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="serviceKey">The key. <see langword="null"/> means no key.</param>
    /// <returns>The service, or <see langword="null"/> when there is none.</returns>
    T? GetKeyedService<T>(object? serviceKey)
        where T : class;

    /// <summary>Gets the service of type <typeparamref name="T"/> with a key.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="serviceKey">The key. <see langword="null"/> means no key.</param>
    /// <returns>The service.</returns>
    /// <exception cref="InvalidOperationException">There is no service of type <typeparamref name="T"/> with the key.</exception>
    T GetRequiredKeyedService<T>(object? serviceKey)
        where T : notnull;

    /// <summary>Gets all services of type <typeparamref name="T"/> with a key.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="serviceKey">The key. <see cref="KeyedService.AnyKey"/> gives all services that have a key.</param>
    /// <returns>The services. The collection is empty when there is none.</returns>
    IEnumerable<T> GetKeyedServices<T>(object? serviceKey);
}
