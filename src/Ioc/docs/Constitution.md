# SourceGen.Ioc constitution

This document holds the highest rules for SourceGen.Ioc. Use it when you design a feature or write a specification.

## 1. Purpose

SourceGen.Ioc is a source generator. It has two outputs for dependency injection:

- A container that implements Inversion of Control. The generator builds it at compile time.
- Registration code for the `IServiceCollection` of `Microsoft.Extensions.DependencyInjection` (MS.DI).

The container implements the interfaces of `Microsoft.Extensions.DependencyInjection.Abstractions`. The lowest supported version is 10.0, because the design uses `ServiceKeyLookupMode`.

The container and the `IServiceProvider` that MS.DI builds from the registration code must behave the same way where possible. MS.DI is the reference. Each difference must be recorded in [section 8](#8-documented-differences-from-msdi).

## 2. Precedence and amendment

The design documents are in `src/Ioc/docs/`.

| Document | Role |
| --- | --- |
| `Constitution.md` | Principles, scope, limits, terms. It changes rarely. |
| `spec/*.spec.md` | The source of truth for current behavior. When behavior changes, rewrite the specification in place. |
| `adr/*.md` | Architectural Decision Records. They record why a decision was made and which alternatives were rejected. They do not describe current behavior. |

Rules:

1. A specification MUST NOT contradict this constitution.
2. If a specification and this constitution disagree, this constitution wins. The person or agent who finds the conflict MUST tell the user.
3. A change to this constitution MUST come with a new ADR that gives the reason.
4. A change to a specification needs an ADR only when the decision has a rejected alternative that someone can propose again, or when the decision looks strange without its background.
5. An ADR MUST record each tested fact that the decision relies on, with the versions of the tools and packages used in the test.
6. All design documents are written in English.
7. A specification states the current rule only. It does not mention the previous library. A comparison with the previous library belongs in an ADR.

## 3. Terms

| Term | Meaning |
| --- | --- |
| Instance definition | The unit that identifies an instance. Its identity is the source of the instance (implementation type, factory method, or static instance member) together with the key. |
| Concrete registration | An instance definition that the user declares directly and that is not an open generic, for example `IntRepo` or `Handler<Foo>`. |
| Open registration | An instance definition that the user declares on an open generic, for example `Repo<>`. It is a template and not a node of the dependency graph. |
| Derived registration | An instance definition that the generator gets when it closes an open registration for a demand, for example `Repo<int>` from `Repo<>`. |
| Closed definition | A concrete registration or a derived registration. Closed definitions are the nodes of the dependency graph. |
| Registration attribute | `IocRegisterAttribute`, `IocRegisterForAttribute`, `IocRegisterBatchAttribute`, `IocFactoryAttribute`, `IocFactoryForAttribute`, `IocInstanceAttribute`, `IocInstanceForAttribute`. |
| Dependency site | A place that asks for a dependency: a constructor parameter, an injected member, a parameter of a factory method, a parameter of a decorator, or a partial accessor. |
| Module | An assembly that marks a class with `IocModuleAttribute` so that other assemblies can import its registrations. |
| Importer | An assembly that imports a module with `IocImportModuleAttribute`. An importer can be a module too. |
| Composer | The party that flattens all imported modules into one dependency graph and owns the instances. A composer is a generated container or the `Add{Name}` method of an assembly. |
| Output | One of the three independent results of the generator: a container, a module output (module info and entries), or an `Add{Name}` method. |
| Fallback provider | The `IServiceProvider` that a container asks when it has no registration for a request. |
| Suppression | The rule that a concrete registration removes a derived registration with the same implementation type and key. |
| Definite edge | A dependency edge whose target cannot change when tags change or when an importer adds registrations. |
| Documented difference | A behavior that differs from MS.DI on purpose and is listed in section 8. |

Terms that belong to one output only, such as the kinds of entries or the holder types, are defined in the specifications.

## 4. Principles

### 4.1 Explicitness

All settings MUST be explicit. The generator MUST NOT convert types implicitly, lose a setting, or let one setting silently replace another.

The generator decides a result from types alone only in the cases below. The dependency graph MUST be able to show the result of each case. Everything else needs an explicit setting.

1. An implementation type registers itself as a service type.
2. An open generic service type on a non-generic or partly closed implementation type expands to all closed forms that the implementation type implements.
3. An open generic implementation or decorator whose generic constraints do not fit is not a candidate.
4. A decorator applies only to the service types for which it has an inner parameter.
5. A concrete registration suppresses a derived registration with the same implementation type and key.
6. When a dependency site asks for one service, a concrete registration wins over a derived registration.
7. A parameter with a default value, a reference type annotated as nullable, or a field or property that has `[IocInject]`, has an initializer, and can be assigned after construction is an optional dependency. An initializer counts only for a type whose source is in the current compilation.
8. When a dependency site asks for `Task<T>` and the service is synchronous, the generator wraps the service in a completed task.
9. When a dependency site asks for keyed services by key type (`KeyValuePair<TKey, T>`, a collection of them, or a dictionary), a registration matches only when its key type is exactly `TKey`. When `TKey` is `object`, all keyed registrations match.

### 4.2 Correctness

The generated code MUST reflect the registration settings exactly.

### 4.3 NativeAOT

The generated code MUST compile and publish with NativeAOT without warnings. It MUST handle the limits of trimming and static analysis correctly.

### 4.4 Thread safety by default

The generated container is thread-safe by default.

### 4.5 Performance

A resolution path that the container handles completely by itself MUST NOT be slower than MS.DI. This covers single services, keyed services, collections, scope creation, scope disposal, and each wrapper type. Benchmarks verify each path.

A path that goes through the fallback provider is outside this guarantee. It still MUST have a benchmark record.

`IocContainerAttribute.IntegrateServiceProvider` is `true` by default, so a container has a fallback provider by default. With this default, a missing dependency is only an Info diagnostic. Keyed and collection requests also go through the fallback provider, outside the performance guarantee. Set the option to `false` to get the full compile-time guarantee and the full performance guarantee.

### 4.6 One analysis for diagnostics and generation

The generator reports IoC semantic errors itself, from the same analysis result that it uses to generate code. A separate analyzer MUST NOT repeat that analysis.

### 4.7 A dependency graph that people can read

A user MUST be able to understand the dependencies of an application, also when the graph is large. The information that the generator emits MUST be enough to rebuild the dependency graph. A tool that shows the graph has its own specification.

## 5. Scope

### 5.1 General rule

All registration information MUST be known at compile time, unless a specification says otherwise. The generator generates code from that information.

The generator does not check type compatibility, unless a specification says otherwise. It generates code from the types that the user gives, and the C# compiler reports type errors. The generated code MUST contain static casts, for example from the implementation to the declared service type, so that a type error appears at compile time and not as an `InvalidCastException` at run time.

### 5.2 Limits

| Area | Limit |
| --- | --- |
| Variance | Service type matching does not use covariance or contravariance. A dependency on `IHandler<object>` does not resolve to a service registered as `IHandler<string>`. This limit does not apply to the check of generic constraints. |
| Nested open generics | `Outer<>.Inner<>` is not supported and is an error. `Outer<int>.Inner<>` and `Outer.Inner<>` are supported. |
| Recursive generics | A generic resolution that never ends is an error. |
| Open generics | Only closed types that the generator finds at compile time are supported. |
| Disposable transient | The container does not support a transient service that is disposable. This is an error on the container only. It is not relaxed. |
| Implementation type | Only a non-abstract, non-static class, including a record class. A struct, an interface, or an abstract class is an error. |
| Service type | Only a reference type can be registered. A factory or instance member with a value type is an error. The generated `KeyValuePair<TKey, T>` is the exception. A value-type dependency that has no registration goes to the fallback provider like any other missing dependency. |
| Injection method | Not generic. No `ref`, `out`, or `in` parameter. |
| `ValueTask<T>` | Not supported. |
| One instance per service type | Not offered. Use different keys or a factory registration to get different instances. |
| `KeyedService.AnyKey` | Cannot be used to register. It can be used to query. |
| Wrapper types | The user cannot register `Func<T>`, `Lazy<T>`, `Task<T>`, `KeyValuePair<TKey, T>`, collections, or dictionaries as service types. The generator creates them. |
| Nested scopes | Not supported. A scope created from a scope is another scope of the root. |

### 5.3 Deferred features

These features are outside the core. They are handled as extensions after the core is complete:

- The activator interfaces of MVC and Blazor.
- Feature switches (`SourceGenIocFeatures`).
- A tool that shows the dependency graph.
- A generic visitor that lets a module close an open generic of the composer with an inaccessible type.
- Service lookup by a generated `switch` in the container.
- A compile-time check for a call to an `Add{Name}` method that another `Add{Name}` method already covers.

## 6. Composition model

Only the composer owns instances.

- A module holds no instance. It gives registration information and entries.
- The composer flattens the registrations of all modules into one dependency graph. It manages lifetime, caching, and disposal itself.
- A module gives its information to the importer at compile time only. The information MUST be complete enough that the importer does not depend on a symbol that it cannot access.
- A module gives public entries so that the composer can create and use types that are internal to the module.
- The order of registrations does not depend on which assembly declares them or on the order of imports. An assembly cannot override a registration of a module that it imports.
- A public type or member that exists only for generated code is not a user API. Its shape can change with the version of the generator.

## 7. Diagnostic policy

Errors are of two kinds:

- **Errors that the C# compiler reports**, such as an inaccessible member or an incompatible type. The generator still generates code, and the compile error informs the user. The generator reports no diagnostic for them.
- **IoC semantic problems.** The generator reports them.

Levels:

| Level | Use | Can the user change it? |
| --- | --- | --- |
| Error | A registration or dependency graph that is wrong. | No. Configuration files and `#pragma` cannot disable it or lower it. |
| Warning | A situation that can be a problem but is not always wrong. | Yes. The user can change the level or disable it. The generator does not read the changed level, and the generated code stays the same. |
| Info | A fact that the user can want to know. `dotnet build` does not show it on the console. | Yes. |

Each diagnostic MUST have exactly one of these consequences:

- **Skip the declaration.** The generator skips that declaration and continues. The skipped instance definition stays in the dependency graph as an error node. A dependency on an error node does not cause another diagnostic and does not go to the fallback provider.
- **Do not generate the output.** The affected container, module output, or `Add{Name}` method is not generated. Other outputs are not affected.
- **Inform only.** Warning or Info. Generation is not affected.

The generator checks the dependency graph at two levels:

- **Definite check.** Each assembly does it, with or without a container or module. It checks definite edges, and the rules that need no candidate to decide. The specifications list those rules.
- **Full check.** Only a container does it. It repeats all checks on the complete dependency graph.

A diagnostic about the dependency graph MUST name the module and the instance definition that has the problem.

Diagnostic reporting MUST NOT break incremental caching.

## 8. Documented differences from MS.DI

"Container" means the generated container. "Registration" means the `Add{Name}` method.

| # | Difference | Applies to |
| --- | --- | --- |
| 1 | A disposable transient service is an error. | Container |
| 2 | An implementation type registers itself as a service type. | Both |
| 3 | A singleton or scoped instance definition shares one instance across its service types, decorators, and wrapper types. Use different keys or a factory registration to get different instances. | Both |
| 4 | A request for one service that has more than one candidate is an error, at compile time and at run time. MS.DI returns the last registration. | Container |
| 5 | The order of a collection follows the rules of the specifications. It is not the order in which the user wrote the registrations. | Both |
| 6 | Resolution of a scoped service from the root always throws. MS.DI does this only with `ValidateScopes`. | Container |
| 7 | A captive dependency and a circular dependency are compile-time errors. MS.DI checks them at run time, and the first one only with `ValidateScopes`. | Both |
| 8 | A singleton or scoped service that depends on a transient service, except through `Func<T>`, causes a compile-time warning. MS.DI does not check this. | Both |
| 9 | Singletons are resolved eagerly by default. | Container |
| 10 | Open generics support only the closed types found at compile time. | Both |
| 11 | An asynchronous service cannot be resolved synchronously. | Both |
| 12 | The constructor must be unambiguous. The generator does not choose the longest constructor that it can satisfy. | Both |
| 13 | A reference type annotated as nullable is an optional dependency. It gets `null` when no service is found. | Both |
| 14 | `KeyedService.AnyKey` cannot be used to register. | Both |
| 15 | Wrapper types cannot be registered as service types. | Both |
| 16 | Only reference types can be registered as service types. | Both |
| 17 | A dependency site that the generator sees at compile time matches keys by their compile-time identity. A lookup at run time by `Type` and key uses `Equals`, as MS.DI does. MS.DI uses `Equals` in both cases, so two keys with different identities that are equal at run time match in `Add{Name}` and do not match at a compile-time dependency site of the container. | Container |
| 18 | Ambiguity, missing dependencies, and disposable transient services are not checked at compile time. MS.DI handles them at run time. | Registration |
| 19 | `Task<T>` is not registered as a service. An asynchronous service cannot be resolved with `GetService<Task<T>>()`. | Registration |
| 20 | When a service type has asynchronous candidates, `GetServices<T>()` returns only the synchronous candidates. The container throws. | Registration |
| 21 | MS.DI calls `Dispose` on the same Singleton or Scoped object more than once: once through its holder and once for each service type registration that returns it. The container disposes it once. | Registration |
| 22 | When the return type of a transient factory does not show at compile time whether the object is disposable, the object is not disposed. MS.DI tracks and disposes it. | Container |
| 23 | When a service throws during disposal, the container continues to dispose the other services and throws at the end. MS.DI stops, and the other services are not disposed. | Container |
| 24 | Without a fallback provider, a request by `Type` for an `IEnumerable<T>` that the container does not know returns `null`, so `sp.GetServices<T>()` throws. MS.DI returns an empty collection. Calls that are visible at compile time and the strongly typed `GetServices<T>()` of the container are not affected. | Container |
| 25 | A resolve call in code asks for `Lazy<T>` or `Func<T>`, and `T` has no registration in the assembly view (for example a service of the framework). The container creates the wrapper and resolves `T` inside it. `Add{Name}` registers no wrapper, so the call gets `null`. A dependency site is not affected: it creates the wrapper itself. | Registration |

## 9. Testing and benchmarks

### 9.1 Tests

- The test framework is TUnit.
- Generated code MUST have snapshot tests. A snapshot test records the generated code and shows each change.
- A snapshot test MUST verify that the generated code compiles.
- A snapshot test is not a unit test. It verifies the generated code for a scenario, and one test case can cover several scenarios.
- The generated container is large. For a focused test, remove the unrelated parts and keep only the code under test in the snapshot.
- Container behavior MUST be tested with NativeAOT, including module imports across assemblies.
- A NativeAOT test MUST use the objects that it creates. NativeAOT removes a call whose result is not used, together with the casts inside it, so a test that does not use the object does not verify its type.
- Incremental behavior MUST be tested. The tests verify that the output of each pipeline stage can be cached and that an unrelated edit does not run a stage again.

### 9.2 Benchmarks

- MS.DI is the baseline.
- Each path that the container resolves completely by itself is verified to be no slower than MS.DI. Paths through the fallback provider are recorded only.
- The run time of the generator and the compile time of the generated code MUST have benchmark records.
