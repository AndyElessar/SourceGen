namespace SourceGen.Ioc.Benchmark;

/// <summary>
/// Shared helper for creating and warming up a <see cref="ServiceProvider"/>
/// with the same registrations used by the realistic benchmark containers.
/// </summary>
internal static class MsdiHelper
{
    /// <summary>
    /// Creates a fully-configured <see cref="ServiceProvider"/> matching the
    /// service registrations of the realistic benchmark containers.
    /// </summary>
    public static ServiceProvider CreateServiceProvider()
    {
        return new ServiceCollection()
            // Infrastructure Layer - Singleton
            .AddSingleton<IAppConfiguration, AppConfiguration>()
            .AddSingleton(typeof(IAppLogger<>), typeof(AppLogger<>))
            .AddSingleton<ICacheService, CacheService>()
            // Data Access Layer - Scoped
            .AddScoped<IDbContext, AppDbContext>()
            .AddScoped<IRepository<User>, UserRepository>()
            .AddScoped<IRepository<Order>, OrderRepository>()
            .AddScoped<IRepository<Product>, ProductRepository>()
            // Business Logic Layer - Scoped
            .AddScoped<IUserService, UserService>()
            .AddScoped<IOrderService, OrderService>()
            .AddScoped<ICatalogService, CatalogService>()
            // API Layer - Transient
            .AddTransient<IRequestContext, RequestContext>()
            .AddTransient<IRequestHandler<GetUserRequest, GetUserResponse>, GetUserHandler>()
            .AddTransient<IRequestHandler<CreateOrderRequest, CreateOrderResponse>, CreateOrderHandler>()
            .AddTransient<IRequestHandler<GetProductRequest, GetProductResponse>, GetProductHandler>()
            .BuildServiceProvider();
    }

    /// <summary>
    /// Pre-resolves all singleton services to ensure they are initialized before benchmarking.
    /// </summary>
    public static void WarmUpSingletons(IServiceProvider provider)
    {
        _ = provider.GetRequiredService<IAppConfiguration>();
        _ = provider.GetRequiredService<IAppLogger<UserRepository>>();
        _ = provider.GetRequiredService<IAppLogger<OrderRepository>>();
        _ = provider.GetRequiredService<IAppLogger<UserService>>();
        _ = provider.GetRequiredService<IAppLogger<OrderService>>();
        _ = provider.GetRequiredService<IAppLogger<CatalogService>>();
        _ = provider.GetRequiredService<IAppLogger<GetUserHandler>>();
        _ = provider.GetRequiredService<IAppLogger<CreateOrderHandler>>();
        _ = provider.GetRequiredService<IAppLogger<GetProductHandler>>();
        _ = provider.GetRequiredService<ICacheService>();
    }
}
