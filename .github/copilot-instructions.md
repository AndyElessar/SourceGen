# Copilot Instructions for SourceGen

This repository contains C# Source Generator projects. Follow these instructions when assisting with code in this repository.

## Project Overview

- **SourceGen.Ioc.SourceGenerator**: Source Generator library targeting .NET Standard 2.0
- **SourceGen.Ioc**: IoC (Inversion of Control) library targeting .NET Standard 2.0 / .NET 10
- **SourceGen.Ioc.Cli**: Command-line tool for adding attributes, generate attributes annotation in C# projects
- **Tests**: Unit,Snapshot and benchmark tests for the Source Generator and CLI tool

## Project Requirements

### Always Use C# 14 Syntax

When generating or modifying code, **always use the latest C# 14 features** where applicable.  
C# instructions can be found here: [C# Best Practices](./instructions/csharp.instructions.md)

### Source Generator Guidelines
When working on source generators, follow the best practices outlined here: [C# Source Generator Best Practices](./instructions/csharp-source-generator.instructions.md)

## File Organization

```
src/
├── SourceGen.Ioc/                    # Main library, where attributes are defined
├── SourceGen.Ioc.Cli/                # CLI tool for adding attributes
└── SourceGen.Ioc.SourceGenerator/    # Source Generator project
    ├── Analyzer/                     # Roslyn analyzer implementations
    │   └── SPEC.md                   # Analyzer specification document
    ├── Generator/                    # Generator implementations
    │   └── Spec/                     # Generator specification documents
    └── Models/                       # Data models for generation
tests/
├── SourceGen.Ioc.Benchmark/          # Benchmark tests
├── SourceGen.Ioc.Cli.Test/           # CLI unit tests
└── SourceGen.Ioc.Test/               # Generator unit tests
```

## Code Style

- Use file-scoped namespaces
- Use `readonly record struct` or `sealed record class` for data models in generators
- Prefer records for immutable data transfer objects
- Use nullable reference types (`#nullable enable`)
- Follow .NET naming conventions

## Testing

- **ALWAYS use the VS Code `runTests` tool** to run tests instead of terminal commands
- If terminal is required, use TUnit's correct command format:
  ```powershell
  # TUnit uses 'dotnet run' with --treenode-filter, NOT 'dotnet test'
  dotnet run --project path/to/TestProject.csproj -- --treenode-filter "/*/*/TestClass/*"
  ```
- Never use `dotnet test` with `--filter` for TUnit projects
- Test projects use TUnit framework - see [TUnit Best Practices](./prompts/csharp-tunit.prompt.md)

## Reference

- [C# Best Practices](./instructions/csharp.instructions.md)
- [C# Source Generator Best Practices](./instructions/csharp-source-generator.instructions.md)
- [TUnit Best Practices](./prompts/csharp-tunit.prompt.md)