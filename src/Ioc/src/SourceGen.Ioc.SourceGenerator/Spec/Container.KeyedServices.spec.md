# Keyed Service Support

## Overview

Full support for keyed services with various key types. Each keyed registration gets its own field and resolver, named after the implementation type and the key. `GetKeyedService` looks up `(serviceType, key)` in the `_serviceResolvers` dictionary.

## Keyed Service Resolution

```csharp
#region Define:
public interface ICache;

[IocRegister<ICache>(ServiceLifetime.Singleton, Key = "memory")]
public class MemoryCache : ICache;

[IocRegister<ICache>(ServiceLifetime.Singleton, Key = "redis")]
public class RedisCache : ICache;

[IocRegister<ICache>(ServiceLifetime.Singleton, Key = CacheType.Distributed, KeyType = KeyType.Value)]
public class DistributedCache : ICache;

public enum CacheType { Memory, Distributed }

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Keyed members are named after the implementation type and the key (eager singletons by default)
    private global::MemoryCache _memoryCache__memory_ = null!;
    private global::MemoryCache GetMemoryCache__memory_() => _memoryCache__memory_ ?? CreateMemoryCache__memory_();

    private global::RedisCache _redisCache__redis_ = null!;
    private global::RedisCache GetRedisCache__redis_() => _redisCache__redis_ ?? CreateRedisCache__redis_();

    private global::DistributedCache _distributedCache_CacheType_Distributed = null!;
    private global::DistributedCache GetDistributedCache_CacheType_Distributed() => _distributedCache_CacheType_Distributed ?? CreateDistributedCache_CacheType_Distributed();

    // ...CreateXxx methods...

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        // ...built-in services...
        __KeyedResolver(typeof(global::MemoryCache), "memory", static c => c.GetMemoryCache__memory_()),
        __KeyedResolver(typeof(global::ICache), "memory", static c => c.GetMemoryCache__memory_()),
        __KeyedResolver(typeof(global::RedisCache), "redis", static c => c.GetRedisCache__redis_()),
        __KeyedResolver(typeof(global::ICache), "redis", static c => c.GetRedisCache__redis_()),
        __KeyedResolver(typeof(global::DistributedCache), global::CacheType.Distributed, static c => c.GetDistributedCache_CacheType_Distributed()),
        __KeyedResolver(typeof(global::ICache), global::CacheType.Distributed, static c => c.GetDistributedCache_CacheType_Distributed()),
    ];

    public object? GetKeyedService(global::System.Type serviceType, object? serviceKey)
    {
        ThrowIfDisposed();

        var key = serviceKey ?? global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey;

        if(_serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key), out var resolver))
            return resolver(this);

        return GetFallbackServiceProvider() is global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;
    }

    public bool IsKeyedService(global::System.Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey;

        if(_serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key))) return true;

        return _fallbackProvider is global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService isKeyed && isKeyed.IsKeyedService(serviceType, serviceKey);
    }
}
#endregion
```

## See Also

- [Basic Registration](Register.Basic.spec.md)
- [KeyValuePair Registration](Register.KeyValuePair.spec.md)
- [Dictionary Resolution](Container.Collections.spec.md)
