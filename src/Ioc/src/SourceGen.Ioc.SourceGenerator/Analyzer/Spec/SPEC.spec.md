# Ioc Analyzer

Index of the SourceGen.Ioc analyzer specs: the analyzer pipelines and the diagnostic rules.

## Pipeline Architecture

### Entry Points

| Analyzer | Role | File |
| :------- | :--- | :--- |
| `RegisterAnalyzer` | Registration validation, dependency/lifetime checks, key diagnostics | `Analyzer/RegisterAnalyzer.cs` |
| `ContainerAnalyzer` | Container class validation, resolvability, module cycle detection | `Analyzer/ContainerAnalyzer.cs` |

Both are `[DiagnosticAnalyzer(LanguageNames.CSharp)]` with lifecycle: `Initialize` → `CompilationStart` → actions → `CompilationEnd`.

| Spec | Scope |
| :--- | :---- |
| [RegisterAnalyzer](Analyzer.Register.spec.md) | CompilationStart setup, registered actions, compilation end analysis, default settings, partial files |
| [ContainerAnalyzer](Analyzer.Container.spec.md) | CompilationStart setup, registered actions, compilation end analysis, registration resolution, container scope |

### Diagnostic Coverage

| Analyzer | Diagnostics |
| :------- | :---------- |
| RegisterAnalyzer | SGIOC001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012, 013, 014, 015, 016, 017, 022, 023, 024, 026, 028, 030, 032, 033, 034, 035, 036, 037, 038, 039, 040, 041 |
| ContainerAnalyzer | SGIOC018, 019, 020, 021, 025, 027, 029, 031 |


## Diagnostics

Format: ID - Level - Category - Description

| Spec | Diagnostics |
| :--- | :---------- |
| [Registration](Diagnostics.Registration.spec.md) | SGIOC001, SGIOC009, SGIOC010, SGIOC011, SGIOC012, SGIOC032, SGIOC038, SGIOC040, SGIOC041 |
| [Dependency](Diagnostics.Dependencies.spec.md) | SGIOC002, SGIOC003, SGIOC004, SGIOC005, SGIOC033 |
| [Injection](Diagnostics.Injection.spec.md) | SGIOC006, SGIOC007, SGIOC022, SGIOC023, SGIOC024, SGIOC028 |
| [Key](Diagnostics.Keys.spec.md) | SGIOC013, SGIOC014, SGIOC015 |
| [Factory](Diagnostics.Factory.spec.md) | SGIOC008, SGIOC016, SGIOC017, SGIOC039 |
| [Container](Diagnostics.Container.spec.md) | SGIOC018, SGIOC019, SGIOC020, SGIOC021, SGIOC025, SGIOC031 |
| [Async-init](Diagnostics.AsyncInit.spec.md) | SGIOC026, SGIOC027, SGIOC029, SGIOC030 |
| [Decorator](Diagnostics.Decorators.spec.md) | SGIOC034, SGIOC035 |
| [Generic Dependency](Diagnostics.Generics.spec.md) | SGIOC036, SGIOC037 |
