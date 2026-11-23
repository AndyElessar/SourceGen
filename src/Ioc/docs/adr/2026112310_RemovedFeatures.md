---
status: accepted
date: 2026-11-23
---

# Features removed in the rewrite

## Context and problem statement

The rewrite (see [Rewrite of SourceGen.Ioc](2026112301_IocRewrite.md)) does not keep compatibility with the previous version of SourceGen.Ioc. Each feature of the previous version was kept only when it fits the principles of the [constitution](../Constitution.md), most of all explicitness.

## Decision outcome

The rewrite removes these features:

| Removed feature | Replacement or reason |
| --- | --- |
| `IocRegisterDefaultsAttribute` | `IocRegisterBatchAttribute` |
| `RegisterAllInterfaces`, `RegisterAllBaseClasses` | Service types are listed explicitly. |
| MSBuild property `SourceGenIocDefaultLifetime` | The lifetime is a required setting. |
| MSBuild property `SourceGenIocName` | `IocServiceCollectionExtensionsAttribute.Name`. See [Generation of the Add method](2026112308_AddMethodGeneration.md). |
| A factory given as a string | `nameof()` or the factory attributes |
| A key that uses a type parameter | A key must be known at compile time. |
| `UseSwitchStatement` | The container uses lookup tables only. See [Lifetime and thread safety](2026112306_LifetimeAndThreadSafety.md). |
| `IocGenericFactoryAttribute` and `GenericTypeMap` | A generic factory method |
| Generic attributes (`IocRegister<T>` and others) | The non-generic attributes |
| Third-party `[Inject]` attributes | `IocInjectAttribute` |
| Registration of collection types by the user | The generator creates wrapper types. |
| A module imported as an independent container (`IIocContainer<TContainer>.Resolvers`, merge at run time) | The composer owns the instances. See [Cross-assembly composition](2026112303_CrossAssemblyComposition.md). |
| Separate analyzers (`RegisterAnalyzer`, `ContainerAnalyzer`) | The generator reports diagnostics. See [Diagnostics by the generator](2026112302_DiagnosticsByGenerator.md). |
| Decorators on factory registrations | Decorators apply only to registrations of an implementation type. |
| `Task<T>` registrations in `Add{Name}` | `IocTask<S>`. See [Holders and asynchronous services](2026112309_HoldersAndAsyncServices.md). |
| The open generic registration in `Add{Name}` | Only derived registrations are registered. See [Generics and constraints](2026112304_GenericsAndConstraints.md). |
| One `Add{Name}` call for each module | Only the method of the final assembly is called. |
| `ThreadSafeStrategy.SemaphoreSlim`, `ThreadSafeStrategy.SpinLock` | `Lock` or `CompareExchange` |
| The parameterless container constructor when `IntegrateServiceProvider` is `true` | Missing dependencies go to the fallback provider at compile time, so a container without one fails at run time. |
| All diagnostics `SGIOC001` to `SGIOC046` | New IDs by category. The new numbers do not avoid the old numbers, so one ID can have a different meaning in the old and the new version. |
| `AsyncInitHolder` | `IocHolder.OwnTask` and `IocTask<S>`. See [Holders and asynchronous services](2026112309_HoldersAndAsyncServices.md). |

## Rejected options

- **Keep the features for compatibility.** Several of them let the generator decide a result that the user did not state, which is against the explicitness principle. Others exist in two forms that do the same thing.

## Evidence

No tests apply to this decision.
