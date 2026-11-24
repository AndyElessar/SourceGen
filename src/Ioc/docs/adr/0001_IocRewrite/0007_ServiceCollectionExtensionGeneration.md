---
status: accepted
date: 2026-11-23
---

# Generation of the service collection extension

## Context and problem statement

The service collection extension registers the complete dependency graph of an assembly, including all imported modules, with an `IServiceCollection`. MS.DI then owns the instances.

In the previous design every assembly got a service collection extension, and the user called the method of each module. The method name came from the MSBuild property `SourceGenIocName`.

In the new design the method of the final assembly already contains every imported module. A call to the method of a module as well would register its services twice.

## Decision outcome

1. **The service collection extension is opt-in.** The generator emits it only for an assembly that has `[assembly: IocServiceCollectionExtensions]`. The `Name` property of the attribute gives `{Name}` and replaces `SourceGenIocName`. Without `Name`, the assembly name is used.
2. **The attribute is named `IocServiceCollectionExtensionsAttribute`.** The name matches the generated class `{Name}ServiceCollectionExtensions`.
3. **A run-time guard stops duplicate registration.** Before it registers anything, the service collection extension calls `IocRegistrationMarker.Enter` with the names of all assemblies that it covers. If one of them already has a marker in the `IServiceCollection`, the call throws `InvalidOperationException` and changes nothing. The guard stops three cases:
   - The same service collection extension is called twice, also with different tags.
   - A module is imported and its own service collection extension is called too.
   - Two service collection extensions import the same module.
4. **A definite ambiguity causes Warning `SGIOC504`.** The service collection extension does not do the full check, because MS.DI resolves a single service to the last registration. The warning covers only the cases that no tag or importer can change: two or more concrete registrations without tags for one service type and key, or no concrete registration at all and two or more derived registrations without tags.
5. **Each closed definition has its own creation method.** The generator always emits it as a private static method and does not inline it into the registration lambda.
6. **Generated code does not use `var`.**

## Rejected options

- **Generate the service collection extension for every assembly.** A module that is imported through several levels would have its registration code compiled once at each level. The user would also see a method on every module and call them all.
- **An MSBuild property as the opt-in.** An attribute is visible in source and to the generator of an importer. The method name then also has one source only.
- **`IocServiceCollectionAttribute` as the name.** It is easy to confuse with the `IocRegister` attributes.
- **A compile-time check that scans calls to the service collection extension.** The project that makes the call does not always run this generator, and a call can go through a helper or a variable. It is deferred.
- **No guard.** A duplicate call fails silently: collections contain each service twice, and singletons have two holders.
- **A full check in the service collection extension.** Ambiguity and missing dependencies are legal for MS.DI, and other code can add registrations to the same `IServiceCollection`.
- **Inline the creation code into each registration lambda.** The holder registration, the wrappers, and the decorator chain of one definition all call the same creation code.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10.0.15.

- MS.DI does not dispose an object that is registered as an instance. The marker is registered that way.
- MS.DI resolves a single service to the last registration and returns a collection in registration order. The generator therefore registers derived registrations first and concrete registrations last. A concrete registration then wins, as in the container.

Not tested: the cost of the linear scan in `IocRegistrationMarker.Enter` for a large `IServiceCollection`.
