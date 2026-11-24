---
status: accepted
date: 2026-11-23
---

# Registration rules

## Context and problem statement

These decisions define how the generator reads a registration: which constructor it uses, which parameter of a decorator receives the inner instance, and which registrations it refuses.

## Decision outcome

1. **Constructor selection.** The generator chooses in this order:
   1. The one constructor marked with `[IocInject]` or `[ActivatorUtilitiesConstructor]`.
   2. If no constructor is marked, the primary constructor.
   3. If there is no primary constructor, the one public constructor.

   Any other situation is an error. Step 2 applies only to types of the current compilation. The generator decides "current compilation" by the assembly of the type, not by whether syntax is available.
2. **Inner parameter of a decorator.** The inner parameter is the constructor parameter whose type equals the service type of the chain. More than one such parameter is an error.
   - A parameter with an explicit key, and a `[ServiceKey]` parameter, are excluded first. The other parameters are judged by type only.
   - The generator decides the inner parameter once, at the level of the definition. It does not decide again after an open generic is closed.
   - Decorators apply only to registrations of an implementation type. They do not apply to factory registrations.
3. **No value-type service types.** A factory or instance member whose type is a value type is an error. The generated `KeyValuePair<TKey, T>` is the exception. A dependency of a value type that has no registration goes to the fallback provider. See [Container disposal and fallback](0006_ContainerDisposalAndFallback.md).
4. **No registration with `KeyedService.AnyKey`.** It is an error. A query with `AnyKey` is supported.
5. **No `IsDisposable` setting on attributes.** The generator decides at compile time whether a type is disposable, from the interfaces that it can see.

## Rejected options

- **Constructor: count only public constructors.** A class with a primary constructor and a second convenience constructor would need a mark, although the primary constructor is the obvious choice.
- **Constructor: choose the longest constructor that can be satisfied**, as MS.DI does. The choice then depends on which services are registered, which is not explicit.
- **Inner parameter: by type only.** A decorator could not depend on a keyed service of the same type, because the generator would see two inner parameters.
- **Inner parameter: decide again after closing.** After closing, another parameter can happen to have the service type. The decision would then change with the type argument.
- **Value-type services.** They are possible but need a second path almost everywhere:
  - The cache uses `null` for "not created yet".
  - A struct larger than a pointer cannot be read atomically.
  - A singleton struct gives each dependency site a copy.
  - `T?` is `Nullable<T>`, a different type.
  - Decorators and disposal need new rules.

  The common use is a configuration value, and a class wrapper covers it.
- **`AnyKey` registration.** With MS.DI, a singleton registered with `AnyKey` has one instance for each requested key. The number of instances is known only at run time. The container allocates singletons as fixed fields at compile time and decides the disposal order at compile time. An `AnyKey` registration also has no key for a dictionary entry or a `KeyValuePair`.
- **An `IsDisposable` setting.** Its only use is a partial type whose `IDisposable` implementation comes from another generator. The user can list the interface on the hand-written part instead. The setting would also need a place for decorators, separate values for `IDisposable` and `IAsyncDisposable`, a merge rule, and a run-time failure when the user sets it wrongly.

## Evidence

Tested with Roslyn 5.12.0 (.NET SDK 11.0.100) and `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10.0.15.

- Roslyn 5.12.0 has no public API that identifies a primary constructor. For a source type, the declaring syntax of the primary constructor is the type declaration itself. For a type from metadata, a primary constructor and a hand-written constructor have the same members and attributes.
- With a compilation reference, which stands for a project reference in an IDE, a type of another assembly also has declaring syntax. With a DLL reference it has none. A check based on "syntax is available" would therefore choose different constructors in the IDE and on the command line. This was tested with `Compilation.ToMetadataReference()`, not in a real IDE.
- Partial type and interfaces:
  - The user writes `partial class Foo : IDisposable;` and another generator supplies `Dispose()`. The code compiles, and this generator sees `IDisposable` in `AllInterfaces`.
  - Only the part from the other generator declares the interface. This generator sees no interface.
  - The order of the two generators does not change either result.
- MS.DI: a singleton registered with `KeyedService.AnyKey` gives a different instance for each distinct requested key, for a type registration and for a factory registration.
- MS.DI: a value-type service type registered through the non-generic `ServiceDescriptor`, keyed or not, resolves as a single service under JIT and NativeAOT. Without a registration:
  - `GetService(Type)` returns `null`.
  - The generic `GetService<T>()` throws `NullReferenceException` for a value type `T`.
- MS.DI: `[FromKeyedServices]` with no argument inherits the key of the containing service, and `[FromKeyedServices(null)]` resolves the non-keyed service. The parameterless form and `ServiceKeyLookupMode` exist from version 10.0.0. When the inherited key has no registration, a required parameter throws and does not fall back to the non-keyed service.
