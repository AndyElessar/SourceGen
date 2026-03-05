# SourceGen.Ioc Domain

IoC (Inversion of Control) source generator domain — compile-time dependency injection container generation.

## Projects

| Project                       | Purpose                                   | Path                                    |
| ----------------------------- | ----------------------------------------- | --------------------------------------- |
| SourceGen.Ioc                 | Public API: attributes and runtime types  | `src/SourceGen.Ioc/`                    |
| SourceGen.Ioc.Cli             | CLI tool for container visualization      | `src/SourceGen.Ioc.Cli/`                |
| SourceGen.Ioc.SourceGenerator | Incremental source generator and analyzer | `src/SourceGen.Ioc.SourceGenerator/`    |
| SourceGen.Ioc.Test            | TUnit tests for generator output          | `test/SourceGen.Ioc.Test/`              |
| SourceGen.Ioc.TestAot         | Native AOT validation tests               | `test/SourceGen.Ioc.TestAot/`           |
| SourceGen.Ioc.TestCase        | Shared test case projects                 | `test/SourceGen.Ioc.TestCase/`          |
| SourceGen.Ioc.Cli.Test        | CLI tool tests                            | `test/SourceGen.Ioc.Cli.Test/`          |
| SourceGen.Ioc.Benchmark       | BenchmarkDotNet performance tests         | `test/SourceGen.Ioc.Benchmark/`         |

## Commands

```powershell
# Build
dotnet build SourceGen.slnx

# Run tests (TUnit — MUST use dotnet run, NOT dotnet test)
dotnet run --project src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj -- --treenode-filter "/*/*/TestClass/*"

# Run all tests
dotnet run --project src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj

# AOT tests (publish first, then run)
dotnet publish src/Ioc/test/SourceGen.Ioc.TestAot/SourceGen.Ioc.TestAot.csproj -c Release
.\src\Ioc\test\SourceGen.Ioc.TestAot\bin\Release\net10.0\win-x64\publish\SourceGen.Ioc.TestAot.exe
```

## Specifications

| Spec | Scope |
| --- | --- |
| [Generator SPEC](src/SourceGen.Ioc.SourceGenerator/Generator/Spec/SPEC.spec.md) | Index — registration, container generation, all sub-specs |
| [Analyzer SPEC](src/SourceGen.Ioc.SourceGenerator/Analyzer/Spec/SPEC.spec.md) | Diagnostic rules and analyzers |

## Domain Rules

These rules extend the root [AGENTS.md](../../AGENTS.md) for this domain:

- Diagnostic IDs follow the `SGIOC` prefix pattern (e.g., `SGIOC001`)

## Versioning

- Uses [nbgv](https://github.com/dotnet/Nerdbank.GitVersioning) with tag prefix `ioc-v` (e.g., `ioc-v0.9.1-alpha`)
- CLI uses tag prefix `ioc-cli-v` (e.g., `ioc-cli-v1.0.0`)
