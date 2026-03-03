# Copilot Instructions for SourceGen

This repository contains C# Source Generator projects. Follow the project principles defined in [AGENTS.md](../AGENTS.md).

## Project Requirements

### Always Use C# 14 Syntax

When generating or modifying code, **always use the latest C# 14 features** where applicable.  
C# instructions can be found here: [C# Best Practices](./instructions/csharp.instructions.md)

### Source Generator Guidelines
When working on source generators, follow the best practices outlined here: [C# Source Generator Best Practices](./instructions/csharp-source-generator.instructions.md)

## Specifications

Always read the relevant spec (`**/Spec/*.md`) before implementing or modifying features.

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

### Mandatory Final Steps

Every task that modifies code **MUST** complete these steps before finishing:

1. **Run Tests**: Run all related tests via terminal. Fix any failing tests before proceeding.
2. **SubAgent Review**: Follow the workflow agent's review step.

## Reference

- [C# Best Practices](./instructions/csharp.instructions.md)
- [C# Source Generator Best Practices](./instructions/csharp-source-generator.instructions.md)
- [TUnit Best Practices](./instructions/csharp-tunit.instructions.md)
