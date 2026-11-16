# Generic Dependency Diagnostics

Diagnostics for dependencies of open generic registrations that nest type arguments without end. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC036 - Error - Design - Dependency nests type arguments without end

Report when a dependency of an open generic registration (or of one of its decorators) is expanding, as defined by [Generic Registration](../../Spec/Register.Generics.spec.md): closing it nests type arguments deeper on every round of a dependency cycle, so the generator does not close it at compile time. A cycle through it has no dependency resolved through `Lazy<T>` or `Func<...>`, so its object graph is infinite: resolving it at runtime from the open generic registration never ends either.

**Analysis:**

- Builds the open generic registrations of the compilation (implementation, service types, interfaces, decorators, injected members, `Factory` and `Instance`) like the generator's open generic entries, including the registrations of `[IocRegisterDefaults]` with a `Factory` and an open generic target type, and runs the generator's own analysis on them, so the reported dependencies are the ones the generator does not close. The `ImplementationTypes` of `[IocRegisterDefaults]` / `[IocRegisterDefaults<T>]` of the compiled assembly, assembly-level and type-level, are registered with the generator's own code (`CreateRegistrationDataFromDefaultSettings`, `ProcessSingleRegistrationFromDefaults`): with the service types, decorators and `[IocInject]` members the settings give them, and the `Factory` of the settings for an open implementation. An open implementation is analyzed like an `[IocRegister]` one and reported at its own dependencies (see below), not at the `typeof()` in `ImplementationTypes`; a closed one adds the closed generic dependencies of how it is created. An implementation also registered by an attribute is reported once. A registration's own `Factory` takes precedence over the `Factory` of its best default settings (`SGIOC010` covers `Factory` with `Instance` on the same attribute); the `Factory` method is resolved by the attribute syntax node action, which has the semantic model that `nameof()` needs. A `Factory` given as a string has no known parameters, like in the generator.
- Only the dependencies of how the closed registrations are created are analyzed (see [Generic Registration](../../Spec/Register.Generics.spec.md)): the constructor parameters and injected members of a constructed implementation, the `Factory` method parameters except `IServiceProvider` and `[ServiceKey]` parameters (written with the factory's own type parameters and those of the implementation type, such as `INode<List<T>>` of `Node<T>.Create`, which are the implementation's own), and none for an `Instance` (whose decorators are not analyzed either).
- The closed generic dependencies of closed registrations, which a dependency that is a type parameter can become, are collected like the generator's: by how each registration creates its instance (the constructor and injected members, the `Factory` parameters substituted for each registered type a generic factory's mapping matches, including the implementation type, none for an `Instance`), and the dependencies of the closed decorators applied to them.
- The analysis is static: an expanding dependency is reported whether or not a dependency of a closed registration reaches the open generic registration.
- A dependency is deferred (`SGIOC037`) when each of its expanding cycles has a dependency wrapped in `Lazy<T>` or `Func<...>` (also inside collections); otherwise `SGIOC036` is reported. Cycles are found among the dependencies without such a wrapper, and the decorator and factory links, which belong to no dependency, so a `Lazy<T>` on another cycle does not make an eager expanding cycle deferred.
- Reports once per constructor parameter, injected property or field, or injected method parameter, at that symbol, when it is declared in source. A member listed in `InjectMembers` of the registration attribute (resolved on the open generic implementation, like the generator) is reported once at its `InjectMembers` element, unless `[IocInject]` on the member takes precedence. The members are collected by the attribute syntax node action, which has the semantic model that `nameof()` needs. Injected members whose feature is disabled (`SGIOC022`) are not reported. An expanding `Factory` method parameter is reported once at that parameter, when the method is declared in source, even when the factory serves several registrations (`SGIOC036` when it is eagerly expanding for any of them, otherwise `SGIOC037`); `{Implementation}` is then the factory method (such as `NodeFactory.Create<TF>`), and `{DependencyType}` is written with the factory's type parameters as declared (`INode<List<TF>>`). A factory declared in metadata is analyzed but not reported.

**Message format:** `Dependency '{DependencyType}' of '{Implementation}' nests type arguments deeper on every round of a dependency cycle without a Lazy<T> or Func<T> dependency, so its object graph is infinite and it is not closed at compile time`

**Fix:** Break the cycle, or depend on a type argument that does not nest the implementation's type parameters.

---

## SGIOC037 - Warning - Design - Dependency nests type arguments without end behind a deferred wrapper

Report when each expanding cycle of an expanding dependency (see `SGIOC036`) has a `Lazy<T>` or `Func<...>` dependency. The object graph only grows when the wrapper is used, but the generator does not close the dependency: it is resolved at runtime from the open generic registration of the integrated `IServiceProvider` (by reflection, which is not Native AOT safe), and a container with `IntegrateServiceProvider = false` cannot resolve it.

**Analysis:** Same as `SGIOC036`.

**Message format:** `Dependency '{DependencyType}' of '{Implementation}' nests type arguments deeper on every round of a dependency cycle, so it is not closed at compile time`

**Fix:** Register the closed forms that are needed explicitly, or break the cycle.

---

## Known Limitations

- `SGIOC036` / `SGIOC037`: the analyzer only sees the registrations `RegisterAnalyzer` collects and the `ImplementationTypes` of the default settings of the compiled assembly, so the registrations of imported modules are not analyzed. Default settings with a `Factory` and an open generic target are analyzed whether they are assembly-level or type-level; those of imported modules have no `Factory`, like in the generator. The closed generic types that a dependency of type `T` (a type parameter) can become are taken from these registrations, without `IServiceProvider` invocations or `[IocDiscover]`. These limits can miss a dependency the generator does not close, but do not report one it closes. The registrations of all containers are analyzed together, also for `ExplicitOnly` or `IncludeTags` containers whose own registrations do not form the cycle.
