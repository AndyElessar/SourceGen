# Keyed Service Support

## Overview

Full support for keyed services with various key types. The container efficiently resolves keyed services using either switch statements or dictionary lookup.

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
    // Note: In actual generated code, fields are placed above their resolver methods.
    // This example focuses on the GetKeyedService implementation.
    private MemoryCache? _memoryCache;
    private RedisCache? _redisCache;
    private DistributedCache? _distributedCache;

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceType == typeof(ICache))
        {
            return serviceKey switch
            {
                "memory" => GetMemoryCache(),
                "redis" => GetRedisCache(),
                CacheType.Distributed => GetDistributedCache(),
                _ => _fallbackProvider is IKeyedServiceProvider keyed
                    ? keyed.GetKeyedService(serviceType, serviceKey)
                    : null
            };
        }

        return _fallbackProvider is IKeyedServiceProvider keyed2
            ? keyed2.GetKeyedService(serviceType, serviceKey)
            : null;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceType == typeof(ICache))
        {
            return serviceKey is "memory" or "redis" or CacheType.Distributed;
        }

        return _fallbackProvider is IServiceProviderIsKeyedService isKeyed
            && isKeyed.IsKeyedService(serviceType, serviceKey);
    }
}
#endregion
```

## See Also

- [Basic Registration](Register.Basic.spec.md)
- [KeyValuePair Registration](Register.KeyValuePair.spec.md)
- [Dictionary Resolution](Container.Collections.spec.md)
