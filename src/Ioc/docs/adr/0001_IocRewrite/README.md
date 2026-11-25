---
status: open
date: 2026-11-23
---

# Rewrite of SourceGen.Ioc

This document is the entry point of the ADR set of the rewrite, the folder `0001_IocRewrite`. It gives the background and the goal that all ADRs of the set share, and it links to each of them. Other documents refer to an ADR of the set as `IocRewrite/NNNN`, for example `IocRewrite/0016`. The set is open while the rewrite goes on, so its ADRs are edited in place when a decision changes (see [IocRewrite/0010](0010_DesignDocumentLayout.md)).

## Context and problem statement

The previous design of SourceGen.Ioc had two problems.

It was not explicit, and it was hard to use:

- Many settings worked through default values. A user could not see from a registration which lifetime, service types, or key applied.
- One set of attributes mixed three different things: registration of an implementation type, registration of a factory method, and registration of an instance. The meaning of a property depended on which of the three the attribute described.

It became hard to maintain:

- As features were added, many situations appeared that the first design did not consider.
- Those situations caused many bugs. Each fix made the next one harder, because the fixes worked around the design and did not change it.

## Decision drivers

- A user must be able to read a registration and know what it does.
- The limits and the supported range must be stated before the code is written.
- Each difference from `Microsoft.Extensions.DependencyInjection` (MS.DI) must be recorded.
- A rule must have one implementation, so that two code paths cannot disagree.

## Decision outcome

SourceGen.Ioc is rewritten. The rewrite does not keep compatibility with the previous version.

The goal of the rewrite is to:

1. Design the way of use again, so that every setting is explicit.
2. Define the limits and the supported range clearly.
3. Record the differences from MS.DI.
4. Avoid the confusion and the maintenance problems of the previous design.

The [constitution](../../Constitution.md) holds the principles, the scope and limits, and the documented differences that follow from this goal. The [specifications](../../spec/SPEC.spec.md) hold the behavior.

### Decisions of this set

| ADR | Topic |
| --- | --- |
| [0001](0001_DiagnosticsByGenerator.md) | The generator reports diagnostics. Levels and numbering of diagnostics. |
| [0002](0002_CrossAssemblyComposition.md) | Modules, module info, entries, and the JSON reader and writer. |
| [0003](0003_GenericsAndConstraints.md) | Open generics and the check of generic constraints. |
| [0004](0004_RegistrationRules.md) | Constructor selection, the inner parameter of a decorator, and registrations that are refused. |
| [0005](0005_LifetimeAndThreadSafety.md) | Rules between lifetimes, thread safety strategies, root and scope. |
| [0006](0006_ContainerDisposalAndFallback.md) | Disposal in the container and the fallback provider. |
| [0007](0007_ServiceCollectionExtensionGeneration.md) | Generation of the service collection extension. |
| [0008](0008_HoldersAndAsyncServices.md) | Holders, asynchronous services, and failure during creation in the service collection extension and the container. |
| [0009](0009_RemovedFeatures.md) | Features of the previous version that the rewrite removes. |
| [0010](0010_DesignDocumentLayout.md) | Layout and rules of the design documents. |
| [0011](0011_ImplicitRulesInConstitution.md) | Initializers and the `object` key type in the list of implicit rules of the constitution. |
| [0012](0012_ServiceCollectionExtensionWrapperWithoutCandidate.md) | A wrapper of a service that the service collection extension does not register. |
| [0013](0013_CompositionRootAndServiceCollectionExtension.md) | The terms "composition root" and "service collection extension". |
| [0014](0014_CycleReportForEachComponent.md) | One report of a circular dependency for each strongly connected component. |
| [0015](0015_ContainerClassIsNotAService.md) | A container class cannot be registered as a service. |
| [0016](0016_OptionalDependencies.md) | Which dependencies are optional: only an explicit default value or a real initializer, not a nullable annotation. A partial accessor is the exception. |
| [0017](0017_FallbackInterfacesAsServices.md) | The container gets `IServiceProviderIsService` and `IServiceProviderIsKeyedService` from the fallback provider as services. |
| [0018](0018_NoAnyKeyRegistration.md) | A registration cannot use `KeyedService.AnyKey` as its key. A documented difference of the constitution. |
| [0019](0019_KeyMatchesOnlyAtRunTime.md) | A key of a dependency site that can be equal to a registered key only at run time is a warning (`SGIOC505`). The container keeps the match by compile-time identity. |

### Consequences

- Good: one analysis gives both the generated code and the diagnostics.
- Good: a registration states its lifetime, its service types, and its kind (implementation type, factory, or instance) directly.
- Bad: users of the previous version must change their code. The attributes, the diagnostics, and the generated API are different.
- Bad: some features of the previous version do not exist in the rewrite. See [0009](0009_RemovedFeatures.md).

## Rejected options

- **Continue to repair the previous design.** The bugs came from situations that the design did not consider. More repairs would add more special cases.
- **Rewrite and keep the previous attributes and defaults.** The defaults and the mixed attributes are the cause of the first problem.

## Evidence

No tests apply to this decision. Each ADR of the set lists its own tested facts.
