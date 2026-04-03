# KeyValuePair and Dictionary Registration

## Overview

When services with keys are dependencies, the generator automatically produces `KeyValuePair<K, V>` registrations so that dictionary-based resolution works correctly.

## Feature: KeyValuePair and Dictionary Registration

When consumer dependencies include `KeyValuePair<K, V>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`, or `IEnumerable<KeyValuePair<K, V>>`, the generator also produces explicit `KeyValuePair<K, V>` service registrations for each matching keyed service.

### Purpose

`IDictionary<K, V>` and similar types resolve via `sp.GetServices<KeyValuePair<K, V>>().ToDictionary()`. For this to work, `KeyValuePair<K, V>` must be registered as a service. Without explicit registrations, `GetServices<KeyValuePair<K, V>>()` returns an empty collection.

### Behavior

- Triggered by consumer dependencies — only keyed services that match a needed `(K, V)` pair produce KVP registrations
- **Key type filtering**: a keyed service is only matched when its `KeyValueType` is compatible with the consumer's key type `K`:
  - `K` is `object` → matches **all** keyed services (regardless of their key type)
  - `KeyValueType` is `null` (e.g., `KeyType=Csharp` without `nameof()`) → matches only `object` consumers
  - Otherwise → `KeyValueType.Name` must match `K` exactly (case-sensitive)
- Lifetime matches the keyed value service's lifetime
- Uses `ServiceDescriptor` directly because `KeyValuePair<K, V>` is a struct (generic `AddXxx<T>` has a `class` constraint)
- Registrations are emitted after all normal service registrations, before `return services;`

### Example

```csharp
#region Define:
[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "h1")]
public class Handler1 : IHandler { }

[IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "h2")]
public class Handler2 : IHandler { }

[IocRegister(Lifetime = ServiceLifetime.Singleton)]
public class Registry(IDictionary<string, IHandler> handlers) { }
#endregion

#region Generate:
services.AddKeyedSingleton<Handler1, Handler1>("h1");
services.AddKeyedSingleton<IHandler>("h1", (sp, key) => sp.GetRequiredKeyedService<Handler1>(key));
services.AddKeyedScoped<Handler2, Handler2>("h2");
services.AddKeyedScoped<IHandler>("h2", (sp, key) => sp.GetRequiredKeyedService<Handler2>(key));
services.AddSingleton<Registry>((sp) =>
{
    var p0 = sp.GetServices<KeyValuePair<string, IHandler>>().ToDictionary();
    return new Registry(p0);
});

// KeyValuePair registrations for keyed services
services.Add(new ServiceDescriptor(typeof(KeyValuePair<string, IHandler>),
    (sp) => (object)new KeyValuePair<string, IHandler>("h1", sp.GetRequiredKeyedService<IHandler>("h1")),
    ServiceLifetime.Singleton));
services.Add(new ServiceDescriptor(typeof(KeyValuePair<string, IHandler>),
    (sp) => (object)new KeyValuePair<string, IHandler>("h2", sp.GetRequiredKeyedService<IHandler>("h2")),
    ServiceLifetime.Scoped));
#endregion
```

## See Also

- [Keyed Services](Register.Basic.spec.md)
- [Injection](Register.Injection.spec.md)
- [Container Collections](Container.Collections.spec.md)
