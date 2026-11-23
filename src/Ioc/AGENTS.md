# SourceGen.Ioc Domain

IoC (Inversion of Control) source generator domain — compile-time dependency injection container generation.

## Commands

```powershell
# Build
dotnet build SourceGen.slnx

# Projects target net10.0 and net11.0 (the CLI and its tests: net11.0 only), so run, test and publish commands need -f <tfm>.
# Use net10.0 while iterating; run the full test projects and the AOT tests on both before finishing.

# Run filtered tests
dotnet run --project src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj -f net10.0 -- --treenode-filter "/*/*/TestClass/*"

# Run all tests
dotnet run --project src/Ioc/test/SourceGen.Ioc.Test/SourceGen.Ioc.Test.csproj -f net10.0
dotnet run --project src/Ioc/test/SourceGen.Ioc.Cli.Test/SourceGen.Ioc.Cli.Test.csproj   # CLI and its tests target net11.0 only, so no -f

# AOT tests (publish first, then run the native executable for the current RID).
# A failed publish leaves the previous executable in place: check the exit code (or the executable's timestamp) before running it.
dotnet publish src/Ioc/test/SourceGen.Ioc.TestAot/SourceGen.Ioc.TestAot.csproj -c Release -f net10.0
./src/Ioc/test/SourceGen.Ioc.TestAot/bin/Release/net10.0/<rid>/publish/SourceGen.Ioc.TestAot   # e.g. linux-x64; add .exe on win-x64
```

## Design documents

| Document | Role |
| --- | --- |
| [Constitution](docs/Constitution.md) | Principles, scope and limits, terms, documented differences from MS.DI. Read it first. It wins over a spec; when they disagree, tell the user. A change needs a new ADR. |
| [Specifications](docs/spec/SPEC.spec.md) | Index of the specs. The source of truth for current behavior. |
| [ADRs](docs/adr/) | Why a decision was made, the rejected alternatives, and the tested facts with versions. |

Design documents are in English, in the style of the `ste80-humanizer` skill. See the `sourcegen-spec-writing` skill.

## Domain Rules

These rules extend the root [AGENTS.md](../../AGENTS.md) for this domain:

- Diagnostic IDs follow the `SGIOC` prefix pattern (e.g., `SGIOC001`), grouped by category (see [Diagnostics](docs/spec/Diagnostics.spec.md))
- The generator reports IoC semantic diagnostics itself; no separate analyzer repeats its analysis

## Versioning

Released with nbgv and tag prefixes `ioc-v` / `ioc-cli-v` — see the `sourcegen-release-commands` skill.
