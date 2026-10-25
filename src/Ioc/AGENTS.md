# SourceGen.Ioc Domain

IoC (Inversion of Control) source generator domain — compile-time dependency injection container generation.

## Projects

| Project                          | Purpose                                     | Path                                     |
| -------------------------------- | ------------------------------------------- | ---------------------------------------- |
| SourceGen.Ioc                    | Public API: attributes and runtime types    | `src/SourceGen.Ioc/`                     |
| SourceGen.Ioc.Cli                | CLI tool for container visualization        | `src/SourceGen.Ioc.Cli/`                 |
| SourceGen.Ioc.SourceGenerator    | Incremental source generator and analyzer   | `src/SourceGen.Ioc.SourceGenerator/`     |
| SourceGen.Ioc.Test               | TUnit tests for generator output            | `test/SourceGen.Ioc.Test/`               |
| SourceGen.Ioc.TestAot            | Native AOT validation tests                 | `test/SourceGen.Ioc.TestAot/`            |
| SourceGen.Ioc.TestCase           | Shared test case projects                   | `test/SourceGen.Ioc.TestCase/`           |
| SourceGen.Ioc.Cli.Test           | CLI tool tests                              | `test/SourceGen.Ioc.Cli.Test/`           |
| SourceGen.Ioc.Benchmark          | BenchmarkDotNet performance tests           | `test/SourceGen.Ioc.Benchmark/`          |
| SourceGen.Ioc.GeneratorBenchmark | BenchmarkDotNet source generator benchmarks | `test/SourceGen.Ioc.GeneratorBenchmark/` |

## Commands

```powershell
# Build
dotnet build SourceGen.slnx

# Run filtered tests
dotnet run --project src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj -- --treenode-filter "/*/*/TestClass/*"

# Run all tests
dotnet run --project src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj
dotnet run --project src/Ioc/test/SourceGen.Ioc.Cli.Test/SourceGen.Ioc.Cli.Test.csproj

# AOT tests (publish first, then run the native executable for the current RID)
dotnet publish src/Ioc/test/SourceGen.Ioc.TestAot/SourceGen.Ioc.TestAot.csproj -c Release
./src/Ioc/test/SourceGen.Ioc.TestAot/bin/Release/net10.0/<rid>/publish/SourceGen.Ioc.TestAot   # e.g. linux-x64; add .exe on win-x64
```

## Specifications

| Spec | Scope |
| --- | --- |
| [Generator SPEC](src/SourceGen.Ioc.SourceGenerator/Spec/SPEC.spec.md) | Index — registration, container generation, all sub-specs |
| [Analyzer SPEC](src/SourceGen.Ioc.SourceGenerator/Analyzer/Spec/SPEC.spec.md) | Diagnostic rules and analyzers |

## Domain Rules

These rules extend the root [AGENTS.md](../../AGENTS.md) for this domain:

- Diagnostic IDs follow the `SGIOC` prefix pattern (e.g., `SGIOC001`)

## Versioning

Released with nbgv and tag prefixes `ioc-v` / `ioc-cli-v` — see the `sourcegen-release-commands` skill.
