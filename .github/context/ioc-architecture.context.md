---
description: "IoC source generator architecture summary for quick agent context loading."
applyTo: "src/Ioc/**"
---

# IoC Generator Architecture

## Overview

Compile-time IoC container generator based on `Microsoft.Extensions.DependencyInjection.Abstractions`. Produces zero-reflection, AOT-compatible dependency injection containers via C# incremental source generation.

## Entry Point

`IocSourceGenerator` — implements `IIncrementalGenerator` in `src/Ioc/src/SourceGen.Ioc.SourceGenerator/Generator/IocSourceGenerator.cs`

## Pipeline Stages

```text
ForAttributeWithMetadataName
├── IocRegisterAttribute        → BasicRegistrationResult
├── IocRegisterForAttribute     → BasicRegistrationResult
├── IocRegisterDefaultsAttribute→ DefaultSettingsResult
├── IocImportModuleAttribute    → ImportModuleResult
├── IocDiscoverAttribute        → ClosedGenericDependency
└── IocContainerAttribute       → Container generation output
         ↓
    CombineAndResolve → ServiceRegistrationWithTags
         ↓
    RegisterSourceOutput (registration code + container code)
```

## Key Data Models

| Type | Purpose | Location |
| ---- | ------- | -------- |
| `BasicRegistrationResult` | Core registration pipeline output | `Models/BasicRegistrationResult.cs` |
| `DefaultSettingsResult` | Defaults transform result | `Models/DefaultSettingsResult.cs` |
| `ImportModuleResult` | Module import transform result | `Models/ImportModuleResult.cs` |
| `ServiceRegistrationWithTags` | Merged registration with tags | `Models/ServiceRegistrationWithTags.cs` |
| `OpenGenericEntry` | Open generic service entries | `Models/BasicRegistrationResult.cs` |
| `ClosedGenericDependency` | Discovered closed generic deps | `Models/BasicRegistrationResult.cs` |

## Public Attributes

| Attribute | Purpose |
| --------- | ------- |
| `[IocContainer]` | Marks a partial class as the container |
| `[IocRegister]` | Register a service type directly |
| `[IocRegisterFor]` | Register implementation for specific service types |
| `[IocRegisterDefaults]` | Set default lifetime and options for registrations |
| `[IocImportModule]` | Import registrations from another assembly/module |
| `[IocDiscover]` | Auto-discover and register closed generics |
| `[IocInject]` | Mark field/property/method for injection |
| `[IocGenericFactory]` | Mark a method as generic factory provider |

## Project Layout

| Directory | Purpose |
| --------- | ------- |
| `src/SourceGen.Ioc/` | Public API attributes and runtime types |
| `src/SourceGen.Ioc.Cli/` | CLI tool for container visualization |
| `src/SourceGen.Ioc.SourceGenerator/Generator/` | Incremental generator pipeline |
| `src/SourceGen.Ioc.SourceGenerator/Analyzer/` | Roslyn analyzers (SGIOC diagnostics) |
| `src/SourceGen.Ioc.SourceGenerator/Models/` | Immutable data models for pipeline |
| `test/SourceGen.Ioc.Test/` | TUnit tests |
| `test/SourceGen.Ioc.TestAot/` | Native AOT validation tests |
| `test/SourceGen.Ioc.TestCase/` | Shared test case projects |

## Specifications

- Generator: `src/SourceGen.Ioc.SourceGenerator/Generator/Spec/SPEC.spec.md`
- Analyzer: `src/SourceGen.Ioc.SourceGenerator/Analyzer/Spec/SPEC.spec.md`
