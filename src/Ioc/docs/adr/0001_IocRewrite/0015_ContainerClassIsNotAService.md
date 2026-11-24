---
status: accepted
date: 2026-11-24
---

# A container class cannot be registered as a service

## Context and problem statement

A container class is a built-in service of its own container (see [Built-in services](../../spec/Analysis.Model.spec.md#built-in-services)). A dependency on it gets the container instance that resolves it.

The specification did not say what happens when the user registers a container class, as the implementation type or as a service type. Such a registration gives a container class two meanings. In its own container, it is the built-in service. In another container, or in the service collection extension, it is an ordinary closed definition, and the resolution creates a new container instance. The checks of the dependency graph then have to know which container classes are not nodes.

## Decision outcome

- A container class (a class that has `IocContainerAttribute`) cannot be an implementation type. The generator reports `SGIOC003`.
- A container class cannot be a service type. The generator reports `SGIOC004`.

### Consequences

- Good: a container class has one meaning. No closed definition provides it.
- Good: the definite check does not need the list of container classes, so a change to a container attribute does not run it again.
- Bad: a user who wants to resolve one container from another container must register a factory or an instance of a wrapper class.

## Rejected options

- **Allow the registration, and use it outside the own container.** The container class then means different things in different places. A dependency on it in another container creates a new container instance, which is rarely what the user wants.
- **A new diagnostic ID.** `SGIOC003` and `SGIOC004` already describe an invalid implementation type and an invalid service type. A new reason in their messages is enough.

## Evidence

No tests apply to this decision. The tests of the generator check the diagnostics (`DependencyGraphTests`).