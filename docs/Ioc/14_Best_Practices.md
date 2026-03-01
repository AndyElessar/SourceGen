# Best Practices

This guide provides production-oriented recommendations for SourceGen.Ioc.
Use it as a companion to the feature documents in this folder.

## Relationship to MS.E.DI

SourceGen.Ioc is an extension to `Microsoft.Extensions.DependencyInjection` (`MS.E.DI`) or any other valid implementation of `Microsoft.Extensions.DependencyInjection.Abstractions`, not a replacement.
Use generated registration methods to improve compile-time safety and reduce registration boilerplate while keeping `MS.E.DI` as the baseline DI ecosystem.
For service registration fundamentals, see [02_Basic.md](02_Basic.md). For container-specific behavior and options, see [12_Container.md](12_Container.md).

## Golden Path

Use this baseline in most projects:

1. Prefer dedicated registration files that use `[IocRegisterFor]` and `[IocRegisterDefaults]` to keep domain/user code free of DI attributes.
2. Use `[IocRegister]` / `[IocRegister<T>]` when the implementation type is infrastructure code you own and local annotation improves readability.
3. Prefer constructor injection by default.
4. Use tags for startup-time profile selection, and keys for resolve-time selection.
5. Add `[IocContainer]` only when you need typed compile-time container APIs.

> [!TIP]
> Use assembly-level or mark on marker/container class when using `[IocRegisterFor]` and `[IocRegisterDefaults]` in dedicated registration files to avoid scattering SourceGen.Ioc attributes across business/domain types.

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

[IocRegister<IClock>(ServiceLifetime.Singleton)]
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

[IocRegisterDefaults(typeof(IHandler), ServiceLifetime.Scoped)]
public interface IHandler;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddSingleton<global::MyNamespace.SystemClock, global::MyNamespace.SystemClock>();
services.AddSingleton<global::MyNamespace.IClock>((global::System.IServiceProvider sp) =>
    sp.GetRequiredService<global::MyNamespace.SystemClock>());
```

</details>

## Registration Decision Matrix

Choose the registration mechanism by intent:

|Scenario|Use|Reason|
|:---|:---|:---|
|You own the implementation type|`[IocRegister]` / `[IocRegister<T>]`|Most explicit and easiest to maintain.|
|You do not own the implementation type|`[IocRegisterFor]` / `[IocRegisterFor<T>]`|Registers external types without editing third-party code.|
|Many implementations share policy|`[IocRegisterDefaults]`|Centralized lifetime/key/tag/decorator policy.|
|Reuse defaults from another module|`[IocImportModule]`|Shares policy across modules and assemblies.|

> [!TIP]
> Start explicit first. Introduce defaults and imported modules only when duplication is clear.

> [!TIP]
> If you want to avoid polluting your code, prioritize `[IocRegisterFor]` for implementation discovery and `[IocRegisterDefaults]` for shared policy, then keep business types attribute-free.

## Lifetime Design Rules

Treat lifetime diagnostics as architecture feedback:

|Rule|Diagnostic|Recommended Action|
|:---|:---|:---|
|Avoid circular dependencies|`SGIOC002`|Break the cycle using abstractions, `Func<T>`, or boundary refactoring.|
|Singleton should not depend on scoped|`SGIOC003`|Promote dependency lifetime or move access behind a singleton-safe abstraction.|
|Singleton should not depend on transient|`SGIOC004`|Stabilize lifetime or redesign composition.|
|Scoped should not depend on transient|`SGIOC005`|Promote transient dependency or split responsibilities.|

> [!NOTE]
> Better fix lifetime diagnostics in design, not by suppressing analyzers.

## Keys vs Tags

Use keys and tags for different phases:

|Need|Prefer|Why|
|:---|:---|:---|
|Select implementation at resolve time|Keyed services|Natural one-to-one selection model.|
|Select profile at registration time|Tags|Natural startup profile model.|
|Tenant, region, or payment strategy selection|Keyed services|Easy lookup with `GetRequiredKeyedService`.|
|Feature bundles or app mode switches|Tags|Switches registration groups in one call.|

> [!NOTE]
> Tag behavior is mutually exclusive: no-tag services are registered only when no tags are passed.

<details>
<summary>Generated Code</summary>

```csharp
public static IServiceCollection AddMyProject(this IServiceCollection services, params IEnumerable<string> tags)
{
    if (!tags.Any())
    {
        // register no-tag services
    }

    if (tags.Contains("Feature1"))
    {
        // register Feature1 services
    }

    return services;
}
```

</details>

## Defaults and Override Strategy

Prefer this order:

1. Keep defaults on service contracts (`interface` / base type).
2. Override at implementation only when behavior truly differs.
3. Keep decorator and key policy close to default declarations.

Settings and registration behavior follow documented precedence. See [03_Defaults.md](03_Defaults.md) for full details.

## Decorator Strategy

1. Keep decorator chains short and intentional.
2. Place cross-cutting concerns (logging, metrics, tracing) in defaults.
3. Use per-implementation override only for exceptional behavior.
4. Validate the generated order in code review.

> [!NOTE]
> Decorators are applied in declared order (first declared decorator is outermost in behavior).

<details>
<summary>Generated Code</summary>

```csharp
services.AddScoped<global::MyNamespace.MyHandler, global::MyNamespace.MyHandler>();
services.AddScoped<global::MyNamespace.IHandler>((global::System.IServiceProvider sp) =>
{
    var s0 = sp.GetRequiredService<global::MyNamespace.MyHandler>();
    var s1 = new global::MyNamespace.MetricsDecorator<global::MyNamespace.MyHandler>(s0);
    var s2 = new global::MyNamespace.LoggingDecorator<global::MyNamespace.MetricsDecorator<global::MyNamespace.MyHandler>>(s1);
    return s2;
});
```

</details>

## Open Generic Strategy

1. Start with regular open-generic registration.
2. Use auto-discovery for common dependency graph closure.
3. Add `[IocDiscover]` only when you need explicit discover control.
4. Re-check generated output when combining nested generics with decorators.

<details>
<summary>Generated Code</summary>

```csharp
services.AddTransient(typeof(global::MyNamespace.IHandler<>), typeof(global::MyNamespace.Handler<>));
```

</details>

## Injection Style Priority

Use this order for maintainability:

1. Constructor injection
2. Property injection
3. Method injection
4. Field injection

> [!WARNING]
> If `SourceGenIocFeatures` disables a member injection feature, `[IocInject]` on that member is ignored and `SGIOC022` is reported.

## Container Recommendations

> [!IMPORTANT]
> `[IocContainer]` is a specialized compile-time container API and is not a full replacement for `MS.E.DI`.
> The generated container does not parse `IServiceCollection` registrations by itself, so common extension-method registrations (for example `services.AddLogging()`, `services.AddOptions()`, `services.AddHttpClient()`) are not available in container-only mode.
> Use this container primarily when you need high performance or a container tailored for specific scenarios.

> [!NOTE]
> For general-purpose app composition, keep `MS.E.DI` as the primary container and use SourceGen.Ioc-generated registration methods as an extension layer.
> When `IntegrateServiceProvider = true`, unresolved services can still fall back to an external `IServiceProvider`.

Default recommendations for most apps:

- `IntegrateServiceProvider = true`
- `ExplicitOnly = false`
- `UseSwitchStatement = false`
- `ThreadSafeStrategy = ThreadSafeStrategy.Lock`
- `EagerResolveOptions = EagerResolveOptions.Singleton`

Only tighten options with clear intent:

1. Use `IntegrateServiceProvider = false` for strict compile-time-only resolution boundaries.
2. Use `ExplicitOnly = true` for tightly controlled container surfaces.
3. Use `UseSwitchStatement = true` only for small service sets and no imported modules.

> [!WARNING]
> With imported modules, `UseSwitchStatement = true` is ignored and `SGIOC020` is reported.

<details>
<summary>Generated Code</summary>

```csharp
// <auto-generated/>
partial class AppContainer :
    IIocContainer<global::AppContainer>,
    IServiceProvider,
    IServiceScopeFactory,
    IServiceScope,
    IDisposable,
    IAsyncDisposable
{
    private readonly IServiceProvider? _fallbackProvider;

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
    }

    // generated resolvers and cached service fields
}
```

</details>

## CLI Workflow for Existing Codebases

Use a safe CLI sequence:

1. Start with `dry-run`.
2. Restrict scope with regex and target path.
3. Review diff.
4. Apply changes.
5. Build and resolve diagnostics.

```bash
sourcegen-ioc add -t ./src -s -cn ".*Service" -n -v
sourcegen-ioc add -t ./src -s -cn ".*Service"
sourcegen-ioc generate ioc-defaults -o ./Generated/Defaults.g.cs -t ./src -s -cn ".*" -b "I.*"
```

## Diagnostics Quick Fix Map

|Diagnostic|Meaning|First Place to Check|
|:---|:---|:---|
|`SGIOC001`|Invalid registration target (`private` / `abstract`)|[02_Basic.md](02_Basic.md#diagnostics)|
|`SGIOC002`|Circular dependency|[01_Overview.md](01_Overview.md#compile-time-safety)|
|`SGIOC003`|Singleton depends on scoped|[01_Overview.md](01_Overview.md#compile-time-safety)|
|`SGIOC004`|Singleton depends on transient|[01_Overview.md](01_Overview.md#compile-time-safety)|
|`SGIOC005`|Scoped depends on transient|[01_Overview.md](01_Overview.md#compile-time-safety)|
|`SGIOC006`|`[FromKeyedServices]` + `[IocInject]` conflict|[05_Keyed.md](05_Keyed.md#diagnostics)|
|`SGIOC007`|Invalid `[IocInject]` usage|[04_Field_Property_Method_Injection.md](04_Field_Property_Method_Injection.md#diagnostics)|
|`SGIOC008`|Invalid/inaccessible `Factory` or `Instance` member|[08_Factory_Instance.md](08_Factory_Instance.md#diagnostics)|
|`SGIOC009`|`Instance` requires singleton lifetime|[08_Factory_Instance.md](08_Factory_Instance.md#diagnostics)|
|`SGIOC010`|Both `Factory` and `Instance` are specified|[08_Factory_Instance.md](08_Factory_Instance.md#diagnostics)|
|`SGIOC011`|Duplicate registration|[02_Basic.md](02_Basic.md#diagnostics)|
|`SGIOC012`|Duplicate defaults|[03_Defaults.md](03_Defaults.md#diagnostics)|
|`SGIOC013`|`[ServiceKey]` type mismatch|[05_Keyed.md](05_Keyed.md#diagnostics)|
|`SGIOC014`|`[ServiceKey]` used without keyed registration|[05_Keyed.md](05_Keyed.md#diagnostics)|
|`SGIOC015`|Key type mismatch in keyed wrappers|[05_Keyed.md](05_Keyed.md#diagnostics)|
|`SGIOC016`|Generic factory missing `[IocGenericFactory]`|[08_Factory_Instance.md](08_Factory_Instance.md#diagnostics)|
|`SGIOC017`|Duplicate placeholder types in `[IocGenericFactory]`|[08_Factory_Instance.md](08_Factory_Instance.md#diagnostics)|
|`SGIOC018`|Container dependency unresolved with strict integration|[12_Container.md](12_Container.md#diagnostics)|
|`SGIOC019`|Invalid container type declaration|[12_Container.md](12_Container.md#diagnostics)|
|`SGIOC020`|`UseSwitchStatement` ignored with imported modules|[12_Container.md](12_Container.md#diagnostics)|
|`SGIOC021`|Container accessor return type unresolved with strict integration|[12_Container.md](12_Container.md#diagnostics)|
|`SGIOC022`|Injection feature disabled by MSBuild feature flags|[04_Field_Property_Method_Injection.md](04_Field_Property_Method_Injection.md#diagnostics), [13_MSBuild_Configuration.md](13_MSBuild_Configuration.md#sourcegeniocfeatures)|

## Production Checklist

- Treat `SGIOC002` to `SGIOC005` as release-blocking design issues.
- Run CLI commands in `dry-run` mode before bulk edits.
- Use conservative container defaults unless benchmark data supports changes.
- Keep this checklist and your project-level conventions close to your app docs.

---

[← Back to Overview](01_Overview.md#table-of-contents)
