# Copilot Instructions for SourceGen

This repository contains C# Source Generator projects. Follow these instructions when assisting with code in this repository.

## Project Overview

- **SourceGen.Ioc.SourceGenerator**: Source Generator library targeting .NET Standard 2.0
- **SourceGen.Ioc**: IoC (Inversion of Control) library targeting .NET 10
- **SourceGen.Ioc.Cli**: Command-line tool for adding attributes, generate attributes annotation in C# projects
- **Tests**:
  - **SourceGen.Ioc.Test**: Unit and snapshot tests for the Source Generator
  - **SourceGen.Ioc.Cli.Test**: CLI unit tests
  - **SourceGen.Ioc.Benchmark**: Benchmark tests
  - **SourceGen.Ioc.TestAot**: AOT integration tests (validates Native AOT compatibility)
  - **SourceGen.Ioc.TestCase**: Shared test case code

## Project Requirements

### Always Use C# 14 Syntax

When generating or modifying code, **always use the latest C# 14 features** where applicable.  
C# instructions can be found here: [C# Best Practices](./instructions/csharp.instructions.md)

### Source Generator Guidelines
When working on source generators, follow the best practices outlined here: [C# Source Generator Best Practices](./instructions/csharp-source-generator.instructions.md)

## Specifications

Always read the relevant spec before implementing or modifying features:

- [Generator Spec](../src/SourceGen.Ioc.SourceGenerator/Generator/Spec/SPEC.md) — Source Generator data flow, parse logic, and architecture
- [Analyzer Spec](../src/SourceGen.Ioc.SourceGenerator/Analyzer/Spec/SPEC.md) — Diagnostic rules (SGIOC001–SGIOC021)

## Code Style

- Use file-scoped namespaces
- Use `readonly record struct` or `sealed record class` for data models in generators
- Prefer records for immutable data transfer objects
- Use nullable reference types (`#nullable enable`)
- Follow .NET naming conventions

## Testing

- **ALWAYS run tests via terminal** using TUnit's correct command format:
  ```powershell
  # TUnit uses 'dotnet run' with --treenode-filter, NOT 'dotnet test'
  dotnet run --project path/to/TestProject.csproj -- --treenode-filter "/*/*/TestClass/*"
  ```
- Never use `dotnet test` with `--filter` for TUnit projects
- If facing issue where don't know is design decision or test failure, **ask user for clarification**
- Test projects use TUnit framework - see [TUnit Best Practices](./instructions/csharp-tunit.instructions.md)

## AOT Testing

The `SourceGen.Ioc.TestAot` project validates that the Source Generator produces Native AOT compatible code.

**Important**: This project requires AOT publishing before running tests.

### Running AOT Tests

1. **Publish the AOT executable first**:
   ```powershell
   dotnet publish tests/SourceGen.Ioc.TestAot/SourceGen.Ioc.TestAot.csproj -c Release
   ```

2. **Run the published executable**:
   ```powershell
   # Windows
   .\tests\SourceGen.Ioc.TestAot\bin\Release\net10.0\win-x64\publish\SourceGen.Ioc.TestAot.exe
   
   # Or use dotnet run (will trigger publish automatically)
   dotnet run --project tests/SourceGen.Ioc.TestAot/SourceGen.Ioc.TestAot.csproj -c Release
   ```

> **Note**: AOT tests must be published and executed as native binaries to properly validate AOT compatibility.

## Agent Workflow

Delegate work to SubAgents for context isolation and parallel efficiency. Every modification must end with testing and review.

### SubAgent Delegation

| Task Type | Delegate To | Purpose |
| --------- | ----------- | ------- |
| Codebase exploration | `Explore` SubAgent | Read-only research, gather context before implementation |
| Implementation | Main Agent | Apply changes with full tool access |
| Test execution | Main Agent | Run tests via `runTests` tool |
| Code review (code changes) | `Review` SubAgent (via `Dev.agent.md`) | Validate spec compliance, refactoring opportunities, and performance |
| Documentation review | `DocReview` SubAgent (via `Doc.agent.md`) | Validate documentation accuracy, links, and consistency |

- **MUST**: Use `Explore` SubAgent for initial codebase research before making changes.
- **MUST**: Use the review flow defined by the active workflow agent (`Dev.agent.md` or `Doc.agent.md`) for post-change review.
- **MUST NOT**: Use SubAgents for file edits — only the main agent should write code.

### Mandatory Final Steps

Every task that modifies code **MUST** complete these steps before finishing:

1. **Run Tests**: Run all related tests via terminal. Fix any failing tests before proceeding.
2. **SubAgent Review**: Follow the workflow agent's review step.
  - For code implementation, use the `Review` SubAgent as defined in `Dev.agent.md`.
  - For documentation updates, use the `DocReview` SubAgent as defined in `Doc.agent.md`.

## Reference

- [Generator Spec](../src/SourceGen.Ioc.SourceGenerator/Generator/Spec/SPEC.md) — Data flow, parse logic, architecture
- [Analyzer Spec](../src/SourceGen.Ioc.SourceGenerator/Analyzer/Spec/SPEC.md) — Diagnostic rules (SGIOC001–SGIOC021)
- [C# Best Practices](./instructions/csharp.instructions.md)
- [C# Source Generator Best Practices](./instructions/csharp-source-generator.instructions.md)
- [TUnit Best Practices](./instructions/csharp-tunit.instructions.md)